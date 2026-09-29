using Sharp.Xmpp.Extensions;

namespace Sharp.Ws.Xmpp.Tests.Omemo;

public class OmemoPrimitivesTests
{
    private static byte[] Hex(string hex) => Convert.FromHexString(hex);

    [Fact]
    public void X25519Agreement_MatchesRfc7748Vector()
    {
        // RFC 7748 §6.1
        byte[] alicePrivate = Hex("77076d0a7318a57d3c16c17251b26645df4c2f87ebc0992ab177fba51db92c2a");
        byte[] bobPublic = Hex("de9edb7d7b7dc1b4d35b61c2ece435373f8343c85b78674dadfc7e146f882b4f");

        byte[] shared = OmemoPrimitives.X25519Agreement(alicePrivate, bobPublic);

        Assert.Equal("4a5d9d5ba4ce2de1728e3bf480350f25e07e21c947d19e3376f09b3c1e161742", Convert.ToHexString(shared).ToLowerInvariant());
    }

    [Fact]
    public void Ed25519PublicToX25519_MatchesKeyDerivedFromSeed()
    {
        for (int i = 0; i < 20; i++)
        {
            OmemoPrimitives.GenerateEd25519KeyPair(out byte[] seed, out byte[] edPublic);

            byte[] fromPublic = OmemoPrimitives.Ed25519PublicToX25519(edPublic);
            byte[] fromPrivate = OmemoPrimitives.X25519PublicFromPrivate(OmemoPrimitives.Ed25519SeedToX25519Private(seed));

            Assert.Equal(fromPrivate, fromPublic);
        }
    }

    [Fact]
    public void IdentityKey_CanBeUsedForX25519AgreementFromBothSides()
    {
        OmemoPrimitives.GenerateEd25519KeyPair(out byte[] seed, out byte[] edPublic);
        OmemoPrimitives.GenerateX25519KeyPair(out byte[] otherPrivate, out byte[] otherPublic);

        byte[] a = OmemoPrimitives.X25519Agreement(OmemoPrimitives.Ed25519SeedToX25519Private(seed), otherPublic);
        byte[] b = OmemoPrimitives.X25519Agreement(otherPrivate, OmemoPrimitives.Ed25519PublicToX25519(edPublic));

        Assert.Equal(a, b);
    }

    [Fact]
    public void Ed25519Sign_VerifiesAndRejectsTampering()
    {
        OmemoPrimitives.GenerateEd25519KeyPair(out byte[] seed, out byte[] pub);
        byte[] message = { 1, 2, 3 };
        byte[] sig = OmemoPrimitives.Ed25519Sign(seed, message);

        Assert.True(OmemoPrimitives.Ed25519Verify(pub, message, sig));
        Assert.False(OmemoPrimitives.Ed25519Verify(pub, new byte[] { 1, 2, 4 }, sig));
        Assert.False(OmemoPrimitives.Ed25519Verify(pub, message, null));
    }

    [Fact]
    public void HkdfSha256_MatchesRfc5869Case1()
    {
        byte[] okm = OmemoPrimitives.HkdfSha256(
            Hex("0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b0b"),
            Hex("000102030405060708090a0b0c"),
            Hex("f0f1f2f3f4f5f6f7f8f9"),
            42);

        Assert.Equal("3cb25f25faacd57a90434f64d0362f2a2d2d0a90cf1a5a4c5db02d56ecc4c5bf34007208d5b887185865",
            Convert.ToHexString(okm).ToLowerInvariant());
    }

    [Fact]
    public void RandomPositiveInt32_IsInSpecRange()
    {
        for (int i = 0; i < 1000; i++)
        {
            int v = OmemoPrimitives.RandomPositiveInt32();
            Assert.InRange(v, 1, int.MaxValue);
        }
    }
}
