using System.Buffers.Binary;
using System.Text;

namespace PvPSentinel.FrontlineCore.Sensors;

// A marker's Utf8String can be freed between reading the marker and decoding
// its text. Never create a Span over the game's StringPtr: an invalid pointer
// there raises an uncatchable AccessViolationException in the framework tick.
internal static class NativeUtf8Reader
{
    private const int HeaderSize = 0x22;
    private const int MaximumTooltipBytes = 512;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static string Read(nint address) => Read(address, NativeMemorySnapshot.TryReadBytes);

    internal static string Read(nint address, Func<nint, byte[], bool> readMemory)
    {
        if (address == 0)
            return string.Empty;

        var header = new byte[HeaderSize];
        if (!readMemory(address, header))
            return string.Empty;

        // FFXIVClientStructs Utf8String: StringPtr at 0, BufSize at 8,
        // BufUsed at 0x10, StringLength at 0x18, inline flag at 0x21.
        var bufferAddress = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0, 8));
        var capacity = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(8, 8));
        var used = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0x10, 8));
        var length = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(0x18, 8));
        if (length < 0 || length > MaximumTooltipBytes || used != length + 1 ||
            capacity < used || capacity > 65536 || bufferAddress == 0 ||
            header[0x21] > 1)
            return string.Empty;
        if (length == 0)
            return string.Empty;

        var inline = header[0x21] == 1;
        if (inline && capacity > 64)
            return string.Empty;
        var source = inline ? address + HeaderSize : (nint)bufferAddress;
        var bytes = new byte[(int)length + 1];
        if (!readMemory(source, bytes) || bytes[^1] != 0)
            return string.Empty;
        try
        {
            return StrictUtf8.GetString(bytes, 0, (int)length);
        }
        catch (DecoderFallbackException)
        {
            return string.Empty;
        }
    }

}
