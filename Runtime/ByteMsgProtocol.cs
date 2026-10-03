using System;

namespace ByteMsg233
{
    public readonly struct ByteMsgProtocolHello
    {
        public ByteMsgProtocolHello(ulong version, ulong minCompatible)
        {
            if (minCompatible > version) throw new ArgumentOutOfRangeException(nameof(minCompatible));
            Version = version;
            MinCompatible = minCompatible;
        }

        public ulong Version { get; }
        public ulong MinCompatible { get; }
    }

    public static class ByteMsgProtocol
    {
        public static void WriteHello(ByteMsgWriter writer, ByteMsgProtocolHello hello)
        {
            writer.WriteULongField(1, hello.Version);
            writer.WriteULongField(2, hello.MinCompatible);
        }

        public static ByteMsgProtocolHello ReadHello(byte[] data)
        {
            var reader = new ByteMsgReader(data);
            ulong version = 0;
            ulong minCompatible = 0;
            bool hasVersion = false, hasMinimum = false;

            while (!reader.IsEof)
            {
                var header = reader.ReadFieldHeader();
                switch (header.Tag)
                {
                    case 1:
                        if (hasVersion || header.WireType != ByteMsgWireType.Varint) throw new FormatException("Invalid protocol version field.");
                        hasVersion = true;
                        version = reader.ReadVarint();
                        break;
                    case 2:
                        if (hasMinimum || header.WireType != ByteMsgWireType.Varint) throw new FormatException("Invalid protocol minimum field.");
                        hasMinimum = true;
                        minCompatible = reader.ReadVarint();
                        break;
                    default:
                        reader.SkipField(header.WireType);
                        break;
                }
            }

            if (!hasVersion || !hasMinimum || minCompatible > version) throw new FormatException("Incomplete or invalid protocol hello.");
            return new ByteMsgProtocolHello(version, minCompatible);
        }

        public static void CheckCompatible(ByteMsgProtocolHello local, ByteMsgProtocolHello remote)
        {
            if (remote.Version < local.MinCompatible || local.Version < remote.MinCompatible)
            {
                throw new InvalidOperationException("ByteMsg233 protocol version mismatch.");
            }
        }
    }
}
