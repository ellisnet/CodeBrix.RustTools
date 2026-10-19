using System;
using System.Buffers.Binary;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Building.Verification;

/// <summary>
/// Bounds-checked little-endian reads over a file's bytes; any read outside the file throws
/// <see cref="FormatException"/> (a truncated or corrupt file), never an index exception.
/// </summary>
internal sealed class BinaryView
{
    private readonly byte[] _bytes;

    public BinaryView(byte[] bytes)
    {
        _bytes = bytes ?? throw new ArgumentNullException(nameof(bytes));
    }

    public long Length => _bytes.Length;

    public byte U8(long offset) => Slice(offset, 1)[0];

    public ushort U16(long offset) => BinaryPrimitives.ReadUInt16LittleEndian(Slice(offset, 2));

    public uint U32(long offset) => BinaryPrimitives.ReadUInt32LittleEndian(Slice(offset, 4));

    public uint U32BigEndian(long offset) => BinaryPrimitives.ReadUInt32BigEndian(Slice(offset, 4));

    public ulong U64(long offset) => BinaryPrimitives.ReadUInt64LittleEndian(Slice(offset, 8));

    public long I64(long offset) => BinaryPrimitives.ReadInt64LittleEndian(Slice(offset, 8));

    /// <summary>A NUL-terminated string starting at <paramref name="offset"/> (UTF-8), bounded
    /// by <paramref name="limit"/> (an absolute offset; -1 = the end of the file).</summary>
    public string CString(long offset, long limit = -1)
    {
        var end = limit < 0 || limit > _bytes.Length ? _bytes.Length : limit;
        if (offset < 0 || offset >= end)
        {
            throw new FormatException($"A string offset (0x{offset:x}) lies outside the file.");
        }

        var span = new ReadOnlySpan<byte>(_bytes, (int)offset, (int)(end - offset));
        var nul = span.IndexOf((byte)0);
        return Encoding.UTF8.GetString(nul < 0 ? span : span.Slice(0, nul));
    }

    public bool StartsWith(ReadOnlySpan<byte> prefix)
        => _bytes.Length >= prefix.Length && new ReadOnlySpan<byte>(_bytes, 0, prefix.Length).SequenceEqual(prefix);

    public static long Checked(ulong value)
    {
        if (value > long.MaxValue)
        {
            throw new FormatException("An offset or size in the file is out of range.");
        }

        return (long)value;
    }

    private ReadOnlySpan<byte> Slice(long offset, int count)
    {
        if (offset < 0 || offset > _bytes.Length - count)
        {
            throw new FormatException($"The file is truncated or corrupt (a read at 0x{offset:x} of {count} byte(s) lies outside its {_bytes.Length} bytes).");
        }

        return new ReadOnlySpan<byte>(_bytes, (int)offset, count);
    }
}
