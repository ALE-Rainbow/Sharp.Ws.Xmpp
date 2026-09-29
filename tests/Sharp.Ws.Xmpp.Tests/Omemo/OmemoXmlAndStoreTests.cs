using Sharp.Xmpp;
using Sharp.Xmpp.Extensions;
using System.Text;
using System.Xml;

namespace Sharp.Ws.Xmpp.Tests.Omemo;

public class OmemoXmlAndStoreTests
{
    private static XmlElement Parse(string xml)
    {
        var doc = new XmlDocument();
        doc.LoadXml(xml);
        return doc.DocumentElement!;
    }

    [Fact]
    public void ParseDevices_ReadsSpecExample()
    {
        var item = Parse("<item id='current' xmlns='http://jabber.org/protocol/pubsub#event'><devices xmlns='urn:xmpp:omemo:2'>" +
            "<device id='12345' /><device id='4223' label='Gajim on Ubuntu Linux' labelsig='YQ==' /><device id='0'/><device id='x'/></devices></item>");

        var devices = OmemoXml.ParseDevices(item).ToList();

        Assert.Equal(new[] { 12345, 4223 }, devices.Select(d => d.Id));
        Assert.Equal("Gajim on Ubuntu Linux", devices[1].Label);
    }

    [Fact]
    public void Bundle_RoundTripsThroughXml()
    {
        var manager = new OmemoSessionManager(new InMemoryOmemoStore(), "a@b.c");
        var bundle = manager.GetBundle();

        var element = OmemoXml.BuildBundleElement(bundle);
        var parsed = OmemoXml.ParseBundle(Parse(element.ToXmlString()))!;

        Assert.Equal("bundle", element.LocalName);
        Assert.Equal(OmemoConstants.OmemoNamespace, element.NamespaceURI);
        Assert.Equal(bundle.SignedPreKeyId, parsed.SignedPreKeyId);
        Assert.Equal(bundle.IdentityKeyPublic, parsed.IdentityKeyPublic);
        Assert.Equal(bundle.SignedPreKeySignature, parsed.SignedPreKeySignature);
        Assert.Equal(bundle.PreKeys.Count, parsed.PreKeys.Count);
    }

    [Fact]
    public void DevicesElement_OmitsUnsignedLabels()
    {
        var element = OmemoXml.BuildDevicesElement(new[]
        {
            new OmemoDevice { Id = 1, Label = "unsigned" },
            new OmemoDevice { Id = 2, Label = "signed", LabelSignature = "c2ln" }
        });
        string xml = element.OuterXml;

        Assert.DoesNotContain("unsigned", xml);
        var devices = element.ChildNodes.OfType<XmlElement>().ToList();
        Assert.Equal("", devices[0].GetAttribute("label"));
        Assert.Equal("signed", devices[1].GetAttribute("label"));
        Assert.Equal("c2ln", devices[1].GetAttribute("labelsig"));
    }

    [Fact]
    public void EncryptedElement_OmitsPayloadForEmptyMessages()
    {
        var keys = new[] { new OmemoHeaderKey { BareJid = "b@c", RecipientDeviceId = 7, IsKeyExchange = true, Ciphertext = new byte[] { 1 } } };

        var empty = OmemoXml.BuildEncryptedElement(5, keys, null);
        var full = OmemoXml.BuildEncryptedElement(5, keys, new byte[] { 2 });

        Assert.Null(empty["payload", OmemoConstants.OmemoNamespace]);
        Assert.NotNull(full["payload", OmemoConstants.OmemoNamespace]);
        var key = empty["header", OmemoConstants.OmemoNamespace]!["keys", OmemoConstants.OmemoNamespace]!["key", OmemoConstants.OmemoNamespace]!;
        Assert.Equal("7", key.GetAttribute("rid"));
        Assert.Equal("true", key.GetAttribute("kex"));
        Assert.Equal("5", empty["header", OmemoConstants.OmemoNamespace]!.GetAttribute("sid"));
    }

