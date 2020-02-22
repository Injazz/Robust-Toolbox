﻿using System;
using Robust.Shared.Maths;
using Robust.Shared.Serialization;

namespace Robust.Shared.GameObjects
{
    /// <summary>
    ///     Serialized state of a PhysicsComponent.
    /// </summary>
    [Serializable, NetSerializable]
    public class PhysicsComponentState : ComponentState
    {
        public override uint NetID => NetIDs.PHYSICS;

        /// <summary>
        ///     Current mass of the entity, stored in grams.
        /// </summary>
        public readonly int Mass;

        /// <summary>
        ///     Current velocity of the entity.
        /// </summary>
        public readonly Vector2 Velocity;

        /// <summary>
        ///     Constructs a new state snapshot of a PhysicsComponent.
        /// </summary>
        /// <param name="mass">Current Mass of the entity.</param>
        /// <param name="velocity">Current Velocity of the entity.</param>
        public PhysicsComponentState(float mass, Vector2 velocity)
        {
            Mass = (int) Math.Round(mass *1000); // rounds kg to nearest gram
            Velocity = velocity;
        }
    }
}
