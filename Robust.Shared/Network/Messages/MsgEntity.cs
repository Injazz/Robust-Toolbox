using Lidgren.Network;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.GameObjects;
using System.IO;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;

namespace Robust.Shared.Network.Messages
{

    public class MsgEntity : NetMessageCompressed
    {

        // If a state is large enough we send it ReliableUnordered instead.
        // This is to avoid states being so large that they consistently fail to reach the other end
        // (due to being in many parts).
        public const int ReliableThreshold = 1300;

        #region REQUIRED

        public static readonly MsgGroups GROUP = MsgGroups.EntityEvent;

        public static readonly string NAME = nameof(MsgEntity);

        public MsgEntity(INetChannel channel) : base(NAME, GROUP)
        {
        }

        #endregion

        public EntityMessageType Type { get; set; }

        public EntitySystemMessage SystemMessage { get; set; }

        public EntityEventArgs EntityMessage { get; set; }

        public ComponentMessage ComponentMessage { get; set; }

        public EntityUid EntityUid { get; set; }

        public uint NetId { get; set; }

        private bool _hasWritten;

        public override void ReadFromBuffer(NetIncomingMessage buffer)
        {
            Type = (EntityMessageType) buffer.ReadByte();

            switch (Type)
            {
                case EntityMessageType.SystemMessage:
                {
                    SystemMessage = DeserializeFromBuffer<EntitySystemMessage>(buffer, out _);
                    break;
                }
                case EntityMessageType.EntityMessage:
                {
                    EntityUid = new EntityUid(buffer.ReadInt32());
                    EntityMessage = DeserializeFromBuffer<EntityEventArgs>(buffer, out _);
                    break;
                }
                case EntityMessageType.ComponentMessage:
                {
                    EntityUid = new EntityUid(buffer.ReadInt32());
                    NetId = buffer.ReadVariableUInt32();
                    ComponentMessage = DeserializeFromBuffer<ComponentMessage>(buffer, out _);
                    break;
                }
            }
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer)
        {
            buffer.Write((byte) Type);

            switch (Type)
            {
                case EntityMessageType.SystemMessage:
                {
                    SerializeToBuffer(buffer, SystemMessage);
                }
                    break;
                case EntityMessageType.EntityMessage:
                {
                    buffer.Write((int) EntityUid);

                    SerializeToBuffer(buffer, EntityMessage);
                }
                    break;
                case EntityMessageType.ComponentMessage:
                {
                    buffer.Write((int) EntityUid);
                    buffer.WriteVariableUInt32(NetId);
                    SerializeToBuffer(buffer, ComponentMessage);
                }
                    break;
            }

            _hasWritten = false;
            MsgSize = buffer.LengthBytes;
        }

        /// <summary>
        ///     Whether this state message is large enough to warrant being sent reliably.
        ///     This is only valid after
        /// </summary>
        /// <returns></returns>
        public bool ShouldSendReliably()
        {
            // This check will be true in integration tests.
            // TODO: Maybe handle this better so that packet loss integration testing can be done?
            if (!_hasWritten)
            {
                return true;
            }
            return MsgSize > ReliableThreshold;
        }

        public override NetDeliveryMethod DeliveryMethod
        {
            get
            {
                if (ShouldSendReliably())
                {
                    return NetDeliveryMethod.ReliableUnordered;
                }

                return base.DeliveryMethod;
            }
        }

    }

}