    [Fact]
    public void SceEnvelope_HasRandomPaddingAndAffixes()
    {
        var a = OmemoXml.CreateSceEnvelope(new[] { OmemoXml.CreateBody("hi") }, new Jid("me@x.y/r"), new Jid("room@muc.x.y"));
        var b = OmemoXml.CreateSceEnvelope(new[] { OmemoXml.CreateBody("hi") }, new Jid("me@x.y/r"), new Jid("room@muc.x.y"));

        var parsed = OmemoXml.ParseEnvelope(Encoding.UTF8.GetBytes(a.OuterXml));

        Assert.Equal("me@x.y", parsed.From);
        Assert.Equal("room@muc.x.y", parsed.To);
        Assert.Equal("hi", parsed.Content.Single().InnerText);
        Assert.NotEqual(a["rpad", OmemoConstants.SceNamespace]!.InnerText, b["rpad", OmemoConstants.SceNamespace]!.InnerText);
        Assert.NotNull(a["time", OmemoConstants.SceNamespace]);
    }

    [Fact]
    public void ParseEnvelope_RejectsNonEnvelope()
    {
        Assert.Throws<FormatException>(() => OmemoXml.ParseEnvelope(Encoding.UTF8.GetBytes("<body xmlns='jabber:client'>x</body>")));
    }

    [Fact]
    public void PayloadCipher_RoundTripsAndDetectsTampering()
    {
        var material = OmemoPayloadCipher.Encrypt(Encoding.UTF8.GetBytes("secret"));
        byte[] transport = OmemoPayloadCipher.BuildTransportPlaintext(material);
        OmemoPayloadCipher.ParseTransportPlaintext(transport, out var key, out var mac);

        Assert.Equal("secret", Encoding.UTF8.GetString(OmemoPayloadCipher.Decrypt(material.Ciphertext, key, mac)));
        material.Ciphertext[0] ^= 1;
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => OmemoPayloadCipher.Decrypt(material.Ciphertext, key, mac));
    }

    [Fact]
    public void TrustStore_KeysDecisionsByIdentityKey()
    {
        var trust = new OmemoTrustStore(new InMemoryOmemoStore(), "me@x.y");
        byte[] ik1 = Enumerable.Repeat((byte)1, 32).ToArray();
        byte[] ik2 = Enumerable.Repeat((byte)2, 32).ToArray();

        Assert.Equal(OmemoTrustLevel.Undecided, trust.GetTrust("bob@x.y", ik1));
        trust.SetTrust("bob@x.y", ik1, OmemoTrustLevel.Verified);
        Assert.Equal(OmemoTrustLevel.Verified, trust.GetTrust("bob@x.y", ik1));
        Assert.Equal(OmemoTrustLevel.Undecided, trust.GetTrust("bob@x.y", ik2));
        Assert.Equal(OmemoTrustLevel.Undecided, trust.GetTrust("carol@x.y", ik1));

        trust.RecordIdentityKey(OmemoVersion.Legacy, "bob@x.y", 5, ik2);
        Assert.Equal(ik2, trust.GetIdentityKey(OmemoVersion.Legacy, "bob@x.y", 5));
        Assert.Null(trust.GetIdentityKey(OmemoVersion.Omemo2, "bob@x.y", 5));
    }

    [Fact]
    public void SqliteStore_PersistsValuesAndDevices()
    {
        string path = Path.Combine(Path.GetTempPath(), "omemo-test-" + Guid.NewGuid().ToString("N") + ".db3");
        try
        {
            var store = new SqliteOmemoStore(path);
            store.SetValue("k1", new byte[] { 1, 2 });
            store.SetValue("k2", new byte[] { 3 });
            store.SetValue("k2", null);
            store.SetDevices("bob@x.y", new[] { new OmemoDevice { Id = 3 } });

            var reopened = new SqliteOmemoStore(path);
            Assert.Equal(new byte[] { 1, 2 }, reopened.GetValue("k1"));
            Assert.Null(reopened.GetValue("k2"));
            Assert.Equal(new[] { "k1" }, reopened.GetKeys("k"));
            Assert.Equal(3, reopened.GetDevices("bob@x.y").Single().Id);

            var manager = new OmemoSessionManager(reopened, "me@x.y");
            var again = new OmemoSessionManager(new SqliteOmemoStore(path), "me@x.y");
            Assert.Equal(manager.DeviceId, again.DeviceId);
            Assert.Equal(manager.IdentityKey, again.IdentityKey);
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { }
        }
    }
}
