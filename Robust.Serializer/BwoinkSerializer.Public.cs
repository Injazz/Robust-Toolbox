using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Joveler.Compression.XZ;
using Robust.Shared.Interfaces.Serialization;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Serialization;

//[module: IoCRegister(typeof(IRobustSerializer), typeof(BwoinkSerializer))]


namespace Robust.Shared.Serialization
{

    public partial class BwoinkSerializer : IRobustSerializer
    {

        #region Statistics

        public static long LargestObjectSerializedBytes { get; private set; }

        public static Type LargestObjectSerializedType { get; private set; }

        public static long TotalObjectsSerialized { get; private set; }

        public static long TotalObjectsDeserialized { get; private set; }

        public static long TotalBytesWritten { get; private set; }

        public static long TotalBytesRead { get; private set; }

        public static long TotalBytesCompressed { get; private set; }

        public static long TotalBytesDecompressed { get; private set; }

        public long ObjectsSerialized
        {
            get => _objectsSerialized;
            private set
            {
                TotalObjectsSerialized += (value - _objectsSerialized);
                _objectsSerialized = value;
            }
        }

        public long ObjectsDeserialized
        {
            get => _objectsDeserialized;
            private set
            {
                TotalObjectsDeserialized += (value - _objectsDeserialized);
                _objectsDeserialized = value;
            }
        }

        public long BytesWritten
        {
            get => _bytesWritten;
            private set
            {
                TotalBytesWritten += (value - _bytesWritten);
                _bytesWritten = value;
            }
        }

        public long BytesRead
        {
            get => _bytesRead;
            private set
            {
                TotalBytesRead += (value - _bytesRead);
                _bytesRead = value;
            }
        }

        public long BytesCompressed
        {
            get => _bytesCompressed;
            private set
            {
                TotalBytesCompressed += (value - _bytesCompressed);
                _bytesCompressed = value;
            }
        }

        public long BytesDecompressed
        {
            get => _bytesDecompressed;
            private set
            {
                TotalBytesDecompressed += (value - _bytesDecompressed);
                _bytesDecompressed = value;
            }
        }

        private long _bytesWritten;

        private long _bytesRead;

        private long _bytesCompressed;

        private long _bytesDecompressed;

        private long _objectsSerialized;

        private long _objectsDeserialized;

        #endregion

        public void Initialize()
        {
            _bytesWritten = 0;
            _bytesRead = 0;
            _bytesCompressed = 0;
            _bytesDecompressed = 0;
            _objectsSerialized = 0;
            _objectsDeserialized = 0;
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
                var compStatsStream = new StatisticsGatheringStreamWrapper(stream);
                using var encodeStream = new XZStream(compStatsStream, XzEncOpts);
                var writeStatsStream = new StatisticsGatheringStreamWrapper(encodeStream);
                Serialize(encodeStream, obj, new List<object>());
                BytesWritten += writeStatsStream.BytesWritten;
                BytesCompressed += compStatsStream.BytesWritten;
                ++ObjectsSerialized;
            }
            else
            {
                AttachRecordingStreamWrapper(ref stream);
                var writeStatsStream = new StatisticsGatheringStreamWrapper(stream);
                Serialize(writeStatsStream, obj, new List<object>());
                BytesWritten += writeStatsStream.BytesWritten;
                ++ObjectsSerialized;
                RecordSerialized(stream);
            }
        }

        public void Serialize<T>(Stream stream, T obj)
        {
            if (UseCompression)
            {
                var compStatsStream = new StatisticsGatheringStreamWrapper(stream);
                using var encodeStream = new XZStream(compStatsStream, XzEncOpts);
                var writeStatsStream = new StatisticsGatheringStreamWrapper(encodeStream);
                Serialize(encodeStream, obj, new List<object>());
                BytesWritten += writeStatsStream.BytesWritten;
                BytesCompressed += compStatsStream.BytesWritten;
                ++ObjectsSerialized;
            }
            else
            {
                AttachRecordingStreamWrapper(ref stream);
                var writeStatsStream = new StatisticsGatheringStreamWrapper(stream);
                Serialize(writeStatsStream, obj, new List<object>());
                BytesWritten += writeStatsStream.BytesWritten;
                RecordSerialized(stream);
                ++ObjectsSerialized;
            }
        }

        public T Deserialize<T>(Stream stream)
        {
            if (UseCompression)
            {
                var dcmpStatsStream = new StatisticsGatheringStreamWrapper(stream);
                using var decodeStream = new XZStream(dcmpStatsStream, XzDecOpts);
                var readStatsStream = new StatisticsGatheringStreamWrapper(decodeStream);
                var result = Deserialize<T>(readStatsStream, new List<object>());
                BytesRead += readStatsStream.BytesWritten;
                BytesDecompressed += dcmpStatsStream.BytesWritten;
                ++ObjectsDeserialized;
                return result;
            }
            else
            {
                RecordDeserializing(stream);
                AttachRecordingStreamWrapper(ref stream);
                var readStatsStream = new StatisticsGatheringStreamWrapper(stream);
                var result = Deserialize<T>(stream, new List<object>());
                BytesRead += readStatsStream.BytesWritten;
                TotalBytesRead += readStatsStream.BytesWritten;
                ++ObjectsDeserialized;
                return result;
            }
        }

        public object Deserialize(Stream stream)
        {
            if (UseCompression)
            {
                var dcmpStatsStream = new StatisticsGatheringStreamWrapper(stream);
                using var decodeStream = new XZStream(dcmpStatsStream, XzDecOpts);
                var readStatsStream = new StatisticsGatheringStreamWrapper(decodeStream);
                var result = Deserialize(decodeStream, new List<object>());
                BytesRead += readStatsStream.BytesWritten;
                BytesDecompressed += dcmpStatsStream.BytesWritten;
                ++ObjectsDeserialized;
                return result;
            }
            else
            {
                RecordDeserializing(stream);
                AttachRecordingStreamWrapper(ref stream);
                var readStatsStream = new StatisticsGatheringStreamWrapper(stream);
                var result = Deserialize(stream, new List<object>());
                BytesRead += readStatsStream.BytesWritten;
                ++ObjectsDeserialized;
                return result;
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

        private static readonly XZCompressOptions XzEncOpts = new XZCompressOptions
        {
            BufferSize = 4 * 1024 * 1024,
            Check = LzmaCheck.None,
            ExtremeFlag = false,
            Level = LzmaCompLevel.Level1,
            LeaveOpen = true
        };

        private static readonly XZDecompressOptions XzDecOpts = new XZDecompressOptions
        {
            BufferSize = 4 * 1024 * 1024,
            DecodeFlags = LzmaDecodingFlag.Concatenated | LzmaDecodingFlag.IgnoreCheck,
            MemLimit = 24 * 1024 * 1024,
            LeaveOpen = true
        };

        public BwoinkSerializer()
        {
            _stringTableWrapper = new ReadOnlyCollection<string>(_stringTable);
        }

    }

}
