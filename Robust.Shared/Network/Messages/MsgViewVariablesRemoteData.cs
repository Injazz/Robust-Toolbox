using System.IO;
using Lidgren.Network;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;
using Robust.Shared.ViewVariables;

namespace Robust.Shared.Network.Messages
{
    /// <summary>
    ///     Sent server to client to contain object data read by VV.
    /// </summary>
    public class MsgViewVariablesRemoteData : NetMessage
    {
        #region REQUIRED

        public const MsgGroups GROUP = MsgGroups.Command;
        public const string NAME = nameof(MsgViewVariablesRemoteData);

        public MsgViewVariablesRemoteData(INetChannel channel) : base(NAME, GROUP)
        {
        }

        #endregion

        /// <summary>
        ///     The request ID equal to the ID sent in <see cref="RequestId"/>,
        ///     to identify multiple, potentially concurrent, requests.
        /// </summary>
        public uint RequestId { get; set; }

        /// <summary>
        ///     The data blob containing the requested data.
        /// </summary>
        public ViewVariablesBlob Blob { get; set; }

        public override void ReadFromBuffer(NetIncomingMessage buffer, bool isCompressed = false)
        {
            RequestId = buffer.ReadUInt32();
            var serializer = IoCManager.Resolve<IRobustSerializer>();
            serializer.UseCompression = isCompressed;
            var length = buffer.ReadInt32();
            var bytes = buffer.ReadBytes(length);
            using (var stream = new MemoryStream(bytes))
            {
                Blob = serializer.Deserialize<ViewVariablesBlob>(stream);
            }
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer, bool willBeCompressed = false)
        {
            buffer.Write(RequestId);
            var serializer = IoCManager.Resolve<IRobustSerializer>();
            serializer.UseCompression = !willBeCompressed;
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, Blob);
                buffer.Write((int)stream.Length);
                buffer.Write(stream.ToArray());
            }
        }
    }
}
