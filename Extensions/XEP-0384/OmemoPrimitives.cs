using Org.BouncyCastle.Math.EC.Rfc7748;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Org.BouncyCastle.Security;
using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Cryptographic primitives used by OMEMO (XEP-0384 §4): X25519, Ed25519, HKDF-SHA-256,
    /// HMAC-SHA-256 and AES-256-CBC.
    /// </summary>
    internal static class OmemoPrimitives
    {
        public const int KeySize = 32;
        public const int SignatureSize = 64;

        private static readonly SecureRandom secureRandom = new SecureRandom();

        // p = 2^255 - 19
        private static readonly BigInteger fieldPrime = BigInteger.Pow(2, 255) - 19;

        public static byte[] RandomBytes(int length)
        {
            var result = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(result);
            return result;
        }

        /// <summary>
        /// Returns a random integer between 1 and 2^31 - 1 (inclusive), as required for device,
        /// PreKey and signed PreKey ids.
        /// </summary>
        public static int RandomPositiveInt32()
        {
            while (true)
            {
                int value = BitConverter.ToInt32(RandomBytes(4), 0) & Int32.MaxValue;
                if (value != 0)
                    return value;
            }
        }

        public static void GenerateX25519KeyPair(out byte[] privateKey, out byte[] publicKey)
        {
            privateKey = new byte[KeySize];
            publicKey = new byte[KeySize];
            X25519.GeneratePrivateKey(secureRandom, privateKey);
            X25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
        }

        public static byte[] X25519PublicFromPrivate(byte[] privateKey)
        {
            var publicKey = new byte[KeySize];
            X25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
            return publicKey;
        }

        public static byte[] X25519Agreement(byte[] privateKey, byte[] publicKey)
        {
            if (privateKey == null || privateKey.Length != KeySize)
                throw new CryptographicException("Invalid X25519 private key.");
            if (publicKey == null || publicKey.Length != KeySize)
                throw new CryptographicException("Invalid X25519 public key.");

            var shared = new byte[KeySize];
            if (!X25519.CalculateAgreement(privateKey, 0, publicKey, 0, shared, 0))
                throw new CryptographicException("X25519 agreement produced an all-zero secret.");
            return shared;
        }

        /// <summary>
        /// Generates a new Ed25519 identity key. The 32-byte seed is the private part.
        /// </summary>
        public static void GenerateEd25519KeyPair(out byte[] seed, out byte[] publicKey)
        {
            seed = new byte[Ed25519.SecretKeySize];
            Ed25519.GeneratePrivateKey(secureRandom, seed);
            publicKey = Ed25519PublicFromSeed(seed);
        }

        public static byte[] Ed25519PublicFromSeed(byte[] seed)
        {
            var publicKey = new byte[Ed25519.PublicKeySize];
            Ed25519.GeneratePublicKey(seed, 0, publicKey, 0);
            return publicKey;
        }

        public static byte[] Ed25519Sign(byte[] seed, byte[] message)
        {
            var signature = new byte[Ed25519.SignatureSize];
            Ed25519.Sign(seed, 0, message, 0, message.Length, signature, 0);
            return signature;
        }

        public static bool Ed25519Verify(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != Ed25519.PublicKeySize)
                return false;
            if (signature == null || signature.Length != Ed25519.SignatureSize || message == null)
                return false;
            try
            {
                return Ed25519.Verify(signature, 0, publicKey, 0, message, 0, message.Length);
            }
            catch (ArgumentException)
            {
                return false;
            }
        }

        /// <summary>
        /// Converts an Ed25519 private seed to the equivalent X25519 private scalar
        /// (libsodium crypto_sign_ed25519_sk_to_curve25519).
        /// </summary>
        public static byte[] Ed25519SeedToX25519Private(byte[] seed)
        {
            byte[] hash;
            using (var sha = SHA512.Create())
                hash = sha.ComputeHash(seed);

            var result = new byte[KeySize];
            Buffer.BlockCopy(hash, 0, result, 0, KeySize);
            result[0] &= 248;
            result[31] &= 127;
            result[31] |= 64;
            return result;
        }

        /// <summary>
        /// Converts an Ed25519 public key to its Curve25519 (Montgomery u) form using the
        /// birational map u = (1 + y) / (1 - y) from RFC 7748.
        /// </summary>
        public static byte[] Ed25519PublicToX25519(byte[] edPublic)
        {
            if (edPublic == null || edPublic.Length != KeySize)
                throw new CryptographicException("Invalid Ed25519 public key.");

            var yBytes = new byte[KeySize + 1];
            Buffer.BlockCopy(edPublic, 0, yBytes, 0, KeySize);
            yBytes[31] &= 0x7F;
            var y = new BigInteger(yBytes);
            if (y >= fieldPrime)
                throw new CryptographicException("Invalid Ed25519 public key.");

            BigInteger denominator = Mod(1 - y);
            if (denominator.IsZero)
                throw new CryptographicException("Invalid Ed25519 public key.");

            BigInteger u = Mod((1 + y) * BigInteger.ModPow(denominator, fieldPrime - 2, fieldPrime));

            byte[] uBytes = u.ToByteArray();
            var result = new byte[KeySize];
            Buffer.BlockCopy(uBytes, 0, result, 0, Math.Min(uBytes.Length, KeySize));
            return result;
        }

        /// <summary>
        /// Generates an Ed25519 identity key whose public key has a clear sign bit, so that
        /// XEdDSA/libsignal verifiers, which derive the Edwards key from the Curve25519 key with sign
        /// bit 0, accept its signatures (needed for legacy OMEMO).
        /// </summary>
        public static void GenerateXeddsaCompatibleIdentity(out byte[] seed, out byte[] publicKey)
        {
            do
                GenerateEd25519KeyPair(out seed, out publicKey);
            while ((publicKey[31] & 0x80) != 0);
        }

        /// <summary>
        /// Converts a Curve25519 public key to its Ed25519 form with sign bit 0,
        /// y = (u - 1) / (u + 1).
        /// </summary>
        public static byte[] X25519PublicToEd25519(byte[] curvePublic)
        {
            if (curvePublic == null || curvePublic.Length != KeySize)
                throw new CryptographicException("Invalid Curve25519 public key.");

            var uBytes = new byte[KeySize + 1];
            Buffer.BlockCopy(curvePublic, 0, uBytes, 0, KeySize);
            uBytes[31] &= 0x7F;
            BigInteger u = Mod(new BigInteger(uBytes));
            BigInteger denominator = Mod(u + 1);
            if (denominator.IsZero)
                throw new CryptographicException("Invalid Curve25519 public key.");
            BigInteger y = Mod((u - 1) * BigInteger.ModPow(denominator, fieldPrime - 2, fieldPrime));

            byte[] yBytes = y.ToByteArray();
            var result = new byte[KeySize];
            Buffer.BlockCopy(yBytes, 0, result, 0, Math.Min(yBytes.Length, KeySize));
            result[31] &= 0x7F;
            return result;
        }

        /// <summary>
        /// Verifies a signature made with a Curve25519 identity key, as libsignal does: the Edwards
        /// sign bit is carried in the top bit of the signature. Plain XEdDSA signatures (sign bit 0)
        /// verify as well.
        /// </summary>
        public static bool Curve25519Verify(byte[] curvePublic, byte[] message, byte[] signature)
        {
            if (signature == null || signature.Length != SignatureSize)
                return false;
            byte[] edPublic;
            try
            {
                edPublic = X25519PublicToEd25519(curvePublic);
            }
            catch (CryptographicException)
            {
                return false;
            }

            byte[] sig = (byte[])signature.Clone();
            edPublic[31] |= (byte)(sig[63] & 0x80);
            sig[63] &= 0x7F;
            return Ed25519Verify(edPublic, message, sig);
        }

        public static byte[] HmacSha256(byte[] key, byte[] data)
        {
            using (var hmac = new HMACSHA256(key))
                return hmac.ComputeHash(data);
        }

        public static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int length)
        {
            ikm.ThrowIfNull("ikm");
            salt.ThrowIfNull("salt");
            info.ThrowIfNull("info");

            byte[] prk = HmacSha256(salt, ikm);
            using (var hmacExpand = new HMACSHA256(prk))
            using (var ms = new MemoryStream())
            {
                byte[] t = new byte[0];
                byte counter = 1;
                while (ms.Length < length)
                {
                    byte[] input = new byte[t.Length + info.Length + 1];
                    Buffer.BlockCopy(t, 0, input, 0, t.Length);
                    Buffer.BlockCopy(info, 0, input, t.Length, info.Length);
                    input[input.Length - 1] = counter++;

                    t = hmacExpand.ComputeHash(input);
                    ms.Write(t, 0, t.Length);
                }

                return Slice(ms.ToArray(), 0, length);
            }
        }

        public static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plaintext)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var encryptor = aes.CreateEncryptor())
                    return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }
        }

        public static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] ciphertext)
        {
            using (var aes = Aes.Create())
            {
                aes.Key = key;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var decryptor = aes.CreateDecryptor())
                    return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }
        }

        public static byte[] Slice(byte[] src, int offset, int length)
        {
            byte[] result = new byte[length];
            Buffer.BlockCopy(src, offset, result, 0, length);
            return result;
        }

        public static byte[] Concat(params byte[][] parts)
        {
            int len = 0;
            foreach (var p in parts)
                len += p?.Length ?? 0;

            var result = new byte[len];
            int offset = 0;
            foreach (var p in parts)
            {
                if (p == null || p.Length == 0)
                    continue;
                Buffer.BlockCopy(p, 0, result, offset, p.Length);
                offset += p.Length;
            }
            return result;
        }

        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= (a[i] ^ b[i]);
            return diff == 0;
        }

        private static BigInteger Mod(BigInteger value)
        {
            BigInteger r = value % fieldPrime;
            return r.Sign < 0 ? r + fieldPrime : r;
        }
    }
}
