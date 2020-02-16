#nullable enable
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace Robust.Shared.Network
{

    [PublicAPI]
    public sealed class StatisticsGatheringStreamWrapper : Stream
    {

        private long _bytesRead;

        private long _bytesWritten;

        public Stream WrappedStream { get; }

        public long BytesRead => _bytesRead;

        public long BytesWritten => _bytesWritten;

        public StatisticsGatheringStreamWrapper(Stream stream)
            => WrappedStream = stream;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Record()
        {
        }

        [Conditional("VERBOSE")]
        private void RecordVerbose()
        {
        }

        public override bool CanRead
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WrappedStream.CanRead;
        }

        public override bool CanSeek
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WrappedStream.CanSeek;
        }

        public override bool CanTimeout
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WrappedStream.CanTimeout;
        }

        public override bool CanWrite
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WrappedStream.CanWrite;
        }

        public override long Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WrappedStream.Length;
        }

        public override long Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WrappedStream.Position;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => WrappedStream.Position = value;
        }

        public override int ReadTimeout
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WrappedStream.ReadTimeout;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => WrappedStream.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => WrappedStream.WriteTimeout;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => WrappedStream.WriteTimeout = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override object InitializeLifetimeService() => WrappedStream.InitializeLifetimeService();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object? state) =>
            WrappedStream.BeginRead(buffer, offset, count, callback, state);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object? state)
        {
            try
            {
                return WrappedStream.BeginWrite(buffer, offset, count, callback, state);
            }
            finally { _bytesWritten += count; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Close() => WrappedStream.Close();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Obsolete("CreateWaitHandle will be removed eventually.  Please use \"new ManualResetEvent(false)\" instead.")]
        protected override WaitHandle CreateWaitHandle() => base.CreateWaitHandle();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected override void Dispose(bool disposing) => WrappedStream.Dispose();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override ValueTask DisposeAsync() => WrappedStream.DisposeAsync();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int EndRead(IAsyncResult asyncResult)
        {
            int read = 0;
            try
            {
                return read = WrappedStream.EndRead(asyncResult);
            }
            finally { _bytesRead += read; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void EndWrite(IAsyncResult asyncResult) => WrappedStream.EndWrite(asyncResult);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Flush() => WrappedStream.Flush();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Task FlushAsync(CancellationToken cancellationToken) => WrappedStream.FlushAsync(cancellationToken);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Obsolete("Do not call or override this method.")]
        protected override void ObjectInvariant() => base.ObjectInvariant();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = 0;
            try
            {
                return read = WrappedStream.Read(buffer, offset, count);
            }
            finally { _bytesRead += read; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int Read(Span<byte> buffer)
        {
            var read = 0;
            try
            {
                return read = WrappedStream.Read(buffer);
            }
            finally { _bytesRead += read; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var read = 0;
            try
            {
                return read = await WrappedStream.ReadAsync(buffer, offset, count, cancellationToken);
            }
            finally { _bytesRead += read; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            var read = 0;
            try
            {
                return read = await WrappedStream.ReadAsync(buffer, cancellationToken);
            }
            finally { _bytesRead += read; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int ReadByte()
        {
            var read = 0;
            try
            {
                return read = WrappedStream.ReadByte();
            }
            finally
            {
                if (read > 0)
                {
                    ++_bytesRead;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override long Seek(long offset, SeekOrigin origin) => WrappedStream.Seek(offset, origin);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void SetLength(long value) => WrappedStream.SetLength(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Write(byte[] buffer, int offset, int count)
        {
            try
            {
                WrappedStream.Write(buffer, offset, count);
            }
            finally { _bytesWritten += count; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            try
            {
                WrappedStream.Write(buffer);
            }
            finally { _bytesWritten += buffer.Length; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            try
            {
                return WrappedStream.WriteAsync(buffer, offset, count, cancellationToken);
            }
            finally { _bytesWritten += count; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = new CancellationToken())
        {
            try
            {
                return WrappedStream.WriteAsync(buffer, cancellationToken);
            }
            finally { _bytesWritten += buffer.Length; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override void WriteByte(byte value)
        {
            try
            {
                WrappedStream.WriteByte(value);
            }
            finally { ++_bytesWritten; }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override bool Equals(object? obj) => WrappedStream.Equals(obj);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override int GetHashCode() => WrappedStream.GetHashCode();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override string ToString() => WrappedStream.ToString() ?? "";

    }

}
