using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace PvPSentinel.FrontlineCore.Sensors;

// ReadProcessMemory copies a native snapshot into managed memory. Invalid or
// concurrently freed source pages fail the read instead of crashing the CLR.
internal static class NativeMemorySnapshot
{
    internal static bool TryReadBytes(nint address, byte[] destination) =>
        address != 0 && ReadProcessMemory(GetCurrentProcess(), address, destination,
            (nuint)destination.Length, out var bytesRead) && bytesRead == (nuint)destination.Length;

    internal static unsafe bool TryReadStruct<T>(nint address, out T value) where T : unmanaged
    {
        var bytes = new byte[sizeof(T)];
        if (!TryReadBytes(address, bytes))
        {
            value = default;
            return false;
        }
        value = MemoryMarshal.Read<T>(bytes);
        return true;
    }

    internal static bool TryReadPointerList(nint address, int maximum, out nint[] pointers) =>
        TryReadPointerList(address, maximum, TryReadBytes, out pointers);

    internal static bool TryReadPointerList(nint address, int maximum,
        Func<nint, byte[], bool> readMemory, out nint[] pointers)
    {
        pointers = [];
        var vector = new byte[24];
        if (address == 0 || !readMemory(address, vector))
            return false;
        var first = BinaryPrimitives.ReadUInt64LittleEndian(vector.AsSpan(0, 8));
        var last = BinaryPrimitives.ReadUInt64LittleEndian(vector.AsSpan(8, 8));
        var end = BinaryPrimitives.ReadUInt64LittleEndian(vector.AsSpan(16, 8));
        if (last < first || end < last || (last - first) % 8 != 0 ||
            (last - first) / 8 > (ulong)maximum)
            return false;
        var count = (int)((last - first) / 8);
        if (count == 0)
            return true;
        if (first == 0)
            return false;
        var data = new byte[count * 8];
        if (!readMemory((nint)first, data))
            return false;
        pointers = new nint[count];
        for (var i = 0; i < count; i++)
            pointers[i] = (nint)BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(i * 8, 8));
        return true;
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(nint process, nint address,
        [Out] byte[] buffer, nuint size, out nuint bytesRead);
}
