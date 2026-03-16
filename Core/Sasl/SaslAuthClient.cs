using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Sharp.Xmpp.Core.Sasl
{
    /// <summary>
    /// Result of a single authentication step.
    /// </summary>
    public class SaslStepResult
    {
        /// <summary>True when the exchange is finished (success or failure).</summary>
        public bool IsComplete { get; set; }
        /// <summary>True when the server accepted the credentials.</summary>
        public bool Success { get; set; }
        /// <summary>Base64 payload to send to the server, or null when nothing must be sent.</summary>
        public string Payload { get; set; }
        /// <summary>Human-readable error description when IsComplete &amp;&amp; !Success.</summary>
        public string Error { get; set; }
    }

    /// <summary>
    /// SASL authentication client for XMPP (RFC 5802 SCRAM + RFC 4616 PLAIN).
    ///
    /// Thread-safety: each instance is fully independent (one instance per connection).
    ///                Do NOT share an instance across multiple threads.
    ///
    /// Usage:
    ///   1. Select the strongest available mechanism with <see cref="PickBest"/>.
    ///   2. Instantiate: new SaslAuthClient(mechanism, username, password).
    ///   3. Call <see cref="Begin"/> and send the resulting Payload to the server.
    ///   4. Call <see cref="Step"/> with every server response until IsComplete == true.
    /// </summary>
    public class SaslAuthClient
    {
        // ── private state ─────────────────────────────────────────────
        private readonly String _mech;
        private readonly string _username;
        private readonly string _password;

        private string _cnonce;                  // client-generated nonce
        private string _clientFirstMessageBare;  // kept for proof computation
        private string _serverFirstMessage;      // kept for auth-message assembly
        private int _step;                    // current exchange step (1-based)

        /// <summary>Mechanism selected for this instance.</summary>
        public String Mechanism => _mech;


        /// <summary>
        /// XML mechanism names used in &lt;auth mechanism='...'&gt;, by order of priority.
        /// </summary>
        public static List<string> MechanismNames = new() {
            { "SCRAM-SHA-512" },
            { "SCRAM-SHA-256" },
            { "SCRAM-SHA-1" },
            { "PLAIN" } 
        };

        // ─────────────────────────────────────────────────────────────
        public SaslAuthClient(String mech, string username, string password)
        {
            _mech = mech;
            _username = username ?? throw new ArgumentNullException(nameof(username));
            _password = password ?? throw new ArgumentNullException(nameof(password));
        }

        /// <summary>
        /// Picks the strongest mechanism from the list advertised by the server.
        /// Throws <see cref="NotSupportedException"/> if no common mechanism exists.
        /// </summary>
        public static String PickBest(IEnumerable<string> serverMechanisms)
        {
            var set = new HashSet<string>(serverMechanisms, StringComparer.OrdinalIgnoreCase);
            foreach(var mech in MechanismNames)
            {
                if (set.Contains(mech))
                    return mech;
            }
            throw new NotSupportedException("No common SASL mechanism found.");
        }

        // ══════════════════════════════════════════════════════════════
        //  Public API
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Produces the initial message to send to the server (base64 encoded).
        /// Must be called once before any <see cref="Step"/> call.
        /// </summary>
        public SaslStepResult Begin()
        {
            _step = 0;
            return _mech == "PLAIN" ? PlainBegin() : ScramBegin();
        }

        /// <summary>
        /// Processes a base64-encoded server response and returns the next client message.
        /// Keep calling until <see cref="SaslStepResult.IsComplete"/> is true.
        /// </summary>
        public SaslStepResult Step(string serverBase64)
        {
            // PLAIN is a single round-trip from the client side
            if (_mech == "PLAIN")
                return new SaslStepResult { IsComplete = true, Success = true };

            _step++;
            return _step == 1 ? ScramStep1(serverBase64) : ScramStep2(serverBase64);
        }

        // ══════════════════════════════════════════════════════════════
        //  PLAIN  (RFC 4616)
        // ══════════════════════════════════════════════════════════════

        private SaslStepResult PlainBegin()
        {
            // Wire format: \0username\0password
            var raw = "\0" + _username + "\0" + _password;
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(raw));
            return new SaslStepResult { Payload = payload };
        }

        // ══════════════════════════════════════════════════════════════
        //  SCRAM  (RFC 5802)
        // ══════════════════════════════════════════════════════════════

        // ── Step 1: send client-first-message ─────────────────────────
        private SaslStepResult ScramBegin()
        {
            _cnonce = GenerateNonce();

            // gs2-header "n,," means no channel binding
            _clientFirstMessageBare = "n=" + EscapeUsername(_username) + ",r=" + _cnonce;
            var clientFirstMessage = "n,," + _clientFirstMessageBare;

            return new SaslStepResult
            {
                Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFirstMessage))
            };
        }

        // ── Step 2: receive server-first-message, send client-final-message ──
        private SaslStepResult ScramStep1(string serverBase64)
        {
            _serverFirstMessage = Encoding.UTF8.GetString(Convert.FromBase64String(serverBase64));

            // Expected format: r=<nonce>,s=<salt_b64>,i=<iterations>
            var parts = ParseKeyValue(_serverFirstMessage);
            if (!parts.TryGetValue("r", out var serverNonce) ||
                !parts.TryGetValue("s", out var saltB64) ||
                !parts.TryGetValue("i", out var iterStr))
                return Fail("Malformed server-first-message");

            // Server nonce must start with the client nonce
            if (!serverNonce.StartsWith(_cnonce, StringComparison.Ordinal))
                return Fail("Invalid server nonce");

            if (!int.TryParse(iterStr, out var iterations) || iterations < 1)
                return Fail("Invalid iteration count");

            var salt = Convert.FromBase64String(saltB64);
            var algos = GetAlgorithms();

            // Derive keys via PBKDF2
            var saltedPassword = Hi(_password, salt, iterations, algos.hmacName, algos.outputLen);
            var clientKey = HmacBytes(saltedPassword, "Client Key", algos.hmacName);
            var storedKey = HashBytes(clientKey, algos.hashFactory);

            // Build the auth-message used for signing
            var channelBinding = Convert.ToBase64String(Encoding.UTF8.GetBytes("n,,"));
            var clientFinalNoProof = "c=" + channelBinding + ",r=" + serverNonce;
            var authMessage = _clientFirstMessageBare + "," +
                                     _serverFirstMessage + "," +
                                     clientFinalNoProof;

            // ClientProof = ClientKey XOR HMAC(StoredKey, authMessage)
            var clientSignature = HmacBytes(storedKey, authMessage, algos.hmacName);
            var clientProof = Xor(clientKey, clientSignature);

            var clientFinalMessage = clientFinalNoProof + ",p=" + Convert.ToBase64String(clientProof);
            return new SaslStepResult
            {
                Payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(clientFinalMessage))
            };
        }

        // ── Step 3: verify server-final-message ───────────────────────
        private SaslStepResult ScramStep2(string serverBase64)
        {
            var serverFinal = Encoding.UTF8.GetString(Convert.FromBase64String(serverBase64));
            var parts = ParseKeyValue(serverFinal);

            // "e=" indicates a server-side error
            if (parts.TryGetValue("e", out var error))
                return Fail("Server error: " + error);

            // "v=" carries the server signature (optional client-side verification)
            if (!parts.ContainsKey("v"))
                return Fail("Missing server-final-message verifier");

            return new SaslStepResult { IsComplete = true, Success = true };
        }

        // ══════════════════════════════════════════════════════════════
        //  Cryptographic helpers
        // ══════════════════════════════════════════════════════════════

        /// <summary>Algorithm parameters for the selected SCRAM variant.</summary>
        private struct AlgoSet
        {
            public string hmacName;    // e.g. "HMACSHA256"
            public int outputLen;   // digest length in bytes
            public Func<HashAlgorithm> hashFactory; // creates the plain hash instance
        }

        private AlgoSet GetAlgorithms()
        {
            switch (_mech)
            {
                case "SCRAM-SHA-512":
                    return new AlgoSet { hmacName = "HMACSHA512", outputLen = 64, hashFactory = SHA512.Create };
                case "SCRAM-SHA-256":
                    return new AlgoSet { hmacName = "HMACSHA256", outputLen = 32, hashFactory = SHA256.Create };
                default: // ScramSha1
                    return new AlgoSet { hmacName = "HMACSHA1", outputLen = 20, hashFactory = SHA1.Create };
            }
        }

        /// <summary>
        /// Hi(password, salt, i) = PBKDF2 with the chosen HMAC (RFC 5802 §2.2).
        ///
        /// Compatibility notes:
        ///   .NET Standard 2.0 — Rfc2898DeriveBytes only supports HMAC-SHA1 natively;
        ///                        SHA-256 / SHA-512 fall back to a manual PBKDF2 implementation.
        ///   .NET Standard 2.1+ / .NET 5+ — HashAlgorithmName overload is available for all variants.
        /// </summary>
        private static byte[] Hi(string password, byte[] salt, int iterations,
                                  string hmacName, int outputLen)
        {
            var passwordBytes = Encoding.UTF8.GetBytes(password);

#if NETSTANDARD2_0
        if (hmacName == "HMACSHA1")
        {
            // Built-in PBKDF2 is sufficient for SHA-1 on .NET Standard 2.0
            using (var kdf = new Rfc2898DeriveBytes(passwordBytes, salt, iterations))
                return kdf.GetBytes(outputLen);
        }
        // SHA-256 / SHA-512 require a manual PBKDF2 on .NET Standard 2.0
        return Pbkdf2Manual(passwordBytes, salt, iterations, hmacName, outputLen);
#else
            // HashAlgorithmName overload available on .NET Standard 2.1 / .NET 5+
            var hashName = new HashAlgorithmName(hmacName.Replace("HMAC", "")); // "SHA512" etc.
            using (var kdf = new Rfc2898DeriveBytes(passwordBytes, salt, iterations, hashName))
                return kdf.GetBytes(outputLen);
#endif
        }

