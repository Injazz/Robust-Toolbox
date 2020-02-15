using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;
using Robust.Shared.Serialization;

//[module: IoCRegister(typeof(IRobustSerializer), typeof(RobustSerializer))]

namespace Robust.Shared.Serialization
{

    using static RobustNetTypeInfo;

    public sealed partial class BwoinkSerializer
    {

        // TRACE is set on release, so relying on DEBUG

#if DEBUG

        private bool _tracing = false;

        internal bool Tracing
        {
            get => Debugger.IsAttached || _tracing;
            set => _tracing = value;
        }
#endif

        //internal static TextWriter _traceWriter = new TraceWriter();
        private static Lazy<TextWriter> _lazyTraceWriter = new Lazy<TextWriter>(() => new StreamWriter(
            File.Open("robust-serializer.trace", FileMode.Create, FileAccess.Write),
            Encoding.UTF8, 65536, false)
        {
            AutoFlush = false
        }, LazyThreadSafetyMode.ExecutionAndPublication);

        private static TextWriter _traceWriter;

        private static TraceWriter TraceWriterInstance = new TraceWriter();

        internal static TextWriter TraceWriter
        {
            get => _traceWriter ?? _lazyTraceWriter.Value;
            set => _traceWriter = value;
        }

        [Conditional("DEBUG")]
        private void TraceWriteLine(string msg)
        {
            if (!Tracing)
            {
                return;
            }

            var formatted = $"{msg}".ToString();
            /*
                if (formatted.Length > 2000)
                {
                    Debug.WriteLine(formatted);
                }
                */

            //Trace.WriteLine(formatted);
            //System.Console.WriteLine(formatted);
            TraceWriter.WriteLine(formatted);
            TraceWriter.Flush();
        }

        [Conditional("DEBUG")]
        private void TraceIndent()
        {
            if (Tracing)
            {
                Trace.Indent();
            }
        }

        [Conditional("DEBUG")]
        private void TraceUnindent()
        {
            if (Tracing)
            {
                Trace.Unindent();
            }
        }

        public static IEnumerable<Type> GetClassHierarchy(Type t)
        {
            yield return t;

            while (t.BaseType != null)
            {
                t = t.BaseType;
                yield return t;
            }
        }

        private readonly IDictionary<Type, ImmutableSortedSet<FieldInfo>> _fieldCache
            = new Dictionary<Type, ImmutableSortedSet<FieldInfo>>();

        private ImmutableSortedSet<FieldInfo> GetFields(Type type)
        {
            // ReSharper disable once InvertIf
            if (!_fieldCache.TryGetValue(type, out var fields))
            {
                var fieldsBuilder = ImmutableSortedSet.CreateBuilder<FieldInfo>(FieldComparer);
                foreach (var p in GetClassHierarchy(type))
                {
                    foreach (var field in p.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (field.IsLiteral)
                        {
                            continue;
                        }

                        var customAttributes = field.GetCustomAttributes().ToList();
                        if (customAttributes.OfType<IgnoreDataMemberAttribute>().Any()
                            || customAttributes.OfType<NonSerializedAttribute>().Any())
                        {
                            continue;
                        }

                        fieldsBuilder.Add(field);
                    }
                }

                _fieldCache.Add(type, fields = fieldsBuilder.ToImmutableSortedSet());
            }

            return fields;
        }

        private SortedSet<PropertyInfo> GetPublicProperties(Type type)
        {
            var props = new SortedSet<PropertyInfo>(PropertyComparer);
            foreach (var p in GetClassHierarchy(type))
            {
                foreach (var prop in p.GetProperties(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public))
                {
                    if (prop.CanRead && prop.CanWrite)
                    {
                        continue;
                    }

                    props.Add(prop);
                }
            }

            return props;
        }

        private static IEnumerable<object> GetObjects(IEnumerable items)
        {
            foreach (var item in items)
            {
                yield return item;
            }
        }

        internal void WriteNonGenericEnumerable(Stream stream, IEnumerable items, List<object> backRefs)
        {
            if (items == null)
            {
                stream.Write(BitConverter.GetBytes(0)); // null indicator
                return;
            }

            var array = GetObjects(items).ToArray();
            stream.Write(BitConverter.GetBytes(array.Length + 1));
            foreach (var item in array)
            {
                Serialize(stream, item, backRefs);
            }
        }

