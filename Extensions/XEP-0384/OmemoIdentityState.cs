using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Persistent private key material of the local OMEMO device.
    /// </summary>
    internal sealed class OmemoIdentityState
    {
        private const byte SerializationVersion = 2;

        public int DeviceId { get; set; }

        /// <summary>Ed25519 seed of the IdentityKey.</summary>
        public byte[] IdentitySeed { get; set; }

        /// <summary>Ed25519 public IdentityKey.</summary>
        public byte[] IdentityPublic { get; set; }

        /// <summary>Signed PreKeys, newest last. Older ones are kept for one rotation period (§4.2).</summary>
        public List<SignedPreKey> SignedPreKeys { get; set; } = new List<SignedPreKey>();

        /// <summary>PreKeys currently published in the bundle.</summary>
        public Dictionary<int, PreKey> PreKeys { get; set; } = new Dictionary<int, PreKey>();

        /// <summary>
        /// PreKeys removed from the bundle whose private keys are kept until the session built
        /// from them is confirmed (§6, message catch-up).
        /// </summary>
        public List<PreKey> RetiredPreKeys { get; set; } = new List<PreKey>();

        /// <summary>libsignal registration id sent in legacy PreKey messages (1..16380).</summary>
        public int RegistrationId { get; set; }

        /// <summary>True once the device id was checked against the account's device list (§6).</summary>
        public bool DeviceIdAnnounced { get; set; }

        public SignedPreKey CurrentSignedPreKey => SignedPreKeys.LastOrDefault();

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(SerializationVersion);
                bw.Write(DeviceId);
                OmemoSession.WriteBytes(bw, IdentitySeed);
                OmemoSession.WriteBytes(bw, IdentityPublic);
                bw.Write(DeviceIdAnnounced);
                bw.Write(RegistrationId);

                bw.Write(SignedPreKeys.Count);
                foreach (var spk in SignedPreKeys)
                {
                    bw.Write(spk.Id);
                    OmemoSession.WriteBytes(bw, spk.PrivateKey);
                    OmemoSession.WriteBytes(bw, spk.PublicKey);
                    OmemoSession.WriteBytes(bw, spk.Signature);
                    bw.Write(spk.CreatedUtc.Ticks);
                }

                WritePreKeys(bw, PreKeys.Values);
                WritePreKeys(bw, RetiredPreKeys);
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static OmemoIdentityState Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                byte version = br.ReadByte();
                if (version != 1 && version != SerializationVersion)
                    throw new InvalidDataException("Unsupported OMEMO identity state version.");

                var state = new OmemoIdentityState
                {
                    DeviceId = br.ReadInt32(),
                    IdentitySeed = OmemoSession.ReadBytes(br),
                    IdentityPublic = OmemoSession.ReadBytes(br),
                    DeviceIdAnnounced = br.ReadBoolean()
                };
                if (version >= 2)
                    state.RegistrationId = br.ReadInt32();

                int spkCount = br.ReadInt32();
                for (int i = 0; i < spkCount; i++)
                {
                    state.SignedPreKeys.Add(new SignedPreKey
                    {
                        Id = br.ReadInt32(),
                        PrivateKey = OmemoSession.ReadBytes(br),
                        PublicKey = OmemoSession.ReadBytes(br),
                        Signature = OmemoSession.ReadBytes(br),
                        CreatedUtc = new DateTime(br.ReadInt64(), DateTimeKind.Utc)
                    });
                }

                foreach (var pk in ReadPreKeys(br))
                    state.PreKeys[pk.Id] = pk;
                state.RetiredPreKeys.AddRange(ReadPreKeys(br));
                return state;
            }
        }

        private static void WritePreKeys(BinaryWriter bw, ICollection<PreKey> keys)
        {
            bw.Write(keys.Count);
            foreach (var pk in keys)
            {
                bw.Write(pk.Id);
                OmemoSession.WriteBytes(bw, pk.PrivateKey);
                OmemoSession.WriteBytes(bw, pk.PublicKey);
            }
        }

        private static IEnumerable<PreKey> ReadPreKeys(BinaryReader br)
        {
            int count = br.ReadInt32();
            var result = new List<PreKey>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(new PreKey
                {
                    Id = br.ReadInt32(),
                    PrivateKey = OmemoSession.ReadBytes(br),
                    PublicKey = OmemoSession.ReadBytes(br)
                });
            }
            return result;
        }

        internal sealed class SignedPreKey
        {
            public int Id { get; set; }
            public byte[] PrivateKey { get; set; }
            public byte[] PublicKey { get; set; }
            public byte[] Signature { get; set; }
            public DateTime CreatedUtc { get; set; }
        }

        internal sealed class PreKey
        {
            public int Id { get; set; }
            public byte[] PrivateKey { get; set; }
            public byte[] PublicKey { get; set; }
        }
    }
}
