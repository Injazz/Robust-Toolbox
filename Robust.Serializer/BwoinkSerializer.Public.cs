using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using Newtonsoft.Json.Serialization;
using Robust.Shared.Interfaces.Serialization;

namespace Robust.Shared.Serialization
{

    public partial class BwoinkSerializer : IRobustSerializer
    {

        private static readonly LZ4EncoderSettings Lz4EncoderSettings = new LZ4EncoderSettings {BlockSize = 4 * 1024 * 1024, ChainBlocks = true, CompressionLevel = LZ4Level.L09_HC, ExtraMemory = 12 * 1024 * 1024};

        public void Initialize()
        {
        }

        [Conditional("DEBUG")]
        public void AttachRecordingStreamWrapper(ref Stream stream)
        {
            if (!Tracing)
            {
                return;
            }

            //stream = new RecordingStreamWrapper(stream, System.Console.Out);
            //stream = new RecordingStreamWrapper(stream, new TraceWriter());
            stream = new RecordingStreamWrapper(stream, TraceWriter);
        }

#if DEBUG
        [Conditional("TRACE_IO")]
        private void RecordSerialized(Stream stream)
        {
            if (!Tracing)
            {
                return;
            }

            if (stream is RecordingStreamWrapper w)
            {
                stream = w.WrappedStream;
            }

            if (stream is MemoryStream ms)
            {
                TraceWriteLine($"Serialized: {BitConverter.ToString(ms.ToArray()).Replace('-', ' ')}");
            }
        }

        [Conditional("TRACE_IO")]
        private void RecordDeserializing(Stream stream)
        {
            if (!Tracing)
            {
                return;
            }

            if (stream is MemoryStream ms)
            {
                TraceWriteLine($"Deserializing: {BitConverter.ToString(ms.ToArray()).Replace('-', ' ')}");
            }
        }
#else
        [Conditional("NEVER")]
        private void RecordDeserializing(Stream stream) {}
        [Conditional("NEVER")]
        private void RecordSerialized(Stream stream) {}
#endif

        public void Serialize(Stream stream, object obj)
        {
            if (UseCompression)
            {
                using var encodeStream = LZ4Stream.Encode(stream, Lz4EncoderSettings, leaveOpen: true);

                Serialize(encodeStream, obj, new List<object>());
            }
            else
            {
                AttachRecordingStreamWrapper(ref stream);

                Serialize(stream, obj, new List<object>());

                RecordSerialized(stream);
            }
        }

        public void Serialize<T>(Stream stream, T obj)
        {
            if (UseCompression)
            {
                using var encodeStream = LZ4Stream.Encode(stream, Lz4EncoderSettings, leaveOpen: true);
                Serialize(encodeStream, obj, new List<object>());
            }
            else
            {
                AttachRecordingStreamWrapper(ref stream);

                Serialize(stream, obj, new List<object>());

                RecordSerialized(stream);
            }
        }

        public T Deserialize<T>(Stream stream)
        {
            if (UseCompression)
            {
                using var decodeStream = LZ4Stream.Decode(stream, leaveOpen: true);
                return Deserialize<T>(decodeStream, new List<object>());
            }
            else
            {
                RecordDeserializing(stream);

                AttachRecordingStreamWrapper(ref stream);

                return Deserialize<T>(stream, new List<object>());
            }
        }

        public object Deserialize(Stream stream)
        {
            if (UseCompression)
            {
                using var decodeStream = LZ4Stream.Decode(stream, leaveOpen: true);
                return Deserialize(decodeStream, new List<object>());
            }
            else
            {
                RecordDeserializing(stream);

                AttachRecordingStreamWrapper(ref stream);

                return Deserialize(stream, new List<object>());
            }
        }

        public bool CanSerialize(Type type) =>
            RobustNetTypeInfo.GetAssemblies().Contains(type.Assembly);

        public bool RegisterStrings(IEnumerable<string> strs, bool clear = false)
        {
            if (clear)
            {
                _stringSet.Clear();
                _stringSet.Clear();
                _stringTableHashCache = null;
            }

            var sb = new StrongBox<int>();

            _stringTable.AddRange(StringSetFilter(strs, sb));

            if (sb.Value <= 0)
            {
                return false;
            }

            _stringTableHashCache = null;
            return true;
        }

        private IEnumerable<string> StringSetFilter(IEnumerable<string> strs, StrongBox<int> passed)
        {
            foreach (var str in strs)
            {
                if (!_stringSet.Add(str))
                {
                    continue;
                }

                ++passed.Value;
                yield return str;
            }
        }

        private byte[] _stringTableHashCache;

        public unsafe byte[] StringTableHash
        {
            get
            {
                if (_stringTableHashCache == null)
                {
                    var nullBlock = new byte[] {0, 0};

                    using var hasher = SHA512.Create();

                    foreach (var s in StringTable)
                    {
                        var l = s.Length;
                        fixed (char* p = s)
                        {
                            var sp = new Span<byte>((byte*) p, l * sizeof(char));
                            var a = sp.ToArray();
                            hasher.TransformBlock(a, 0, sp.Length, null, 0);
                        }

                        hasher.TransformBlock(nullBlock, 0, 2, null, 0);
                    }

                    hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

                    _stringTableHashCache = hasher.Hash;
                }

                return _stringTableHashCache;
            }
        }

        public IReadOnlyList<string> StringTable
        {
            get
            {
                _stringTable.Capacity = _stringTable.Count;
                return _stringTableWrapper;
            }
        }

        public bool UseCompression { get; set; }

        private readonly List<string> _stringTable = new List<string>();

        private readonly HashSet<string> _stringSet = new HashSet<string>();

        private readonly ReadOnlyCollection<string> _stringTableWrapper;

        public BwoinkSerializer()
        {
            _stringTableWrapper = new ReadOnlyCollection<string>(_stringTable);
        }

    }

}