        internal IEnumerable ReadNonGenericEnumerable(Stream stream, List<object> backRefs, out int len)
        {
            var buf = new byte[4];
            if (stream.Read(buf) == 0)
            {
                throw new InvalidOperationException("Could not read any more data.");
            }

            len = BitConverter.ToInt32(buf);

            if (len == 0)
            {
                return null;
            }

            --len;

            return ReadNonGenericEnumerableInternal(stream, backRefs, len);
        }

        internal IEnumerable ReadNonGenericEnumerableInternal(Stream stream, List<object> backRefs, int len)
        {
            for (var i = 0; i < len; ++i)
            {
                yield return Deserialize(stream, backRefs);
            }
        }

        internal void WriteGenericEnumerable(Stream stream, Type itemType, IEnumerable items, List<object> backRefs, bool skipElemTypeInfo = true)
        {
            if (items == null)
            {
                stream.Write(BitConverter.GetBytes(0));

                return;
            }

            var array = items.Cast<object>().ToArray();
            stream.Write(BitConverter.GetBytes(array.Length + 1));
            if (!skipElemTypeInfo)
            {
                WriteTypeInfo(stream, itemType);
            }

            var inheritorTypes = GetInheritorTypes(itemType);

            if (inheritorTypes.Length == 0)
            {
                foreach (var item in array)
                {
                    Write(stream, itemType, item, backRefs);
                }
            }
            else
            {
                foreach (var item in array)
                {
                    if (item == null)
                    {
                        stream.WriteByte(0);
                        continue;
                    }

                    var specificItemType = item.GetType();

                    if (specificItemType == itemType)
                    {
                        stream.WriteByte(1);
                        Write(stream, itemType, item, backRefs);
                        continue;
                    }

                    var idx = -1;
                    var isGeneric = specificItemType.IsConstructedGenericType;
                    if (isGeneric)
                    {
                        for (var i = 0; i < inheritorTypes.Length; ++i)
                        {
                            var it = inheritorTypes[i];

                            if (it.IsGenericTypeDefinition
                                && it == specificItemType.GetGenericTypeDefinition())
                            {
                                idx = i;
                                break;
                            }
                        }
                    }
                    else
                    {
                        for (var i = 0; i < inheritorTypes.Length; ++i)
                        {
                            var it = inheritorTypes[i];

                            if (it == specificItemType)
                            {
                                idx = i;
                                break;
                            }
                        }
                    }

                    if (idx == -1)
                    {
                        throw new NotImplementedException("Runtime generated types are not yet serializable by this method.");
                    }

                    if (idx >= 250)
                    {
                        throw new NotImplementedException();
                    }

                    stream.WriteByte((byte) (idx + 2));

                    if (isGeneric)
                    {
                        WriteGenericTypeInfo(stream, specificItemType);
                    }

                    Write(stream, specificItemType, item, backRefs);
                }
            }
        }

        private static readonly IDictionary<Type, Type[]> _inheritorCache =
            new Dictionary<Type, Type[]>();

        private static Type[] GetInheritorTypes(Type type)
        {
            var isInterface = type.IsInterface;
            var isBaseType = type.IsClass && !type.IsSealed;

            if (!isInterface && !isBaseType)
            {
                return Type.EmptyTypes;
            }

            // ReSharper disable once InvertIf
            if (!_inheritorCache.TryGetValue(type, out var inheritorTypes))
            {
                var inheritors = isInterface ? FindImplementingTypes(type) : FindInheritingTypes(type);

                inheritorTypes = inheritors
                    .Select(t =>
                    {
                        var asmIndex = GetAssemblyIndex(t.Assembly);
                        if (asmIndex == null)
                            return default;

                        return (asmIndex: asmIndex.Value, type: t);
                    })
                    .Where(p => p.asmIndex != 0)
                    .OrderBy(t => (t.asmIndex, t.type.MetadataToken))
                    .Select(t => t.type)
                    .ToArray();

                _inheritorCache.Add(type, inheritorTypes);

                if (inheritorTypes.Length == 0)
                {
                    // TraceWriteLine is not static
                    Trace.WriteLine($"{type.FullName} might need to be sealed.");
                }

                return inheritorTypes;
            }

            return inheritorTypes ?? Type.EmptyTypes;
        }

