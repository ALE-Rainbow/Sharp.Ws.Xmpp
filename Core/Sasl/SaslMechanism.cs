using Rainbow.Cryptography.Util;
using Rainbow.Cryptography.SASL;
using Rainbow.Cryptography.SASL.SCRAM;
using System;
using System.Collections.Generic;
using System.Text;

namespace Sharp.Xmpp.Core.Sasl
{

    public static class Mechanisms
    {
        /// <summary>
        /// List mechanism to use by priority: Can only contain "SCRAM-SHA-512", "SCRAM-SHA-256", "SCRAM-SHA-1" or "PLAIN"
        /// </summary>
        // List by priority order
        static public List<String> ListByPriority = new List<String>() {
            { "SCRAM-SHA-512" },
            { "SCRAM-SHA-256" },
            { "SCRAM-SHA-1" },
            { "PLAIN" } };
    }

    internal class SaslMechanism
    {
        String type;

        private int Step = 0;

        private SASLMechanism client;
        private IEncodingInfo encoding;
        private ResizableArray<Byte> writeArray;
        private SASLCredentialsSCRAMForClient credentials;

        /// <summary>
        /// True if the authentication exchange between client and server
        /// has been completed.
        /// </summary>
        public bool IsCompleted
        {
            get;
            private set;
        }

        /// <summary>
        /// True if the mechanism requires initiation by the client.
        /// </summary>
        public bool HasInitial
        {
            get;
            private set;
        }

        /// <summary>
        /// Computes the client response to a challenge sent by the server.
        /// </summary>
        /// <param name="challenge"></param>
        /// <returns>The client response to the specified challenge.</returns>
        protected byte[] ComputeResponse(byte[] challenge)
        {
            if(type == "PLAIN")
            {
                // Sasl Plain does not involve another roundtrip.
                IsCompleted = true;
                // Username and password are delimited by a NUL (U+0000) character
                // and the response shall be encoded as UTF-8.
                return Encoding.UTF8.GetBytes("\0" + credentials.Username + "\0" + credentials.Password);
            }

            if (Step == 2)
                IsCompleted = true;
            byte[] ret = Step == 0 ? ComputeInitialResponse() :
                (Step == 1 ? ComputeFinalResponse(challenge) :
                VerifyServerSignature(challenge));
            Step++;
            return ret;
        }

        /// <summary>
        /// </summary>
        internal SaslMechanism(String type, String userName, String password)
        {
            this.type = type;

            HasInitial = true; // Ok for PLAIN and SCRAM-SHA-1

            credentials = new SASLCredentialsSCRAMForClient(
                     userName,
                     password // password may be clear-text password as string, or result of PBKDF2 iteration as byte array.
                   );

            switch (type)
            {
                case "PLAIN":
                    return;

                case "SCRAM-SHA-1":
                    var sha1 = new Rainbow.Cryptography.Digest.SHA128();
                    client = sha1.CreateSASLClientSCRAM();
                    break;

                case "SCRAM-SHA-256":
                    var sha256 = new Rainbow.Cryptography.Digest.SHA256();
                    client = sha256.CreateSASLClientSCRAM();
                    break;

                case "SCRAM-SHA-512":
                    var sha512 = new Rainbow.Cryptography.Digest.SHA512();
                    client = sha512.CreateSASLClientSCRAM();
                    break;
            }

            encoding = new UTF8Encoding(false, false).CreateDefaultEncodingInfo();
            writeArray = new ResizableArray<Byte>();
        }

        /// <summary>
        /// Retrieves the base64-encoded client response for the specified
        /// base64-encoded challenge sent by the server.
        /// </summary>
        /// <param name="challenge">A base64-encoded string representing a challenge
        /// sent by the server.</param>
        /// <returns>A base64-encoded string representing the client response to the
        /// server challenge.</returns>
        /// <remarks>The IMAP, POP3 and SMTP authentication commands expect challenges
        /// and responses to be base64-encoded. This method automatically decodes the
        /// server challenge before passing it to the Sasl implementation and
        /// encodes the client response to a base64-string before returning it to the
        /// caller.</remarks>
        /// <exception cref="SaslException">The client response could not be retrieved.
        /// Refer to the inner exception for error details.</exception>
        public string GetResponse(string challenge)
        {
            try
            {
                byte[] data = String.IsNullOrEmpty(challenge) ? new byte[0] :
                    Convert.FromBase64String(challenge);
                byte[] response = ComputeResponse(data);
                return Convert.ToBase64String(response);
            }
            catch (Exception e)
            {
                throw new SaslException("The challenge-response could not be " +
                    "retrieved.", e);
            }
        }

