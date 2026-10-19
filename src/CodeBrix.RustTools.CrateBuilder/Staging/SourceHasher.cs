using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CodeBrix.RustTools.CrateBuilder.Staging;

/// <summary>
/// The source hash of a staged tree: SHA-256 over, for every regular file under the root in
/// ordinal order of its forward-slash relative path, the UTF-8 bytes of that path, a NUL
/// byte, the file's bytes and a NUL byte. Lower-case hex. Nothing else (line endings,
/// timestamps, permissions) is normalised or included, so the same files give the same hash
/// on every operating system.
/// </summary>
internal static class SourceHasher
{
    /// <summary>Hashes every regular file under <paramref name="rootDirectory"/> (symbolic
    /// links are not followed and not hashed).</summary>
    public static string Compute(string rootDirectory, CancellationToken cancellationToken = default)
    {
        var root = Path.GetFullPath(rootDirectory);
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        var files = Directory.EnumerateFiles(root, "*", options)
            .Select(f => (Full: f, Relative: StagingFileSystem.RelativePath(root, f)))
            .OrderBy(f => f.Relative, StringComparer.Ordinal)
            .ToList();

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var separator = new byte[] { 0 };
        var buffer = new byte[81920];
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            hash.AppendData(Encoding.UTF8.GetBytes(file.Relative));
            hash.AppendData(separator);
            using (var stream = new FileStream(file.Full, FileMode.Open, FileAccess.Read, FileShare.Read, buffer.Length, FileOptions.SequentialScan))
            {
                int read;
                while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                }
            }

            hash.AppendData(separator);
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
