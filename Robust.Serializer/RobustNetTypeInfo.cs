using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using JetBrains.Annotations;

namespace Robust.Shared.Serialization
{

    [PublicAPI]
    public static class RobustNetTypeInfo
    {

        // TODO: use IoC?
        private static readonly Assembly[] _Assemblies =
        {
            AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == "System.Private.CoreLib"), // used for net type ids but not for strings
            AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == "Robust.Shared"), // should not be null
            AssemblyLoadContext.Default.Assemblies.FirstOrDefault(a => a.GetName().Name == "Content.Shared"), // can be null during tests
            null, // reserved for back refs
            null, // reserved for user loaded content e.g. yaml
        };

        private static readonly bool[] _AssemblyValidStrings =
        {
            false, // System.Private.CoreLib
            true, // Robust.Shared
            true, // Content.Shared
            false, // back references
            true // user loaded content e.g. yaml
        };

        public static int GetAssemblyCount() => _Assemblies.Count(a => a != null);

        public static IEnumerable<Assembly> GetAssemblies() => _Assemblies.Where(a => a != null);

        public static Assembly GetAssembly(int asmIndex)
        {
            var index = asmIndex - 1;
            if (index < 0 || index >= _Assemblies.Length)
            {
                return null;
            }

            return _Assemblies[index];
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

        public static byte[] GetAssemblyStringsHash(Assembly asm)
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
                s = null;
                return false;
            }

            s = AssemblyStringDictionaries[asmIndex].IdToString[stringId.StringIndex];
            return true;
        }

        public static byte[] GetNetTypeInfo(Type type)
        {
            using var ms = new MemoryStream(5);
            WriteTypeInfo(ms, type);
            return ms.ToArray();
        }

        public static Type ResolveNetTypeInfo(byte[] type)
        {
            using var ms = new MemoryStream(type);
            return ReadTypeInfo(ms);
        }

        public static void WriteTypeInfo(Stream stream, Type type)
        {
            var asm = type.Assembly;
            var asmIndex = Array.IndexOf(_Assemblies, asm) + 1;
            if (asmIndex <= 0)
            {
                throw new NotSupportedException($"Assembly not in list: {asm.FullName}");
            }

            var typeId = type.MetadataToken;
            stream.WriteByte((byte) asmIndex);
            stream.Write(BitConverter.GetBytes(typeId)); // could maybe drop the high byte, should always be same?
            if (type.IsConstructedGenericType)
            {
                WriteGenericTypeInfo(stream, type);
            }
            else if (type.IsArray)
            {
                stream.WriteByte((byte)type.GetArrayRank());
                WriteTypeInfo(stream,type.GetElementType());
            }
        }

        private static void WriteGenericTypeInfo(Stream stream, Type type)
        {
            foreach (var typeArg in type.GenericTypeArguments)
            {
                var asmIndex = Array.IndexOf(_Assemblies, type.Assembly) + 1;
                if (asmIndex <= 0)
                {
                    throw new NotSupportedException($"Assembly not in list: {type.Assembly.FullName}");
                }

                stream.WriteByte((byte) asmIndex);
                stream.Write(BitConverter.GetBytes(typeArg.MetadataToken));
                if (typeArg.IsConstructedGenericType)
                {
                    WriteGenericTypeInfo(stream, typeArg);
                }
            }
        }

        public static Type ReadTypeInfo(Stream stream)
        {
            var asmIndex = stream.ReadByte() - 1;
            if (asmIndex < 0)
            {
                return null;
            }

            var asm = _Assemblies[asmIndex];
            var buf = new byte[4];
            stream.Read(buf);
            var typeId = BitConverter.ToInt32(buf);
            if (typeId == 0x02000000)
            {
                var rank = stream.ReadByte();
                var elemType = ReadTypeInfo(stream);
                if (rank > 1)
                {
                    throw new NotImplementedException();
                }

                return rank == 1 ? elemType.MakeArrayType() : elemType.MakeArrayType(rank);
            }
            var type = asm.ManifestModule.ResolveType(typeId);
            if (type.IsGenericTypeDefinition)
            {
                var typeArgs = ReadGenericTypeInfo(stream, type);
                type = type.MakeGenericType(typeArgs.ToArray());
            }

            return type;
        }

        private static IEnumerable<Type> ReadGenericTypeInfo(Stream stream, Type type)
        {
            foreach (var _ in type.GetGenericArguments())
            {
                var asmIndex = stream.ReadByte() - 1;
                if (asmIndex < 0)
                {
                    throw new NotSupportedException("Can't have null generic type argument.");
                }

                var asm = _Assemblies[asmIndex];
                var buf = new byte[4];
                stream.Read(buf);
                var typeId = BitConverter.ToInt32(buf);
                var resolved = asm.ManifestModule.ResolveType(typeId);
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
