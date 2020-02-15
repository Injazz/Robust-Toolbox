using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

#pragma warning disable 8632

namespace Robust.Shared.Serialization
{

    [PublicAPI]
    public sealed class RecordingStreamWrapper : Stream
    {

        public Stream WrappedStream { get; }

        private readonly TextWriter _writer;

        private long _lastReport;

        private LinkedList<(StackTrace Trace, long Position)> _trace = new LinkedList<(StackTrace Trace, long Position)>();

        public RecordingStreamWrapper(Stream stream, TextWriter writer)
        {
            WrappedStream = stream;
            _writer = writer;
            _lastReport = 0;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Record()
        {
            var newPos = WrappedStream.Position;
            var diff = newPos - _lastReport;
            var st = new StackTrace(1, true);
            var sf1 = st.GetFrame(0);
            _trace.AddLast((st, newPos));
            //_writer.Write($"{DateTime.Now.TimeOfDay} @ {newPos}, {(diff > 0 ? "+" : "")}{diff}: {sf1.GetMethod().Name}");
            _writer.Write($"@ {newPos}, {(diff > 0 ? "+" : "")}{diff}: {sf1.GetMethod().Name}");
            if (diff > 0 && WrappedStream is MemoryStream ms)
            {
                ms.Position = _lastReport;
                var buf = new byte[diff];
                ms.Read(buf);
                ms.Position = newPos;
                _writer.Write(": ");
                _writer.Write(BitConverter.ToString(buf).Replace('-', ' '));
            }
            _writer.WriteLine();
            _writer.Flush();

            const int relevantStackFrameDepth = 5;
            for (var i = 1; i <= relevantStackFrameDepth; ++i)
            {
                var sf = st.GetFrame(i);
                var method = sf.GetMethod();
                var line = sf.GetFileLineNumber();
                _writer.WriteLine($"\tfrom {method.DeclaringType?.Name ?? "_"}.{method.Name}:{line}");
            }

            _lastReport = newPos;
        }


        [Conditional("VERBOSE")]
        private void RecordVerbose()
        {

        }

        public IEnumerable<(StackTrace Trace, long Position)> GetTrace() => _trace;

        public override bool CanRead
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                try
                {
                    return WrappedStream.CanRead;
                }
                finally { RecordVerbose(); }
            }
        }

        public override bool CanSeek
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                try
                {
                    return WrappedStream.CanSeek;
                }
                finally { RecordVerbose(); }
            }
        }

        public override bool CanTimeout
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                try
                {
                    return WrappedStream.CanTimeout;
                }
                finally { RecordVerbose(); }
            }
        }

        public override bool CanWrite
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                try
                {
                    return WrappedStream.CanWrite;
                }
                finally { RecordVerbose(); }
            }
        }

        public override long Length
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                try
                {
                    return WrappedStream.Length;
                }
                finally { RecordVerbose(); }
            }
        }

        public override long Position
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                try
                {
                    return WrappedStream.Position;
                }
                finally { RecordVerbose(); }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            set
            {
                try
                {
                    WrappedStream.Position = value;
                }
                finally { Record(); }
            }
        }

        public override int ReadTimeout
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                try
                {
                    return WrappedStream.ReadTimeout;
                }
                finally { RecordVerbose(); }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            set
            {
                try
                {
                    WrappedStream.ReadTimeout = value;
                }
                finally { Record(); }
            }
        }

        public override int WriteTimeout
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            get
            {
                try
                {
                    return WrappedStream.WriteTimeout;
                }
                finally { RecordVerbose(); }
            }
            [MethodImpl(MethodImplOptions.NoInlining)]
            set
            {
                try
                {
                    WrappedStream.WriteTimeout = value;
                }
                finally { Record(); }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override object InitializeLifetimeService()
        {
            try
            {
                return WrappedStream.InitializeLifetimeService();
            }
            finally { RecordVerbose(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object? state)
        {
            try
            {
                return WrappedStream.BeginRead(buffer, offset, count, callback, state);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object? state)
        {
            try
            {
                return WrappedStream.BeginWrite(buffer, offset, count, callback, state);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Close()
        {
            Record();
            WrappedStream.Close();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void CopyTo(Stream destination, int bufferSize)
        {
            try
            {
                WrappedStream.CopyTo(destination, bufferSize);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        {
            try
            {
                return WrappedStream.CopyToAsync(destination, bufferSize, cancellationToken);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Obsolete("CreateWaitHandle will be removed eventually.  Please use \"new ManualResetEvent(false)\" instead.")]
        protected override WaitHandle CreateWaitHandle()
        {
            try
            {
                return base.CreateWaitHandle();
            }
            finally { RecordVerbose(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        protected override void Dispose(bool disposing)
        {
            Record();
            if (disposing)
            {
                WrappedStream.Dispose();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override ValueTask DisposeAsync()
        {
            Record();
            return WrappedStream.DisposeAsync();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int EndRead(IAsyncResult asyncResult)
        {
            try
            {
                return WrappedStream.EndRead(asyncResult);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void EndWrite(IAsyncResult asyncResult)
        {
            try
            {
                WrappedStream.EndWrite(asyncResult);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Flush()
        {
            try
            {
                WrappedStream.Flush();
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            try
            {
                return WrappedStream.FlushAsync(cancellationToken);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [Obsolete("Do not call or override this method.")]
        protected override void ObjectInvariant()
        {
            try
            {
                base.ObjectInvariant();
            }
            finally { RecordVerbose(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int Read(byte[] buffer, int offset, int count)
        {
            try
            {
                return WrappedStream.Read(buffer, offset, count);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int Read(Span<byte> buffer)
        {
            try
            {
                return WrappedStream.Read(buffer);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            try
            {
                return WrappedStream.ReadAsync(buffer, offset, count, cancellationToken);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            try
            {
                return WrappedStream.ReadAsync(buffer, cancellationToken);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int ReadByte()
        {
            try
            {
                return WrappedStream.ReadByte();
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override long Seek(long offset, SeekOrigin origin)
        {
            try
            {
                return WrappedStream.Seek(offset, origin);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void SetLength(long value)
        {
            try
            {
                WrappedStream.SetLength(value);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Write(byte[] buffer, int offset, int count)
        {
            try
            {
                WrappedStream.Write(buffer, offset, count);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            try
            {
                WrappedStream.Write(buffer);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            try
            {
                return WrappedStream.WriteAsync(buffer, offset, count, cancellationToken);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            try
            {
                return WrappedStream.WriteAsync(buffer, cancellationToken);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override void WriteByte(byte value)
        {
            try
            {
                WrappedStream.WriteByte(value);
            }
            finally { Record(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override bool Equals(object? obj)
        {
            try
            {
                return WrappedStream.Equals(obj);
            }
            finally { RecordVerbose(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override int GetHashCode()
        {
            try
            {
                return WrappedStream.GetHashCode();
            }
            finally { RecordVerbose(); }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public override string ToString()
        {
            try
            {
                return WrappedStream.ToString();
            }
            finally { RecordVerbose(); }
        }

    }

}
