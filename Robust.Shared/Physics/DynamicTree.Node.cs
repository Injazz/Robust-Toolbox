using System.Diagnostics;
using System.Runtime.CompilerServices;
using Robust.Shared.Maths;

namespace Robust.Shared.Physics {

    public partial class DynamicTree<T> {

        public struct Node {

            public Box2 Aabb;

            public Proxy Parent;

            public Proxy Child1, Child2;

            public int Height;

            public T Item;

            public bool IsLeaf {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Child2 == Proxy.Free;
            }

            public bool IsFree {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => Height == -1;
            }

            public override string ToString()
                => IsLeaf
                    ? Height != 0
                        ? $"Leaf (invalid height of {Height}): {Item}"
                        : $"Leaf: {Item}"
                    : IsFree
                        ? "Free"
                        : $"Branch at height {Height}, children: {Child1} and {Child2}";

        }

    }

}
