using System.IO;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// A Double Ratchet session with one remote device plus the X3DH data OMEMO keeps with it.
    /// </summary>
    internal sealed class OmemoSession
    {
        private const byte SerializationVersion = 1;

        public OmemoDoubleRatchet Ratchet { get; set; }

        /// <summary>Remote IdentityKey in Ed25519 form.</summary>
        public byte[] RemoteIdentityKey { get; set; }

        /// <summary>The X3DH ephemeral public key of this session (§4.3 ratchet initialization).</summary>
        public byte[] EphemeralKey { get; set; }

        /// <summary>True if this device initiated the key exchange.</summary>
        public bool Initiator { get; set; }

        /// <summary>
        /// While the key exchange is unconfirmed, the X3DH header data sent with every message (§4.3).
        /// </summary>
        public uint PendingPreKeyId { get; set; }
        public uint PendingSignedPreKeyId { get; set; }
        public bool KeyExchangePending { get; set; }

        /// <summary>For a passively built session, the PreKey whose private key can be deleted once confirmed.</summary>
        public int ConsumedPreKeyId { get; set; }

        /// <summary>The remote ratchet key a heartbeat was last sent for (§6).</summary>
        public byte[] HeartbeatRatchetKey { get; set; }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(SerializationVersion);
                WriteBytes(bw, Ratchet.Serialize());
                WriteBytes(bw, RemoteIdentityKey);
                WriteBytes(bw, EphemeralKey);
                bw.Write(Initiator);
                bw.Write(PendingPreKeyId);
                bw.Write(PendingSignedPreKeyId);
                bw.Write(KeyExchangePending);
                bw.Write(ConsumedPreKeyId);
                WriteBytes(bw, HeartbeatRatchetKey);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static OmemoSession Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                if (br.ReadByte() != SerializationVersion)
                    throw new InvalidDataException("Unsupported OMEMO session version.");
                return new OmemoSession
                {
                    Ratchet = OmemoDoubleRatchet.Deserialize(ReadBytes(br)),
                    RemoteIdentityKey = ReadBytes(br),
                    EphemeralKey = ReadBytes(br),
                    Initiator = br.ReadBoolean(),
                    PendingPreKeyId = br.ReadUInt32(),
                    PendingSignedPreKeyId = br.ReadUInt32(),
                    KeyExchangePending = br.ReadBoolean(),
                    ConsumedPreKeyId = br.ReadInt32(),
                    HeartbeatRatchetKey = ReadBytes(br)
                };
            }
        }

        internal static void WriteBytes(BinaryWriter bw, byte[] value)
        {
            if (value == null)
            {
                bw.Write(-1);
                return;
            }
            bw.Write(value.Length);
            bw.Write(value);
        }

        internal static byte[] ReadBytes(BinaryReader br)
        {
            int len = br.ReadInt32();
            return len < 0 ? null : br.ReadBytes(len);
        }
    }
}
