using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using Robust.Shared.Interfaces.Serialization;

namespace Robust.Shared.Serialization
{

    using static RobustNetTypeInfo;

    public class RobustSerializer : IRobustSerializer
    {

        public void Initialize()
        {
        }

        public void Serialize(Stream stream, object obj)
            => Serialize(stream, obj, new List<object>());

        public T Deserialize<T>(Stream stream)
            => Deserialize<T>(stream, new List<object>());

        public object Deserialize(Stream stream)
            => Deserialize(stream, new List<object>());

        private IDictionary<Type, FieldInfo[]> _fieldCache
            = new Dictionary<Type, FieldInfo[]>();

        public static IEnumerable<Type> GetClassHierarchy(Type t)
        {
            yield return t;

            while (t.BaseType != null)
            {
                t = t.BaseType;
                yield return t;
            }
        }

        private SortedSet<FieldInfo> GetFields(Type type)
        {
            var fields = new SortedSet<FieldInfo>(FieldComparer);
            foreach (var p in GetClassHierarchy(type))
            {
                foreach (var field in p.GetFields(BindingFlags.Instance | BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.IsLiteral)
                    {
                        continue;
                    }

                    if (field.GetCustomAttributes<IgnoreDataMemberAttribute>().Any())
                    {
                        continue;
                    }

                    fields.Add(field);
                }
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
                return;
            }

            stream.WriteByte(1); // not null indicator
            var array = GetObjects(items).ToArray();
            stream.Write(BitConverter.GetBytes(array.Length));
            foreach (var item in array)
            {
                Serialize(stream, item, backRefs);
            }
        }

        internal IEnumerable ReadNonGenericEnumerable(Stream stream, List<object> backRefs)
        {
            var notNullIndicator = stream.ReadByte();
            if (notNullIndicator == 0)
            {
                return null;
            }

            var buf = new byte[4];
            stream.Read(buf);
            var len = BitConverter.ToInt32(buf);
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
                // length (4) + null asm index (1)
                for (var i = 0; i < 5; ++i)
                {
                    stream.WriteByte(0);
                }

                return;
            }

            var array = items.Cast<object>().ToArray();
            stream.Write(BitConverter.GetBytes(array.Length));
            stream.WriteByte(1);
            if (!skipElemTypeInfo)
            {
                WriteTypeInfo(stream, itemType);
            }

            foreach (var item in array)
            {
                Write(stream, itemType, item, backRefs);
            }
        }

        internal static IEnumerable<T> ReadGenericEnumerableGeneric<T>(RobustSerializer szr, Stream stream, List<object> backRefs)
        {
            var buf = new byte[4];
            stream.Read(buf);
            var len = BitConverter.ToInt32(buf);

            var isNull = stream.ReadByte() == 0;

            if (len == 0 && isNull)
            {
                return null;
            }

            return ReadGenericEnumerableGenericInternal<T>(szr, stream, backRefs, len);
        }

        internal static IEnumerable<T> ReadGenericEnumerableGenericInternal<T>(RobustSerializer szr, Stream stream, List<object> backRefs, int len)
        {
            var type = typeof(T);
            for (var i = 0; i < len; ++i)
            {
                yield return (T) szr.Read(stream, type, backRefs);
            }
        }

        private delegate IEnumerable ReadGenericEnumerableDelegate(RobustSerializer szr, Stream stream, List<object> backRefs);

        private IDictionary<Type, ReadGenericEnumerableDelegate> _readGenericEnumerableDelegateCache =
            new Dictionary<Type, ReadGenericEnumerableDelegate>();

        internal IEnumerable ReadGenericEnumerable(Type type, Stream stream, List<object> backRefs)
        {
            if (!_readGenericEnumerableDelegateCache.TryGetValue(type, out var dlg))
            {
                dlg = CreateDelegate<ReadGenericEnumerableDelegate, RobustSerializer>(
                    BindingFlags.NonPublic | BindingFlags.Static,
                    nameof(ReadGenericEnumerableGeneric), type);
            }

            return dlg(this, stream, backRefs);
        }

        // see https://github.com/dotnet/runtime/blob/master/src/coreclr/src/vm/field.h class FieldDesc ~ line 75
        [StructLayout(LayoutKind.Sequential)]
        private ref struct FieldDesc
        {

            private unsafe void* mt;

            private uint bits1;

            private uint bits2;

            public int Offset => (int) (bits2 & 0x07FFFFFFu);

        }

