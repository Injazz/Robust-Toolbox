using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace Robust.Shared.Serialization
{

    public static partial class RobustNetTypeInfo
    {

        private static readonly Type[] _CustomSymbolTable =
            new SortedSet<Type>(Comparer<Type>.Create((a, b) =>
            {
                var nameCmp = string.CompareOrdinal(a.FullName, b.FullName);
                if (nameCmp == 0)
                {
                    return string.CompareOrdinal(a.Assembly.FullName, b.Assembly.FullName);
                }

                return nameCmp;
            }))
            {
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
                typeof(object),
                typeof(object[]),
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
                typeof(KeyValuePair<,>),
                typeof(DateTime),
                typeof(DateTimeOffset),
                typeof(ArrayList),
                typeof(Hashtable),
                typeof(DictionaryBase),
                typeof(CollectionBase),
                typeof(DictionaryEntry),
                typeof(BitArray),
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
                typeof(ValueTuple),
                typeof(ValueTuple<>),
                typeof(ValueTuple<,>),
                typeof(ValueTuple<,,>),
                typeof(ValueTuple<,,,>),
                typeof(ValueTuple<,,,,>),
                typeof(ValueTuple<,,,,,,>),
                typeof(ValueTuple<,,,,,,,>),
                typeof(Nullable<>),
                typeof(Nullable)
            }.ToArray();

        private static readonly IDictionary<Type, int> _CustomSymbolIndex =
            _CustomSymbolTable.Select(KeyValuePair.Create).ToImmutableDictionary();

    }

}
