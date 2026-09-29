using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sharp.Xmpp.Extensions
{
    internal static class OmemoPayloadCipher
    {
        public static OmemoPayloadMaterial Encrypt(byte[] plaintext)
        {
            plaintext.ThrowIfNull("plaintext");

            byte[] payloadKey = OmemoPrimitives.RandomBytes(32);

            byte[] okm = HkdfSha256(payloadKey, new byte[32], Encoding.UTF8.GetBytes(OmemoConstants.PayloadKdfInfo), 80);
            byte[] encKey = Slice(okm, 0, 32);
            byte[] authKey = Slice(okm, 32, 32);
            byte[] iv = Slice(okm, 64, 16);

            byte[] ciphertext;
            using (var aes = Aes.Create())
            {
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var encryptor = aes.CreateEncryptor())
                    ciphertext = encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
            }

            byte[] mac;
            using (var hmac = new HMACSHA256(authKey))
            {
                byte[] full = hmac.ComputeHash(ciphertext);
                mac = Slice(full, 0, 16);
            }

            return new OmemoPayloadMaterial
            {
                PayloadKey = payloadKey,
                PayloadMac = mac,
                Ciphertext = ciphertext
            };
        }

        public static byte[] Decrypt(byte[] ciphertext, byte[] payloadKey, byte[] payloadMac)
        {
            ciphertext.ThrowIfNull("ciphertext");
            payloadKey.ThrowIfNull("payloadKey");
            payloadMac.ThrowIfNull("payloadMac");

            byte[] okm = HkdfSha256(payloadKey, new byte[32], Encoding.UTF8.GetBytes(OmemoConstants.PayloadKdfInfo), 80);
            byte[] encKey = Slice(okm, 0, 32);
            byte[] authKey = Slice(okm, 32, 32);
            byte[] iv = Slice(okm, 64, 16);

            using (var hmac = new HMACSHA256(authKey))
            {
                byte[] full = hmac.ComputeHash(ciphertext);
                if (!FixedTimeEquals(Slice(full, 0, 16), payloadMac))
                    throw new CryptographicException("OMEMO payload MAC validation failed.");
            }

            using (var aes = Aes.Create())
            {
                aes.Key = encKey;
                aes.IV = iv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using (var decryptor = aes.CreateDecryptor())
                    return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
            }
        }

        public static byte[] BuildTransportPlaintext(OmemoPayloadMaterial material)
        {
            material.ThrowIfNull("material");
            byte[] plain = new byte[48];
            Buffer.BlockCopy(material.PayloadKey, 0, plain, 0, 32);
            Buffer.BlockCopy(material.PayloadMac, 0, plain, 32, 16);
            return plain;
        }

        public static void ParseTransportPlaintext(byte[] transportPlaintext, out byte[] payloadKey, out byte[] payloadMac)
        {
            transportPlaintext.ThrowIfNull("transportPlaintext");
            if (transportPlaintext.Length != 48)
                throw new CryptographicException("Invalid OMEMO key material length.");

            payloadKey = Slice(transportPlaintext, 0, 32);
            payloadMac = Slice(transportPlaintext, 32, 16);
        }

        private static byte[] HkdfSha256(byte[] ikm, byte[] salt, byte[] info, int length) => OmemoPrimitives.HkdfSha256(ikm, salt, info, length);

        private static byte[] Slice(byte[] src, int offset, int length) => OmemoPrimitives.Slice(src, offset, length);

        private static bool FixedTimeEquals(byte[] a, byte[] b) => OmemoPrimitives.FixedTimeEquals(a, b);
    }
}