        private static unsafe object BoxingFieldReader<T>(void* p) where T : unmanaged => *(T*) p;

        private unsafe delegate object BoxingFieldReaderDelegate(void* p);

        private static readonly Dictionary<Type, BoxingFieldReaderDelegate> _boxingFieldReaderDelegates =
            new Dictionary<Type, BoxingFieldReaderDelegate>();

        private static BoxingFieldReaderDelegate GetBoxingFieldReader(Type t)
        {
            if (!_boxingFieldReaderDelegates.TryGetValue(t, out var d))
            {
                d = CreateDelegate<BoxingFieldReaderDelegate, RobustSerializer>(
                    BindingFlags.NonPublic | BindingFlags.Static,
                    nameof(BoxingFieldReader), t);
                _boxingFieldReaderDelegates[t] = d;
            }

            return d;
        }

        private Dictionary<Type, (MethodInfo Value, MethodInfo HasValue)> _nullableGetters =
            new Dictionary<Type, (MethodInfo, MethodInfo)>();

        private Dictionary<Type, MethodInfo> _nullableSetters =
            new Dictionary<Type, MethodInfo>();

        internal void Write(Stream stream, Type type, object obj, List<object> backRefs)
        {
            if (type.IsConstructedGenericType)
            {
                var gtd = type.GetGenericTypeDefinition();

                if (gtd == typeof(Nullable<>))
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
                        // ReSharper disable once PossibleNullReferenceException
                        var valueSetter = valueProp.SetMethod;
                        _nullableSetters[type] = valueSetter;
                    }

                    var hasValue = (bool) hasValueGetter.Invoke(obj, null);

                    if (hasValue)
                    {
                        stream.WriteByte(1);
                        var value = valueGetter.Invoke(obj, null);
                        Write(stream, underType, value, backRefs);
                        return;
                    }
                    else
                    {
                        stream.WriteByte(0);
                        return;
                    }
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
            foreach (var field in GetFields(type))
            {
                //var typedRef = TypedReference.MakeTypedReference(obj, new[] {field});
                var ft = field.FieldType;
                var isValType = ft.IsValueType;
                object value;
                if (field.DeclaringType == type)
                {
                    value = field.GetValue(obj);
                }
                else
                {
                    throw new NotImplementedException(field.ToString());
                }

                if (!isValType)
                {
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

                    var bytes = Encoding.UTF8.GetBytes((string) value);
                    stream.Write(BitConverter.GetBytes(bytes.Length + 1));
                    stream.Write(bytes);
                    continue;
                }

                if (typeof(IEnumerable).IsAssignableFrom(ft))
                {
                    var typedEnum = ft.GetInterfaces()
                        .FirstOrDefault(intf => intf.IsConstructedGenericType
                            && intf.Namespace == "System.Collections.Generic" && intf.Name.StartsWith("IEnumerable`1")
                            && intf.GetGenericTypeDefinition().Name == "IEnumerable`1");
                    if (typedEnum != null)
                    {
                        WriteGenericEnumerable(stream, typedEnum.GenericTypeArguments[0], (IEnumerable) value, backRefs);
                    }
                    else
                    {
                        WriteNonGenericEnumerable(stream, (IEnumerable) value, backRefs);
                    }

                    continue;
                }

                if (!isValType)
                {
                    if (value == null)
                    {
                        stream.WriteByte(0);
                        continue; // null type id already written
                    }

                    WriteTypeInfo(stream, value.GetType());
                }

                Write(stream, ft, value, backRefs);
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
                dlg = CreateDelegate<CreateValueTypeDelegate, RobustSerializer>(
                    BindingFlags.NonPublic | BindingFlags.Static,
                    "CreateValueTypeGeneric",
                    type
                );
            }

            return dlg(bytes);
        }

        internal Array MakeGenericArray(IEnumerable objects, Type itemType)
        {
            if (objects == null)
            {
                return null;
            }

            var objs = new List<object>();

            foreach (var obj in objects)
            {
                objs.Add(obj);
            }

            var array = Array.CreateInstance(itemType, objs.Count);
            for (var i = 0; i < objs.Count; i++)
            {
                array.SetValue(objs[i], i);
            }

            return array;
        }

