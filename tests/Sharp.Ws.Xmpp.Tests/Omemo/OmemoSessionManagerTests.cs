using Sharp.Xmpp.Extensions;
using System.Security.Cryptography;

namespace Sharp.Ws.Xmpp.Tests.Omemo;

public class OmemoSessionManagerTests
{
    private const string AliceJid = "alice@example.org";
    private const string BobJid = "bob@example.org";

    private readonly InMemoryOmemoStore aliceStore = new();
    private readonly InMemoryOmemoStore bobStore = new();
    private readonly OmemoSessionManager alice;
    private readonly OmemoSessionManager bob;

    public OmemoSessionManagerTests()
    {
        alice = new OmemoSessionManager(aliceStore, AliceJid);
        bob = new OmemoSessionManager(bobStore, BobJid);
    }

    private static byte[] KeyMaterial(byte seed) => Enumerable.Range(0, 48).Select(i => (byte)(i + seed)).ToArray();

    private (byte[] data, bool kex) AliceToBob(byte[] plaintext)
    {
        byte[] data = alice.Encrypt(BobJid, bob.DeviceId, plaintext, out bool kex);
        return (data, kex);
    }

    private OmemoDecryptResult BobReceives((byte[] data, bool kex) m) => bob.Decrypt(AliceJid, alice.DeviceId, m.kex, m.data);

    private void Establish()
    {
        alice.BuildSession(BobJid, bob.DeviceId, bob.GetBundle());
        BobReceives(AliceToBob(KeyMaterial(0)));
        byte[] reply = bob.Encrypt(AliceJid, alice.DeviceId, new byte[32], out bool kex);
        Assert.False(kex);
        alice.Decrypt(BobJid, bob.DeviceId, kex, reply);
    }

    [Fact]
    public void NewIdentity_HasBundleWithSpecSizes()
    {
        var bundle = alice.GetBundle();

        Assert.Equal(OmemoSessionManager.PreKeyTarget, bundle.PreKeys.Count);
        Assert.All(bundle.PreKeys, pk => { Assert.InRange(pk.Key, 1, int.MaxValue); Assert.Equal(32, pk.Value.Length); });
        Assert.Equal(32, bundle.IdentityKeyPublic.Length);
        Assert.Equal(32, bundle.SignedPreKeyPublic.Length);
        Assert.True(OmemoPrimitives.Ed25519Verify(bundle.IdentityKeyPublic, bundle.SignedPreKeyPublic, bundle.SignedPreKeySignature));
        Assert.InRange(alice.DeviceId, 1, int.MaxValue);
    }

    [Fact]
    public void KeyExchange_DecryptsAndConsumesPreKey()
    {
        alice.BuildSession(BobJid, bob.DeviceId, bob.GetBundle());
        var first = AliceToBob(KeyMaterial(1));
        Assert.True(first.kex);

        var result = BobReceives(first);

        Assert.Equal(KeyMaterial(1), result.Plaintext);
        Assert.True(result.NewSession);
        Assert.True(result.ViaKeyExchange);
        Assert.True(result.BundleChanged);
        Assert.Equal(alice.IdentityKey, result.RemoteIdentityKey);
        var usedPk = OmemoKeyExchangeProto.Parse(first.data).PkId;
        Assert.DoesNotContain((int)usedPk, bob.GetBundle().PreKeys.Keys);
        Assert.Equal(OmemoSessionManager.PreKeyTarget, bob.GetBundle().PreKeys.Count);
    }

    [Fact]
    public void KeyExchange_IsRepeatedUntilConfirmed()
    {
        alice.BuildSession(BobJid, bob.DeviceId, bob.GetBundle());
        var first = AliceToBob(KeyMaterial(1));
        var second = AliceToBob(KeyMaterial(2));
        Assert.True(first.kex);
        Assert.True(second.kex);

        Assert.Equal(KeyMaterial(1), BobReceives(first).Plaintext);
        var r2 = BobReceives(second);
        Assert.Equal(KeyMaterial(2), r2.Plaintext);
        Assert.False(r2.NewSession);

        // Bob's (empty) reply confirms the session.
        byte[] reply = bob.Encrypt(AliceJid, alice.DeviceId, new byte[32], out bool replyKex);
        Assert.False(replyKex);
        Assert.Equal(new byte[32], alice.Decrypt(BobJid, bob.DeviceId, false, reply).Plaintext);

        var third = AliceToBob(KeyMaterial(3));
        Assert.False(third.kex);
        Assert.Equal(KeyMaterial(3), BobReceives(third).Plaintext);
    }

    [Fact]
    public void LostFirstKeyExchange_StillWorksThroughRepeatedHeader()
    {
        alice.BuildSession(BobJid, bob.DeviceId, bob.GetBundle());
        AliceToBob(KeyMaterial(1)); // lost
        var second = AliceToBob(KeyMaterial(2));

        var r = BobReceives(second);
        Assert.Equal(KeyMaterial(2), r.Plaintext);
        Assert.True(r.NewSession);
    }

    [Fact]
    public void OutOfOrderMessages_AreDecryptedWithSkippedKeys()
    {
        Establish();
        var m1 = AliceToBob(KeyMaterial(1));
        var m2 = AliceToBob(KeyMaterial(2));
        var m3 = AliceToBob(KeyMaterial(3));

        Assert.Equal(KeyMaterial(3), BobReceives(m3).Plaintext);
        Assert.Equal(KeyMaterial(1), BobReceives(m1).Plaintext);
        Assert.Equal(KeyMaterial(2), BobReceives(m2).Plaintext);
    }

    [Fact]
    public void DuplicateMessage_IsReportedAsDuplicate()
    {
        Establish();
        var m1 = AliceToBob(KeyMaterial(1));
        BobReceives(m1);

        var again = BobReceives(m1);

        Assert.True(again.Duplicate);
        Assert.Null(again.Plaintext);
    }

