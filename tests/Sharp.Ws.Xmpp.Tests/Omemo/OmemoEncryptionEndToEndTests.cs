using Sharp.Xmpp;
using Sharp.Xmpp.Extensions;
using System.Xml;

namespace Sharp.Ws.Xmpp.Tests.Omemo;

public class OmemoEncryptionEndToEndTests
{
    private const string Ns = "urn:xmpp:omemo:2";

    private readonly FakeXmppNetwork network = new();
    private readonly FakeClient alice;
    private readonly FakeClient bob;

    public OmemoEncryptionEndToEndTests()
    {
        alice = network.AddClient("alice@example.org/phone");
        bob = network.AddClient("bob@example.org/laptop");
        alice.Omemo.PublishOwnDeviceAndBundle();
        bob.Omemo.PublishOwnDeviceAndBundle();
        alice.Omemo.Enabled = true;
        bob.Omemo.Enabled = true;
    }

    private static void TrustAll(FakeClient who, string jid)
    {
        foreach (var d in who.Omemo.GetDevices(new Jid(jid), true).Where(d => !d.IsLocalDevice))
            who.Omemo.SetTrust(new Jid(jid), d.DeviceId, OmemoTrustLevel.Verified);
    }

    private static XmlElement LastSentEncrypted(FakeClient who)
    {
        var doc = new XmlDocument();
        doc.LoadXml(who.Sent.Last());
        return doc.DocumentElement!["encrypted", Ns]!;
    }

    [Fact]
    public void Publish_AnnouncesDeviceAndBundleWithRequiredNodeOptions()
    {
        var devices = network.GetItems("alice@example.org", OmemoConstants.DevicesNode);
        var bundles = network.GetItems("alice@example.org", OmemoConstants.BundlesNode);

        Assert.Equal("current", devices.Single().GetAttribute("id"));
        Assert.Equal(alice.Omemo.LocalDeviceId.ToString(), bundles.Single().GetAttribute("id"));
        var ids = OmemoXml.ParseDevices(devices.Single()).Select(d => d.Id);
        Assert.Contains(alice.Omemo.LocalDeviceId, ids);

        string options = string.Join("", network.PublishOptions.Select(o => o.OuterXml));
        Assert.Contains("pubsub#access_model", options);
        Assert.Contains("open", options);
        Assert.Contains("pubsub#max_items", options);
    }

    [Fact]
    public void Publish_KeepsOtherDevicesOnTheList()
    {
        var second = network.AddClient("alice@example.org/desktop");
        second.Omemo.PublishOwnDeviceAndBundle();

        var ids = OmemoXml.ParseDevices(network.GetItems("alice@example.org", OmemoConstants.DevicesNode).Single()).Select(d => d.Id).ToList();

        Assert.Contains(alice.Omemo.LocalDeviceId, ids);
        Assert.Contains(second.Omemo.LocalDeviceId, ids);
    }

    [Fact]
    public void Send_ToUntrustedDevice_IsBlocked()
    {
        var ex = Assert.Throws<OmemoSendException>(() => alice.Send("bob@example.org", "hello"));

        Assert.Equal(OmemoSendFailure.UntrustedDevices, ex.Failure);
        Assert.Equal(bob.Omemo.LocalDeviceId, ex.UndecidedDevices.Single().DeviceId);
        Assert.Equal(bob.Omemo.LocalFingerprint, ex.UndecidedDevices.Single().Fingerprint);
        Assert.Empty(alice.Sent);
    }

    [Fact]
    public void Send_ToContactWithoutOmemo_IsBlocked()
    {
        var ex = Assert.Throws<OmemoSendException>(() => alice.Send("carol@example.org", "hello"));
        Assert.Equal(OmemoSendFailure.NoDevices, ex.Failure);
        Assert.Empty(alice.Sent);
    }