        private static int ByteArrayCompare(byte[] a, byte[] b)
        {
            var lenCompare = a.Length.CompareTo(b.Length);
            if (lenCompare != 0)
            {
                return lenCompare;
            }

            var l = a.Length;
            for (var i = 0; i < l; ++i)
            {
                var valCompare = a[i].CompareTo(b[i]);
                if (valCompare != 0)
                {
                    return valCompare;
                }
            }

            return 0;
        }

        internal static IEnumerable<T> ReadGenericEnumerableGeneric<T>(BwoinkSerializer szr, Stream stream, List<object> backRefs, StrongBox<int> lengthBox)
        {
            var buf = new byte[4];
            if (stream.Read(buf) == 0)
            {
                throw new InvalidOperationException("Could not read any more data.");
            }

            var len = BitConverter.ToInt32(buf);

            if (len == 0)
            {
                return null;
            }

            --len;

            lengthBox.Value = len;

            return ReadGenericEnumerableGenericInternal<T>(szr, stream, backRefs, len);
        }

        internal static IEnumerable<T> ReadGenericEnumerableGenericInternal<T>(BwoinkSerializer szr, Stream stream, List<object> backRefs, int len)
        {
            var type = typeof(T);
            var inheritorTypes = GetInheritorTypes(type);
            if (inheritorTypes.Length == 0)
            {
                for (var i = 0; i < len; ++i)
                {
                    yield return (T) szr.Read(stream, type, backRefs);
                }
            }
            else
            {
                for (var i = 0; i < len; ++i)
                {
                    var idx = stream.ReadByte();
                    if (idx == -1)
                    {
                        throw new InvalidOperationException("Could not read any more data.");
                    }

                    if (idx == 0)
                    {
                        yield return default;
                    }
                    else
                    {
                        if (idx == 1)
                        {
                            yield return (T) szr.Read(stream, type, backRefs);
                        }
                        else
                        {
                            var inheritor = inheritorTypes[idx - 2];
                            yield return (T) szr.Read(stream, inheritor, backRefs);
                        }
                    }
                }
            }
        }

        private delegate IEnumerable ReadGenericEnumerableDelegate(BwoinkSerializer szr, Stream stream, List<object> backRefs, StrongBox<int> lengthBox);

        private IDictionary<Type, ReadGenericEnumerableDelegate> _readGenericEnumerableDelegateCache =
            new Dictionary<Type, ReadGenericEnumerableDelegate>();

        internal IEnumerable ReadGenericEnumerable(Type type, Stream stream, List<object> backRefs, out int len)
        {
            if (!_readGenericEnumerableDelegateCache.TryGetValue(type, out var dlg))
            {
                dlg = CreateDelegate<ReadGenericEnumerableDelegate, BwoinkSerializer>(
                    BindingFlags.NonPublic | BindingFlags.Static,
                    nameof(ReadGenericEnumerableGeneric), type);
            }

            var lengthBox = new StrongBox<int>();
            var e = dlg(this, stream, backRefs, lengthBox);
            len = lengthBox.Value;
            return e;
        }

        private Dictionary<Type, (MethodInfo Value, MethodInfo HasValue)> _nullableGetters =
            new Dictionary<Type, (MethodInfo, MethodInfo)>();

