using System;
using System.IO;

namespace Sharp.Xmpp.Extensions
{
    /// <summary>
    /// OMEMOMessage.proto (XEP-0384 §12).
    /// </summary>
    internal sealed class OmemoMessageProto
    {
        public uint N { get; set; }
        public uint PN { get; set; }
        public byte[] DhPub { get; set; }
        public byte[] Ciphertext { get; set; }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            {
                OmemoProtobuf.WriteUInt32(ms, 1, N);
                OmemoProtobuf.WriteUInt32(ms, 2, PN);
                OmemoProtobuf.WriteBytes(ms, 3, DhPub);
                if (Ciphertext != null)
                    OmemoProtobuf.WriteBytes(ms, 4, Ciphertext);
                return ms.ToArray();
            }
        }

        public static OmemoMessageProto Parse(byte[] data)
        {
            var result = new OmemoMessageProto();
            bool hasN = false, hasPN = false;
            OmemoProtobuf.Read(data, (field, varint, bytes) =>
            {
                switch (field)
                {
                    case 1: result.N = (uint)varint; hasN = true; break;
                    case 2: result.PN = (uint)varint; hasPN = true; break;
                    case 3: result.DhPub = bytes; break;
                    case 4: result.Ciphertext = bytes; break;
                }
            });
            if (!hasN || !hasPN || result.DhPub == null)
                throw new FormatException("OMEMOMessage is missing required fields.");
            return result;
        }
    }

    /// <summary>
    /// OMEMOAuthenticatedMessage.proto (XEP-0384 §12).
    /// </summary>
    internal sealed class OmemoAuthenticatedMessageProto
    {
        public byte[] Mac { get; set; }
        public byte[] Message { get; set; }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            {
                OmemoProtobuf.WriteBytes(ms, 1, Mac);
                OmemoProtobuf.WriteBytes(ms, 2, Message);
                return ms.ToArray();
            }
        }

        public static OmemoAuthenticatedMessageProto Parse(byte[] data)
        {
            var result = new OmemoAuthenticatedMessageProto();
            OmemoProtobuf.Read(data, (field, varint, bytes) =>
            {
                switch (field)
                {
                    case 1: result.Mac = bytes; break;
                    case 2: result.Message = bytes; break;
                }
            });
            if (result.Mac == null || result.Message == null)
                throw new FormatException("OMEMOAuthenticatedMessage is missing required fields.");
            return result;
        }
    }

    /// <summary>
    /// OMEMOKeyExchange.proto (XEP-0384 §12).
    /// </summary>
    internal sealed class OmemoKeyExchangeProto
    {
        public uint PkId { get; set; }
        public uint SpkId { get; set; }
        public byte[] Ik { get; set; }
        public byte[] Ek { get; set; }
        public OmemoAuthenticatedMessageProto Message { get; set; }

        public byte[] Serialize()
        {
            using (var ms = new MemoryStream())
            {
                OmemoProtobuf.WriteUInt32(ms, 1, PkId);
                OmemoProtobuf.WriteUInt32(ms, 2, SpkId);
                OmemoProtobuf.WriteBytes(ms, 3, Ik);
                OmemoProtobuf.WriteBytes(ms, 4, Ek);
                OmemoProtobuf.WriteBytes(ms, 5, Message.Serialize());
                return ms.ToArray();
            }
        }

        public static OmemoKeyExchangeProto Parse(byte[] data)
        {
            var result = new OmemoKeyExchangeProto();
            bool hasPk = false, hasSpk = false;
            byte[] message = null;
            OmemoProtobuf.Read(data, (field, varint, bytes) =>
            {
                switch (field)
                {
                    case 1: result.PkId = (uint)varint; hasPk = true; break;
                    case 2: result.SpkId = (uint)varint; hasSpk = true; break;
                    case 3: result.Ik = bytes; break;
                    case 4: result.Ek = bytes; break;
                    case 5: message = bytes; break;
                }
            });
            if (!hasPk || !hasSpk || result.Ik == null || result.Ek == null || message == null)
                throw new FormatException("OMEMOKeyExchange is missing required fields.");
            result.Message = OmemoAuthenticatedMessageProto.Parse(message);
            return result;
        }
    }

    /// <summary>
    /// Minimal protobuf (proto2) wire format support for the three OMEMO structures.
    /// </summary>
    internal static class OmemoProtobuf
    {
        private const int WireVarint = 0;
        private const int Wire64Bit = 1;
        private const int WireLengthDelimited = 2;
        private const int Wire32Bit = 5;

        public static void WriteUInt32(Stream s, int field, uint value)
        {
            WriteVarint(s, (ulong)((field << 3) | WireVarint));
            WriteVarint(s, value);
        }

        public static void WriteBytes(Stream s, int field, byte[] value)
        {
            value = value ?? new byte[0];
            WriteVarint(s, (ulong)((field << 3) | WireLengthDelimited));
            WriteVarint(s, (ulong)value.Length);
            s.Write(value, 0, value.Length);
        }

        /// <summary>
        /// Reads all fields; the callback receives (fieldNumber, varintValue, bytesValue).
        /// Unknown fields are skipped.
        /// </summary>
        public static void Read(byte[] data, Action<int, ulong, byte[]> onField)
        {
            if (data == null)
                throw new FormatException("Protobuf data is null.");

            int pos = 0;
            while (pos < data.Length)
            {
                ulong tag = ReadVarint(data, ref pos);
                int field = (int)(tag >> 3);
                int wire = (int)(tag & 7);
                if (field <= 0)
                    throw new FormatException("Invalid protobuf field number.");

                switch (wire)
                {
                    case WireVarint:
                        onField(field, ReadVarint(data, ref pos), null);
                        break;
                    case WireLengthDelimited:
                        ulong len = ReadVarint(data, ref pos);
                        if (len > (ulong)(data.Length - pos))
                            throw new FormatException("Truncated protobuf field.");
                        var bytes = new byte[(int)len];
                        Buffer.BlockCopy(data, pos, bytes, 0, (int)len);
                        pos += (int)len;
                        onField(field, 0, bytes);
                        break;
                    case Wire64Bit:
                        Skip(data, ref pos, 8);
                        break;
                    case Wire32Bit:
                        Skip(data, ref pos, 4);
                        break;
                    default:
                        throw new FormatException("Unsupported protobuf wire type " + wire + ".");
                }
            }
        }

        private static void Skip(byte[] data, ref int pos, int count)
        {
            if (data.Length - pos < count)
                throw new FormatException("Truncated protobuf field.");
            pos += count;
        }

        private static void WriteVarint(Stream s, ulong value)
        {
            while (value >= 0x80)
            {
                s.WriteByte((byte)(value | 0x80));
                value >>= 7;
            }
            s.WriteByte((byte)value);
        }

        private static ulong ReadVarint(byte[] data, ref int pos)
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                if (pos >= data.Length)
                    throw new FormatException("Truncated protobuf varint.");
                byte b = data[pos++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
                shift += 7;
                if (shift > 63)
                    throw new FormatException("Protobuf varint is too long.");
            }
        }
    }
}