    [Fact]
    public void Conversation_EncryptsDecryptsAndCompletesKeyExchange()
    {
        TrustAll(alice, "bob@example.org");
        alice.Send("bob@example.org", "hello <bob> & friends");
        network.WaitIdle();

        // On the wire: no plaintext, a key exchange for Bob, a fallback body.
        string wire = alice.Sent.First();
        Assert.DoesNotContain("hello", wire);
        Assert.Contains(OmemoEncryption.FallbackBody, wire);
        var key = LastSentEncrypted(alice)["header", Ns]!["keys", Ns]!["key", Ns]!;
        Assert.Equal("true", key.GetAttribute("kex"));

        var received = bob.Inbox.Single();
        Assert.Equal("hello <bob> & friends", received.Body);
        var info = OmemoMessageInfo.FromMessage(received)!;
        Assert.Equal(alice.Omemo.LocalDeviceId, info.SenderDeviceId);
        Assert.Equal(OmemoTrustLevel.Undecided, info.Trust);
        Assert.Equal(alice.Omemo.LocalFingerprint, info.Fingerprint);

        // Bob answered the key exchange with an empty OMEMO message (§6).
        var empty = bob.Sent.Single();
        Assert.DoesNotContain("<payload", empty);
        Assert.Contains("<header", empty);

        // So Alice's next message is no longer a key exchange.
        alice.Send("bob@example.org", "second");
        network.WaitIdle();
        Assert.Equal("", LastSentEncrypted(alice)["header", Ns]!["keys", Ns]!["key", Ns]!.GetAttribute("kex"));
        Assert.Equal("second", bob.Inbox.Last().Body);

        // Bob replies once he trusts Alice.
        TrustAll(bob, "alice@example.org");
        bob.Send("alice@example.org", "hi alice");
        network.WaitIdle();
        Assert.Equal("hi alice", alice.Inbox.Single().Body);
        Assert.Equal(OmemoTrustLevel.Verified, OmemoMessageInfo.FromMessage(alice.Inbox.Single())!.Trust);
    }

    [Fact]
    public void KeyExchange_RepublishesBundleWithoutUsedPreKey()
    {
        TrustAll(alice, "bob@example.org");
        alice.Send("bob@example.org", "hello");
        network.WaitIdle();

        var kexBytes = Convert.FromBase64String(LastSentEncrypted(alice)["header", Ns]!["keys", Ns]!["key", Ns]!.InnerText);
        uint usedPk = OmemoKeyExchangeProto.Parse(kexBytes).PkId;
        var bundle = OmemoXml.ParseBundle(network.GetItems("bob@example.org", OmemoConstants.BundlesNode).Single())!;

        Assert.DoesNotContain((int)usedPk, bundle.PreKeys.Keys);
        Assert.Equal(100, bundle.PreKeys.Count);
    }

    [Fact]
    public void OwnOtherDevices_ReceiveCopiesAsCarbons()
    {
        var aliceDesktop = network.AddClient("alice@example.org/desktop");
        aliceDesktop.Omemo.PublishOwnDeviceAndBundle();
        TrustAll(alice, "bob@example.org");
        TrustAll(alice, "alice@example.org");

        alice.Send("bob@example.org", "copy me");
        network.WaitIdle();

        var jids = LastSentEncrypted(alice)["header", Ns]!.ChildNodes.OfType<XmlElement>().Select(k => k.GetAttribute("jid")).ToList();
        Assert.Contains("alice@example.org", jids);
        Assert.Equal("copy me", aliceDesktop.Inbox.Single().Body);
    }

    [Fact]
    public void DuplicateDelivery_IsIgnoredSilently()
    {
        TrustAll(alice, "bob@example.org");
        alice.Send("bob@example.org", "once");
        network.WaitIdle();

        var doc = new XmlDocument();
        doc.LoadXml(alice.Sent.First());
        doc.DocumentElement!.SetAttribute("from", alice.Jid.ToString());
        bob.Deliver(doc.DocumentElement);

        Assert.Single(bob.Inbox);
    }