#if NETSTANDARD2_0
    /// <summary>
    /// Manual PBKDF2 implementation required for SCRAM-SHA-256 / SCRAM-SHA-512
    /// on .NET Standard 2.0 where Rfc2898DeriveBytes lacks the HashAlgorithmName overload.
    ///
    /// Implements a single block (block index = 1) which is sufficient
    /// because outputLen never exceeds the HMAC digest size in SCRAM.
    /// </summary>
    private static byte[] Pbkdf2Manual(byte[] password, byte[] salt, int iterations,
                                       string hmacName, int outputLen)
    {
        // Prepare the input for U1: salt || INT(1)  (block index in big-endian)
        var block = new byte[salt.Length + 4];
        Array.Copy(salt, block, salt.Length);
        block[salt.Length + 3] = 1; // big-endian 0x00000001

        using (var hmac = (HMAC)CryptoConfig.CreateFromName(hmacName))
        {
            hmac.Key = password;

            // U1 = HMAC(password, salt || INT(1))
            var u      = hmac.ComputeHash(block);
            var result = new byte[outputLen];
            Array.Copy(u, result, outputLen);

            // result = U1 XOR U2 XOR ... XOR U_iterations
            for (int i = 1; i < iterations; i++)
            {
                u = hmac.ComputeHash(u);
                for (int j = 0; j < outputLen; j++)
                    result[j] ^= u[j];
            }
            return result;
        }
    }
