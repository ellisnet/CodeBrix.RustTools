using System;
using System.Buffers.Binary;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.WebGpu.Internal;

/// <summary>Bounds-checked reads from a file's bytes; anything outside the file is a
/// <see cref="FormatException"/>.</summary>
internal static class BinaryData
{
    public static ushort U16(byte[] data, long offset) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(data, offset, 2));

    public static uint U32(byte[] data, long offset) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(data, offset, 4));

    public static ulong U64(byte[] data, long offset) => BinaryPrimitives.ReadUInt64LittleEndian(Slice(data, offset, 8));

    public static uint U32BigEndian(byte[] data, long offset) => BinaryPrimitives.ReadUInt32BigEndian(Slice(data, offset, 4));

    /// <summary>A NUL-terminated UTF-8 string starting at <paramref name="offset"/>, ending at
    /// the NUL or at <paramref name="limit"/> (exclusive), whichever comes first.</summary>
    public static string CString(byte[] data, long offset, long limit = -1)
    {
        var end = limit < 0 || limit > data.Length ? data.Length : limit;
        if (offset < 0 || offset >= end)
        {
            throw new FormatException("A string offset is outside the file.");
        }

        var index = Array.IndexOf(data, (byte)0, (int)offset, (int)(end - offset));
        var length = (index < 0 ? end : index) - offset;
        return Encoding.UTF8.GetString(data, (int)offset, (int)length);
    }

    private static ReadOnlySpan<byte> Slice(byte[] data, long offset, int length)
    {
        if (offset < 0 || offset > data.Length - length)
        {
            throw new FormatException("A read at offset " + offset + " is outside the file (" + data.Length + " bytes).");
        }

        return new ReadOnlySpan<byte>(data, (int)offset, length);
    }
}
