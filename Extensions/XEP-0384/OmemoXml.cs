using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml;

namespace Sharp.Xmpp.Extensions
{
    internal static class OmemoXml
    {
        private const string RpadAlphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";

        public static XmlElement BuildEncryptedElement(int senderDeviceId, IEnumerable<OmemoHeaderKey> keys, byte[] payloadCiphertext)
        {
            var encrypted = Xml.Element("encrypted", OmemoConstants.OmemoNamespace);
            var header = Xml.Element("header", OmemoConstants.OmemoNamespace)
                .Attr("sid", senderDeviceId.ToString(CultureInfo.InvariantCulture));

            foreach (var grp in keys.GroupBy(k => k.BareJid, StringComparer.OrdinalIgnoreCase))
            {
                var keysElement = Xml.Element("keys", OmemoConstants.OmemoNamespace)
                    .Attr("jid", grp.Key);

                foreach (var k in grp)
                {
                    var key = Xml.Element("key", OmemoConstants.OmemoNamespace)
                        .Attr("rid", k.RecipientDeviceId.ToString(CultureInfo.InvariantCulture));
                    if (k.IsKeyExchange)
                        key.Attr("kex", "true");
                    key.Text(Convert.ToBase64String(k.Ciphertext));
                    keysElement.Child(key);
                }

                header.Child(keysElement);
            }

            encrypted.Child(header);

            // Empty OMEMO messages carry no <payload/> (§5.5.3).
            if (payloadCiphertext != null)
            {
                encrypted.Child(Xml.Element("payload", OmemoConstants.OmemoNamespace)
                    .Text(Convert.ToBase64String(payloadCiphertext)));
            }

            return encrypted;
        }

        /// <summary>
        /// Builds the OMEMO SCE profile envelope (§5.5.1): random-length &lt;rpad/&gt;, &lt;time/&gt;,
        /// &lt;from/&gt; and &lt;to/&gt; affixes.
        /// </summary>
        public static XmlElement CreateSceEnvelope(IEnumerable<XmlElement> content, Jid from, Jid to)
        {
            var envelope = Xml.Element("envelope", OmemoConstants.SceNamespace);
            var contentElement = Xml.Element("content", OmemoConstants.SceNamespace);
            foreach (var e in content)
                contentElement.Child(e);
            envelope.Child(contentElement);

            envelope.Child(Xml.Element("rpad", OmemoConstants.SceNamespace).Text(RandomPadding()));
            envelope.Child(Xml.Element("time", OmemoConstants.SceNamespace)
                .Attr("stamp", DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture)));
            if (from != null)
                envelope.Child(Xml.Element("from", OmemoConstants.SceNamespace).Attr("jid", from.GetBareJid().ToString()));
            if (to != null)
                envelope.Child(Xml.Element("to", OmemoConstants.SceNamespace).Attr("jid", to.GetBareJid().ToString()));

            return envelope;
        }

        public static XmlElement CreateBody(string body) => Xml.Element("body", "jabber:client").Text(body ?? string.Empty);

        public static XmlElement CreateOptOut(string reason)
        {
            var optOut = Xml.Element("opt-out", OmemoConstants.OmemoNamespace);
            if (!String.IsNullOrEmpty(reason))
                optOut.Child(Xml.Element("reason", OmemoConstants.OmemoNamespace).Text(reason));
            return optOut;
        }

