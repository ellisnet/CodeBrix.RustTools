using System;
using System.IO;

namespace CodeBrix.RustTools.CrateBuilder.Tests.Helpers;

/// <summary>
/// A throw-away folder under the test output directory (sandboxes/&lt;guid&gt;). Cargo-running
/// tests copy a sample into one so that target/ never lands inside rust-samples. (A copy of the
/// helper of the same name in CodeBrix.RustTools.Tests.)
/// </summary>
internal sealed class SampleSandbox : IDisposable
{
    private SampleSandbox(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    /// <summary>The sandbox folder (for a sample copy, the copied project root).</summary>
    public string Directory { get; }

    /// <summary>Creates an empty sandbox.</summary>
    public static SampleSandbox CreateEmpty()
        => new SampleSandbox(NewSandboxPath());

    /// <summary>Copies rust-samples/test_project_NN into a new sandbox (skipping target/ and
    /// hidden folders).</summary>
    public static SampleSandbox FromSample(int number)
    {
        var source = RepoPaths.Sample(number);
        if (!System.IO.Directory.Exists(source))
        {
            throw new DirectoryNotFoundException(source);
        }

        var sandbox = new SampleSandbox(Path.Combine(NewSandboxPath(), RepoPaths.SampleName(number)));
        CopyDirectory(source, sandbox.Directory);
        return sandbox;
    }

    /// <summary>Writes a text file (creating folders) relative to the sandbox and returns its path.</summary>
    public string WriteFile(string relativePath, string contents)
    {
        var path = Path.Combine(Directory, relativePath);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, contents);
        return path;
    }

    /// <summary>Creates a sub-folder relative to the sandbox and returns its path.</summary>
    public string CreateDirectory(string relativePath)
        => System.IO.Directory.CreateDirectory(Path.Combine(Directory, relativePath)).FullName;

    public void Dispose()
    {
        try
        {
            var root = Directory;
            var sandboxes = Path.Combine(AppContext.BaseDirectory, "sandboxes");
            while (Path.GetDirectoryName(root) != null
                && !string.Equals(Path.GetDirectoryName(root), sandboxes, StringComparison.Ordinal))
            {
                root = Path.GetDirectoryName(root);
            }

            if (System.IO.Directory.Exists(root) && root.StartsWith(sandboxes, StringComparison.Ordinal))
            {
                System.IO.Directory.Delete(root, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best effort; the folder lives under bin/ and is removed by a clean.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort.
        }
    }

    private static string NewSandboxPath()
        => Path.Combine(AppContext.BaseDirectory, "sandboxes", Guid.NewGuid().ToString("N"));

    private static void CopyDirectory(string source, string destination)
    {
        System.IO.Directory.CreateDirectory(destination);
        foreach (var file in System.IO.Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (var directory in System.IO.Directory.GetDirectories(source))
        {
            var name = Path.GetFileName(directory);
            if (name == "target" || name.StartsWith('.'))
            {
                continue;
            }

            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }
}
