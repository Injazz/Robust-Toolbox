using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Joveler.Compression.XZ;
using Lidgren.Network;
using Robust.Shared.Interfaces.Network;
using Robust.Shared.Interfaces.Timing;
using Robust.Shared.IoC;
using Robust.Shared.Log;

namespace Robust.Shared.Network
{

    /// <summary>
    ///     A network connection from this local peer to a remote peer.
    /// </summary>
    internal class NetChannel : INetChannel
    {

        static NetChannel()
        {
            var arch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X86 => "x86",
                Architecture.X64 => "x64",
                Architecture.Arm => "armhf",
                Architecture.Arm64 => "arm64",
                _ => null
            };

            string libPath = null;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                libPath = Path.Combine(Environment.CurrentDirectory, arch, "liblzma.dll");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                libPath = Path.Combine(Environment.CurrentDirectory, arch, "liblzma.so");
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                libPath = Path.Combine(Environment.CurrentDirectory, arch, "liblzma.dylib");
            }

            if (libPath == null || !File.Exists(libPath))
            {
                throw new PlatformNotSupportedException();
            }

            XZInit.GlobalInit(libPath);
        }

        public static void StaticInitializer()
        {
        }

        private readonly NetManager _manager;

        private readonly NetConnection _connection;

        private readonly TcpClient _tcpClient;

        private StatisticsGatheringStreamWrapper _backChannelInbound;

        private StatisticsGatheringStreamWrapper _backChannelOutbound;

        /// <inheritdoc />
        public long ConnectionId => _connection.RemoteUniqueIdentifier;

        /// <inheritdoc />
        public INetManager NetPeer => _manager;

        /// <inheritdoc />
        public short Ping => (short) Math.Round(_connection.AverageRoundtripTime * 1000);

        /// <inheritdoc />
        public IPEndPoint RemoteEndPoint => _connection.RemoteEndPoint;

        /// <summary>
        ///     Exposes the lidgren connection.
        /// </summary>
        public NetConnection Connection => _connection;

        /// <summary>
        ///     Exposes the TCP connection.
        /// </summary>
        private TcpClient TcpClient => _tcpClient;

        /// <remarks>
        ///     You need to wrap this if you want to gather send/receive statistics.
        /// </remarks>
        private NetworkStream BackChannelNetworkStream => TcpClient.GetStream();

        private StatisticsGatheringStreamWrapper _backChannelStats;

        private StatisticsGatheringStreamWrapper StatsGatherer => _backChannelStats
            ??= new StatisticsGatheringStreamWrapper(BackChannelNetworkStream);

        private StatisticsGatheringStreamWrapper BackChannelInbound => _backChannelInbound
            ??= new StatisticsGatheringStreamWrapper(
                new XZStream(StatsGatherer, new XZDecompressOptions
                {
                    LeaveOpen = true,
                    BufferSize = 4 * 1024 * 1024,
                    DecodeFlags = LzmaDecodingFlag.Concatenated | LzmaDecodingFlag.IgnoreCheck
                })
            );

        private StatisticsGatheringStreamWrapper BackChannelOutbound => _backChannelOutbound
            ??= new StatisticsGatheringStreamWrapper(
                new XZStream(StatsGatherer, new XZCompressOptions
                {
                    LeaveOpen = true,
                    BufferSize = 8 * 1024 * 1024,
                    Check = LzmaCheck.None,
                    ExtremeFlag = false,
                    Level = LzmaCompLevel.Level1
                })
            );

        public long BackChannelBytesSent => _backChannelOutbound.BytesWritten;
        public long BackChannelBytesReceived => _backChannelInbound.BytesRead;
        public long BackChannelBytesSentCompressed => _backChannelStats.BytesWritten;
        public long BackChannelBytesReceivedCompressed => _backChannelStats.BytesRead;

        public void BackChannelWrite(byte[] buffer, int offset, int length) =>
            BackChannelOutbound.Write(buffer, offset, length);

        public byte[] BackChannelFill(byte[] buffer, ref int read)
        {
            if (read == buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(read));

            var size = buffer.Length - read;
            try
            {
                read += BackChannelInbound.Read(buffer, read, size);
            }
            catch (IOException ioex)
            {
                if (TcpClient.Connected)
                {
                    Logger.WarningS("net.tcp",
                        $"Can't complete read of {size} real bytes from {TcpClient.Available} compressed bytes buffered yet.");
                }
            }

            return buffer;
        }

        public NetSessionId SessionId { get; }

        public bool BackChannelConnected => TcpClient.Connected;

        public int BackChannelDataAvailable => TcpClient.Client.Available;

        private int _lastFlushedTick = -1;

        private TimeSpan _lastFlushedTime;

        public void FlushOutbound(bool force = false)
        {
            var timing = IoCManager.Resolve<IGameTiming>();
            var currentTick = timing.CurTick.Value;
            var realTime = timing.RealTime;
            if (force)
            {
                _lastFlushedTick = (int) currentTick;
                _lastFlushedTime = realTime;
                BackChannelOutbound.Flush();
            }

            if (currentTick > _lastFlushedTick)
            {
                _lastFlushedTick = (int) currentTick;
                _lastFlushedTime = realTime;
                BackChannelOutbound.Flush();
            }
            else if (currentTick <= _lastFlushedTick)
            {
                var twoTicks = timing.TickPeriod * 60;
                var deadline = _lastFlushedTime.Add(twoTicks);
                if (realTime > deadline)
                {
                    Logger.WarningS("net.tcp", $"Flushing outbound stream for an overly long tick: {currentTick}");
                    _lastFlushedTick = (int) currentTick;
                    _lastFlushedTime = realTime;
                    BackChannelOutbound.Flush();
                }
            }
        }

        /// <summary>
        ///     Creates a new instance of a NetChannel.
        /// </summary>
        /// <param name="manager">The server this channel belongs to.</param>
        /// <param name="connection">The raw NetConnection to the remote peer.</param>
        internal NetChannel(NetManager manager, NetConnection connection, NetSessionId sessionId, TcpClient tcpClient)
        {
            _manager = manager;
            _connection = connection;
            _tcpClient = tcpClient;
            SessionId = sessionId;
        }

        /// <inheritdoc />
        public T CreateNetMessage<T>()
            where T : NetMessage
        {
            return _manager.CreateNetMessage<T>();
        }

        /// <inheritdoc />
        public void SendMessage(NetMessage message)
        {
            _manager.ServerSendMessage(message, this);
        }

        /// <inheritdoc />
        public void Disconnect(string reason)
        {
            if (_connection.Status == NetConnectionStatus.Connected)
                _connection.Disconnect(reason);
        }

    }

}
