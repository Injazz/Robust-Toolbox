using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Robust.Shared.Network;

namespace Robust.Shared.Utility
{
    /// <summary>
    ///     Extension methods for working with streams.
    /// </summary>
    public static class StreamExt
    {
        /// <summary>
        ///     Copies any stream into a byte array.
        /// </summary>
        /// <param name="stream">The stream to copy.</param>
        /// <returns>The byte array.</returns>
        public static byte[] CopyToArray(this Stream stream)
        {
            using (var memStream = new MemoryStream())
            {
                stream.CopyTo(memStream);
                return memStream.ToArray();
            }
        }

        /// <exception cref="EndOfStreamException">
        /// Thrown if not exactly <paramref name="amount"/> bytes could be read.
        /// </exception>
        public static byte[] ReadExact(this Stream stream, int amount, bool network = false)
        {
            var buffer = new byte[amount];
            var read = 0;
            do
            {
                var cRead = stream.Read(buffer, read, amount - read);
                if (cRead == 0)
                {
                    throw new EndOfStreamException();
                }

                read += cRead;
            } while (read < amount);

            return buffer;
        }

        /// <exception cref="EndOfStreamException">
        /// Thrown if not exactly <paramref name="amount"/> bytes could be read.
        /// </exception>
        public static async Task<byte[]> ReadExactAsync(this Stream stream, int amount, bool network = false, CancellationToken ct = default)
        {
            var buffer = new byte[amount];
            var read = 0;
            while (read < amount)
            {
                var cRead = await stream.ReadAsync(buffer, read, amount - read, ct);
                if (cRead == 0)
                {
                    throw new EndOfStreamException();
                }

                read += cRead;
            }

            return buffer;
        }
    }
}