    [Fact]
    public void MessageForAnotherDevice_ShowsWarning()
    {
        var bobPhone = network.AddClient("bob@example.org/phone");
        TrustAll(alice, "bob@example.org");
        alice.Send("bob@example.org", "only laptop");
        network.WaitIdle();

        var warning = bobPhone.Inbox.Single();
        Assert.Equal(OmemoEncryption.WarningNotForThisDevice, warning.Body);
        Assert.Equal(OmemoReceiveFailure.NotEncryptedForThisDevice, OmemoMessageInfo.FromMessage(warning)!.Failure);
    }

    [Fact]
    public void OptOut_BlocksSendingUntilPlaintextIsConfirmed()
    {
        TrustAll(alice, "bob@example.org");
        TrustAll(bob, "alice@example.org");
        OmemoOptOutEventArgs? optOut = null;
        alice.Omemo.OptOutReceived += (s, e) => optOut = e;

        bob.Omemo.SendOptOut(new Jid("alice@example.org"), "compliance");
        network.WaitIdle();

        Assert.NotNull(optOut);
        Assert.Equal("compliance", optOut!.Reason);
        var ex = Assert.Throws<OmemoSendException>(() => alice.Send("bob@example.org", "still there?"));
        Assert.Equal(OmemoSendFailure.OptedOut, ex.Failure);

        alice.Omemo.ConfirmPlaintext(new Jid("bob@example.org"));
        alice.Send("bob@example.org", "plain now");
        Assert.Contains("plain now", alice.Sent.Last());
    }

    [Fact]
    public void ResetSessions_BuildsNewSessionThePeerAccepts()
    {
        TrustAll(alice, "bob@example.org");
        TrustAll(bob, "alice@example.org");
        alice.Send("bob@example.org", "before");
        network.WaitIdle();

        alice.Omemo.ResetSessions(new Jid("bob@example.org"));
        network.WaitIdle();
        alice.Send("bob@example.org", "after");
        network.WaitIdle();

        Assert.Equal(new[] { "before", "after" }, bob.Inbox.Select(m => m.Body));
    }

    [Fact]
    public void DistrustedDevice_IsNeverEncryptedFor()
    {
        foreach (var d in alice.Omemo.GetDevices(new Jid("bob@example.org"), true))
            alice.Omemo.SetTrust(new Jid("bob@example.org"), d.DeviceId, OmemoTrustLevel.Distrusted);

        var ex = Assert.Throws<OmemoSendException>(() => alice.Send("bob@example.org", "x"));
        Assert.Equal(OmemoSendFailure.SessionFailure, ex.Failure);
    }

    [Fact]
    public void Unpublish_RemovesDeviceAndBundle()
    {
        alice.Omemo.UnpublishOwnDevice();

        var ids = OmemoXml.ParseDevices(network.GetItems("alice@example.org", OmemoConstants.DevicesNode).Single()).Select(d => d.Id);
        Assert.DoesNotContain(alice.Omemo.LocalDeviceId, ids);
        Assert.Empty(network.GetItems("alice@example.org", OmemoConstants.BundlesNode));
        Assert.False(alice.Omemo.Enabled);
    }

    [Fact]
    public void Devices_ReportSignedLabels()
    {
        var labelled = network.AddClient("dave@example.org/tablet");
        labelled.Omemo.DeviceLabel = "Dave's tablet";
        labelled.Omemo.PublishOwnDeviceAndBundle();

        var device = alice.Omemo.GetDevices(new Jid("dave@example.org"), true).Single(d => d.Version == OmemoVersion.Omemo2);

        Assert.Equal("Dave's tablet", device.Label);
        Assert.Equal(labelled.Omemo.LocalFingerprint, device.Fingerprint);
    }

