using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Wire format of legacy OMEMO (eu.siacs.conversations.axolotl, XEP-0384 v0.3) as used by
    /// Conversations.
    /// </summary>
    internal static class OmemoLegacyXml
    {
        public static XmlElement BuildDevicesElement(IEnumerable<int> deviceIds)
        {
            var list = Xml.Element("list", OmemoConstants.LegacyNamespace);
            foreach (int id in deviceIds)
                list.Child(Xml.Element("device", OmemoConstants.LegacyNamespace).Attr("id", id.ToString(CultureInfo.InvariantCulture)));
            return list;
        }

        public static IEnumerable<OmemoDevice> ParseDevices(XmlElement item)
        {
            if (item == null)
                return new OmemoDevice[0];
            XmlElement list = item.LocalName == "list" ? item : item.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "list");
            if (list == null)
                return new OmemoDevice[0];

            var result = new List<OmemoDevice>();
            foreach (var e in list.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "device"))
            {
                if (Int32.TryParse(e.GetAttribute("id"), NumberStyles.None, CultureInfo.InvariantCulture, out int id) && id > 0 && result.All(d => d.Id != id))
                    result.Add(new OmemoDevice { Id = id });
            }
            return result;
        }

        /// <summary>Builds a legacy bundle; keys are serialized with the 0x05 type byte.</summary>
        public static XmlElement BuildBundleElement(OmemoBundle bundle)
        {
            var e = Xml.Element("bundle", OmemoConstants.LegacyNamespace)
                .Child(Xml.Element("signedPreKeyPublic", OmemoConstants.LegacyNamespace)
                    .Attr("signedPreKeyId", bundle.SignedPreKeyId.ToString(CultureInfo.InvariantCulture))
                    .Text(Convert.ToBase64String(OmemoLegacySession.Encode(bundle.SignedPreKeyPublic))))
                .Child(Xml.Element("signedPreKeySignature", OmemoConstants.LegacyNamespace)
                    .Text(Convert.ToBase64String(bundle.SignedPreKeySignature)))
                .Child(Xml.Element("identityKey", OmemoConstants.LegacyNamespace)
                    .Text(Convert.ToBase64String(OmemoLegacySession.Encode(bundle.IdentityKeyPublic))));

            var prekeys = Xml.Element("prekeys", OmemoConstants.LegacyNamespace);
            foreach (var pk in bundle.PreKeys)
            {
                prekeys.Child(Xml.Element("preKeyPublic", OmemoConstants.LegacyNamespace)
                    .Attr("preKeyId", pk.Key.ToString(CultureInfo.InvariantCulture))
                    .Text(Convert.ToBase64String(OmemoLegacySession.Encode(pk.Value))));
            }
            return e.Child(prekeys);
        }

        /// <summary>Parses a legacy bundle into 32-byte Curve25519 keys, or null if invalid.</summary>
        public static OmemoBundle ParseBundle(XmlElement item)
        {
            if (item == null)
                return null;
            XmlElement bundle = item.LocalName == "bundle" ? item : item.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == "bundle");
            if (bundle == null)
                return null;

            XmlElement Child(XmlElement parent, string name) => parent.ChildNodes.OfType<XmlElement>().FirstOrDefault(e => e.LocalName == name);

            var spk = Child(bundle, "signedPreKeyPublic");
            var spks = Child(bundle, "signedPreKeySignature");
            var ik = Child(bundle, "identityKey");
            if (spk == null || spks == null || ik == null)
                return null;

            try
            {
                Int32.TryParse(spk.GetAttribute("signedPreKeyId"), NumberStyles.None, CultureInfo.InvariantCulture, out int spkId);
                var result = new OmemoBundle
                {
                    SignedPreKeyId = spkId,
                    SignedPreKeyPublic = OmemoLegacySession.Decode(OmemoXml.FromBase64(spk.InnerText)),
                    SignedPreKeySignature = OmemoXml.FromBase64(spks.InnerText),
                    IdentityKeyPublic = OmemoLegacySession.Decode(OmemoXml.FromBase64(ik.InnerText))
                };

                var prekeys = Child(bundle, "prekeys");
                foreach (var pk in prekeys?.ChildNodes.OfType<XmlElement>().Where(e => e.LocalName == "preKeyPublic") ?? Enumerable.Empty<XmlElement>())
                {
                    if (!Int32.TryParse(pk.GetAttribute("preKeyId"), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
                        continue;
                    try
                    {
                        result.PreKeys[id] = OmemoLegacySession.Decode(OmemoXml.FromBase64(pk.InnerText));
                    }
                    catch (CryptographicException)
                    {
                    }
                }
                return result;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }

        /// <summary>
        /// Builds &lt;encrypted xmlns='eu.siacs.conversations.axolotl'&gt; with keys addressed by rid
        /// only; a null payload makes it a key transport (empty) message.
        /// </summary>
        public static XmlElement BuildEncryptedElement(int senderDeviceId, IEnumerable<OmemoHeaderKey> keys, byte[] iv, byte[] payload)
        {
            var encrypted = Xml.Element("encrypted", OmemoConstants.LegacyNamespace);
            var header = Xml.Element("header", OmemoConstants.LegacyNamespace)
                .Attr("sid", senderDeviceId.ToString(CultureInfo.InvariantCulture));
            foreach (var k in keys)
            {
                var key = Xml.Element("key", OmemoConstants.LegacyNamespace)
                    .Attr("rid", k.RecipientDeviceId.ToString(CultureInfo.InvariantCulture));
                if (k.IsKeyExchange)
                    key.Attr("prekey", "true");
                header.Child(key.Text(Convert.ToBase64String(k.Ciphertext)));
            }
            header.Child(Xml.Element("iv", OmemoConstants.LegacyNamespace).Text(Convert.ToBase64String(iv)));
            encrypted.Child(header);
            if (payload != null)
                encrypted.Child(Xml.Element("payload", OmemoConstants.LegacyNamespace).Text(Convert.ToBase64String(payload)));
            return encrypted;
        }
    }

    /// <summary>
    /// Legacy OMEMO payload encryption: AES-128-GCM with a 12-byte IV. The 16-byte auth tag
    /// travels with the key (key || tag) inside the Signal message, as Conversations does.
    /// </summary>
    internal static class OmemoLegacyPayloadCipher
    {
        public static byte[] Encrypt(byte[] plaintext, out byte[] keyAndTag, out byte[] iv)
        {
            byte[] key = OmemoPrimitives.RandomBytes(16);
            iv = OmemoPrimitives.RandomBytes(12);
            byte[] output = Process(true, key, iv, plaintext);

            int ctLen = output.Length - 16;
            keyAndTag = OmemoPrimitives.Concat(key, OmemoPrimitives.Slice(output, ctLen, 16));
            return OmemoPrimitives.Slice(output, 0, ctLen);
        }

        /// <exception cref="CryptographicException">Authentication failed.</exception>
        public static byte[] Decrypt(byte[] keyMaterial, byte[] iv, byte[] ciphertext)
        {
            if (keyMaterial == null || keyMaterial.Length < 32 || iv == null || iv.Length == 0 || ciphertext == null)
                throw new CryptographicException("Invalid legacy OMEMO key material.");
            byte[] key = OmemoPrimitives.Slice(keyMaterial, 0, 16);
            byte[] tag = OmemoPrimitives.Slice(keyMaterial, 16, keyMaterial.Length - 16);
            return Process(false, key, iv, OmemoPrimitives.Concat(ciphertext, tag));
        }

        private static byte[] Process(bool encrypt, byte[] key, byte[] iv, byte[] input)
        {
            var gcm = new GcmBlockCipher(new AesEngine());
            gcm.Init(encrypt, new AeadParameters(new KeyParameter(key), 128, iv));
            var output = new byte[gcm.GetOutputSize(input.Length)];
            try
            {
                int len = gcm.ProcessBytes(input, 0, input.Length, output, 0);
                len += gcm.DoFinal(output, len);
                return len == output.Length ? output : OmemoPrimitives.Slice(output, 0, len);
            }
            catch (InvalidCipherTextException ex)
            {
                throw new CryptographicException("Legacy OMEMO payload authentication failed.", ex);
            }
        }
    }
}
