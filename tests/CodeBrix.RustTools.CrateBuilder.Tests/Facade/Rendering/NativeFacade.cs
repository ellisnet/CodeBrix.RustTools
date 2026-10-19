using System;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Facade.Rendering;

/// <summary>
/// A built facade library loaded into the test process (NativeLibrary), with typed function
/// pointers through delegates and helpers for the facade's last-error message and buffers.
/// </summary>
internal sealed class NativeFacade : IDisposable
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int LastErrorFn(byte[] buf, nuint cap, out nuint written);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void FreeFn(FacadeBuffer buffer);

    private readonly IntPtr _module;
    private readonly string _prefix;

    public NativeFacade(string path, string prefix)
    {
        _module = NativeLibrary.Load(path);
        _prefix = prefix;
    }

    public T Fn<T>(string suffix) where T : Delegate
        => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_module, _prefix + suffix));

    /// <summary>The calling thread's last-error message (asks for the length first).</summary>
    public string LastError()
    {
        var read = Fn<LastErrorFn>("last_error_message");
        var status = read(null, 0, out var length);
        if (length == 0)
        {
            return status == 0 ? string.Empty : "(status " + status + ")";
        }

        var buffer = new byte[(int)length];
        status = read(buffer, length, out var written);
        if (status != 0 || written != length)
        {
            throw new InvalidOperationException("last_error_message failed: " + status);
        }

        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>Copies a buffer's bytes and releases it with <paramref name="free"/>.</summary>
    public byte[] TakeBytes(FacadeBuffer buffer, string free = "free_buffer")
    {
        var bytes = new byte[(int)buffer.Len];
        if (buffer.Len > 0)
        {
            Marshal.Copy(buffer.Data, bytes, 0, bytes.Length);
        }

        Fn<FreeFn>(free)(buffer);
        return bytes;
    }

    public string TakeString(FacadeBuffer buffer) => Encoding.UTF8.GetString(TakeBytes(buffer, "free_string"));

    public static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    public void Dispose() => NativeLibrary.Free(_module);
}

[StructLayout(LayoutKind.Sequential)]
internal struct FacadeBuffer
{
    public IntPtr Data;
    public nuint Len;
    public nuint Cap;
}
