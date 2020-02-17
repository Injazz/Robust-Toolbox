using System.IO;
using System.Threading;
using Joveler.Compression.XZ;
using Lidgren.Network;
using Robust.Shared.GameStates;
using Robust.Shared.Interfaces.Configuration;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;

namespace Robust.Shared.Network.Messages
{

    public abstract class NetMessageCompressed : NetMessage
    {

        private static readonly ThreadLocal<IConfigurationManager> LazyConfig
            = new ThreadLocal<IConfigurationManager>(IoCManager.Resolve<IConfigurationManager>);

        private static IConfigurationManager Config => LazyConfig.Value;

        static NetMessageCompressed()
        {
            Config.RegisterCVar("net.compression", false);
            Config.RegisterCVar("net.compressthresh", 32);
        }

        // If a state is large enough we send it ReliableUnordered instead.
        // This is to avoid states being so large that they consistently fail to reach the other end
        // (due to being in many parts).
        public static int CompressionThreshold => Config.GetCVar<int>("net.compressthresh");

        public static bool CompressionAllowed => Config.GetCVar<bool>("net.compression");

        private static readonly XZCompressOptions XzEncOpts = new XZCompressOptions
        {
            BufferSize = 12 * 1024 * 1024,
            Check = LzmaCheck.None,
            ExtremeFlag = false,
            Level = IoCManager.Resolve<INetManager>().IsClient
                ? LzmaCompLevel.Level1
                : LzmaCompLevel.Level6,
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

        protected void DeserializeFromBuffer<T>(NetIncomingMessage buffer, out T item, out int length) =>
            item = DeserializeFromBuffer<T>(buffer, out length);

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
                if (CompressionAllowed && length > CompressionThreshold)
                {
                    using var compressed = new MemoryStream();

                    using (var compressor = new XZStream(compressed, XzEncOpts))
                    {
                        stateStream.Position = 0;
                        stateStream.CopyTo(compressor);
                        compressor.Flush();
                    }

                    compressed.Position = 0;

                    var compressedLength = (int) compressed.Length;
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