        private static Dictionary<Type, ConstructorInfo> _ctorCache =
            new Dictionary<Type, ConstructorInfo>();

        private static readonly Comparer<FieldInfo> FieldComparer = Comparer<FieldInfo>.Create((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

        private static readonly Comparer<PropertyInfo> PropertyComparer = Comparer<PropertyInfo>.Create((a, b) => a.MetadataToken.CompareTo(b.MetadataToken));

        internal object Read(Stream stream, Type type, List<object> backRefs)
        {
            if (type == null)
            {
                return null;
            }

            if (type.IsConstructedGenericType)
            {
                var obj = FormatterServices.GetUninitializedObject(type);
                var gtd = type.GetGenericTypeDefinition();

                if (gtd == typeof(Nullable<>))
                {
                    var underType = type.GetGenericArguments()[0];

                    if (!_nullableSetters.TryGetValue(type, out var valueSetter))
                    {
                        // ReSharper disable once PossibleNullReferenceException
                        valueSetter = type.GetProperty("Value").SetMethod;
                        _nullableSetters[type] = valueSetter;
                    }

                    var hasValue = stream.ReadByte() != 0;

                    if (!hasValue)
                    {
                        return obj;
                    }

                    var value = Read(stream, underType, backRefs);
                    valueSetter.Invoke(obj, new[] {value});
                    return obj;
                }
            }
            else if (type.IsArray)
            {

                if (type.HasElementType)
                {
                    var elemType = type.GetElementType();
                    if (elemType == typeof(object))
                    {
                        return MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs), elemType);
                    }
                    return MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs), elemType);
                }
                // do non-generic arrays exist anymore?
                return MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs), typeof(object));
            }

            {
                var obj = FormatterServices.GetUninitializedObject(type);
                foreach (var field in GetFields(type))
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
                        var size = Marshal.SizeOf(ft);
                        var bytes = new byte[size];
                        stream.Read(bytes);
                        //field.SetValueDirect(typedRef, CreateValueType(ft, bytes));
                        field.SetValue(obj, CreateValueType(ft, bytes));
                        continue;
                    }

                    if (ft.IsArray)
                    {
                        var elemType = ft.GetElementType();
                        var array = MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs), elemType);
                        //field.SetValueDirect(typedRef, MakeGenericArray(items, elemType));
                        field.SetValue(obj, array);
                        continue;
                    }

                    if (ft == typeof(string))
                    {
                        var buf = new byte[4];
                        stream.Read(buf);
                        var len = BitConverter.ToInt32(buf) - 1;
                        if (len == -1)
                        {
                            // null
                            continue;
                        }

                        if (len == 0)
                        {
                            field.SetValue(obj, "");
                        }

                        buf = new byte[len];
                        if (len >= 32768) // TODO: when it's stable remove this
                        {
                            throw new NotSupportedException();
                        }

                        stream.Read(buf);
                        var str = Encoding.UTF8.GetString(buf);
                        field.SetValue(obj, str);
                        continue;
                    }

                    if (typeof(IEnumerable).IsAssignableFrom(ft))
                    {
                        var typedEnum = ft.GetInterfaces()
                            .FirstOrDefault(t => t.IsConstructedGenericType
                                && t.Namespace == "System.Collections.Generic" && t.Name.StartsWith("IEnumerable`1")
                                && t.GetGenericTypeDefinition().Name == "IEnumerable`1");
                        if (typedEnum != null)
                        {
                            if (_ctorCache.TryGetValue(ft, out var ctor))
                            {
                                var paramType = ctor.GetParameters()[0].ParameterType;
                                if (paramType.IsArray)
                                {
                                    var elemType = paramType.GetElementType();
                                    //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                                    field.SetValue(obj, ctor.Invoke(new object[]
                                        {
                                            MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs), elemType)
                                        }
                                    ));
                                }
                                else
                                {
                                    var elemType = paramType.GenericTypeArguments[0];
                                    //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                                    field.SetValue(obj, ctor.Invoke(new object[]
                                        {
                                            MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs), elemType)
                                        }
                                    ));
                                }

                                continue;
                            }

                            // variable name scope
                            {
                                var ctors = ft.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                var elemType = typedEnum.GenericTypeArguments[0];
                                var arrayType = elemType.MakeArrayType();
                                ctor = ctors.FirstOrDefault(c =>
                                {
                                    var ps = c.GetParameters();
                                    return ps.Length == 1 && ps[0].ParameterType == typedEnum;
                                });

                                if (ctor != null)
                                {
                                    //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                                    var array = MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs), elemType);
                                    if (array != null)
                                    {
                                        field.SetValue(obj, ctor.Invoke(new object[]
                                            {
                                                array
                                            }
                                        ));
                                    }

                                    _ctorCache[ft] = ctor;
                                    continue;
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
                                    var array = MakeGenericArray(ReadGenericEnumerable(elemType, stream, backRefs), elemType);
                                    if (array != null)
                                    {
                                        field.SetValue(obj, ctor.Invoke(new object[]
                                            {
                                                array
                                            }
                                        ));
                                    }

                                    _ctorCache[ft] = ctor;
                                    continue;
                                }

                                throw new NotSupportedException($"{ft.FullName} {type.FullName}.{field.Name}");
                            }
                        }
                        else
                        {
                            if (_ctorCache.TryGetValue(ft, out var ctor))
                            {
                                var paramType = ctor.GetParameters()[0].ParameterType;
                                if (paramType.IsArray)
                                {
                                    //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                                    field.SetValue(obj, ctor.Invoke(new object[]
                                        {
                                            MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs), typeof(object))
                                        }
                                    ));
                                }
                                else
                                {
                                    //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                                    field.SetValue(obj, ctor.Invoke(new object[]
                                        {
                                            MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs), typeof(object))
                                        }
                                    ));
                                }

                                continue;
                            }

                            var ctors = ft.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                            var untypedEnum = typeof(IEnumerable);

                            ctor = ctors.FirstOrDefault(c =>
                            {
                                var ps = c.GetParameters();
                                return ps.Length == 1 && ps[0].ParameterType == untypedEnum;
                            });

                            if (ctor != null)
                            {
                                //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                                field.SetValue(obj, ctor.Invoke(new object[]
                                    {
                                        MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs), typeof(object))
                                    }
                                ));
                                _ctorCache[ft] = ctor;
                                continue;
                            }

                            ctor = ctors.FirstOrDefault(c =>
                            {
                                var ps = c.GetParameters();
                                return ps.Length == 1 && ps[0].ParameterType.IsArray && ps[0].ParameterType == typeof(object[]);
                            });
                            if (ctor != null)
                            {
                                //field.SetValueDirect(typedRef, ctor.Invoke(new object[]
                                field.SetValue(obj, ctor.Invoke(new object[]
                                    {
                                        MakeGenericArray(ReadNonGenericEnumerable(stream, backRefs), typeof(object))
                                    }
                                ));
                            }

                            throw new NotSupportedException($"{ft.FullName} {type.FullName}.{field.Name}");
                        }
                    }

                    //field.SetValueDirect(typedRef, Read(stream, ft, backRefs));
                    var it = ft;
                    if (!isValType)
                    {
                        it = ReadTypeInfo(stream);
                    }

                    field.SetValue(obj, Read(stream, it, backRefs));
                }

                return obj;
            }
        }

        internal void Serialize<T>(Stream stream, T obj, List<object> backRefs)
        {
            if (obj == null)
            {
                stream.WriteByte(0);
                return;
            }

            var type = obj.GetType();

            if (type == typeof(object))
            {
                throw new NotSupportedException("Synchronization objects (plain empty objects) are not supported and not serialized.");
            }

            var start = stream.Position;

            WriteTypeInfo(stream, type);

            Write(stream, type, obj, backRefs);

            var finish = stream.Position;

            var byteCount = finish - start;
        }

        internal void SerializeGeneric<T>(Stream stream, T obj, List<object> backRefs) =>
            Serialize(stream, (object) obj, backRefs);

        internal IEnumerable<Type> GetImplementingTypes(Type intf)
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

        internal IEnumerable<Type> GetInheritingTypes(Type baseType)
        {
            foreach (var asm in GetAssemblies())
            {
                foreach (var type in asm.DefinedTypes)
                {
                    if (type.BaseType == baseType)
                    {
                        yield return type;
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

        public bool CanSerialize(Type type) =>
            GetAssemblies().Contains(type.Assembly);

    }

}