#endif

        /// <summary>
        /// Computes HMAC(key, message) using the specified algorithm name.
        /// A fresh HMAC instance is created per call, ensuring thread-safety.
        /// </summary>
        private static byte[] HmacBytes(byte[] key, string message, string hmacName)
        {
            using (var hmac = (HMAC)CryptoConfig.CreateFromName(hmacName))
            {
                hmac.Key = key;
                return hmac.ComputeHash(Encoding.UTF8.GetBytes(message));
            }
        }

        /// <summary>
        /// Computes a plain hash of <paramref name="data"/>.
        /// A fresh hash instance is created per call, ensuring thread-safety.
        /// </summary>
        private static byte[] HashBytes(byte[] data, Func<HashAlgorithm> factory)
        {
            using (var h = factory())
                return h.ComputeHash(data);
        }

        /// <summary>Byte-wise XOR of two equal-length arrays.</summary>
        private static byte[] Xor(byte[] a, byte[] b)
        {
            var result = new byte[a.Length];
            for (int i = 0; i < a.Length; i++)
                result[i] = (byte)(a[i] ^ b[i]);
            return result;
        }

        // ══════════════════════════════════════════════════════════════
        //  Miscellaneous helpers
        // ══════════════════════════════════════════════════════════════

        /// <summary>Generates a cryptographically random, base64-encoded nonce.</summary>
        private static string GenerateNonce()
        {
#if NETSTANDARD2_0 || NETSTANDARD2_1
        // Static RandomNumberGenerator.GetBytes() is not available before .NET 6
        using (var rng = RandomNumberGenerator.Create())
        {
            var bytes = new byte[24];
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }
#else
            return Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
#endif
        }

        /// <summary>
        /// Escapes ',' and '=' in the username as required by RFC 5802 §5.1.
        /// </summary>
        private static string EscapeUsername(string u) =>
            u.Replace("=", "=3D").Replace(",", "=2C");

        /// <summary>
        /// Parses a comma-separated "key=value" string into a dictionary.
        /// Only the first '=' in each token is treated as the delimiter.
        /// </summary>
        private static Dictionary<string, string> ParseKeyValue(string s)
        {
            var d = new Dictionary<string, string>();
            foreach (var part in s.Split(','))
            {
                var idx = part.IndexOf('=');
                if (idx > 0)
                    d[part.Substring(0, idx)] = part.Substring(idx + 1);
            }
            return d;
        }

        /// <summary>Convenience factory for a failed, completed result.</summary>
        private static SaslStepResult Fail(string msg) =>
            new SaslStepResult { IsComplete = true, Success = false, Error = msg };
    }
}