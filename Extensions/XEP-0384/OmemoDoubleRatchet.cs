using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// Double Ratchet session with the OMEMO parameters from XEP-0384 §4.3.
    /// Instances are not thread-safe; callers serialize access per session.
    /// </summary>
    internal sealed class OmemoDoubleRatchet
    {
        /// <summary>Maximum skipped message keys kept per session, and per message (§4.3 MAX_SKIP).</summary>
        public const int MaxSkip = 1000;

        private const byte SerializationVersion = 1;

        private static readonly byte[] rootChainInfo = Encoding.ASCII.GetBytes("OMEMO Root Chain");
        private static readonly byte[] messageKeyInfo = Encoding.ASCII.GetBytes("OMEMO Message Key Material");
        private static readonly byte[] messageKeyConstant = { 0x01 };
        private static readonly byte[] chainKeyConstant = { 0x02 };

        private byte[] associatedData;
        private byte[] dhsPrivate;
        private byte[] dhsPublic;
        private byte[] dhr;
        private byte[] rootKey;
        private byte[] sendingChainKey;
        private byte[] receivingChainKey;
        private uint ns;
        private uint nr;
        private uint pn;
        private LinkedList<SkippedKey> skipped = new LinkedList<SkippedKey>();

        private OmemoDoubleRatchet() { }

        /// <summary>The ratchet public key of the current receiving chain (null before the first message).</summary>
        public byte[] RemoteRatchetKey => dhr;

        /// <summary>
        /// Initializes the session of the party that actively initiated the key exchange (Alice).
        /// </summary>
        public static OmemoDoubleRatchet InitializeActive(byte[] sharedSecret, byte[] associatedData, byte[] remoteSignedPreKey)
        {
            var r = new OmemoDoubleRatchet
            {
                associatedData = associatedData,
                dhr = remoteSignedPreKey
            };
            OmemoPrimitives.GenerateX25519KeyPair(out r.dhsPrivate, out r.dhsPublic);
            KdfRootKey(sharedSecret, OmemoPrimitives.X25519Agreement(r.dhsPrivate, r.dhr), out r.rootKey, out r.sendingChainKey);
            return r;
        }

        /// <summary>
        /// Initializes the session of the party that passively accepted the key exchange (Bob).
        /// </summary>
        public static OmemoDoubleRatchet InitializePassive(byte[] sharedSecret, byte[] associatedData, byte[] signedPreKeyPrivate, byte[] signedPreKeyPublic)
        {
            return new OmemoDoubleRatchet
            {
                associatedData = associatedData,
                dhsPrivate = signedPreKeyPrivate,
                dhsPublic = signedPreKeyPublic,
                rootKey = sharedSecret
            };
        }

        public bool CanSend => sendingChainKey != null;

        /// <summary>
        /// True if a message with this counter on the current receiving chain was already
        /// processed (and its key is not among the skipped keys).
        /// </summary>
        public bool IsPastMessage(uint n) => receivingChainKey != null && n < nr;

        /// <summary>
        /// Encrypts a plaintext and returns the serialized OMEMOAuthenticatedMessage.
        /// </summary>
        public OmemoAuthenticatedMessageProto Encrypt(byte[] plaintext)
        {
            if (sendingChainKey == null)
                throw new InvalidOperationException("The session cannot send before it has received a message.");

            KdfChainKey(sendingChainKey, out sendingChainKey, out byte[] messageKey);
            var header = new OmemoMessageProto { N = ns, PN = pn, DhPub = dhsPublic };
            ns++;
            return Seal(messageKey, plaintext, header);
        }

        /// <summary>
        /// Decrypts an OMEMOAuthenticatedMessage. The session state only changes when decryption
        /// succeeds.
        /// </summary>
        /// <param name="message">The message to decrypt.</param>
        /// <param name="header">Receives the parsed OMEMOMessage header.</param>
        /// <param name="usedSkippedKey">true if the message was an out-of-order message.</param>
        /// <exception cref="CryptographicException">Authentication or decryption failed.</exception>
        public byte[] Decrypt(OmemoAuthenticatedMessageProto message, out OmemoMessageProto header, out bool usedSkippedKey)
        {
            header = OmemoMessageProto.Parse(message.Message);
            usedSkippedKey = false;

            var skippedEntry = FindSkipped(header.DhPub, header.N);
            if (skippedEntry != null)
            {
                byte[] plain = Open(skippedEntry.Value.MessageKey, message);
                skipped.Remove(skippedEntry);
                usedSkippedKey = true;
                return plain;
            }

            var working = Clone();
            if (working.dhr == null || !OmemoPrimitives.FixedTimeEquals(header.DhPub, working.dhr))
            {
                working.SkipMessageKeys(header.PN);
                working.DhRatchet(header);
            }
            working.SkipMessageKeys(header.N);
            KdfChainKey(working.receivingChainKey, out working.receivingChainKey, out byte[] messageKey);
            working.nr++;

            byte[] result = working.Open(messageKey, message);
            CopyFrom(working);
            return result;
        }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(SerializationVersion);
                WriteBytes(bw, associatedData);
                WriteBytes(bw, dhsPrivate);
                WriteBytes(bw, dhsPublic);
                WriteBytes(bw, dhr);
                WriteBytes(bw, rootKey);
                WriteBytes(bw, sendingChainKey);
                WriteBytes(bw, receivingChainKey);
                bw.Write(ns);
                bw.Write(nr);
                bw.Write(pn);
                bw.Write(skipped.Count);
                foreach (var s in skipped)
                {
                    WriteBytes(bw, s.RatchetKey);
                    bw.Write(s.N);
                    WriteBytes(bw, s.MessageKey);
                }
                bw.Flush();
                return ms.ToArray();
            }
        }

        public static OmemoDoubleRatchet Deserialize(byte[] data)
        {
            using (var ms = new MemoryStream(data))
            using (var br = new BinaryReader(ms))
            {
                if (br.ReadByte() != SerializationVersion)
                    throw new FormatException("Unsupported Double Ratchet state version.");
                var r = new OmemoDoubleRatchet
                {
                    associatedData = ReadBytes(br),
                    dhsPrivate = ReadBytes(br),
                    dhsPublic = ReadBytes(br),
                    dhr = ReadBytes(br),
                    rootKey = ReadBytes(br),
                    sendingChainKey = ReadBytes(br),
                    receivingChainKey = ReadBytes(br),
                    ns = br.ReadUInt32(),
                    nr = br.ReadUInt32(),
                    pn = br.ReadUInt32()
                };
                int count = br.ReadInt32();
                for (int i = 0; i < count; i++)
                    r.skipped.AddLast(new SkippedKey(ReadBytes(br), br.ReadUInt32(), ReadBytes(br)));
                return r;
            }
        }

        private OmemoAuthenticatedMessageProto Seal(byte[] messageKey, byte[] plaintext, OmemoMessageProto header)
        {
            DeriveMessageKeys(messageKey, out byte[] encKey, out byte[] authKey, out byte[] iv);
            header.Ciphertext = OmemoPrimitives.AesCbcEncrypt(encKey, iv, plaintext);

            // Serialize exactly once and authenticate that byte sequence (§4.3 ENCRYPT).
            byte[] serialized = header.Serialize();
            byte[] mac = OmemoPrimitives.Slice(OmemoPrimitives.HmacSha256(authKey, OmemoPrimitives.Concat(associatedData, serialized)), 0, 16);
            return new OmemoAuthenticatedMessageProto { Mac = mac, Message = serialized };
        }

        private byte[] Open(byte[] messageKey, OmemoAuthenticatedMessageProto message)
        {
            DeriveMessageKeys(messageKey, out byte[] encKey, out byte[] authKey, out byte[] iv);
            byte[] expected = OmemoPrimitives.Slice(OmemoPrimitives.HmacSha256(authKey, OmemoPrimitives.Concat(associatedData, message.Message)), 0, 16);
            if (!OmemoPrimitives.FixedTimeEquals(expected, message.Mac))
                throw new CryptographicException("OMEMO message authentication failed.");

            var parsed = OmemoMessageProto.Parse(message.Message);
            if (parsed.Ciphertext == null)
                throw new CryptographicException("OMEMO message has no ciphertext.");
            return OmemoPrimitives.AesCbcDecrypt(encKey, iv, parsed.Ciphertext);
        }

        private void SkipMessageKeys(uint until)
        {
            if (receivingChainKey == null)
                return;
            if ((long)until - nr > MaxSkip)
                throw new CryptographicException("Too many skipped OMEMO messages.");

            while (nr < until)
            {
                KdfChainKey(receivingChainKey, out receivingChainKey, out byte[] mk);
                skipped.AddLast(new SkippedKey(dhr, nr, mk));
                nr++;
                while (skipped.Count > MaxSkip)
                    skipped.RemoveFirst();
            }
        }

        private void DhRatchet(OmemoMessageProto header)
        {
            if (header.DhPub == null || header.DhPub.Length != OmemoPrimitives.KeySize)
                throw new CryptographicException("Invalid ratchet public key.");

            pn = ns;
            ns = 0;
            nr = 0;
            dhr = header.DhPub;
            KdfRootKey(rootKey, OmemoPrimitives.X25519Agreement(dhsPrivate, dhr), out rootKey, out receivingChainKey);
            OmemoPrimitives.GenerateX25519KeyPair(out dhsPrivate, out dhsPublic);
            KdfRootKey(rootKey, OmemoPrimitives.X25519Agreement(dhsPrivate, dhr), out rootKey, out sendingChainKey);
        }

        private LinkedListNode<SkippedKey> FindSkipped(byte[] ratchetKey, uint n)
        {
            for (var node = skipped.First; node != null; node = node.Next)
            {
                if (node.Value.N == n && OmemoPrimitives.FixedTimeEquals(node.Value.RatchetKey, ratchetKey))
                    return node;
            }
            return null;
        }

        private OmemoDoubleRatchet Clone() => Deserialize(Serialize());

        private void CopyFrom(OmemoDoubleRatchet other)
        {
            associatedData = other.associatedData;
            dhsPrivate = other.dhsPrivate;
            dhsPublic = other.dhsPublic;
            dhr = other.dhr;
            rootKey = other.rootKey;
            sendingChainKey = other.sendingChainKey;
            receivingChainKey = other.receivingChainKey;
            ns = other.ns;
            nr = other.nr;
            pn = other.pn;
            skipped = other.skipped;
        }

        private static void KdfRootKey(byte[] rk, byte[] dhOut, out byte[] newRootKey, out byte[] chainKey)
        {
            byte[] okm = OmemoPrimitives.HkdfSha256(dhOut, rk, rootChainInfo, 64);
            newRootKey = OmemoPrimitives.Slice(okm, 0, 32);
            chainKey = OmemoPrimitives.Slice(okm, 32, 32);
        }

        private static void KdfChainKey(byte[] ck, out byte[] nextChainKey, out byte[] messageKey)
        {
            messageKey = OmemoPrimitives.HmacSha256(ck, messageKeyConstant);
            nextChainKey = OmemoPrimitives.HmacSha256(ck, chainKeyConstant);
        }

        private static void DeriveMessageKeys(byte[] mk, out byte[] encKey, out byte[] authKey, out byte[] iv)
        {
            byte[] okm = OmemoPrimitives.HkdfSha256(mk, new byte[32], messageKeyInfo, 80);
            encKey = OmemoPrimitives.Slice(okm, 0, 32);
            authKey = OmemoPrimitives.Slice(okm, 32, 32);
            iv = OmemoPrimitives.Slice(okm, 64, 16);
        }

        private static void WriteBytes(BinaryWriter bw, byte[] value)
        {
            if (value == null)
            {
                bw.Write(-1);
                return;
            }
            bw.Write(value.Length);
            bw.Write(value);
        }

        private static byte[] ReadBytes(BinaryReader br)
        {
            int len = br.ReadInt32();
            return len < 0 ? null : br.ReadBytes(len);
        }

        private struct SkippedKey
        {
            public SkippedKey(byte[] ratchetKey, uint n, byte[] messageKey)
            {
                RatchetKey = ratchetKey;
                N = n;
                MessageKey = messageKey;
            }

            public byte[] RatchetKey { get; }
            public uint N { get; }
            public byte[] MessageKey { get; }
        }
    }
}
