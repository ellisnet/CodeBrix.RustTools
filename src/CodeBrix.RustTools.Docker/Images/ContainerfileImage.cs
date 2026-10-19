using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CodeBrix.RustTools.Docker.Images;

/// <summary>
/// Identifies the image built from a <see cref="BuildImageSpec.Containerfile"/>: a hash of
/// everything that goes into the build, and the tag derived from it.
/// </summary>
internal static class ContainerfileImage
{
    /// <summary>The prefix of a Containerfile image's tag; the hash follows it.</summary>
    public const string TagPrefix = "containerfile-";

    /// <summary>
    /// The first 16 lowercase hexadecimal digits of a SHA-256 over the Containerfile's bytes, the
    /// build arguments (sorted by name), the platform when <see cref="BuildImageSpec.Platform"/>
    /// is set (a spec without one keeps the hash it always had) and every file of the build
    /// context (sorted by relative path; a symbolic link counts by its target, and links to
    /// folders are not followed).
    /// </summary>
    /// <param name="spec">A spec whose <see cref="BuildImageSpec.Containerfile"/> is set and
    /// valid.</param>
    public static string ComputeHash(BuildImageSpec spec)
    {
        BuildImageSpec.ThrowIfNull(spec);
        if (!spec.UsesContainerfile)
        {
            throw new ArgumentException("The spec has no Containerfile.", nameof(spec));
        }

        var text = new StringBuilder();
        text.Append("codebrix-containerfile-v1\n");
        text.Append("containerfile=").Append(HashFile(Path.GetFullPath(spec.Containerfile))).Append('\n');
        if (spec.ContainerfileBuildArgs != null)
        {
            foreach (var pair in spec.ContainerfileBuildArgs.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                text.Append("arg=").Append(pair.Key).Append('=').Append(pair.Value ?? string.Empty).Append('\n');
            }
        }

        var platform = Engine.DockerPlatforms.Normalize(spec.Platform);
        if (platform != null)
        {
            text.Append("platform=").Append(platform).Append('\n');
        }

        var context = spec.ResolveContainerfileContext();
        foreach (var entry in ContextEntries(context).OrderBy(e => e.Relative, StringComparer.Ordinal))
        {
            text.Append(entry.Line).Append('\n');
        }

        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).Substring(0, 16);
    }

    /// <summary>The tag of the image a spec's Containerfile builds.</summary>
    public static string TagFor(BuildImageSpec spec) => TagPrefix + ComputeHash(spec);

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>
    /// One line per file and symbolic link below <paramref name="root"/>, without following
    /// links to folders.
    /// </summary>
    private static List<(string Relative, string Line)> ContextEntries(string root)
    {
        var result = new List<(string Relative, string Line)>();
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                var relative = Path.GetRelativePath(root, path).Replace('\\', '/');
                var info = new FileInfo(path);
                if (info.LinkTarget != null)
                {
                    result.Add((relative, "link=" + relative + "->" + info.LinkTarget));
                }
                else if (info.Attributes.HasFlag(FileAttributes.Directory))
                {
                    pending.Push(path);
                }
                else
                {
                    result.Add((relative, "file=" + relative + ":" + HashFile(path)));
                }
            }
        }

        return result;
    }
}
