using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// A libsignal (protocol version 3) session as used by legacy OMEMO
    /// (eu.siacs.conversations.axolotl, XEP-0384 v0.3). Mirrors libsignal-protocol-java's
    /// RatchetingSession, SessionCipher, ChainKey and RootKey so that it interoperates with
    /// Conversations and other libsignal based clients.
    /// </summary>
    internal sealed class OmemoLegacySession
    {
        public const byte MessageVersion = 0x33;
        public const byte DjbType = 0x05;
        private const int MacLength = 8;
        private const int MaxReceiverChains = 5;
        private const int MaxMessageKeys = 2000;
        private const byte SerializationVersion = 1;

        private static readonly byte[] whisperText = Encoding.ASCII.GetBytes("WhisperText");
        private static readonly byte[] whisperRatchet = Encoding.ASCII.GetBytes("WhisperRatchet");
        private static readonly byte[] whisperMessageKeys = Encoding.ASCII.GetBytes("WhisperMessageKeys");

        /// <summary>Remote IdentityKey, Curve25519 (32 bytes, without type byte).</summary>
        public byte[] RemoteIdentityKey { get; private set; }
        public byte[] LocalIdentityKey { get; private set; }

        /// <summary>Alice's base key of this session, used to recognise repeated PreKey messages.</summary>
        public byte[] AliceBaseKey { get; private set; }
        public bool Initiator { get; private set; }

        public bool PreKeyPending { get; private set; }
        public uint? PendingPreKeyId { get; private set; }
        public uint PendingSignedPreKeyId { get; private set; }
        public int ConsumedPreKeyId { get; set; }

        private byte[] rootKey;
        private byte[] senderRatchetPrivate;
        private byte[] senderRatchetPublic;
        private byte[] senderChainKey;
        private uint senderChainIndex;
        private uint previousCounter;
        private List<ReceiverChain> receiverChains = new List<ReceiverChain>();

        private OmemoLegacySession() { }

        public static byte[] Encode(byte[] curvePublic) => OmemoPrimitives.Concat(new[] { DjbType }, curvePublic);

        public static byte[] Decode(byte[] serialized)
        {
            if (serialized != null && serialized.Length == 33 && serialized[0] == DjbType)
                return OmemoPrimitives.Slice(serialized, 1, 32);
            if (serialized != null && serialized.Length == 32)
                return serialized;
            throw new CryptographicException("Invalid Curve25519 public key encoding.");
        }

        /// <summary>RatchetingSession.initializeSession for Alice.</summary>
        public static OmemoLegacySession InitializeActive(byte[] ourIdentityPrivate, byte[] ourIdentityPublic,
            byte[] theirIdentity, byte[] theirSignedPreKey, byte[] theirPreKey, uint? preKeyId, uint signedPreKeyId)
        {
            OmemoPrimitives.GenerateX25519KeyPair(out byte[] basePrivate, out byte[] basePublic);

            byte[] secrets = OmemoPrimitives.Concat(
                Discontinuity(),
                OmemoPrimitives.X25519Agreement(ourIdentityPrivate, theirSignedPreKey),
                OmemoPrimitives.X25519Agreement(basePrivate, theirIdentity),
                OmemoPrimitives.X25519Agreement(basePrivate, theirSignedPreKey),
                theirPreKey == null ? null : OmemoPrimitives.X25519Agreement(basePrivate, theirPreKey));
            DeriveKeys(secrets, out byte[] derivedRoot, out byte[] derivedChain);

            var s = new OmemoLegacySession
            {
                RemoteIdentityKey = theirIdentity,
                LocalIdentityKey = ourIdentityPublic,
                Initiator = true,
                AliceBaseKey = basePublic,
                PreKeyPending = true,
                PendingPreKeyId = preKeyId,
                PendingSignedPreKeyId = signedPreKeyId
            };

            OmemoPrimitives.GenerateX25519KeyPair(out s.senderRatchetPrivate, out s.senderRatchetPublic);
            CreateChain(derivedRoot, theirSignedPreKey, s.senderRatchetPrivate, out s.rootKey, out s.senderChainKey);
            s.receiverChains.Add(new ReceiverChain(theirSignedPreKey, derivedChain, 0));
            return s;
        }

        /// <summary>RatchetingSession.initializeSession for Bob.</summary>
        public static OmemoLegacySession InitializePassive(byte[] ourIdentityPrivate, byte[] ourIdentityPublic,
            byte[] signedPreKeyPrivate, byte[] signedPreKeyPublic, byte[] preKeyPrivate, byte[] theirIdentity, byte[] theirBaseKey)
        {
            byte[] secrets = OmemoPrimitives.Concat(
                Discontinuity(),
                OmemoPrimitives.X25519Agreement(signedPreKeyPrivate, theirIdentity),
                OmemoPrimitives.X25519Agreement(ourIdentityPrivate, theirBaseKey),
                OmemoPrimitives.X25519Agreement(signedPreKeyPrivate, theirBaseKey),
                preKeyPrivate == null ? null : OmemoPrimitives.X25519Agreement(preKeyPrivate, theirBaseKey));
            DeriveKeys(secrets, out byte[] derivedRoot, out byte[] derivedChain);

            return new OmemoLegacySession
            {
                RemoteIdentityKey = theirIdentity,
                LocalIdentityKey = ourIdentityPublic,
                Initiator = false,
                AliceBaseKey = theirBaseKey,
                rootKey = derivedRoot,
                senderRatchetPrivate = signedPreKeyPrivate,
                senderRatchetPublic = signedPreKeyPublic,
                senderChainKey = derivedChain
            };
        }

        /// <summary>
        /// SessionCipher.encrypt: returns a serialized SignalMessage, or a PreKeySignalMessage while
        /// the session is unacknowledged.
        /// </summary>
        public byte[] Encrypt(byte[] plaintext, uint registrationId, out bool isPreKey)
        {
            DeriveMessageKeys(senderChainKey, out byte[] cipherKey, out byte[] macKey, out byte[] iv);
            byte[] ciphertext = OmemoPrimitives.AesCbcEncrypt(cipherKey, iv, plaintext);

            byte[] body;
            using (var ms = new MemoryStream())
            {
                OmemoProtobuf.WriteBytes(ms, 1, Encode(senderRatchetPublic));
                OmemoProtobuf.WriteUInt32(ms, 2, senderChainIndex);
                OmemoProtobuf.WriteUInt32(ms, 3, previousCounter);
                OmemoProtobuf.WriteBytes(ms, 4, ciphertext);
                body = OmemoPrimitives.Concat(new[] { MessageVersion }, ms.ToArray());
            }
            byte[] signalMessage = OmemoPrimitives.Concat(body, Mac(macKey, LocalIdentityKey, RemoteIdentityKey, body));

            senderChainKey = OmemoPrimitives.HmacSha256(senderChainKey, new byte[] { 0x02 });
            senderChainIndex++;

            isPreKey = PreKeyPending;
            if (!isPreKey)
                return signalMessage;

            using (var ms = new MemoryStream())
            {
                if (PendingPreKeyId != null)
                    OmemoProtobuf.WriteUInt32(ms, 1, PendingPreKeyId.Value);
                OmemoProtobuf.WriteBytes(ms, 2, Encode(AliceBaseKey));
                OmemoProtobuf.WriteBytes(ms, 3, Encode(LocalIdentityKey));
                OmemoProtobuf.WriteBytes(ms, 4, signalMessage);
                OmemoProtobuf.WriteUInt32(ms, 5, registrationId);
                OmemoProtobuf.WriteUInt32(ms, 6, PendingSignedPreKeyId);
                return OmemoPrimitives.Concat(new[] { MessageVersion }, ms.ToArray());
            }
        }

        /// <summary>
        /// SessionCipher.decrypt for a SignalMessage. State only changes on success.
        /// </summary>
        /// <exception cref="CryptographicException">Authentication or decryption failed.</exception>
        /// <exception cref="OmemoDuplicateMessageException">The message was already decrypted.</exception>
        public byte[] Decrypt(byte[] signalMessage)
        {
            var parsed = SignalMessage.Parse(signalMessage);
            var working = Clone();
            byte[] plaintext = working.DecryptInPlace(parsed);
            CopyFrom(working);
            return plaintext;
        }

        private byte[] DecryptInPlace(SignalMessage message)
        {
            var chain = GetOrCreateReceiverChain(message.RatchetKey);
            byte[] cipherKey, macKey, iv;

            if (chain.Index > message.Counter)
            {
                var stored = chain.MessageKeys.FirstOrDefault(k => k.Counter == message.Counter);
                if (stored == null)
                    throw new OmemoDuplicateMessageException();
                chain.MessageKeys.Remove(stored);
                Split(stored.Material, out cipherKey, out macKey, out iv);
            }
            else
            {
                if (message.Counter - chain.Index > MaxMessageKeys)
                    throw new CryptographicException("Over 2000 messages into the future.");
                while (chain.Index < message.Counter)
                {
                    chain.MessageKeys.Add(new StoredMessageKey(chain.Index, DeriveMessageKeyMaterial(chain.ChainKey)));
                    if (chain.MessageKeys.Count > MaxMessageKeys)
                        chain.MessageKeys.RemoveAt(0);
                    chain.ChainKey = OmemoPrimitives.HmacSha256(chain.ChainKey, new byte[] { 0x02 });
                    chain.Index++;
                }
                DeriveMessageKeys(chain.ChainKey, out cipherKey, out macKey, out iv);
                chain.ChainKey = OmemoPrimitives.HmacSha256(chain.ChainKey, new byte[] { 0x02 });
                chain.Index++;
            }

            byte[] expected = Mac(macKey, RemoteIdentityKey, LocalIdentityKey, message.Body);
            if (!OmemoPrimitives.FixedTimeEquals(expected, message.Mac))
                throw new CryptographicException("Bad MAC.");

            byte[] plaintext = OmemoPrimitives.AesCbcDecrypt(cipherKey, iv, message.Ciphertext);
            PreKeyPending = false;
            return plaintext;
        }

        private ReceiverChain GetOrCreateReceiverChain(byte[] theirEphemeral)
        {
            var chain = receiverChains.FirstOrDefault(c => OmemoPrimitives.FixedTimeEquals(c.RatchetKey, theirEphemeral));
            if (chain != null)
                return chain;

            CreateChain(rootKey, theirEphemeral, senderRatchetPrivate, out byte[] receiverRoot, out byte[] receiverChainKey);
            OmemoPrimitives.GenerateX25519KeyPair(out byte[] newPrivate, out byte[] newPublic);
            CreateChain(receiverRoot, theirEphemeral, newPrivate, out rootKey, out byte[] newSenderChain);

            chain = new ReceiverChain(theirEphemeral, receiverChainKey, 0);
            receiverChains.Add(chain);
            if (receiverChains.Count > MaxReceiverChains)
                receiverChains.RemoveAt(0);

            previousCounter = senderChainIndex == 0 ? 0 : senderChainIndex - 1;
            senderRatchetPrivate = newPrivate;
            senderRatchetPublic = newPublic;
            senderChainKey = newSenderChain;
            senderChainIndex = 0;
            return chain;
        }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(SerializationVersion);
                OmemoSession.WriteBytes(bw, RemoteIdentityKey);
                OmemoSession.WriteBytes(bw, LocalIdentityKey);
                OmemoSession.WriteBytes(bw, AliceBaseKey);
                bw.Write(Initiator);
                bw.Write(PreKeyPending);
                bw.Write(PendingPreKeyId.HasValue);
                bw.Write(PendingPreKeyId ?? 0);
                bw.Write(PendingSignedPreKeyId);
                bw.Write(ConsumedPreKeyId);
                OmemoSession.WriteBytes(bw, rootKey);
                OmemoSession.WriteBytes(bw, senderRatchetPrivate);
                OmemoSession.WriteBytes(bw, senderRatchetPublic);
                OmemoSession.WriteBytes(bw, senderChainKey);
                bw.Write(senderChainIndex);
                bw.Write(previousCounter);
                bw.Write(receiverChains.Count);
                foreach (var c in receiverChains)
                {
                    OmemoSession.WriteBytes(bw, c.RatchetKey);
                    OmemoSession.WriteBytes(bw, c.ChainKey);
                    bw.Write(c.Index);
                    bw.Write(c.MessageKeys.Count);
                    foreach (var k in c.MessageKeys)
                    {
                        bw.Write(k.Counter);
                        OmemoSession.WriteBytes(bw, k.Material);
                    }
                }
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static OmemoLegacySession Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                if (br.ReadByte() != SerializationVersion)
                    throw new InvalidDataException("Unsupported legacy OMEMO session version.");
                var s = new OmemoLegacySession
                {
                    RemoteIdentityKey = OmemoSession.ReadBytes(br),
                    LocalIdentityKey = OmemoSession.ReadBytes(br),
                    AliceBaseKey = OmemoSession.ReadBytes(br),
                    Initiator = br.ReadBoolean(),
                    PreKeyPending = br.ReadBoolean()
                };
                bool hasPreKey = br.ReadBoolean();
                uint preKeyId = br.ReadUInt32();
                s.PendingPreKeyId = hasPreKey ? preKeyId : (uint?)null;
                s.PendingSignedPreKeyId = br.ReadUInt32();
                s.ConsumedPreKeyId = br.ReadInt32();
                s.rootKey = OmemoSession.ReadBytes(br);
                s.senderRatchetPrivate = OmemoSession.ReadBytes(br);
                s.senderRatchetPublic = OmemoSession.ReadBytes(br);
                s.senderChainKey = OmemoSession.ReadBytes(br);
                s.senderChainIndex = br.ReadUInt32();
                s.previousCounter = br.ReadUInt32();
                int chains = br.ReadInt32();
                for (int i = 0; i < chains; i++)
                {
                    var c = new ReceiverChain(OmemoSession.ReadBytes(br), OmemoSession.ReadBytes(br), br.ReadUInt32());
                    int keys = br.ReadInt32();
                    for (int k = 0; k < keys; k++)
                        c.MessageKeys.Add(new StoredMessageKey(br.ReadUInt32(), OmemoSession.ReadBytes(br)));
                    s.receiverChains.Add(c);
                }
                return s;
            }
        }

        private OmemoLegacySession Clone() => Deserialize(Serialize());

        private void CopyFrom(OmemoLegacySession o)
        {
            RemoteIdentityKey = o.RemoteIdentityKey;
            LocalIdentityKey = o.LocalIdentityKey;
            AliceBaseKey = o.AliceBaseKey;
            Initiator = o.Initiator;
            PreKeyPending = o.PreKeyPending;
            PendingPreKeyId = o.PendingPreKeyId;
            PendingSignedPreKeyId = o.PendingSignedPreKeyId;
            ConsumedPreKeyId = o.ConsumedPreKeyId;
            rootKey = o.rootKey;
            senderRatchetPrivate = o.senderRatchetPrivate;
            senderRatchetPublic = o.senderRatchetPublic;
            senderChainKey = o.senderChainKey;
            senderChainIndex = o.senderChainIndex;
            previousCounter = o.previousCounter;
            receiverChains = o.receiverChains;
        }

        private static byte[] Discontinuity() => Enumerable.Repeat((byte)0xFF, 32).ToArray();

        private static void DeriveKeys(byte[] masterSecret, out byte[] rootKey, out byte[] chainKey)
        {
            byte[] okm = OmemoPrimitives.HkdfSha256(masterSecret, new byte[32], whisperText, 64);
            rootKey = OmemoPrimitives.Slice(okm, 0, 32);
            chainKey = OmemoPrimitives.Slice(okm, 32, 32);
        }

        /// <summary>RootKey.createChain.</summary>
        private static void CreateChain(byte[] rootKey, byte[] theirRatchetKey, byte[] ourRatchetPrivate, out byte[] newRootKey, out byte[] chainKey)
        {
            byte[] shared = OmemoPrimitives.X25519Agreement(ourRatchetPrivate, theirRatchetKey);
            byte[] okm = OmemoPrimitives.HkdfSha256(shared, rootKey, whisperRatchet, 64);
            newRootKey = OmemoPrimitives.Slice(okm, 0, 32);
            chainKey = OmemoPrimitives.Slice(okm, 32, 32);
        }

        private static byte[] DeriveMessageKeyMaterial(byte[] chainKey) =>
            OmemoPrimitives.HkdfSha256(OmemoPrimitives.HmacSha256(chainKey, new byte[] { 0x01 }), new byte[32], whisperMessageKeys, 80);

        private static void DeriveMessageKeys(byte[] chainKey, out byte[] cipherKey, out byte[] macKey, out byte[] iv) =>
            Split(DeriveMessageKeyMaterial(chainKey), out cipherKey, out macKey, out iv);

        private static void Split(byte[] material, out byte[] cipherKey, out byte[] macKey, out byte[] iv)
        {
            cipherKey = OmemoPrimitives.Slice(material, 0, 32);
            macKey = OmemoPrimitives.Slice(material, 32, 32);
            iv = OmemoPrimitives.Slice(material, 64, 16);
        }

        private static byte[] Mac(byte[] macKey, byte[] senderIdentity, byte[] receiverIdentity, byte[] serialized) =>
            OmemoPrimitives.Slice(OmemoPrimitives.HmacSha256(macKey, OmemoPrimitives.Concat(Encode(senderIdentity), Encode(receiverIdentity), serialized)), 0, MacLength);

        private sealed class ReceiverChain
        {
            public ReceiverChain(byte[] ratchetKey, byte[] chainKey, uint index)
            {
                RatchetKey = ratchetKey;
                ChainKey = chainKey;
                Index = index;
            }

            public byte[] RatchetKey { get; }
            public byte[] ChainKey { get; set; }
            public uint Index { get; set; }
            public List<StoredMessageKey> MessageKeys { get; } = new List<StoredMessageKey>();
        }

        private sealed class StoredMessageKey
        {
            public StoredMessageKey(uint counter, byte[] material)
            {
                Counter = counter;
                Material = material;
            }

            public uint Counter { get; }
            public byte[] Material { get; }
        }

        /// <summary>A parsed SignalMessage: version byte, protobuf body, 8-byte MAC.</summary>
        internal sealed class SignalMessage
        {
            public byte[] Body { get; private set; }
            public byte[] Mac { get; private set; }
            public byte[] RatchetKey { get; private set; }
            public uint Counter { get; private set; }
            public uint PreviousCounter { get; private set; }
            public byte[] Ciphertext { get; private set; }

            public static SignalMessage Parse(byte[] serialized)
            {
                if (serialized == null || serialized.Length < 1 + MacLength)
                    throw new FormatException("SignalMessage is too short.");
                if (serialized[0] >> 4 != 3)
                    throw new FormatException("Unsupported SignalMessage version " + (serialized[0] >> 4) + ".");

                var m = new SignalMessage
                {
                    Body = OmemoPrimitives.Slice(serialized, 0, serialized.Length - MacLength),
                    Mac = OmemoPrimitives.Slice(serialized, serialized.Length - MacLength, MacLength)
                };
                bool hasCounter = false;
                OmemoProtobuf.Read(OmemoPrimitives.Slice(m.Body, 1, m.Body.Length - 1), (field, varint, bytes) =>
                {
                    switch (field)
                    {
                        case 1: m.RatchetKey = Decode(bytes); break;
                        case 2: m.Counter = (uint)varint; hasCounter = true; break;
                        case 3: m.PreviousCounter = (uint)varint; break;
                        case 4: m.Ciphertext = bytes; break;
                    }
                });
                if (m.RatchetKey == null || !hasCounter || m.Ciphertext == null)
                    throw new FormatException("Incomplete SignalMessage.");
                return m;
            }
        }

        /// <summary>A parsed PreKeySignalMessage.</summary>
        internal sealed class PreKeySignalMessage
        {
            public uint? PreKeyId { get; private set; }
            public uint SignedPreKeyId { get; private set; }
            public uint RegistrationId { get; private set; }
            public byte[] BaseKey { get; private set; }
            public byte[] IdentityKey { get; private set; }
            public byte[] Message { get; private set; }

            public static PreKeySignalMessage Parse(byte[] serialized)
            {
                if (serialized == null || serialized.Length < 2)
                    throw new FormatException("PreKeySignalMessage is too short.");
                if (serialized[0] >> 4 != 3)
                    throw new FormatException("Unsupported PreKeySignalMessage version " + (serialized[0] >> 4) + ".");

                var m = new PreKeySignalMessage();
                bool hasSpk = false;
                OmemoProtobuf.Read(OmemoPrimitives.Slice(serialized, 1, serialized.Length - 1), (field, varint, bytes) =>
                {
                    switch (field)
                    {
                        case 1: m.PreKeyId = (uint)varint; break;
                        case 2: m.BaseKey = Decode(bytes); break;
                        case 3: m.IdentityKey = Decode(bytes); break;
                        case 4: m.Message = bytes; break;
                        case 5: m.RegistrationId = (uint)varint; break;
                        case 6: m.SignedPreKeyId = (uint)varint; hasSpk = true; break;
                    }
                });
                if (!hasSpk || m.BaseKey == null || m.IdentityKey == null || m.Message == null)
                    throw new FormatException("Incomplete PreKeySignalMessage.");
                return m;
            }
        }
    }

    /// <summary>A legacy OMEMO message was already decrypted (libsignal DuplicateMessageException).</summary>
    internal sealed class OmemoDuplicateMessageException : Exception
    {
    }
}