    [Fact]
    public void PingPong_RatchetsInBothDirections()
    {
        Establish();
        for (byte i = 0; i < 10; i++)
        {
            Assert.Equal(KeyMaterial(i), BobReceives(AliceToBob(KeyMaterial(i))).Plaintext);
            byte[] back = bob.Encrypt(AliceJid, alice.DeviceId, KeyMaterial((byte)(i + 100)), out bool kex);
            Assert.Equal(KeyMaterial((byte)(i + 100)), alice.Decrypt(BobJid, bob.DeviceId, kex, back).Plaintext);
        }
    }

    [Fact]
    public void TamperedMessage_FailsAuthentication()
    {
        Establish();
        var m = AliceToBob(KeyMaterial(1));
        var auth = OmemoAuthenticatedMessageProto.Parse(m.data);
        auth.Mac[0] ^= 0xFF;

        Assert.Throws<CryptographicException>(() => bob.Decrypt(AliceJid, alice.DeviceId, false, auth.Serialize()));
        // The session is unchanged and still decrypts the genuine message.
        Assert.Equal(KeyMaterial(1), BobReceives(m).Plaintext);
    }

    [Fact]
    public void KeyExchangeWithUnknownPreKey_IsRejected()
    {
        alice.BuildSession(BobJid, bob.DeviceId, bob.GetBundle());
        var m = AliceToBob(KeyMaterial(1));
        var kex = OmemoKeyExchangeProto.Parse(m.data);
        kex.PkId = 0;

        Assert.Throws<CryptographicException>(() => bob.Decrypt(AliceJid, alice.DeviceId, true, kex.Serialize()));
    }

    [Fact]
    public void BuildSession_RejectsBadSignedPreKeySignature()
    {
        var bundle = bob.GetBundle();
        bundle.SignedPreKeySignature[0] ^= 0x01;

        Assert.Throws<CryptographicException>(() => alice.BuildSession(BobJid, bob.DeviceId, bundle));
    }

    [Fact]
    public void BuildSession_RejectsBundleWithoutPreKeys()
    {
        var bundle = bob.GetBundle();
        bundle.PreKeys.Clear();

        Assert.Throws<CryptographicException>(() => alice.BuildSession(BobJid, bob.DeviceId, bundle));
    }

    [Fact]
    public void AuthenticatedMessageWithoutSession_ReportsNoSession()
    {
        var r = bob.Decrypt(AliceJid, 12345, false, new OmemoAuthenticatedMessageProto { Mac = new byte[16], Message = new OmemoMessageProto { DhPub = new byte[32] }.Serialize() }.Serialize());
        Assert.True(r.NoSession);
    }

    [Fact]
    public void Heartbeat_IsRequestedAtCounter53()
    {
        Establish();
        OmemoDecryptResult last = null!;
        for (int i = 0; i <= OmemoSessionManager.HeartbeatThreshold; i++)
        {
            last = BobReceives(AliceToBob(KeyMaterial((byte)i)));
            if (i < OmemoSessionManager.HeartbeatThreshold)
                Assert.False(last.HeartbeatRequired);
        }
        Assert.True(last.HeartbeatRequired);

        bob.MarkHeartbeatSent(AliceJid, alice.DeviceId, last.RatchetKey);
        Assert.False(BobReceives(AliceToBob(KeyMaterial(1))).HeartbeatRequired);
    }

    [Fact]
    public void Sessions_SurviveReloadFromStore()
    {
        Establish();
        var reloadedBob = new OmemoSessionManager(bobStore, BobJid);

        Assert.Equal(bob.DeviceId, reloadedBob.DeviceId);
        Assert.Equal(bob.IdentityKey, reloadedBob.IdentityKey);
        var m = AliceToBob(KeyMaterial(7));
        Assert.Equal(KeyMaterial(7), reloadedBob.Decrypt(AliceJid, alice.DeviceId, m.kex, m.data).Plaintext);
    }

    [Fact]
    public void NewKeyExchange_ReplacesExistingSession()
    {
        Establish();
        alice.DeleteSession(BobJid, bob.DeviceId);
        alice.BuildSession(BobJid, bob.DeviceId, bob.GetBundle());

        var r = BobReceives(AliceToBob(KeyMaterial(9)));

        Assert.True(r.NewSession);
        Assert.True(r.ReplacedSession);
        Assert.Equal(KeyMaterial(9), r.Plaintext);
    }

    [Fact]
    public void MaintainKeys_RotatesSignedPreKeyAndKeepsPrevious()
    {
        int oldSpk = bob.GetBundle().SignedPreKeyId;
        alice.BuildSession(BobJid, bob.DeviceId, bob.GetBundle());
        var delayed = AliceToBob(KeyMaterial(4));

        Assert.False(bob.MaintainKeys(DateTime.UtcNow));
        Assert.True(bob.MaintainKeys(DateTime.UtcNow + OmemoSessionManager.SignedPreKeyRotation));
        Assert.NotEqual(oldSpk, bob.GetBundle().SignedPreKeyId);

        // A key exchange using the previous signed PreKey still works.
        Assert.Equal(KeyMaterial(4), BobReceives(delayed).Plaintext);
    }

    [Fact]
    public void Fingerprint_IsCurve25519FormHex()
    {
        string fp = OmemoSessionManager.Fingerprint(alice.IdentityKey);
        Assert.Equal(64, fp.Length);
        Assert.Equal(Convert.ToHexString(OmemoPrimitives.Ed25519PublicToX25519(alice.IdentityKey)).ToLowerInvariant(), fp);
        Assert.Equal(8, OmemoDeviceInfo.FormatFingerprint(fp).Split(' ').Length);
    }
}