        internal void Write(Stream stream, Type type, object obj, List<object> backRefs)
        {
            TraceWriteLine($"Writing {type.Name}");
            TraceIndent();
            try
            {
#if DEBUG
                if (obj != null)
                {
                    var valType = obj.GetType();
                    if (type != valType)
                    {
                        throw new NotImplementedException("Correct type should be passed to Write calls");
                    }
                }
#endif
                if (type.IsConstructedGenericType)
                {
                    var gtd = type.GetGenericTypeDefinition();

                    if (gtd == typeof(Nullable<>))
                    {
                        WriteNullable(stream, type, obj, backRefs);
                        return;
                    }
                }
                else if (type.IsArray)
                {
                    if (type.HasElementType)
                    {
                        var elemType = type.GetElementType();
                        if (elemType != typeof(object))
                        {
                            WriteGenericEnumerable(stream, type.GetElementType(), (IEnumerable) obj, backRefs);
                            return;
                        }
                    }

                    WriteNonGenericEnumerable(stream, (IEnumerable) obj, backRefs);
                    return;
                }
                else if (type.IsPrimitive)
                {
                    var bytes = (byte[]) BitConverter.GetBytes((dynamic) obj);
                    stream.Write(bytes);
                    return;
                }
                else if (type == typeof(string))
                {
                    if (obj == null)
                    {
                        stream.Write(BitConverter.GetBytes(0));
                        return;
                    }

                    if (TryGetStringId((string) obj, out var stringId))
                    {
                        stream.Write(BitConverter.GetBytes(int.MaxValue)); // shared string
                        var intStringId = (stringId.AssemblyIndex << 24) | stringId.StringIndex;
                        stream.Write(BitConverter.GetBytes(intStringId));
                        return;
                    }

                    // shared string
                    var bytes = Encoding.UTF8.GetBytes((string) obj);
                    stream.Write(BitConverter.GetBytes(bytes.Length + 1));
                    stream.Write(bytes);
                    return;
                }

                if (typeof(IEnumerable).IsAssignableFrom(type))
                {
                    WriteEnumerable(stream, type, obj, backRefs);
                }

                if (obj == null)
                {
                    if (type.IsValueType)
                    {
                        throw new NotImplementedException();
                    }

                    stream.Write(BitConverter.GetBytes(0));
                    return;
                }

                foreach (var field in GetFields(type))
                {
                    TraceWriteLine($"Writing {type.Name}.{field.Name}");
                    TraceIndent();
                    try
                    {
                        //var typedRef = TypedReference.MakeTypedReference(obj, new[] {field});
                        var ft = field.FieldType;
                        var isValType = ft.IsValueType;
                        var value = field.GetValue(obj);

                        if (!isValType)
                        {
                            if (value != null)
                            {
                                var valType = value.GetType();
                                ft = valType;
                            }

                            if (!backRefs.Contains(value))
                            {
                                backRefs.Add(value);
                            }
                        }
                        else if (ft.IsPrimitive)
                        {
                            var bytes = (byte[]) BitConverter.GetBytes((dynamic) value);
                            stream.Write(bytes);
                            continue;
                        }

                        if (ft.IsArray)
                        {
                            WriteGenericEnumerable(stream, ft.GetElementType(), (IEnumerable) value, backRefs);
                            continue;
                        }

                        if (ft == typeof(string))
                        {
                            if (value == null)
                            {
                                stream.Write(BitConverter.GetBytes(0));
                                continue;
                            }

                            if (TryGetStringId((string) value, out var stringId))
                            {
                                stream.Write(BitConverter.GetBytes(int.MaxValue)); // shared string
                                var intStringId = (stringId.AssemblyIndex << 24) | stringId.StringIndex;
                                stream.Write(BitConverter.GetBytes(intStringId));
                                continue;
                            }

                            // shared string
                            var bytes = Encoding.UTF8.GetBytes((string) value);
                            stream.Write(BitConverter.GetBytes(bytes.Length + 1));
                            stream.Write(bytes);
                            continue;
                        }

                        if (typeof(IEnumerable).IsAssignableFrom(ft))
                        {
                            WriteEnumerable(stream, ft, value, backRefs);
                            continue;
                        }

                        if (!isValType)
                        {
                            if (value == null)
                            {
                                stream.Write(BitConverter.GetBytes(0));
                                continue;
                            }

                            WriteTypeInfo(stream, value.GetType());
                        }
                        else
                        {
                            if (ft.IsConstructedGenericType)
                            {
                                var gtd = ft.GetGenericTypeDefinition();

                                if (gtd == typeof(Nullable<>))
                                {
                                    WriteNullable(stream, ft, value, backRefs);
                                    continue;
                                }
                            }
                        }

                        Write(stream, ft, value, backRefs);
                    }
                    finally
                    {
                        TraceUnindent();
                    }
                }
            }
            finally
            {
                TraceUnindent();
            }
        }

        private void WriteEnumerable(Stream stream, Type type, object obj, List<object> backRefs)
        {
            var typedEnum = type.GetInterfaces()
                .FirstOrDefault(intf => intf.IsConstructedGenericType
                    && intf.Namespace == "System.Collections.Generic" && intf.Name.StartsWith("IEnumerable`1")
                    && intf.GetGenericTypeDefinition().Name == "IEnumerable`1");
            if (typedEnum != null)
            {
                WriteGenericEnumerable(stream, typedEnum.GenericTypeArguments[0], (IEnumerable) obj, backRefs);
            }
            else
            {
                WriteNonGenericEnumerable(stream, (IEnumerable) obj, backRefs);
            }

            return;
        }

