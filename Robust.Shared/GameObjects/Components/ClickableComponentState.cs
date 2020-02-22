﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Robust.Shared.GameObjects.Components
{
    [Serializable, NetSerializable]
    class ClickableComponentState : ComponentState
    {
        public override uint NetID => NetIDs.CLICKABLE;

        public Box2? LocalBounds { get; }

        public ClickableComponentState(Box2? localBounds)
        {
            LocalBounds = localBounds;
        }
    }
}
