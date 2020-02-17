using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Timing;

namespace Robust.Shared.Serialization
{

    public static partial class RobustNetTypeInfo
    {

        private static readonly Type[] _CustomSymbolTable =
            new SortedSet<Type>(Comparer<Type>.Create((a, b) =>
            {
                var nameCmp = string.CompareOrdinal(a.FullName, b.FullName);
                return nameCmp == 0
                    ? string.CompareOrdinal(a.Assembly.FullName, b.Assembly.FullName)
                    : nameCmp;
            }))
            {
                // primitives
                typeof(object),
                typeof(bool),
                typeof(sbyte),
                typeof(byte),
                typeof(short),
                typeof(ushort),
                typeof(int),
                typeof(uint),
                typeof(long),
                typeof(ulong),
                typeof(float),
                typeof(double),
                typeof(char),
                typeof(string),
                typeof(decimal),
                // default types and GTDs
                typeof(ValueType),
                typeof(Nullable<>),
                typeof(Nullable),
                typeof(object[]),
                typeof(byte[]),
                typeof(short[]),
                typeof(ushort[]),
                typeof(int[]),
                typeof(uint[]),
                typeof(long[]),
                typeof(ulong[]),
                typeof(char[]),
                typeof(Array),
                typeof(List<>),
                typeof(IEnumerable),
                typeof(IEnumerable<>),
                typeof(Queue),
                typeof(Stack),
                typeof(Collection<>),
                typeof(Dictionary<,>),
                typeof(HashSet<>),
                typeof(IDictionary<,>),
                typeof(IReadOnlyList<>),
                typeof(IReadOnlyDictionary<,>),
                typeof(IReadOnlyCollection<>),
                typeof(ISet<>),
                typeof(SortedSet<>),
                typeof(SortedList<,>),
                typeof(SortedDictionary<,>),
                typeof(LinkedList<>),
                typeof(LinkedListNode<>),
                typeof(DateTime),
                typeof(DateTimeOffset),
                typeof(ArrayList),
                typeof(Hashtable),
                typeof(DictionaryBase),
                typeof(CollectionBase),
                typeof(DictionaryEntry),
                typeof(BitArray), // TODO: not optimally packed
                typeof(Queue),
                typeof(Stack),
                typeof(SortedList),
                typeof(IList),
                typeof(IStructuralComparable),
                typeof(IStructuralEquatable),
                typeof(ReadOnlyCollectionBase),
                typeof(BitVector32),
                typeof(HybridDictionary),
                typeof(INotifyCollectionChanged),
                typeof(IOrderedDictionary),
                typeof(ListDictionary),
                typeof(OrderedDictionary),
                typeof(StringCollection),
                typeof(StringDictionary),
                typeof(StringEnumerator),
                typeof(NameObjectCollectionBase),
                typeof(NameValueCollection),
                // immutable types
                typeof(IImmutableList<>),
                typeof(ImmutableArray<>),
                typeof(ImmutableList<>),
                typeof(IImmutableDictionary<,>),
                typeof(ImmutableDictionary<,>),
                typeof(ImmutableSortedDictionary<,>),
                typeof(IImmutableSet<>),
                typeof(ImmutableHashSet<>),
                typeof(ImmutableSortedSet<>),
                typeof(IImmutableQueue<>),
                typeof(ImmutableQueue<>),
                typeof(IImmutableStack<>),
                typeof(ImmutableStack<>),
                // tuples
                typeof(KeyValuePair<,>),
                typeof(Tuple<>),
                typeof(Tuple<,>),
                typeof(Tuple<,,>),
                typeof(Tuple<,,,>),
                typeof(Tuple<,,,,>),
                typeof(Tuple<,,,,,>),
                typeof(Tuple<,,,,,,>),
                typeof(Tuple<,,,,,,,>),
                typeof(ValueTuple),
                typeof(ValueTuple<>),
                typeof(ValueTuple<,>),
                typeof(ValueTuple<,,>),
                typeof(ValueTuple<,,,>),
                typeof(ValueTuple<,,,,>),
                typeof(ValueTuple<,,,,,,>),
                typeof(ValueTuple<,,,,,,,>),
                // common types
                typeof(IReadOnlyList<byte>),
                typeof(IList<byte>),
                typeof(List<byte>),
                typeof(GameState),
                typeof(GameTick),
                typeof(GameStateMapData),
                typeof(List<EntityUid>),
                typeof(IList<EntityUid>),
                typeof(IReadOnlyList<EntityUid>),
                typeof(IEnumerable<EntityUid>),
                typeof(EntityUid[]),
                typeof(EntityUid?),
                typeof(EntityUid),
                typeof(List<EntityState>),
                typeof(IList<EntityState>),
                typeof(IReadOnlyList<EntityState>),
                typeof(IEnumerable<EntityState>),
                typeof(EntityState[]),
                typeof(EntityState),
                typeof(List<ComponentChanged>),
                typeof(ComponentChanged),
                typeof(List<ComponentState>),
                typeof(ComponentState),
                typeof(List<PlayerState>),
                typeof(IList<PlayerState>),
                typeof(IReadOnlyList<PlayerState>),
                typeof(IEnumerable<PlayerState>),
                typeof(PlayerState[]),
                typeof(PlayerState),
                typeof(GameState),
                typeof(Dictionary<MapId, GridId>),
                typeof(Dictionary<GridId, GameStateMapData.GridCreationDatum>),
                typeof(GameStateMapData.GridCreationDatum),
                typeof(Dictionary<GridId, GameStateMapData.GridDatum>),
                typeof(GameStateMapData.GridDatum),
                typeof(List<GameStateMapData.ChunkDatum>),
                typeof(GameStateMapData.ChunkDatum),
                typeof(List<MapCoordinates>),
                typeof(MapCoordinates),
                typeof(List<GridId>),
                typeof(GridId),
                typeof(List<MapId>),
                typeof(MapId),
                typeof(MapIndices),
                typeof(Tile),
                typeof(NetSessionId),
                typeof(SessionStatus),
                typeof(List<Box2>),
                typeof(IList<Box2>),
                typeof(IReadOnlyList<Box2>),
                typeof(IEnumerable<Box2>),
                typeof(Box2[]),
                typeof(Box2),
                typeof(List<Vector2>),
                typeof(IList<Vector2>),
                typeof(IReadOnlyList<Vector2>),
                typeof(IEnumerable<Vector2>),
                typeof(Vector2[]),
                typeof(Vector2),
            }.ToArray();

        private static readonly IDictionary<Type, int> _CustomSymbolIndex =
            _CustomSymbolTable.Select(KeyValuePair.Create).ToImmutableDictionary();

    }

}
