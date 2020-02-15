using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Joveler.Compression.XZ;
using Lidgren.Network;
using Robust.Shared.Interfaces.Network;

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

        public static void StaticInitializer() {}


        private readonly NetManager _manager;

        private readonly NetConnection _connection;

        private readonly TcpClient _tcpClient;


        private XZStream _backChannelInbound;
        private XZStream _backChannelOutbound;

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
        public TcpClient TcpClient => _tcpClient;

        public Stream BackChannelInbound => _backChannelInbound
            ??= new XZStream(_tcpClient.GetStream(), new XZDecompressOptions
            {
                BufferSize = 4 * 1024 * 1024,
                DecodeFlags = LzmaDecodingFlag.IgnoreCheck
            });

        public Stream BackChannelOutbound => _backChannelOutbound
            ??= new XZStream(_tcpClient.GetStream(), new XZCompressOptions
            {
                BufferSize = 4 * 1024 * 1024,
                Check = LzmaCheck.None,
                ExtremeFlag = false,
                LeaveOpen = true,
                Level = LzmaCompLevel.Level6
            });

        public NetSessionId SessionId { get; }

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