        public static OmemoEnvelope ParseEnvelope(byte[] envelopeBytes)
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new System.IO.StringReader(Encoding.UTF8.GetString(envelopeBytes)), settings))
                doc.Load(reader);

            var envelope = doc.DocumentElement;
            if (envelope == null || envelope.LocalName != "envelope" || envelope.NamespaceURI != OmemoConstants.SceNamespace)
                throw new FormatException("Not an SCE envelope.");

            var content = envelope["content", OmemoConstants.SceNamespace] ??
                throw new FormatException("SCE envelope has no content.");

            return new OmemoEnvelope
            {
                Content = content.ChildNodes.OfType<XmlElement>().ToList(),
                From = envelope["from", OmemoConstants.SceNamespace]?.GetAttribute("jid"),
                To = envelope["to", OmemoConstants.SceNamespace]?.GetAttribute("jid")
            };
        }

        public static XmlElement BuildDevicesElement(IEnumerable<OmemoDevice> devices)
        {
            var devicesElement = Xml.Element("devices", OmemoConstants.OmemoNamespace);
            foreach (var d in devices)
            {
                var e = Xml.Element("device", OmemoConstants.OmemoNamespace).Attr("id", d.Id.ToString(CultureInfo.InvariantCulture));
                // A label MUST be accompanied by its signature (§5.3.1).
                if (!String.IsNullOrEmpty(d.Label) && !String.IsNullOrEmpty(d.LabelSignature))
                {
                    e.Attr("label", d.Label);
                    e.Attr("labelsig", d.LabelSignature);
                }
                devicesElement.Child(e);
            }
            return devicesElement;
        }

        public static XmlElement BuildBundleElement(OmemoBundle bundle)
        {
            var bundleElement = Xml.Element("bundle", OmemoConstants.OmemoNamespace)
                .Child(Xml.Element("spk", OmemoConstants.OmemoNamespace)
                    .Attr("id", bundle.SignedPreKeyId.ToString(CultureInfo.InvariantCulture))
                    .Text(Convert.ToBase64String(bundle.SignedPreKeyPublic)))
                .Child(Xml.Element("spks", OmemoConstants.OmemoNamespace)
                    .Text(Convert.ToBase64String(bundle.SignedPreKeySignature)))
                .Child(Xml.Element("ik", OmemoConstants.OmemoNamespace)
                    .Text(Convert.ToBase64String(bundle.IdentityKeyPublic)));

            var prekeys = Xml.Element("prekeys", OmemoConstants.OmemoNamespace);
            foreach (var pk in bundle.PreKeys)
            {
                prekeys.Child(Xml.Element("pk", OmemoConstants.OmemoNamespace)
                    .Attr("id", pk.Key.ToString(CultureInfo.InvariantCulture))
                    .Text(Convert.ToBase64String(pk.Value)));
            }
            return bundleElement.Child(prekeys);
        }

        /// <summary>
        /// Builds a data form with FORM_TYPE and the given fields (publish-options or node_config).
        /// </summary>
        public static XmlElement BuildForm(string formType, IDictionary<string, string> fields)
        {
            var x = Xml.Element("x", OmemoConstants.DataFormsNamespace).Attr("type", "submit")
                .Child(Xml.Element("field").Attr("var", "FORM_TYPE").Attr("type", "hidden")
                    .Child(Xml.Element("value").Text(formType)));
            foreach (var f in fields)
                x.Child(Xml.Element("field").Attr("var", f.Key).Child(Xml.Element("value").Text(f.Value)));
            return x;
        }

        public static IEnumerable<OmemoDevice> ParseDevices(XmlElement item)
        {
            if (item == null)
                return new OmemoDevice[0];

            XmlElement devices = item.LocalName == "devices" ? item : item["devices", OmemoConstants.OmemoNamespace];
            if (devices == null || devices.NamespaceURI != OmemoConstants.OmemoNamespace)
                return new OmemoDevice[0];

            var list = new List<OmemoDevice>();
            foreach (XmlElement e in devices.ChildNodes.OfType<XmlElement>())
            {
                if (e.LocalName != "device")
                    continue;
                if (!Int32.TryParse(e.GetAttribute("id"), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
                    continue;
                if (list.Any(d => d.Id == id))
                    continue;

                list.Add(new OmemoDevice
                {
                    Id = id,
                    Label = NullIfEmpty(e.GetAttribute("label")),
                    LabelSignature = NullIfEmpty(e.GetAttribute("labelsig"))
                });
            }
            return list;
        }

        public static OmemoBundle ParseBundle(XmlElement item)
        {
            if (item == null)
                return null;

            XmlElement bundle = item.LocalName == "bundle" ? item : item["bundle", OmemoConstants.OmemoNamespace];
            if (bundle == null || bundle.NamespaceURI != OmemoConstants.OmemoNamespace)
                return null;

            var spk = bundle["spk", OmemoConstants.OmemoNamespace];
            var spks = bundle["spks", OmemoConstants.OmemoNamespace];
            var ik = bundle["ik", OmemoConstants.OmemoNamespace];
            var prekeys = bundle["prekeys", OmemoConstants.OmemoNamespace];
            if (spk == null || spks == null || ik == null)
                return null;

            Int32.TryParse(spk.GetAttribute("id"), NumberStyles.None, CultureInfo.InvariantCulture, out int spkId);
            var result = new OmemoBundle
            {
                SignedPreKeyId = spkId,
                SignedPreKeyPublic = FromBase64(spk.InnerText),
                SignedPreKeySignature = FromBase64(spks.InnerText),
                IdentityKeyPublic = FromBase64(ik.InnerText)
            };

            if (prekeys != null)
            {
                foreach (XmlElement pk in prekeys.ChildNodes.OfType<XmlElement>())
                {
                    if (pk.LocalName != "pk")
                        continue;
                    if (!Int32.TryParse(pk.GetAttribute("id"), NumberStyles.None, CultureInfo.InvariantCulture, out int id) || id <= 0)
                        continue;
                    byte[] value = FromBase64(pk.InnerText);
                    if (value != null)
                        result.PreKeys[id] = value;
                }
            }

            return result.SignedPreKeyPublic == null || result.IdentityKeyPublic == null ? null : result;
        }

        public static byte[] FromBase64(string text)
        {
            try
            {
                return Convert.FromBase64String((text ?? string.Empty).Trim());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static string RandomPadding()
        {
            byte[] random = OmemoPrimitives.RandomBytes(201);
            int length = random[0] % 200 + 1;
            var sb = new StringBuilder(length);
            for (int i = 1; i <= length; i++)
                sb.Append(RpadAlphabet[random[i] % RpadAlphabet.Length]);
            return sb.ToString();
        }

        private static string NullIfEmpty(string s) => String.IsNullOrEmpty(s) ? null : s;
    }

    internal sealed class OmemoEnvelope
    {
        public List<XmlElement> Content { get; set; }
        public string From { get; set; }
        public string To { get; set; }
    }
}
