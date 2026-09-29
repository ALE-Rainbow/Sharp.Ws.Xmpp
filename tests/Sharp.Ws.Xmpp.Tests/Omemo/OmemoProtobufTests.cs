using Sharp.Xmpp.Extensions;

namespace Sharp.Ws.Xmpp.Tests.Omemo;

public class OmemoProtobufTests
{
    [Fact]
    public void OmemoMessage_UsesProto2WireFormat()
    {
        var message = new OmemoMessageProto { N = 1, PN = 300, DhPub = new byte[] { 0xAA }, Ciphertext = new byte[] { 0xBB, 0xCC } };

        byte[] bytes = message.Serialize();

        // field 1 varint 1, field 2 varint 300 (0xAC 0x02), field 3 len 1, field 4 len 2
        Assert.Equal(new byte[] { 0x08, 0x01, 0x10, 0xAC, 0x02, 0x1A, 0x01, 0xAA, 0x22, 0x02, 0xBB, 0xCC }, bytes);
    }

    [Fact]
    public void OmemoMessage_WithoutCiphertext_OmitsField4()
    {
        var message = new OmemoMessageProto { N = 0, PN = 0, DhPub = new byte[] { 1 } };
        Assert.Equal(new byte[] { 0x08, 0x00, 0x10, 0x00, 0x1A, 0x01, 0x01 }, message.Serialize());
    }

    [Fact]
    public void KeyExchange_RoundTrips()
    {
        var kex = new OmemoKeyExchangeProto
        {
            PkId = 42,
            SpkId = int.MaxValue,
            Ik = Enumerable.Repeat((byte)1, 32).ToArray(),
            Ek = Enumerable.Repeat((byte)2, 32).ToArray(),
            Message = new OmemoAuthenticatedMessageProto { Mac = new byte[16], Message = new OmemoMessageProto { N = 5, PN = 6, DhPub = new byte[32] }.Serialize() }
        };

        var parsed = OmemoKeyExchangeProto.Parse(kex.Serialize());

        Assert.Equal(42u, parsed.PkId);
        Assert.Equal((uint)int.MaxValue, parsed.SpkId);
        Assert.Equal(kex.Ik, parsed.Ik);
        Assert.Equal(kex.Ek, parsed.Ek);
        Assert.Equal(5u, OmemoMessageProto.Parse(parsed.Message.Message).N);
    }

    [Fact]
    public void Parse_RejectsTruncatedOrIncompleteData()
    {
        Assert.Throws<FormatException>(() => OmemoMessageProto.Parse(new byte[] { 0x08 }));
        Assert.Throws<FormatException>(() => OmemoMessageProto.Parse(new byte[] { 0x08, 0x01 }));
        Assert.Throws<FormatException>(() => OmemoAuthenticatedMessageProto.Parse(new byte[] { 0x0A, 0x05, 0x01 }));
    }

    [Fact]
    public void Parse_SkipsUnknownFields()
    {
        byte[] bytes = new byte[] { 0x08, 0x01, 0x10, 0x02, 0x1A, 0x01, 0x07, 0x28, 0x09 };
        var parsed = OmemoMessageProto.Parse(bytes);
        Assert.Equal(1u, parsed.N);
        Assert.Equal(2u, parsed.PN);
    }
}
