using System.IO;
using Joveler.Compression.XZ;
using Lidgren.Network;
using Robust.Shared.GameStates;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;

namespace Robust.Shared.Network.Messages
{

    public abstract class NetMessageCompressed : NetMessage
    {
        // If a state is large enough we send it ReliableUnordered instead.
        // This is to avoid states being so large that they consistently fail to reach the other end
        // (due to being in many parts).
        public static int CompressionThreshold { get; private set; } = 32;

        private static readonly XZCompressOptions XzEncOpts = new XZCompressOptions
        {
            BufferSize = 4 * 1024 * 1024,
            Check = LzmaCheck.None,
            ExtremeFlag = false,
            Level = LzmaCompLevel.Level1,
            LeaveOpen = true
        };

        private static readonly XZDecompressOptions XzDecOpts = new XZDecompressOptions
        {
            BufferSize = 4 * 1024 * 1024,
            DecodeFlags = LzmaDecodingFlag.Concatenated | LzmaDecodingFlag.IgnoreCheck,
            MemLimit = 24 * 1024 * 1024,
            LeaveOpen = true
        };

        protected NetMessageCompressed(string name, MsgGroups @group) : base(name, @group)
        {
        }

        protected T DeserializeFromBuffer<T>(NetIncomingMessage buffer, out int length)
        {
            length = buffer.ReadVariableInt32();
            var isCompressed = false;
            if (length < 0)
            {
                isCompressed = true;
                length = -length;
            }

            var stateData = buffer.ReadBytes(length);
            var serializer = IoCManager.Resolve<IRobustSerializer>();

            if (isCompressed)
            {
                using var ms = new MemoryStream(stateData, false);
                using var stateStream = new XZStream(ms, XzDecOpts);
                return serializer.Deserialize<T>(stateStream);
            }
            else
            {
                using var stateStream = new MemoryStream(stateData, false);
                return serializer.Deserialize<T>(stateStream);
            }

        }


        protected void SerializeToBuffer<T>(NetOutgoingMessage buffer, T item)
        {
            var serializer = IoCManager.Resolve<IRobustSerializer>();
            using (var stateStream = new MemoryStream())
            {
                serializer.Serialize(stateStream, item);
                var length = (int) stateStream.Length;
                if (length > CompressionThreshold)
                {
                    using var compressed = new MemoryStream();

                    using (var compressor = new XZStream(compressed, XzEncOpts))
                    {
                        stateStream.Position = 0;
                        stateStream.CopyTo(compressor);
                        compressor.Flush();
                    }

                    compressed.Position = 0;

                    var compressedLength = (int)compressed.Length;
                    if (compressedLength < length)
                    {
                        buffer.WriteVariableInt32(-compressedLength);
                        buffer.Write(compressed);
                        //CompressionThreshold -= 1;
                    }
                    else
                    {
                        buffer.WriteVariableInt32(length);
                        buffer.Write(stateStream);
                        //CompressionThreshold += 1;
                    }
                }
                else
                {
                    buffer.WriteVariableInt32(length);
                    buffer.Write(stateStream);
                }
            }

            MsgSize += buffer.LengthBytes;
        }


    }

}
