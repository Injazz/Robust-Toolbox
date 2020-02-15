using System;
using System.Collections.Generic;
using Robust.Shared.Physics;
using Robust.Shared.Serialization;

namespace Robust.Shared.GameObjects.Components
{
    [Serializable, NetSerializable]
    public class CollidableComponentState : ComponentState
    {
        public override uint NetID => NetIDs.COLLIDABLE;

        public readonly bool CollisionEnabled;
        public readonly bool HardCollidable;
        public readonly bool ScrapingFloor;
        public readonly List<IPhysShape> PhysShapes;

        public CollidableComponentState(bool collisionEnabled, bool hardCollidable, bool scrapingFloor, List<IPhysShape> physShapes)
        {
            CollisionEnabled = collisionEnabled;
            HardCollidable = hardCollidable;
            ScrapingFloor = scrapingFloor;
            PhysShapes = physShapes;
        }

    }
}