        private void WriteNullable(Stream stream, Type type, object obj, List<object> backRefs)
        {
            var underType = type.GetGenericArguments()[0];

            MethodInfo valueGetter, hasValueGetter;
            if (_nullableGetters.TryGetValue(type, out var getters))
            {
                (valueGetter, hasValueGetter) = getters;
            }
            else
            {
                var valueProp = type.GetProperty("Value");
                // ReSharper disable once PossibleNullReferenceException
                valueGetter = valueProp.GetMethod;
                // ReSharper disable once PossibleNullReferenceException
                hasValueGetter = type.GetProperty("HasValue").GetMethod;
                _nullableGetters[type] = (valueGetter, hasValueGetter);
            }

            var hasValue = obj != null && (bool) hasValueGetter.Invoke(obj, null);

            if (hasValue)
            {
                stream.WriteByte(1);
                var value = valueGetter.Invoke(obj, null);
                Write(stream, underType, value, backRefs);
            }
            else
            {
                stream.WriteByte(0);
            }
        }

        internal static unsafe object CreateValueTypeGeneric<T>(byte[] bytes) where T : struct
        {
            fixed (void* p = &bytes[0])
            {
                return Unsafe.Read<T>(p);
            }
        }

        private delegate object CreateValueTypeDelegate(byte[] bytes);

        private static Dictionary<Type, CreateValueTypeDelegate> _createValueTypeDelegateCache =
            new Dictionary<Type, CreateValueTypeDelegate>();

        internal static TDelegate CreateDelegate<TDelegate>(Type type, BindingFlags bindingFlags, string name, params Type[] genTypes) where TDelegate : Delegate =>
            (TDelegate) type
                .GetMethod(name, bindingFlags)
                ?.MakeGenericMethod(genTypes)
                .CreateDelegate(typeof(TDelegate));

        internal static TDelegate CreateDelegate<TDelegate, TSource>(BindingFlags bindingFlags, string name, params Type[] genTypes) where TDelegate : Delegate =>
            CreateDelegate<TDelegate>(typeof(TSource), bindingFlags, name, genTypes);

        internal static object CreateValueType(Type type, byte[] bytes)
        {
            if (!_createValueTypeDelegateCache.TryGetValue(type, out var dlg))
            {
                dlg = CreateDelegate<CreateValueTypeDelegate, BwoinkSerializer>(
                    BindingFlags.NonPublic | BindingFlags.Static,
                    "CreateValueTypeGeneric",
                    type
                );
            }

            return dlg(bytes);
        }

        internal Array MakeGenericArray(IEnumerable objects, Type itemType, int size)
        {
            if (objects == null)
            {
                return null;
            }

            if (size < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(size));
            }

            var array = Array.CreateInstance(itemType, size);

            if (size == 0)
            {
                return array;
            }

            var e = objects.GetEnumerator();

            for (var i = 0; i < size; i++)
            {
                if (!e.MoveNext())
                {
                    throw new NotImplementedException("Mismatched length and enumeration.");
                }

                array.SetValue(e.Current, i);
            }

            return array;
        }

        private static Dictionary<Type, ConstructorInfo> _ctorCache =
            new Dictionary<Type, ConstructorInfo>();

