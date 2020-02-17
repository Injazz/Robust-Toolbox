using System.Collections.Generic;
using Lidgren.Network;
using Robust.Shared.Network;

namespace Robust.Shared.Serialization
{

    public abstract class SerializerSharedStateMessage : NetMessage
    {

        protected SerializerSharedStateMessage(string name, MsgGroups @group) : base(name, @group)
        {
        }

    }

    public sealed class SerializerSharedStateVerificationMessage : NetMessage
    {

        public List<byte[]> StringTableHashes;

        public List<byte[]> TypeTableHashes;

        public SerializerSharedStateVerificationMessage(string name, MsgGroups @group) : base(name, @group)
        {
        }

        public override void ReadFromBuffer(NetIncomingMessage buffer)
        {
            var strHashCount = StringTableHashes?.Count ?? 0;
            buffer.WriteVariableInt32(strHashCount);
            var typeHashCount = TypeTableHashes?.Count ?? 0;
            buffer.WriteVariableInt32(typeHashCount);

            for (var i = 0; i < strHashCount; i++)
            {
                // ReSharper disable once PossibleNullReferenceException
                var item = StringTableHashes[i];
                if (item == null)
                {
                    buffer.Write(false);
                }
                else
                {
                    buffer.Write(true);
                    buffer.Write(item);
                }
            }

            for (var i = 0; i < typeHashCount; i++)
            {
                // ReSharper disable once PossibleNullReferenceException
                var item = TypeTableHashes[i];
                if (item == null)
                {
                    buffer.Write(false);
                }
                else
                {
                    buffer.Write(true);
                    buffer.Write(item);
                }
            }
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer)
        {
            var strHashCount = buffer.ReadVariableInt32();
            var typeHashCount = buffer.ReadVariableInt32();

            StringTableHashes = new List<byte[]>(strHashCount);
            TypeTableHashes = new List<byte[]>(typeHashCount);

            for (var i = 0; i < strHashCount; ++i)
            {
                StringTableHashes.Add(buffer.ReadBoolean() ? null : buffer.ReadBytes(512 / 8));
            }

            for (var i = 0; i < typeHashCount; ++i)
            {
                TypeTableHashes.Add(buffer.ReadBoolean() ? null : buffer.ReadBytes(512 / 8));
            }
        }

    }

    public sealed class SerializerSharedStringsMessage : NetMessage
    {

        public SerializerSharedStringsMessage(string name, MsgGroups @group) : base(name, @group)
        {
        }

        public override void ReadFromBuffer(NetIncomingMessage buffer)
            => throw new System.NotImplementedException();

        public override void WriteToBuffer(NetOutgoingMessage buffer)
            => throw new System.NotImplementedException();

    }

    public sealed class SerializerSharedTypesMessage : NetMessage
    {

        public SerializerSharedTypesMessage(string name, MsgGroups @group) : base(name, @group)
        {
        }

        public override void ReadFromBuffer(NetIncomingMessage buffer)
            => throw new System.NotImplementedException();

        public override void WriteToBuffer(NetOutgoingMessage buffer)
            => throw new System.NotImplementedException();

    }

}
