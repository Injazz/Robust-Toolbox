using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using K4os.Compression.LZ4;
using K4os.Compression.LZ4.Streams;
using Newtonsoft.Json.Serialization;

namespace Robust.Shared.Serialization
{

    public partial class RobustSerializer
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
            stream = new RecordingStreamWrapper(stream, _traceWriter);
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
            using var encodeStream = LZ4Stream.Encode(stream, Lz4EncoderSettings, leaveOpen:true);

            //AttachRecordingStreamWrapper(ref stream);

            Serialize(encodeStream, obj, new List<object>());

            //RecordSerialized(stream);
        }

        public void Serialize<T>(Stream stream, T obj)
        {
            using var encodeStream = LZ4Stream.Encode(stream, Lz4EncoderSettings, leaveOpen:true);

            //AttachRecordingStreamWrapper(ref stream);

            Serialize(encodeStream, obj, new List<object>());

            //RecordSerialized(stream);
        }

        public T Deserialize<T>(Stream stream)
        {
            using var decodeStream = LZ4Stream.Decode(stream, leaveOpen:true);

            //RecordDeserializing(stream);

            //AttachRecordingStreamWrapper(ref stream);

            return Deserialize<T>(decodeStream, new List<object>());
        }

        public object Deserialize(Stream stream)
        {
            using var decodeStream = LZ4Stream.Decode(stream, leaveOpen:true);

            //RecordDeserializing(stream);

            //AttachRecordingStreamWrapper(ref stream);

            return Deserialize(decodeStream, new List<object>());

        }

        public bool CanSerialize(Type type) =>
            RobustNetTypeInfo.GetAssemblies().Contains(type.Assembly);

    }

}