        private static readonly Comparer<FieldInfo> FieldComparer = Comparer<FieldInfo>.Create((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

        private static readonly Comparer<PropertyInfo> PropertyComparer = Comparer<PropertyInfo>.Create((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

        internal object Read(Stream stream, Type type, List<object> backRefs)
        {
            TraceWriteLine($"Reading {type?.Name ?? "null"}");
            TraceIndent();
            try
            {
                if (type.IsConstructedGenericType)
                {
                    var gtd = type.GetGenericTypeDefinition();

                    if (gtd == typeof(Nullable<>))
                    {
                        return ReadNullable(stream, type, backRefs);
                    }
                }
                else if (type.IsArray)
                {
                    if (type.HasElementType)
                    {
                        var elemType = type.GetElementType();
                        if (elemType == typeof(object))
                        {
                            return MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs, out var len), elemType, len);
                        }
                        else
                        {
                            return MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs, out var len), elemType, len);
                        }
                    }
                    else
                    {
                        // do non-generic arrays exist anymore?
                        return MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs, out var len), typeof(object), len);
                    }
                }
                else if (type == typeof(string))
                {
                    string str;
                    var buf = new byte[4];
                    if (stream.Read(buf) == 0)
                    {
                        throw new InvalidOperationException("Could not read any more data.");
                    }

                    var len = BitConverter.ToInt32(buf);
                    if (len == 0)
                    {
                        // null
                        return null;
                    }

                    if (len == int.MaxValue)
                    {
                        // shared string
                        if (stream.Read(buf) == 0)
                        {
                            throw new InvalidOperationException("Could not read any more data.");
                        }

                        var stringId = BitConverter.ToInt32(buf);
                        var asmIndex = (byte) (stringId >> 24);
                        var stringIdx = stringId & 0x00FFFFFF;
                        if (!TryGetString((asmIndex, stringIdx), out str))
                        {
                            throw new InvalidOperationException($"Shared string {asmIndex}:{stringId} missing!");
                        }

                        return str;
                    }

                    --len;

                    if (len == 0)
                    {
                        return "";
                    }

                    buf = new byte[len];
                    if (len >= 32768) // TODO: when it's stable remove this
                    {
                        throw new NotSupportedException();
                    }

                    if (stream.Read(buf) == 0)
                    {
                        throw new InvalidOperationException("Could not read any more data.");
                    }

                    str = Encoding.UTF8.GetString(buf);
                    return str;
                }

                if (typeof(IEnumerable).IsAssignableFrom(type))
                {
                    return ReadEnumerable(stream, type, backRefs);
                }

