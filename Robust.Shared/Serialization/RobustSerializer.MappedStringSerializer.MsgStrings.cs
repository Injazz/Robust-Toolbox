using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using JetBrains.Annotations;
using Lidgren.Network;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Network;
using Robust.Shared.Utility;

namespace Robust.Shared.Serialization
{

    public partial class RobustSerializer
    {

        public partial class MappedStringSerializer
        {

            [UsedImplicitly]
            private class MsgStrings : NetMessage
            {

                public MsgStrings(INetChannel ch)
                    : base($"{nameof(RobustSerializer)}.{nameof(MappedStringSerializer)}.{nameof(MsgStrings)}", MsgGroups.Core)
                {
                }

                public byte[] Package { get; set; }

                public override void ReadFromBuffer(NetIncomingMessage buffer)
                {
                    var l = buffer.ReadVariableInt32();
                    var success = buffer.ReadBytes(l, out var buf);
                    if (!success)
                    {
                        throw new InvalidDataException("Not all of the bytes were available in the message.");
                    }

                    Package = buf;
                }

                public override void WriteToBuffer(NetOutgoingMessage buffer)
                {
                    buffer.WriteVariableInt32(Package.Length);
                    var start = buffer.LengthBytes;
                    buffer.Write(Package);
                    var added = buffer.LengthBytes - start;
                    if (added != Package.Length)
                    {
                        throw new InvalidOperationException("Not all of the bytes were written to the message.");
                    }
                }

            }

        }

    }

}
