using Sharp.Xmpp.Extensions;
using System.Security.Cryptography;

namespace Sharp.Ws.Xmpp.Tests.Omemo;

public class OmemoLegacySessionTests
{
    private const string AliceJid = "alice@example.org";
    private const string BobJid = "bob@example.org";

    private readonly OmemoSessionManager alice = new(new InMemoryOmemoStore(), AliceJid);
    private readonly OmemoSessionManager bob = new(new InMemoryOmemoStore(), BobJid);

    private static byte[] Key(byte seed) => Enumerable.Range(0, 32).Select(i => (byte)(i * 3 + seed)).ToArray();

    private (byte[] data, bool preKey) AliceSends(byte[] pt)
    {
        var data = alice.EncryptLegacy(BobJid, bob.DeviceId, pt, out bool preKey);
        return (data, preKey);
    }

    private OmemoDecryptResult BobGets((byte[] data, bool preKey) m) => bob.DecryptLegacy(AliceJid, alice.DeviceId, m.preKey, m.data);

    private (byte[] data, bool preKey) BobSends(byte[] pt)
    {
        var data = bob.EncryptLegacy(AliceJid, alice.DeviceId, pt, out bool preKey);
        return (data, preKey);
    }

    private OmemoDecryptResult AliceGets((byte[] data, bool preKey) m) => alice.DecryptLegacy(BobJid, bob.DeviceId, m.preKey, m.data);

    [Fact]
    public void Identity_IsUsableWithLibsignalSignatures()
    {
        var bundle = bob.GetLegacyBundle();
        Assert.True(OmemoPrimitives.Curve25519Verify(bundle.IdentityKeyPublic, OmemoLegacySession.Encode(bundle.SignedPreKeyPublic), bundle.SignedPreKeySignature));
        Assert.Equal(0, bob.IdentityKey[31] & 0x80);
        Assert.Equal(bundle.IdentityKeyPublic, OmemoPrimitives.Ed25519PublicToX25519(bob.IdentityKey));
    }

    [Fact]
    public void Curve25519Verify_AcceptsSignBitCarriedInSignature()
    {
        // A libsignal signer whose Edwards key has the sign bit set stores it in signature[63].
        OmemoPrimitives.GenerateEd25519KeyPair(out byte[] seed, out byte[] edPublic);
        while ((edPublic[31] & 0x80) == 0)
            OmemoPrimitives.GenerateEd25519KeyPair(out seed, out edPublic);
        byte[] message = { 9, 8, 7 };
        byte[] sig = OmemoPrimitives.Ed25519Sign(seed, message);
        sig[63] |= 0x80;

        Assert.True(OmemoPrimitives.Curve25519Verify(OmemoPrimitives.Ed25519PublicToX25519(edPublic), message, sig));
        sig[0] ^= 1;
        Assert.False(OmemoPrimitives.Curve25519Verify(OmemoPrimitives.Ed25519PublicToX25519(edPublic), message, sig));
    }

    [Fact]
    public void X25519PublicToEd25519_InvertsTheBirationalMap()
    {
        OmemoPrimitives.GenerateXeddsaCompatibleIdentity(out _, out byte[] edPublic);
        Assert.Equal(edPublic, OmemoPrimitives.X25519PublicToEd25519(OmemoPrimitives.Ed25519PublicToX25519(edPublic)));
    }

    [Fact]
    public void PreKeyMessage_UsesLibsignalFormat()
    {
        alice.BuildLegacySession(BobJid, bob.DeviceId, bob.GetLegacyBundle());
        var m = AliceSends(Key(1));

        Assert.True(m.preKey);
        Assert.Equal(0x33, m.data[0]);
        var parsed = OmemoLegacySession.PreKeySignalMessage.Parse(m.data);
        Assert.NotNull(parsed.PreKeyId);
        Assert.Equal((uint)bob.GetLegacyBundle().SignedPreKeyId, parsed.SignedPreKeyId);
        Assert.Equal(alice.LegacyIdentityKey, parsed.IdentityKey);
        Assert.Equal(0x33, parsed.Message[0]);
    }

