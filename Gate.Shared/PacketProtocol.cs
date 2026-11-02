using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Gate.Shared;

public static class PacketProtocol
{
    private const int MaxPacketSize = 10 * 1024 * 1024; // 10 MB sanity limit

    public static async Task SendPacketAsync(Stream stream, NetworkPacket packet, CancellationToken ct = default)
    {
        string json = JsonSerializer.Serialize(packet);
        byte[] payloadBytes = Encoding.UTF8.GetBytes(json);

        byte[] lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(lengthPrefix, payloadBytes.Length);

        await stream.WriteAsync(lengthPrefix, ct);
        await stream.WriteAsync(payloadBytes, ct);
        await stream.FlushAsync(ct);
    }

    public static async Task<NetworkPacket?> ReadPacketAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] lengthPrefix = new byte[4];
        int headerRead = await ReadExactBytesAsync(stream, lengthPrefix, 0, 4, ct);
        if (headerRead == 0)
        {
            // Stream closed by remote peer
            return null;
        }

        int payloadLength = BinaryPrimitives.ReadInt32LittleEndian(lengthPrefix);
        if (payloadLength <= 0 || payloadLength > MaxPacketSize)
        {
            throw new InvalidDataException($"Invalid packet size received: {payloadLength} bytes");
        }

        byte[] payloadBuffer = new byte[payloadLength];
        int payloadRead = await ReadExactBytesAsync(stream, payloadBuffer, 0, payloadLength, ct);
        if (payloadRead < payloadLength)
        {
            throw new EndOfStreamException("Connection closed prematurely while reading packet payload");
        }

        string json = Encoding.UTF8.GetString(payloadBuffer);
        return JsonSerializer.Deserialize<NetworkPacket>(json);
    }

    private static async Task<int> ReadExactBytesAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + totalRead, count - totalRead), ct);
            if (read == 0)
            {
                break;
            }
            totalRead += read;
        }
        return totalRead;
    }
}
