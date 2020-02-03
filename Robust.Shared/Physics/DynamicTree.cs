using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using JetBrains.Annotations;
using Robust.Shared.Interfaces.GameObjects;
using Robust.Shared.Maths;

namespace Robust.Shared.Physics {

    [PublicAPI]
    public abstract partial class DynamicTree {

        public const int MinimumCapacity = 16;

        protected const float AabbMultiplier = 2f;

        protected readonly float AabbExtendSize;

        protected readonly Func<int, int> GrowthFunc;

        protected DynamicTree(float aabbExtendSize, Func<int, int> growthFunc) {
            AabbExtendSize = aabbExtendSize;
            GrowthFunc = growthFunc ?? DefaultGrowthFunc;
        }

        // box2d grows by *2, here we're being somewhat more linear
        private static int DefaultGrowthFunc(int x)
            => x + 256;

    }

    [PublicAPI]
    [DebuggerDisplay("{" + nameof(DebuggerDisplay) + "}")]
    public sealed partial class DynamicTree<T>
        : DynamicTree, ICollection<T> {

        public delegate Box2 ExtractAabbDelegate(in T value);

        public delegate bool QueryCallbackDelegate(ref T value);

        public delegate bool RayQueryCallbackDelegate(ref T value, in Vector2 point, float distFromOrigin);

        private readonly IEqualityComparer<T> _equalityComparer;

        private readonly ExtractAabbDelegate _extractAabb;

        private Proxy _freeNodes;

        // avoid "Collection was modified; enumeration operation may not execute."
        private ConcurrentDictionary<T, Proxy> _nodeLookup;

        private Node[] _nodes;

        private Proxy _root;

        public DynamicTree(ExtractAabbDelegate extractAabbFunc, IEqualityComparer<T> comparer = null, float aabbExtendSize = 1f / 32, int capacity = 256, Func<int, int> growthFunc = null)
            : base(aabbExtendSize, growthFunc) {
            _extractAabb = extractAabbFunc;
            _equalityComparer = comparer ?? EqualityComparer<T>.Default;
            capacity = Math.Max(MinimumCapacity, capacity);

            _root = Proxy.Free;

            _nodeLookup = new ConcurrentDictionary<T, Proxy>();
            _nodes = new Node[capacity];

            var l = Capacity - 1;
            for (var i = 0; i < l; ++i) {
                ref var node = ref _nodes[i];
                node.Parent = (Proxy) (i + 1);
                node.Height = -1;
            }

            ref var lastNode = ref _nodes[l];

            lastNode.Parent = Proxy.Free;
            lastNode.Height = -1;
        }

        private int Capacity => _nodes.Length;

        public IEnumerator<T> GetEnumerator()
            => _nodeLookup.Keys.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();

        public void Clear() {
            var capacity = Capacity;

            Count = 0;
            _nodes = new Node[capacity];
            _root = Proxy.Free;

            _nodeLookup = new ConcurrentDictionary<T, Proxy>();
            _nodes = new Node[capacity];

            var l = Capacity - 1;
            for (var i = 0; i < l; ++i) {
                ref var node = ref _nodes[i];
                node.Parent = (Proxy) (i + 1);
                node.Height = -1;
            }

            ref var lastNode = ref _nodes[l];

            lastNode.Parent = Proxy.Free;
            lastNode.Height = -1;
        }

        public bool Contains(T item)
            => item != null && _nodeLookup.ContainsKey(item);

        public void CopyTo(T[] array, int arrayIndex)
            => _nodeLookup.Keys.CopyTo(array, arrayIndex);

        public int Count { get; private set; }

        public bool IsReadOnly
            => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(in T item)
            => CreateProxy(_extractAabb(item), item);

        void ICollection<T>.Add(T item)
            => Add(item);

        private bool TryGetProxy(in T item, out Proxy proxy)
            => _nodeLookup.TryGetValue(item, out proxy);

        public bool Remove(in T item) {
            if (!TryGetProxy(item, out var proxy))
                return false;

            DestroyProxy(proxy);

            _nodeLookup.Remove(item, out _);

            Assert(!Contains(item));
            return true;
        }

        bool ICollection<T>.Remove(T item)
            => Remove(item);

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        private Proxy CreateProxy(in Box2 box, in T item) {
            var proxy = AllocateNode();

            ref var node = ref _nodes[proxy];
            node.Aabb = new Box2(
                box.Left - AabbExtendSize,
                box.Bottom - AabbExtendSize,
                box.Right + AabbExtendSize,
                box.Top + AabbExtendSize
            );
            node.Item = item;

            InsertLeaf(proxy);

            _nodeLookup[item] = proxy;

            Assert(Contains(item));

            return proxy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DestroyProxy(Proxy proxy) {
            RemoveLeaf(proxy);
            FreeNode(proxy);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public bool Update(in T item) {
            if (!TryGetProxy(item, out var leaf))
                return false;

            var leafNode = _nodes[leaf];

            Assert(leafNode.IsLeaf);

            var oldBox = leafNode.Aabb;

            var newBox = _extractAabb(item);

            if (leafNode.Aabb.Contains(newBox))
                return false;

            var movedDist = newBox.Center - oldBox.Center;

            var fattenedNewBox = Box2.Grow(newBox, AabbExtendSize);

            fattenedNewBox = Box2.Combine(newBox, fattenedNewBox.Translated(movedDist));

            RemoveLeaf(leaf);

            leafNode.Aabb = fattenedNewBox;

            InsertLeaf(leaf);

            Assert(Contains(item));

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref T GetValue(Proxy proxy)
            => ref _nodes[proxy].Item;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ref Box2 GetAabb(Proxy proxy)
            => ref _nodes[proxy].Aabb;

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public bool Query(QueryCallbackDelegate callback, in Box2 aabb) {
            var stack = new Stack<Proxy>(256);

            stack.Push(_root);

            var any = false;

            while (stack.Count > 0) {
                var proxy = stack.Pop();

                if (proxy == Proxy.Free)
                    continue;

                ref var node = ref _nodes[proxy];

                if (!node.Aabb.Intersects(aabb))
                    continue;

                if (!node.IsLeaf) {
                    if (node.Child1 != Proxy.Free)
                        stack.Push(node.Child1);
                    if (node.Child2 != Proxy.Free)
                        stack.Push(node.Child2);
                    continue;
                }

                if (!callback(ref node.Item))
                    return true;

                any = true;
            }

            return any;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public bool Query(QueryCallbackDelegate callback, in Vector2 point) {
            var stack = new Stack<Proxy>(256);

            stack.Push(_root);

            var any = false;

            while (stack.Count > 0) {
                var proxy = stack.Pop();

                if (proxy == Proxy.Free)
                    continue;

                ref var node = ref _nodes[proxy];

                if (!node.Aabb.Contains(point))
                    continue;

                if (!node.IsLeaf) {
                    if (node.Child1 != Proxy.Free)
                        stack.Push(node.Child1);
                    if (node.Child2 != Proxy.Free)
                        stack.Push(node.Child2);
                    continue;
                }

                if (!callback(ref node.Item))
                    return true;

                any = true;
            }

            return any;
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public IEnumerable<T> Query(Box2 aabb) {
            var stack = new Stack<Proxy>(256);

            stack.Push(_root);

            var any = false;

            while (stack.Count > 0) {
                var proxy = stack.Pop();

                if (proxy == Proxy.Free)
                    continue;

                // note: non-ref stack local copy here
                var node = _nodes[proxy];

                if (!node.Aabb.Intersects(aabb))
                    continue;

                if (!node.IsLeaf) {
                    if (node.Child1 != Proxy.Free)
                        stack.Push(node.Child1);
                    if (node.Child2 != Proxy.Free)
                        stack.Push(node.Child2);
                    continue;
                }

                yield return node.Item;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public IEnumerable<T> Query(Vector2 point) {
            var stack = new Stack<Proxy>(256);

            stack.Push(_root);

            var any = false;

            while (stack.Count > 0) {
                var proxy = stack.Pop();

                if (proxy == Proxy.Free)
                    continue;

                // note: non-ref stack local copy here
                var node = _nodes[proxy];

                if (!node.Aabb.Contains(point))
                    continue;

                if (!node.IsLeaf) {
                    if (node.Child1 != Proxy.Free)
                        stack.Push(node.Child1);
                    if (node.Child2 != Proxy.Free)
                        stack.Push(node.Child2);
                    continue;
                }

                yield return node.Item;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public bool Query(QueryCallbackDelegate callback, in Vector2 start, in Vector2 end) {
            var r = (end - start).Normalized;

            var v = new Vector2(-r.Y, r.X);
            var absV = new Vector2(MathF.Abs(r.Y), MathF.Abs(r.X));

            var aabb = Box2.Combine(start, end);

            var stack = new Stack<Proxy>(256);

            stack.Push(_root);

            var any = false;

            while (stack.Count > 0) {
                var proxy = stack.Pop();

                if (proxy == Proxy.Free)
                    continue;

                ref var node = ref _nodes[proxy];

                if (!node.Aabb.Intersects(aabb))
                    continue;

                var c = node.Aabb.Center;
                var h = (node.Aabb.BottomRight - node.Aabb.TopLeft) * .5f;

                var separation = MathF.Abs(Dot(v, start - c) - Dot(absV, h));

                if (separation > 0)
                    continue;

                if (node.IsLeaf) {
                    any = true;

                    var carryOn = callback(ref node.Item);

                    if (!carryOn)
                        return true;
                }
                else {
                    if (node.Child1 != Proxy.Free)
                        stack.Push(node.Child1);
                    if (node.Child2 != Proxy.Free)
                        stack.Push(node.Child2);
                }
            }

            return any;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Dot(in Vector2 a, in Vector2 b)
            => a.X * b.X + a.Y * b.Y;

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public bool Query(RayQueryCallbackDelegate callback, in Vector2 start, in Vector2 dir) {
            var stack = new Stack<Proxy>(256);

            stack.Push(_root);

            var any = false;

            var ray = new Ray(start, dir);

            while (stack.Count > 0) {
                var proxy = stack.Pop();

                if (proxy == Proxy.Free)
                    continue;

                ref var node = ref _nodes[proxy];

                if (!ray.Intersects(node.Aabb, out var dist, out var hit))
                    continue;

                if (node.IsLeaf) {
                    any = true;

                    var carryOn = callback(ref node.Item, hit, dist);

                    if (!carryOn)
                        return true;
                }
                else {
                    if (node.Child1 != Proxy.Free)
                        stack.Push(node.Child1);
                    if (node.Child2 != Proxy.Free)
                        stack.Push(node.Child2);
                }
            }

            return any;
        }

        public int Height {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _root == Proxy.Free ? 0 : _nodes[_root].Height;
        }

        public int MaxBalance {
            [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
            get {
                var maxBal = 0;

                for (var i = 0; i < Capacity; ++i) {
                    ref var node = ref _nodes[i];
                    if (node.Height <= 1)
                        continue;

                    ref var child1Node = ref _nodes[node.Child1];
                    ref var child2Node = ref _nodes[node.Child2];

                    var bal = Math.Abs(child2Node.Height - child1Node.Height);
                    maxBal = Math.Max(maxBal, bal);
                }

                return maxBal;
            }
        }

        public float AreaRatio {
            [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
            get {
                if (_root == Proxy.Free)
                    return 0;

                ref var rootNode = ref _nodes[_root];
                var rootPeri = Box2.Perimeter(rootNode.Aabb);

                var totalPeri = 0f;

                for (var i = 0; i < Capacity; ++i) {
                    ref var node = ref _nodes[i];
                    if (node.Height < 0)
                        continue;

                    totalPeri += Box2.Perimeter(node.Aabb);
                }

                return totalPeri / rootPeri;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public void RebuildOptimal(int free = 0) {
            var proxies = new Proxy[Count + free];
            var count = 0;

            for (var i = 0; i < Capacity; ++i) {
                ref var node = ref _nodes[i];
                if (node.Height < 0)
                    continue;

                var proxy = (Proxy) i;
                if (node.IsLeaf) {
                    node.Parent = Proxy.Free;
                    proxies[count++] = proxy;
                }
                else
                    FreeNode(proxy);
            }

            while (count > 1) {
                var minCost = float.MaxValue;

                var iMin = -1;
                var jMin = -1;

                for (var i = 0; i < count; ++i) {
                    ref var aabbI = ref _nodes[proxies[i]].Aabb;

                    for (var j = i + 1; j < count; ++j) {
                        ref var aabbJ = ref _nodes[proxies[j]].Aabb;

                        var cost = Box2.Perimeter(Box2.Combine(aabbI, aabbJ));

                        if (cost >= minCost)
                            continue;

                        iMin = i;
                        jMin = j;
                        minCost = cost;
                    }
                }

                var child1 = proxies[iMin];
                var child2 = proxies[jMin];

                ref var child1Node = ref _nodes[child1];
                ref var child2Node = ref _nodes[child2];

                var parent = AllocateNode();
                ref var parentNode = ref _nodes[parent];

                parentNode.Child1 = child1;
                parentNode.Child2 = child2;
                parentNode.Height = Math.Max(child1Node.Height, child2Node.Height) + 1;
                parentNode.Aabb = Box2.Combine(child1Node.Aabb, child2Node.Aabb);
                parentNode.Parent = Proxy.Free;

                child1Node.Parent = parent;
                child2Node.Parent = parent;

                proxies[jMin] = proxies[count - 1];
                proxies[iMin] = parent;
                --count;
            }

            _root = proxies[0];

            Validate();
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        public void ShiftOrigin(in Vector2 newOrigin) {
            for (var i = 0; i < Capacity; ++i) {
                ref var node = ref _nodes[i];
                node.Aabb = new Box2(
                    node.Aabb.BottomLeft - newOrigin,
                    node.Aabb.TopRight - newOrigin
                );
            }
        }

        private Proxy AllocateNode() {
            if (_freeNodes == Proxy.Free) {
                var newNodeCap = GrowthFunc(Capacity);

                if (newNodeCap <= Capacity)
                    throw new InvalidOperationException("Growth function returned invalid new capacity, must be greater than current capacity.");

                EnsureCapacity(newNodeCap);
                Validate();
            }

            var alloc = _freeNodes;
            ref var allocNode = ref _nodes[alloc];
            _freeNodes = allocNode.Parent;
            allocNode.Parent = Proxy.Free;
            allocNode.Child1 = Proxy.Free;
            allocNode.Child2 = Proxy.Free;
            allocNode.Height = 0;
            ++Count;
            return alloc;
        }

        private void EnsureCapacity(int newCapacity) {
            if (newCapacity <= Capacity)
                return;

            var oldNodes = _nodes;

            _nodes = new Node[newCapacity];

            Array.Copy(oldNodes, _nodes, Count);
            Array.Clear(oldNodes, 0, oldNodes.Length);

            var l = Capacity - 1;
            for (var i = Count; i < l; ++i) {
                ref var node = ref _nodes[i];
                node.Parent = (Proxy) (i + 1);
                node.Height = i;
            }

            ref var lastNode = ref _nodes[l];
            lastNode.Parent = Proxy.Free;
            lastNode.Height = -1;
            _freeNodes = (Proxy) Count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FreeNode(Proxy proxy) {
            ref var node = ref _nodes[proxy];
            node.Parent = _freeNodes;
            node.Height = -1;
            node.Child1 = Proxy.Free;
            node.Child2 = Proxy.Free;
            node.Item = default;
            _freeNodes = proxy;
            --Count;
        }

        [Conditional("DEBUG")]
        private void Validate() {
            Validate(_root);

            var freeCount = 0;
            var freeIndex = _freeNodes;
            while (freeIndex != Proxy.Free) {
                Assert(0 <= freeIndex);
                Assert(freeIndex < Capacity);
                freeIndex = _nodes[freeIndex].Parent;
                ++freeCount;
            }

            Assert(Height == ComputeHeight());

            Assert(Count + freeCount == Capacity);
        }

        [Conditional("DEBUG")]
        private void Validate(Proxy proxy) {
            if (proxy == Proxy.Free) return;

            ref var node = ref _nodes[proxy];

            if (proxy == _root)
                Assert(node.Parent == Proxy.Free);

            var child1 = node.Child1;
            var child2 = node.Child2;

            if (node.IsLeaf) {
                Assert(child1 == Proxy.Free);
                Assert(child2 == Proxy.Free);
                Assert(node.Height == 0);
                return;
            }

            Assert(0 <= child1);
            Assert(child1 < Capacity);
            Assert(0 <= child2);
            Assert(child2 < Capacity);

            ref var child1Node = ref _nodes[child1];
            ref var child2Node = ref _nodes[child2];

            Assert(child1Node.Parent == proxy);
            Assert(child2Node.Parent == proxy);

            var height1 = child1Node.Height;
            var height2 = child2Node.Height;

            var height = 1 + Math.Max(height1, height2);

            Assert(node.Height == height);

            ref var aabb = ref node.Aabb;
            Assert(aabb.Contains(child1Node.Aabb));
            Assert(aabb.Contains(child2Node.Aabb));

            Validate(child1);
            Validate(child2);
        }

        [Conditional("DEBUG")]
        private void ValidateHeight(Proxy proxy) {
            if (proxy == Proxy.Free)
                return;

            ref var node = ref _nodes[proxy];

            if (node.IsLeaf) {
                Assert(node.Height == 0);
                return;
            }

            var child1 = node.Child1;
            var child2 = node.Child2;
            ref var child1Node = ref _nodes[child1];
            ref var child2Node = ref _nodes[child2];

            var height1 = child1Node.Height;
            var height2 = child2Node.Height;

            var height = 1 + Math.Max(height1, height2);

            Assert(node.Height == height);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        private void InsertLeaf(Proxy leaf) {
            if (_root == Proxy.Free) {
                _root = leaf;
                _nodes[_root].Parent = Proxy.Free;
                return;
            }

            Validate();

            ref var leafNode = ref _nodes[leaf];

            _nodeLookup[leafNode.Item] = leaf;

            ref var leafAabb = ref leafNode.Aabb;

            var index = _root;
#if DEBUG
            var loopCount = 0;
#endif
            for (;;) {
#if DEBUG
                Assert(loopCount++ < Capacity * 2);
#endif

                ref var indexNode = ref _nodes[index];
                if (indexNode.IsLeaf) break;

                // assert no loops
                Assert(_nodes[indexNode.Child1].Child1 != index);
                Assert(_nodes[indexNode.Child1].Child2 != index);
                Assert(_nodes[indexNode.Child2].Child1 != index);
                Assert(_nodes[indexNode.Child2].Child2 != index);

                var child1 = indexNode.Child1;
                var child2 = indexNode.Child2;
                ref var child1Node = ref _nodes[child1];
                ref var child2Node = ref _nodes[child2];
                ref var indexAabb = ref indexNode.Aabb;
                var indexPeri = Box2.Perimeter(indexAabb);
                var combinedAabb = Box2.Combine(indexAabb, leafAabb);
                var combinedPeri = Box2.Perimeter(combinedAabb);
                var cost = 2 * combinedPeri;
                var inheritCost = 2 * (combinedPeri - indexPeri);

                /*
                var aabb1 = Box2.Combine(leafAabb, child1Node.AABB);
                var aabb1Peri = Box2.Perimeter(aabb1);
                var cost1 = inheritCost
                    + (child1Node.IsLeaf
                        ? aabb1Peri
                        : aabb1Peri - Box2.Perimeter(child1Node.AABB));

                var aabb2 = Box2.Combine(leafAabb, child2Node.AABB);
                var aabb2Peri = Box2.Perimeter(aabb2);
                var cost2 = inheritCost
                    + (child2Node.IsLeaf
                        ? aabb2Peri
                        : aabb2Peri - Box2.Perimeter(child2Node.AABB));
                */
                float cost1, cost2;

                if (child1Node.IsLeaf) {
                    var aabb = Box2.Combine(leafAabb, child1Node.Aabb);
                    cost1 = Box2.Perimeter(aabb) + inheritCost;
                }
                else {
                    var aabb = Box2.Combine(leafAabb, child1Node.Aabb);
                    var oldPeri = Box2.Perimeter(child1Node.Aabb);
                    var newPeri = Box2.Perimeter(aabb);
                    cost1 = (newPeri - oldPeri) + inheritCost;
                }

                if (child2Node.IsLeaf) {
                    var aabb = Box2.Combine(leafAabb, child2Node.Aabb);
                    cost2 = Box2.Perimeter(aabb) + inheritCost;
                }
                else {
                    var aabb = Box2.Combine(leafAabb, child2Node.Aabb);
                    var oldPeri = Box2.Perimeter(child2Node.Aabb);
                    var newPeri = Box2.Perimeter(aabb);
                    cost2 = (newPeri - oldPeri) + inheritCost;
                }

                if (cost < cost1 && cost < cost2)
                    break;

                index = cost1 < cost2 ? child1 : child2;
            }

            var sibling = index;
            ref var siblingNode = ref _nodes[sibling];

            var oldParent = siblingNode.Parent;

            //Validate(); // verify tree valid
            // tree is not touched above this point

            var newParent = AllocateNode();
            ref var newParentNode = ref _nodes[newParent];
            newParentNode.Parent = oldParent;
            newParentNode.Aabb = Box2.Combine(leafAabb, siblingNode.Aabb);
            newParentNode.Height = 1 + siblingNode.Height;

            ref var proxyNode = ref _nodes[leaf];
            if (oldParent != Proxy.Free) {
                ref var oldParentNode = ref _nodes[oldParent];

                if (oldParentNode.Child1 == sibling)
                    oldParentNode.Child1 = newParent;
                else
                    oldParentNode.Child2 = newParent;

                newParentNode.Child1 = sibling;
                newParentNode.Child2 = leaf;
                siblingNode.Parent = newParent;
                proxyNode.Parent = newParent;
            }
            else {
                newParentNode.Child1 = sibling;
                newParentNode.Child2 = leaf;
                siblingNode.Parent = newParent;
                proxyNode.Parent = newParent;
                _root = newParent;
            }

            Balance(proxyNode.Parent);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        private void RemoveLeaf(Proxy leaf) {
            if (leaf == _root) {
                _root = Proxy.Free;
                return;
            }

            Validate();

            ref var leafNode = ref _nodes[leaf];

            _nodeLookup.TryRemove(leafNode.Item, out _);

            var parent = leafNode.Parent;
            ref var parentNode = ref _nodes[parent];
            var grandParent = parentNode.Parent;
            var sibling = parentNode.Child1 == leaf
                ? parentNode.Child2
                : parentNode.Child1;
            ref var siblingNode = ref _nodes[sibling];

            if (grandParent == Proxy.Free) {
                _root = Proxy.Free;
                siblingNode.Parent = Proxy.Free;
                FreeNode(parent);
                return;
            }

            ref var grandParentNode = ref _nodes[grandParent];
            if (grandParentNode.Child1 == parent)
                grandParentNode.Child1 = sibling;
            else
                grandParentNode.Child2 = sibling;
            siblingNode.Parent = grandParent;
            FreeNode(parent);

            Balance(grandParent);
        }

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        private void Balance(Proxy index) {
            while (index != Proxy.Free) {
                index = BalanceStep(index);

                ref var indexNode = ref _nodes[index];

                var child1 = indexNode.Child1;
                var child2 = indexNode.Child2;

                Assert(child1 != Proxy.Free);
                Assert(child2 != Proxy.Free);

                ref var child1Node = ref _nodes[child1];
                ref var child2Node = ref _nodes[child2];

                indexNode.Height = Math.Max(child1Node.Height, child2Node.Height) + 1;
                indexNode.Aabb = Box2.Combine(child1Node.Aabb, child2Node.Aabb);

                index = indexNode.Parent;
            }

            Validate();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Proxy BalanceStep(Proxy iA) {
            ref var a = ref _nodes[iA];

            if (a.IsLeaf || a.Height < 2)
                return iA;

            var iB = a.Child1;
            var iC = a.Child2;
            Assert(iA != iB);
            Assert(iA != iC);
            Assert(iB != iC);

            ref var b = ref _nodes[iB];
            ref var c = ref _nodes[iC];

            var balance = c.Height - b.Height;

            // Rotate C up
            if (balance > 1) {
                var iF = c.Child1;
                var iG = c.Child2;
                Assert(iC != iF);
                Assert(iC != iG);
                Assert(iF != iG);

                ref var f = ref _nodes[iF];
                ref var g = ref _nodes[iG];

                // A <> C

                // this creates a loop ...
                c.Child1 = iA;
                c.Parent = a.Parent;
                a.Parent = iC;

                if (c.Parent == Proxy.Free)
                    _root = iC;
                else {
                    ref var cParent = ref _nodes[c.Parent];
                    if (cParent.Child1 == iA)
                        cParent.Child1 = iC;
                    else {
                        Assert(cParent.Child2 == iA);
                        cParent.Child2 = iC;
                    }
                }

                // Rotate
                if (f.Height > g.Height) {
                    c.Child2 = iF;
                    a.Child2 = iG;
                    g.Parent = iA;
                    a.Aabb = Box2.Combine(b.Aabb, g.Aabb);
                    c.Aabb = Box2.Combine(a.Aabb, f.Aabb);

                    a.Height = Math.Max(b.Height, g.Height) + 1;
                    c.Height = Math.Max(a.Height, f.Height) + 1;
                }
                else {
                    c.Child2 = iG;
                    a.Child2 = iF;
                    f.Parent = iA;
                    a.Aabb = Box2.Combine(b.Aabb, f.Aabb);
                    c.Aabb = Box2.Combine(a.Aabb, g.Aabb);

                    a.Height = Math.Max(b.Height, f.Height) + 1;
                    c.Height = Math.Max(a.Height, g.Height) + 1;
                }

                return iC;
            }

            // Rotate B up
            if (balance < -1) {
                var iD = b.Child1;
                var iE = b.Child2;
                Assert(iB != iD);
                Assert(iB != iE);
                Assert(iD != iE);

                ref var d = ref _nodes[iD];
                ref var e = ref _nodes[iE];

                // A <> B

                // this creates a loop ...
                b.Child1 = iA;
                b.Parent = a.Parent;
                a.Parent = iB;

                if (b.Parent == Proxy.Free)
                    _root = iB;
                else {
                    ref var bParent = ref _nodes[b.Parent];
                    if (bParent.Child1 == iA)
                        bParent.Child1 = iB;
                    else {
                        Assert(bParent.Child2 == iA);
                        bParent.Child2 = iB;
                    }
                }

                // Rotate
                if (d.Height > e.Height) {
                    b.Child2 = iD;
                    a.Child2 = iE;
                    e.Parent = iA;
                    a.Aabb = Box2.Combine(c.Aabb, e.Aabb);
                    b.Aabb = Box2.Combine(a.Aabb, d.Aabb);

                    a.Height = Math.Max(c.Height, e.Height) + 1;
                    b.Height = Math.Max(a.Height, d.Height) + 1;
                }
                else {
                    b.Child2 = iE;
                    a.Child2 = iD;
                    d.Parent = iA;
                    a.Aabb = Box2.Combine(c.Aabb, d.Aabb);
                    b.Aabb = Box2.Combine(a.Aabb, e.Aabb);

                    a.Height = Math.Max(c.Height, d.Height) + 1;
                    b.Height = Math.Max(a.Height, e.Height) + 1;
                }

                return iB;
            }

            return iA;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ComputeHeight()
            => ComputeHeight(_root);

        [MethodImpl(MethodImplOptions.AggressiveOptimization | MethodImplOptions.NoInlining)]
        private int ComputeHeight(Proxy proxy) {
            ref var node = ref _nodes[proxy];
            if (node.IsLeaf)
                return 0;

            return Math.Max(
                ComputeHeight(node.Child1),
                ComputeHeight(node.Child2)
            ) + 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOrUpdate(T item) {
            if (!Update(item))
                Add(item);
        }

        [Conditional("DEBUG")]
        [DebuggerNonUserCode, DebuggerHidden, DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Assert(bool assertion) {
            if (assertion) return;

            var sf = new StackFrame(1, true);
            var msg = $"Assertion failure in {sf.GetMethod().Name} ({sf.GetFileName()}:{sf.GetFileLineNumber()})";
            Debug.Print(msg);
            Debugger.Break();
            throw new InvalidOperationException(msg);
        }

        public string DebuggerDisplay
            => $"Count = {Count}, Capacity = {Capacity}, Height = {Height}";

        private IEnumerable<(Proxy, Node)> DebugAllocatedNodesEnumerable {
            get {
                for (var i = 0; i < _nodes.Length; i++) {
                    var node = _nodes[i];
                    if (!node.IsFree)
                        yield return ((Proxy) i, node);
                }
            }
        }

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        private (Proxy, Node)[] DebugAllocatedNodes {
            get {
                var data = new (Proxy, Node)[Count];
                var i = 0;
                foreach (var x in DebugAllocatedNodesEnumerable)
                    data[i++] = x;

                return data;
            }
        }

    }

}
