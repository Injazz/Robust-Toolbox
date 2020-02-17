using System;
using System.Diagnostics;
using Lidgren.Network;
using Robust.Shared.GameStates;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;
using System.IO;
using Joveler.Compression.XZ;
using Robust.Shared.Utility;

namespace Robust.Shared.Network.Messages
{
    public class MsgState : NetMessageCompressed
    {
        // If a state is large enough we send it ReliableUnordered instead.
        // This is to avoid states being so large that they consistently fail to reach the other end
        // (due to being in many parts).
        public const int ReliableThreshold = 1300;

        #region REQUIRED

        public static readonly MsgGroups GROUP = MsgGroups.Entity;
        public static readonly string NAME = nameof(MsgState);

        public MsgState(INetChannel channel) : base(NAME, GROUP)
        {
        }

        #endregion

        public GameState State { get; set; }

        private bool _hasWritten;

        public override void ReadFromBuffer(NetIncomingMessage buffer)
        {
            MsgSize = buffer.LengthBytes;

            State = DeserializeFromBuffer<GameState>(buffer, out var length);

            State.PayloadSize = length;
        }

        public override void WriteToBuffer(NetOutgoingMessage buffer)
        {
            SerializeToBuffer(buffer, State);

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
