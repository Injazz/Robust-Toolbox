using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Serialization;

namespace Robust.Shared.Serialization
{

    public partial class RobustSerializer
    {

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
            AttachRecordingStreamWrapper(ref stream);

            Serialize(stream, obj, new List<object>());

            RecordSerialized(stream);
        }

        public void Serialize<T>(Stream stream, T obj)
        {
            AttachRecordingStreamWrapper(ref stream);

            Serialize(stream, obj, new List<object>());

            RecordSerialized(stream);
        }

        public T Deserialize<T>(Stream stream)
        {
            RecordDeserializing(stream);

            AttachRecordingStreamWrapper(ref stream);

            return Deserialize<T>(stream, new List<object>());
        }

        public object Deserialize(Stream stream)
        {
            RecordDeserializing(stream);

            AttachRecordingStreamWrapper(ref stream);

            return Deserialize(stream, new List<object>());
        }

        public bool CanSerialize(Type type) =>
            RobustNetTypeInfo.GetAssemblies().Contains(type.Assembly);

    }

}