        /// <summary>
        /// Computes the initial response sent by the client to the server.
        /// </summary>
        /// <returns>An array of bytes containing the initial client
        /// response.</returns>
        private byte[] ComputeInitialResponse()
        {

            var challengeArguments = credentials.CreateChallengeArguments(
              null, // Initial phase does not read anything
              -1,
              -1,
              writeArray,
              0,
              encoding
              );

            try
            {
                var task = client.ChallengeOrThrowOnErrorAsync(challengeArguments);
                (var bytesWritten, var challengeResult) = task.Result;

                if (bytesWritten > 0)
                {
                    byte[] result = new byte[bytesWritten];
                    Array.Copy(writeArray.Array, result, bytesWritten);
                    return result;
                }
            }
            catch
            {

            }
            return new byte[0];



            //// We don't support channel binding.
            //return Encoding.UTF8.GetBytes("n,,n=" + SaslPrep(Username) + ",r=" +
            //    Cnonce);
        }

        /// <summary>
        /// Computes the "client-final-message" which completes the authentication
        /// process.
        /// </summary>
        /// <param name="challenge">The "server-first-message" challenge received
        /// from the server in response to the initial client response.</param>
        /// <returns>An array of bytes containing the client's challenge
        /// response.</returns>
        private byte[] ComputeFinalResponse(byte[] challenge)
        {
            var challengeArguments = credentials.CreateChallengeArguments(
              challenge,
              0,
              challenge.Length,
              writeArray,
              0,
              encoding
              );

            try
            {
                var task = client.ChallengeOrThrowOnErrorAsync(challengeArguments);
                (var bytesWritten, var challengeResult) = task.Result;

                if (bytesWritten > 0)
                {
                    byte[] result = new byte[bytesWritten];
                    Array.Copy(writeArray.Array, result, bytesWritten);
                    return result;
                }
            }
            catch
            {

            }
            return new byte[0];

            //NameValueCollection nv = ParseServerFirstMessage(challenge);
            //// Extract the server data needed to calculate the client proof.
            //string salt = nv["s"], nonce = nv["r"];
            //int iterationCount = Int32.Parse(nv["i"]);
            //if (!VerifyServerNonce(nonce))
            //    throw new SaslException("Invalid server nonce: " + nonce);
            //// Calculate the client proof (refer to RFC 5802, p.7).
            //string clientFirstBare = "n=" + SaslPrep(Username) + ",r=" + Cnonce,
            //    serverFirstMessage = Encoding.UTF8.GetString(challenge),
            //    withoutProof = "c=" +
            //    Convert.ToBase64String(Encoding.UTF8.GetBytes("n,,")) + ",r=" +
            //    nonce;
            //AuthMessage = clientFirstBare + "," + serverFirstMessage + "," +
            //    withoutProof;
            //SaltedPassword = Hi(Password, salt, iterationCount);
            //byte[] clientKey = HMAC(SaltedPassword, "Client Key"),
            //    storedKey = H(clientKey),
            //    clientSignature = HMAC(storedKey, AuthMessage),
            //    clientProof = Xor(clientKey, clientSignature);
            //// Return the client final message.
            //return Encoding.UTF8.GetBytes(withoutProof + ",p=" +
            //    Convert.ToBase64String(clientProof));
        }

        /// <summary>
        /// Verifies the server signature which is sent by the server as the final
        /// step of the authentication process.
        /// </summary>
        /// <param name="challenge">The server signature as a base64-encoded
        /// string.</param>
        /// <returns>The client's response to the server. This will be an empty
        /// byte array if verification was successful, or the '*' SASL cancellation
        /// token.</returns>
        private byte[] VerifyServerSignature(byte[] challenge)
        {
            var challengeArguments = credentials.CreateChallengeArguments(
              challenge,
              0,
              challenge.Length,
              writeArray,
              0,
              encoding
              );

            var task = client.ChallengeOrThrowOnErrorAsync(challengeArguments);
            (var bytesWritten, var challengeResult) = task.Result;

            if (challengeResult == SASLChallengeResult.Completed)
                return new byte[0];

            throw new Exception("Server signature has not been verified with success");

        }


    }
}