                {
                    var obj = FormatterServices.GetUninitializedObject(type);
                    foreach (var field in GetFields(type))
                    {
                        TraceWriteLine($"Reading {type.Name}.{field.Name}");
                        TraceIndent();
                        try
                        {
                            var ft = field.FieldType;
                            //var typedRef = TypedReference.MakeTypedReference(obj, new[] {field});
                            var isValType = ft.IsValueType;
                            if (!isValType)
                            {
                                // ok
                            }
                            else if (ft.IsPrimitive)
                            {
                                var proto = FormatterServices.GetUninitializedObject(ft);
                                var size = ((byte[]) BitConverter.GetBytes((dynamic) proto)).Length;
                                var bytes = new byte[size];
                                if (stream.Read(bytes) == 0)
                                {
                                    throw new InvalidOperationException("Could not read any more data.");
                                }

                                //field.SetValueDirect(typedRef, CreateValueType(ft, bytes));
                                field.SetValue(obj, CreateValueType(ft, bytes));
                                continue;
                            }

                            if (ft.IsConstructedGenericType)
                            {
                                var gtd = ft.GetGenericTypeDefinition();

                                if (gtd == typeof(Nullable<>))
                                {
                                    field.SetValue(obj, ReadNullable(stream, ft, backRefs));
                                    continue;
                                }
                            }

                            if (ft.IsArray)
                            {
                                var elemType = ft.GetElementType();
                                var array = MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs, out var len), elemType, len);
                                //field.SetValueDirect(typedRef, MakeGenericArray(items, elemType));
                                field.SetValue(obj, array);
                                continue;
                            }

                            if (ft == typeof(string))
                            {
                                string str;
                                var buf = new byte[4];
                                if (stream.Read(buf) == 0)
                                {
                                    throw new InvalidOperationException("Could not read any more data.");
                                }

                                var len = BitConverter.ToInt32(buf);
                                if (len == 0)
                                {
                                    // null
                                    continue;
                                }

                                if (len == int.MaxValue)
                                {
                                    // shared string
                                    if (stream.Read(buf) == 0)
                                    {
                                        throw new InvalidOperationException("Could not read any more data.");
                                    }

                                    var stringId = BitConverter.ToInt32(buf);
                                    var asmIndex = (byte) (stringId >> 24);
                                    var stringIdx = stringId & 0x00FFFFFF;
                                    if (!TryGetString((asmIndex, stringIdx), out str))
                                    {
                                        throw new InvalidOperationException($"Shared string {asmIndex}:{stringId} missing!");
                                    }

                                    field.SetValue(obj, str);

                                    continue;
                                }

                                --len;

                                if (len == 0)
                                {
                                    field.SetValue(obj, "");
                                }

                                buf = new byte[len];
                                if (len >= 32768) // TODO: when it's stable remove this
                                {
                                    throw new NotSupportedException();
                                }

                                if (stream.Read(buf) == 0)
                                {
                                    throw new InvalidOperationException("Could not read any more data.");
                                }

                                str = Encoding.UTF8.GetString(buf);
                                field.SetValue(obj, str);
                                continue;
                            }

                            if (typeof(IEnumerable).IsAssignableFrom(ft))
                            {
                                var value = ReadEnumerable(stream, ft, backRefs);

                                if (value != null)
                                {
                                    field.SetValue(obj, value);
                                }

                                continue;
                            }

                            //field.SetValueDirect(typedRef, Read(stream, ft, backRefs));
                            var it = ft;
                            if (!isValType)
                            {
                                it = ReadTypeInfo(stream);
                                if (it == null)
                                {
                                    //field.SetValue(obj, null);
                                    continue;
                                }
                            }

                            field.SetValue(obj, Read(stream, it, backRefs));
                        }
                        finally
                        {
                            TraceUnindent();
                        }
                    }

                    return obj;
                }
            }
            finally
            {
                TraceUnindent();
            }

            if (type == null)
            {
                return null;
            }
        }

        private object ReadEnumerable(Stream stream, Type type, List<object> backRefs)
        {
            var typedEnum = type.GetInterfaces()
                .FirstOrDefault(t => t.IsConstructedGenericType
                    && t.Namespace == "System.Collections.Generic" && t.Name.StartsWith("IEnumerable`1")
                    && t.GetGenericTypeDefinition().Name == "IEnumerable`1");
            if (typedEnum != null)
            {
                if (_ctorCache.TryGetValue(type, out var ctor))
                {
                    var paramType = ctor.GetParameters()[0].ParameterType;
                    if (paramType.IsArray)
                    {
                        var elemType = paramType.GetElementType();
                        //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                        var array = MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs, out var len), elemType, len);
                        if (array == null)
                        {
                            return null;
                        }

                        return ctor.Invoke(new object[]
                            {
                                array
                            }
                        );
                    }
                    else
                    {
                        var elemType = paramType.GenericTypeArguments[0];
                        //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                        var array = MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs, out var len), elemType, len);
                        if (array == null)
                        {
                            return null;
                        }

                        return ctor.Invoke(new object[]
                            {
                                array
                            }
                        );
                    }
                }

                // variable name scope
                {
                    var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    var elemType = typedEnum.GenericTypeArguments[0];
                    var arrayType = elemType.MakeArrayType();
                    ctor = ctors.FirstOrDefault(c =>
                    {
                        var ps = c.GetParameters();
                        return ps.Length == 1 && ps[0].ParameterType == typedEnum;
                    });

                    if (ctor != null)
                    {
                        _ctorCache[type] = ctor;
                        //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                        var array = MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs, out var len), elemType, len);
                        if (array != null)
                        {
                            return ctor.Invoke(new object[]
                                {
                                    array
                                }
                            );
                        }

                        return null;
                    }

                    ctor = ctors.FirstOrDefault(c =>
                    {
                        var ps = c.GetParameters();
                        return ps.Length == 1 && (ps[0].ParameterType == arrayType
                            );
                    });

                    if (ctor != null)
                    {
                        //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                        _ctorCache[type] = ctor;
                        var array = MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs, out var len), elemType, len);
                        if (array == null)
                        {
                            return null;
                        }

                        return ctor.Invoke(new object[]
                            {
                                array
                            }
                        );
                    }

                    throw new NotSupportedException($"{type.FullName}");
                }
            }
            else
            {
                if (_ctorCache.TryGetValue(type, out var ctor))
                {
                    var paramType = ctor.GetParameters()[0].ParameterType;
                    if (paramType.IsArray)
                    {
                        //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                        return ctor.Invoke(new object[]
                            {
                                MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs, out var len), typeof(object), len)
                            }
                        );
                    }
                    else
                    {
                        //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                        return ctor.Invoke(new object[]
                            {
                                MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs, out var len), typeof(object), len)
                            }
                        );
                    }
                }

                var ctors = type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var untypedEnum = typeof(IEnumerable);

                ctor = ctors.FirstOrDefault(c =>
                {
                    var ps = c.GetParameters();
                    return ps.Length == 1 && ps[0].ParameterType == untypedEnum;
                });

                if (ctor != null)
                {
                    _ctorCache[type] = ctor;
                    //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                    return ctor.Invoke(new object[]
                        {
                            MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs, out var len), typeof(object), len)
                        }
                    );
                }

                ctor = ctors.FirstOrDefault(c =>
                {
                    var ps = c.GetParameters();
                    return ps.Length == 1 && ps[0].ParameterType.IsArray && ps[0].ParameterType == typeof(object[]);
                });

                if (ctor != null)
                {
                    _ctorCache[type] = ctor;
                    //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                    return ctor.Invoke(new object[]
                        {
                            MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs, out var len), typeof(object), len)
                        }
                    );
                }

                throw new NotSupportedException($"{type.FullName}");
            }

            throw new NotImplementedException();
        }

        // ReSharper disable once ConvertNullableToShortForm
        // ReSharper disable once RedundantExplicitNullableCreation
        private static object NullableConstructorGeneric<T>(object value) where T : struct => new Nullable<T>((T) value);

        private delegate object NullableConstructor(object value);

        private readonly IDictionary<Type, NullableConstructor> _nullableConstructors = new Dictionary<Type, NullableConstructor>();

        private object ReadNullable(Stream stream, Type type, List<object> backRefs)
        {
            var obj = FormatterServices.GetUninitializedObject(type);
            var underType = type.GetGenericArguments()[0];

            var hasValue = stream.ReadByte() != 0;

            if (!hasValue)
            {
                return null;
            }

            if (!_nullableConstructors.TryGetValue(type, out var ctor))
            {
                ctor = CreateDelegate<NullableConstructor, BwoinkSerializer>(
                    BindingFlags.NonPublic | BindingFlags.Static,
                    nameof(NullableConstructorGeneric), type.GenericTypeArguments[0]);
                _nullableConstructors.Add(type, ctor);
            }

            return ctor(Read(stream, underType, backRefs));
        }

        internal void Serialize<T>(Stream stream, T obj, List<object> backRefs)
        {
            var type = typeof(T);

            if (obj == null && !type.IsValueType)
            {
                stream.WriteByte(0);
                return;
            }

            if (type == typeof(object))
            {
                if (obj == null)
                {
                    stream.WriteByte(0);
                    return;
                }

                type = obj.GetType();
                if (type == typeof(object))
                {
                    throw new NotSupportedException("Synchronization objects (plain empty objects) are not supported and not serialized.");
                }
            }

            if (obj != null)
            {
                // use *actual* type
                type = obj.GetType();
            }

            var start = stream.Position;

            WriteTypeInfo(stream, type);

            Write(stream, type, obj, backRefs);

            var finish = stream.Position;

            var byteCount = finish - start;
        }

        internal void SerializeGeneric<T>(Stream stream, T obj, List<object> backRefs) =>
            Serialize(stream, (object) obj, backRefs);

        internal static IEnumerable<Type> FindImplementingTypes(Type intf)
        {
            foreach (var asm in GetAssemblies())
            {
                foreach (var type in asm.DefinedTypes)
                {
                    if (type.GetInterfaces().Contains(intf))
                    {
                        yield return type;
                    }
                }
            }
        }

        internal static IEnumerable<Type> FindInheritingTypes(Type baseType)
        {
            foreach (var asm in GetAssemblies())
            {
                foreach (var type in asm.DefinedTypes)
                {
                    if (type.BaseType == baseType)
                    {
                        yield return type;

                        if (!type.IsSealed)
                        {
                            foreach (var subType in FindInheritingTypes(type))
                            {
                                yield return subType;
                            }
                        }
                    }
                }
            }
        }

        internal T Deserialize<T>(Stream stream, List<object> backRefs)
        {
            if (typeof(T) == typeof(object))
            {
                return (T) Deserialize(stream, backRefs);
            }

            T obj;

            var start = stream.Position;

            var type = ReadTypeInfo(stream);
            if (type == null)
            {
                obj = default;
            }

            else
            {
                obj = (T) Read(stream, type, backRefs);
            }

            var finish = stream.Position;

            var byteCount = finish - start;

            return obj;
        }

        internal object Deserialize(Stream stream, List<object> backRefs)
        {
            object obj;

            var start = stream.Position;

            var type = ReadTypeInfo(stream);
            if (type == null)
            {
                obj = null;
            }

            else
            {
                obj = Read(stream, type, backRefs);
            }

            var finish = stream.Position;

            var byteCount = finish - start;
            return obj;
        }

    }

}
