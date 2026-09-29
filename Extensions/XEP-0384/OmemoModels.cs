using System.Collections.Generic;

namespace Sharp.Xmpp.Extensions
{
    internal sealed class OmemoDevice
    {
        public int Id { get; set; }
        public string Label { get; set; }
        public string LabelSignature { get; set; }
    }

    internal sealed class OmemoBundle
    {
        public int SignedPreKeyId { get; set; }
        public byte[] SignedPreKeyPublic { get; set; }
        public byte[] SignedPreKeySignature { get; set; }
        public byte[] IdentityKeyPublic { get; set; }
        public IDictionary<int, byte[]> PreKeys { get; set; } = new Dictionary<int, byte[]>();
    }

    internal sealed class OmemoHeaderKey
    {
        public string BareJid { get; set; }
        public int RecipientDeviceId { get; set; }
        public bool IsKeyExchange { get; set; }
        public byte[] Ciphertext { get; set; }
    }

    internal sealed class OmemoPayloadMaterial
    {
        public byte[] PayloadKey { get; set; }   // 32 bytes
        public byte[] PayloadMac { get; set; }   // 16 bytes (truncated HMAC)
        public byte[] Ciphertext { get; set; }
    }
}