    [Fact]
    public void GroupChat_EncryptsForEveryMemberWithToAffix()
    {
        var carol = network.AddClient("carol@example.org/tablet");
        carol.Omemo.PublishOwnDeviceAndBundle();
        network.JoinRoom("room@muc.example.org", (alice, "alice"), (bob, "bob"), (carol, "carol"));
        TrustAll(alice, "bob@example.org");
        TrustAll(alice, "carol@example.org");

        alice.SendMessage(new Sharp.Xmpp.Im.Message(new Jid("room@muc.example.org"), "hi all", type: Sharp.Xmpp.Im.MessageType.Groupchat));
        network.WaitIdle();

        var jids = LastSentEncrypted(alice)["header", Ns]!.ChildNodes.OfType<XmlElement>().Select(k => k.GetAttribute("jid")).ToList();
        Assert.Contains("bob@example.org", jids);
        Assert.Contains("carol@example.org", jids);
        Assert.Equal("hi all", bob.Inbox.Single(m => m.Type == Sharp.Xmpp.Im.MessageType.Groupchat).Body);
        Assert.Equal("hi all", carol.Inbox.Single(m => m.Type == Sharp.Xmpp.Im.MessageType.Groupchat).Body);
        // The sender's own reflected copy is not shown as a failure.
        Assert.DoesNotContain(alice.Inbox, m => m.Type == Sharp.Xmpp.Im.MessageType.Groupchat);
    }

    [Fact]
    public void GroupChat_WithUnknownMembers_IsBlocked()
    {
        var ex = Assert.Throws<OmemoSendException>(() =>
            alice.SendMessage(new Sharp.Xmpp.Im.Message(new Jid("anon@muc.example.org"), "x", type: Sharp.Xmpp.Im.MessageType.Groupchat)));
        Assert.Equal(OmemoSendFailure.GroupChatNotSupported, ex.Failure);
    }

    /// <summary>Makes a client look like Conversations: only on the legacy device list.</summary>
    private void MakeLegacyOnly(FakeClient client, params int[] keepOnV2)
    {
        string bare = client.Jid.GetBareJid().ToString();
        string devices = string.Join("", keepOnV2.Select(id => $"<device id='{id}'/>"));
        network.SetItems(bare, OmemoConstants.DevicesNode, $"<item id='current'><devices xmlns='urn:xmpp:omemo:2'>{devices}</devices></item>");
    }

    private static XmlElement? LastSentElement(FakeClient who, string ns)
    {
        var doc = new XmlDocument();
        doc.LoadXml(who.Sent.Last());
        return doc.DocumentElement!["encrypted", ns];
    }

    [Fact]
    public void Publish_AnnouncesLegacyDeviceListAndBundle()
    {
        var list = OmemoLegacyXml.ParseDevices(network.GetItems("alice@example.org", OmemoConstants.LegacyDevicesNode).Single());
        var bundleItem = network.GetItems("alice@example.org", OmemoConstants.LegacyBundlesNodePrefix + alice.Omemo.LocalDeviceId).Single();
        var bundle = OmemoLegacyXml.ParseBundle(bundleItem)!;

        Assert.Contains(alice.Omemo.LocalDeviceId, list.Select(d => d.Id));
        Assert.Equal("current", bundleItem.GetAttribute("id"));
        Assert.Equal(100, bundle.PreKeys.Count);
        Assert.True(OmemoPrimitives.Curve25519Verify(bundle.IdentityKeyPublic, OmemoLegacySession.Encode(bundle.SignedPreKeyPublic), bundle.SignedPreKeySignature));
        // The same identity: legacy and OMEMO 2 fingerprints match.
        Assert.Equal(alice.Omemo.LocalFingerprint, OmemoSessionManager.CurveFingerprint(bundle.IdentityKeyPublic));
    }

