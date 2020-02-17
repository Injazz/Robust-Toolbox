using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using JetBrains.Annotations;

namespace Robust.Shared.Serialization
{

    [PublicAPI]
    public static partial class RobustNetTypeInfo
    {

        // this is static readonly so dependencies will not have const prop,
        // so when assembly is updated they don't need to rebuild
        // generics increase by multiples of this if they are not mapped
        public static readonly int RobustNetTypeInfoSize = 4;

        private static HashSet<Type> _seenNewTypes = new HashSet<Type>();

        // TODO: use IoC?
        private static Assembly[] _Assemblies =
        {
            null,
            AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == "Robust.Shared"), // should not be null
            AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == "Content.Shared"), // can be null during tests
            null, // reserved for back refs
            null, // reserved for user loaded content e.g. yaml
        };

        private static readonly bool[] _AssemblyValidStrings =
        {
            false, // reserved
            true, // Robust.Shared
            true, // Content.Shared
            false, // back references
            true // user loaded content e.g. yaml
        };

        private static readonly Dictionary<int, byte[]>[] _CustomSymbolTableHashes =
        {
            null,
            null,
            null,
            null,
            null,
        };

        public static int GetAssemblyCount() => _Assemblies.Count(a => a != null);

        public static bool RegisterAssembly([NotNull] Assembly asm)
        {
            if (_Assemblies.Contains(asm)) return false;

            _Assemblies = _Assemblies.Concat(new[] {asm}).ToArray();
            return true;
        }

        public static IEnumerable<Assembly> GetAssemblies() => _Assemblies.Where(a => a != null);

        [CanBeNull]
        public static Assembly GetAssembly(int asmIndex)
        {
            var index = asmIndex - 1;
            if (index < 0 || index >= _Assemblies.Length)
            {
                return null;
            }

            return _Assemblies[index];
        }

        public static int? GetAssemblyIndex([CanBeNull] Assembly asm)
        {
            if (asm == null)
            {
                return null;
            }

            var index = Array.IndexOf(_Assemblies, asm);

            if (index == -1)
            {
                return null;
            }

            return index + 1;
        }

        private class UserStringDictionary
        {

            public readonly IReadOnlyList<string> IdToString;

            public readonly IReadOnlyDictionary<string, int> StringToId;

            public UserStringDictionary(IReadOnlyList<string> idToString, IReadOnlyDictionary<string, int> stringToId)
            {
                IdToString = idToString;
                StringToId = stringToId;
            }

        }

        private static readonly IReadOnlyList<UserStringDictionary> AssemblyStringDictionaries;

        public static readonly IReadOnlyList<byte[]> AssemblyStringDictionaryHashes;

        static unsafe RobustNetTypeInfo()
        {
            var sw = Stopwatch.StartNew();
            var hashes = ImmutableList.CreateBuilder<byte[]>();
            var dicts = ImmutableList.CreateBuilder<UserStringDictionary>();

            for (var i = 0; i < _Assemblies.Length; i++)
            {
                var forwardList = ImmutableList.CreateBuilder<string>();
                var reverseDict = ImmutableDictionary.CreateBuilder<string, int>();
                //var asmIndex = (byte) (i + 1);
                var asm = _Assemblies[i];
                if (asm != null && _AssemblyValidStrings[i] && asm.TryGetRawMetadata(out var blob, out var len))
                {
                    var reader = new MetadataReader(blob, len);
                    var userStrHeap = blob + reader.GetHeapMetadataOffset(HeapIndex.UserString);
                    var userStrHeapSize = reader.GetHeapSize(HeapIndex.UserString);
                    var strHeap = blob + reader.GetHeapMetadataOffset(HeapIndex.String);
                    var strHeapSize = reader.GetHeapSize(HeapIndex.String);
                    using (var hasher = SHA512.Create())
                    {
                        var userStrHeapStream = new UnmanagedMemoryStream(userStrHeap, userStrHeapSize);

                        var buffer = ArrayPool<byte>.Shared.Rent(4096);

                        int bytesRead;
                        while ((bytesRead = userStrHeapStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                        }

                        var strHeapStream = new UnmanagedMemoryStream(strHeap, strHeapSize);
                        while ((bytesRead = strHeapStream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            hasher.TransformBlock(buffer, 0, bytesRead, null, 0);
                        }

                        hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                        ArrayPool<byte>.Shared.Return(buffer, clearArray: true);

                        hashes.Add(hasher.Hash);
                    }

                    var usrStrHandle = default(UserStringHandle);
                    do
                    {
                        var userStr = reader.GetUserString(usrStrHandle);
                        userStr = string.Intern(userStr);
                        if (userStr != "")
                        {
                            //var offset = Unsafe.Read<int>(&strHandle);
                            var index = forwardList.Count;
                            if (!reverseDict.ContainsKey(userStr) && !dicts.Any(d => d.StringToId.ContainsKey(userStr)))
                            {
                                forwardList.Add(userStr);
                                reverseDict.Add(userStr, index);
                            }
                        }

                        usrStrHandle = reader.GetNextHandle(usrStrHandle);
                    } while (usrStrHandle != default);

                    var strHandle = default(StringHandle);
                    do
                    {
                        var str = reader.GetString(strHandle);
                        str = string.Intern(str);
                        if (str != "")
                        {
                            //var offset = Unsafe.Read<int>(&strHandle);
                            var index = forwardList.Count;
                            if (!reverseDict.ContainsKey(str) && !dicts.Any(d => d.StringToId.ContainsKey(str)))
                            {
                                forwardList.Add(str);
                                reverseDict.Add(str, index);
                            }
                        }

                        strHandle = reader.GetNextHandle(strHandle);
                    } while (strHandle != default);
                }
                else
                {
                    hashes.Add(null);
                }

                var dict = new UserStringDictionary(forwardList.ToImmutable(), reverseDict.ToImmutable());
                dicts.Add(dict);
            }

            // TODO: custom string dicts get loaded here

            AssemblyStringDictionaries = dicts.ToImmutable();
            AssemblyStringDictionaryHashes = hashes.ToImmutable();
            var elapsed = sw.ElapsedMilliseconds;
            Debug.WriteLine($"Building string dictionaries took {elapsed}ms");
        }

        [CanBeNull]
        public static byte[] GetAssemblyStringsHash([CanBeNull] Assembly asm)
        {
            if (asm == null)
            {
                return null;
            }

            var index = Array.IndexOf(_Assemblies, asm);

            if (index <= -1)
            {
                return null;
            }

            var hash = AssemblyStringDictionaryHashes[index];
            var copy = new byte[hash.Length];
            hash.CopyTo(copy, 0);
            return copy;
        }

        public static byte[] GetAssemblyStringsHash(int asmIndex)
        {
            if (asmIndex <= 0)
            {
                return null;
            }

            var index = asmIndex - 1;

            if (index >= AssemblyStringDictionaryHashes.Count)
            {
                return null;
            }

            if (index <= -1)
            {
                return null;
            }

            var hash = AssemblyStringDictionaryHashes[index];
            var copy = new byte[hash.Length];
            hash.CopyTo(copy, 0);
            return copy;
        }

        public static bool TryGetStringId(string s, out (byte AssemblyIndex, int StringIndex) stringId)
        {
            for (var i = 0; i < AssemblyStringDictionaries.Count; i++)
            {
                var asmStrDict = AssemblyStringDictionaries[i];
                var asmIndex = (byte) (i + 1);
                if (!asmStrDict.StringToId.TryGetValue(s, out var offset))
                {
                    continue;
                }

                stringId = (asmIndex, offset);
                return true;
            }

            stringId = default;
            return false;
        }

        public static bool TryGetString((byte AssemblyIndex, int StringIndex) stringId, out string s)
        {
            var asmIndex = stringId.AssemblyIndex - 1;
            if (asmIndex < 0)
            {
                s = null;
                return false;
            }

            if (asmIndex > AssemblyStringDictionaries.Count)
            {
                throw new InvalidOperationException($"No assembly is registered for assembly index {asmIndex}.");
            }

            var strDict = AssemblyStringDictionaries[asmIndex];

            if (strDict == null || strDict.IdToString.Count == 0)
            {
                throw new InvalidOperationException($"No strings are registered for assembly {asmIndex} ({_Assemblies[asmIndex]?.ToString() ?? "reserved"})");
            }

            if (stringId.StringIndex > strDict.IdToString.Count)
            {
                throw new InvalidOperationException($"String is {stringId} missing from assembly {asmIndex} ({_Assemblies[asmIndex]?.ToString() ?? "reserved"})");
            }

            s = strDict.IdToString[stringId.StringIndex];

            return true;
        }

        public static byte[] GetNetTypeInfo(Type type)
        {
            using var ms = new MemoryStream(RobustNetTypeInfoSize);
            WriteTypeInfo(ms, type);
            return ms.ToArray();
        }

        public static Type ResolveNetTypeInfo(byte[] type)
        {
            using var ms = new MemoryStream(type);
            return ReadTypeInfo(ms);
        }

        public static void WriteTypeInfo(Stream stream, [NotNull] Type type)
        {
            //Trace.WriteLine($"Writing Type Info: {type.FullName}");
            // recursion for generic types by loop to avoid stack frames
            var asm = type.Assembly;

            var asmIndex = Array.IndexOf(_Assemblies, asm) + 1;

            var skipGenericInfo = false;
            byte[] bytes;
            if (asmIndex <= 0)
            {
                if (_CustomSymbolIndex.TryGetValue(type, out var typeId))
                {
                    ++typeId;
                    skipGenericInfo = true;
                }
                else
                {
                    if (type.IsArray)
                    {
                        typeId = 0;
                    }
                    else if (type.IsConstructedGenericType)
                    {
                        if (_CustomSymbolIndex.TryGetValue(type.GetGenericTypeDefinition(), out typeId))
                        {
                            ++typeId;
                        }
                        else
                        {
                            throw new NotSupportedException($"No type index mapping for {type.FullName}");
                        }
                    }
                    else
                    {
                        throw new NotSupportedException($"No type index mapping for {type.FullName}");
                    }

                    if (_seenNewTypes.Add(type))
                    {
                        Trace.WriteLine($"Unmapped type: {type}");
                    }
                }

                bytes = BitConverter.GetBytes(typeId);
                bytes[3] = 1;
            }
            else
            {
                var typeId = type.MetadataToken;
                bytes = BitConverter.GetBytes(typeId);
                bytes[3] = (byte) asmIndex;
            }

            stream.Write(bytes);

            if (skipGenericInfo)
            {
                return;
            }

            if (type.IsConstructedGenericType)
            {
                WriteGenericTypeInfo(stream, type);
            }
            else if (type.IsArray)
            {
                stream.WriteByte((byte) type.GetArrayRank());
                type = type.GetElementType();
                if (type == null)
                {
                    throw new NotSupportedException();
                }

                WriteTypeInfo(stream, type);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void WriteGenericTypeInfo(Stream stream, Type type)
        {
            foreach (var typeArg in type.GenericTypeArguments)
            {
                WriteTypeInfo(stream, typeArg);
            }
        }

        [CanBeNull]
        public static Type ReadTypeInfo(Stream stream)
        {
            var buf = new byte[4];
            stream.Read(buf);
            var typeId = BitConverter.ToInt32(buf);
            var asmIndex = (typeId >> 24) - 1;
            if (asmIndex < 0)
            {
                return null;
            }

            typeId &= 0x00FFFFFF;

            Type type;
            if (asmIndex == 0)
            {
                if (typeId == 0)
                {
                    // array
                    type = null;
                }
                else
                {
                    --typeId;

                    if (_CustomSymbolTable.Length <= typeId)
                    {
                        throw new NotSupportedException($"No type mapping for type index {typeId}");
                    }

                    type = _CustomSymbolTable[typeId];
                }
            }
            else
            {
                if (asmIndex >= _Assemblies.Length)
                {
                    throw new NotSupportedException($"Unknown assembly index {asmIndex}, likely corrupt data.");
                }

                var asm = _Assemblies[asmIndex];
                if (asmIndex == 0 && typeId == 0)
                {
                    var rank = stream.ReadByte();
                    var elemType = ReadTypeInfo(stream);
                    if (rank > 1)
                    {
                        throw new NotImplementedException();
                    }

                    if (elemType == null)
                    {
                        throw new NotImplementedException();
                    }

                    return rank == 1 ? elemType.MakeArrayType() : elemType.MakeArrayType(rank);
                }

                typeId |= 0x02000000;
                type = asm.ManifestModule.ResolveType(typeId);
            }

            if (type == null)
            {
                var rank = stream.ReadByte();

                var elemType = ReadTypeInfo(stream);

                if (elemType == null)
                {
                    throw new NotImplementedException();
                }

                var arrayType = rank == 1 ? elemType.MakeArrayType() : elemType.MakeArrayType(rank);

                type = arrayType;
            }
            else if (type.IsGenericTypeDefinition)
            {
                var typeArgs = ReadGenericTypeInfo(stream, type);
                type = type.MakeGenericType(typeArgs.ToArray());
            }

            //Trace.WriteLine($"Read Type Info: {type.FullName}");
            return type;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static IEnumerable<Type> ReadGenericTypeInfo(Stream stream, Type type)
        {
            foreach (var _ in type.GetGenericArguments())
            {
                var resolved = ReadTypeInfo(stream);

                if (resolved == null)
                {
                    throw new NotImplementedException();
                }

                if (resolved.IsGenericTypeDefinition)
                {
                    var moreTypeArgs = ReadGenericTypeInfo(stream, resolved);
                    resolved = resolved.MakeGenericType(moreTypeArgs.ToArray());
                }

                yield return resolved;
            }
        }

    }

}
