using System;
using System.Security.Cryptography;
using System.Text;

namespace CodeBrix.RustTools.Docker.Internal;

/// <summary>
/// Short, stable identifiers derived from text.
/// </summary>
internal static class Hashing
{
    /// <summary>The first 16 lowercase hexadecimal digits of the SHA-256 of the UTF-8
    /// text.</summary>
    public static string Short(string text)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty))).Substring(0, 16);
}