    [Fact]
    public void Conversation_RoundTripsInBothDirections()
    {
        alice.BuildLegacySession(BobJid, bob.DeviceId, bob.GetLegacyBundle());

        var first = BobGets(AliceSends(Key(1)));
        Assert.Equal(Key(1), first.Plaintext);
        Assert.True(first.NewSession);
        Assert.True(first.BundleChanged);

        var second = AliceSends(Key(2));
        Assert.True(second.preKey); // unacknowledged until Bob answers
        Assert.Equal(Key(2), BobGets(second).Plaintext);

        var reply = BobSends(Key(3));
        Assert.False(reply.preKey);
        Assert.Equal(Key(3), AliceGets(reply).Plaintext);

        var third = AliceSends(Key(4));
        Assert.False(third.preKey);
        Assert.Equal(Key(4), BobGets(third).Plaintext);

        for (byte i = 10; i < 20; i++)
        {
            Assert.Equal(Key(i), BobGets(AliceSends(Key(i))).Plaintext);
            Assert.Equal(Key((byte)(i + 50)), AliceGets(BobSends(Key((byte)(i + 50)))).Plaintext);
        }
    }

    [Fact]
    public void OutOfOrderAcrossChains_Decrypts()
    {
        alice.BuildLegacySession(BobJid, bob.DeviceId, bob.GetLegacyBundle());
        BobGets(AliceSends(Key(1)));
        AliceGets(BobSends(Key(2)));

        var a1 = AliceSends(Key(3));
        var a2 = AliceSends(Key(4));
        AliceGets(BobSends(Key(5))); // Bob hasn't seen a1/a2 yet, but Alice ratchets
        var a3 = AliceSends(Key(6));

        Assert.Equal(Key(6), BobGets(a3).Plaintext);
        Assert.Equal(Key(4), BobGets(a2).Plaintext);
        Assert.Equal(Key(3), BobGets(a1).Plaintext);
    }

    [Fact]
    public void Duplicate_IsReported()
    {
        alice.BuildLegacySession(BobJid, bob.DeviceId, bob.GetLegacyBundle());
        BobGets(AliceSends(Key(1)));
        AliceGets(BobSends(Key(2)));
        var m = AliceSends(Key(3));
        BobGets(m);

        Assert.True(BobGets(m).Duplicate);
    }

    [Fact]
    public void RepeatedPreKeyMessage_ReusesSession()
    {
        alice.BuildLegacySession(BobJid, bob.DeviceId, bob.GetLegacyBundle());
        var m1 = AliceSends(Key(1));
        var m2 = AliceSends(Key(2));

        Assert.True(BobGets(m1).NewSession);
        var r2 = BobGets(m2);
        Assert.False(r2.NewSession);
        Assert.True(r2.ViaKeyExchange);
        Assert.Equal(Key(2), r2.Plaintext);
    }

    [Fact]
    public void TamperedMac_IsRejected()
    {
        alice.BuildLegacySession(BobJid, bob.DeviceId, bob.GetLegacyBundle());
        BobGets(AliceSends(Key(1)));
        AliceGets(BobSends(Key(2)));
        var m = AliceSends(Key(3));
        m.data[^1] ^= 1;

        Assert.Throws<CryptographicException>(() => BobGets(m));
    }

    [Fact]
    public void BuildLegacySession_RejectsBadSignature()
    {
        var bundle = bob.GetLegacyBundle();
        bundle.SignedPreKeySignature[5] ^= 1;
        Assert.Throws<CryptographicException>(() => alice.BuildLegacySession(BobJid, bob.DeviceId, bundle));
    }

    [Fact]
    public void LegacyAndOmemo2Bundles_ShareTheSamePreKeys()
    {
        var v2 = bob.GetBundle();
        var legacy = bob.GetLegacyBundle();
        Assert.Equal(v2.SignedPreKeyId, legacy.SignedPreKeyId);
        Assert.Equal(v2.PreKeys.Keys.OrderBy(k => k), legacy.PreKeys.Keys.OrderBy(k => k));
    }
}