    [Fact]
    public void LegacyOnlyContact_GetsLegacyMessages()
    {
        MakeLegacyOnly(bob);
        TrustAll(alice, "bob@example.org");

        alice.Send("bob@example.org", "hello conversations");
        network.WaitIdle();

        Assert.Null(LastSentElement(alice, Ns));
        var legacy = LastSentElement(alice, OmemoConstants.LegacyNamespace)!;
        var header = legacy["header", OmemoConstants.LegacyNamespace]!;
        Assert.Equal("true", header["key", OmemoConstants.LegacyNamespace]!.GetAttribute("prekey"));
        Assert.NotNull(header["iv", OmemoConstants.LegacyNamespace]);
        Assert.DoesNotContain("hello", alice.Sent.First());

        var received = bob.Inbox.Single();
        Assert.Equal("hello conversations", received.Body);
        Assert.Equal(OmemoVersion.Legacy, OmemoMessageInfo.FromMessage(received)!.Version);

        // Bob's key transport reply acknowledged the session.
        alice.Send("bob@example.org", "second");
        network.WaitIdle();
        Assert.Equal("", LastSentElement(alice, OmemoConstants.LegacyNamespace)!["header", OmemoConstants.LegacyNamespace]!["key", OmemoConstants.LegacyNamespace]!.GetAttribute("prekey"));
        Assert.Equal("second", bob.Inbox.Last().Body);

        TrustAll(bob, "alice@example.org");
        MakeLegacyOnly(alice);
        bob.Send("alice@example.org", "reply from legacy");
        network.WaitIdle();
        Assert.Equal("reply from legacy", alice.Inbox.Single().Body);
    }

    [Fact]
    public void MixedDevices_GetBothVersionsInOneMessage()
    {
        var bobPhone = network.AddClient("bob@example.org/phone");
        bobPhone.Omemo.PublishOwnDeviceAndBundle();
        MakeLegacyOnly(bobPhone, bob.Omemo.LocalDeviceId); // the phone is legacy-only, the laptop speaks both
        TrustAll(alice, "bob@example.org");

        alice.Send("bob@example.org", "to every device");
        network.WaitIdle();

        var v2 = LastSentElement(alice, Ns)!;
        var legacy = LastSentElement(alice, OmemoConstants.LegacyNamespace)!;
        Assert.Contains(v2["header", Ns]!.ChildNodes.OfType<XmlElement>().SelectMany(k => k.ChildNodes.OfType<XmlElement>()),
            k => k.GetAttribute("rid") == bob.Omemo.LocalDeviceId.ToString());
        Assert.Contains(legacy["header", OmemoConstants.LegacyNamespace]!.ChildNodes.OfType<XmlElement>(),
            k => k.GetAttribute("rid") == bobPhone.Omemo.LocalDeviceId.ToString());

        Assert.Equal("to every device", bob.Inbox.Single().Body);
        Assert.Equal(OmemoVersion.Omemo2, OmemoMessageInfo.FromMessage(bob.Inbox.Single())!.Version);
        Assert.Equal("to every device", bobPhone.Inbox.Single().Body);
        Assert.Equal(OmemoVersion.Legacy, OmemoMessageInfo.FromMessage(bobPhone.Inbox.Single())!.Version);
    }

    [Fact]
    public void Trust_IsSharedAcrossVersions()
    {
        var devices = alice.Omemo.GetDevices(new Jid("bob@example.org"), true).Where(d => !d.IsLocalDevice).ToList();
        Assert.Equal(new[] { OmemoVersion.Omemo2, OmemoVersion.Legacy }, devices.Select(d => d.Version));
        Assert.Single(devices.Select(d => d.Fingerprint).Distinct());

        alice.Omemo.SetTrust(new Jid("bob@example.org"), devices[0].Fingerprint, OmemoTrustLevel.Verified);

        Assert.All(alice.Omemo.GetDevices(new Jid("bob@example.org"), false).Where(d => !d.IsLocalDevice),
            d => Assert.Equal(OmemoTrustLevel.Verified, d.Trust));
    }

    [Fact]
    public void LegacyDisabled_SendsOnlyOmemo2()
    {
        alice.Omemo.LegacyEnabled = false;
        MakeLegacyOnly(bob);
        TrustAll(alice, "bob@example.org");

        var ex = Assert.Throws<OmemoSendException>(() => alice.Send("bob@example.org", "x"));
        Assert.Equal(OmemoSendFailure.NoDevices, ex.Failure);
    }
}
