using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.RustTools.Toolchain;

namespace CodeBrix.RustTools.Internal;

/// <summary>
/// Finds tool executables. Search order: an explicit override from
/// <see cref="RustWorkbenchOptions"/> (cargo/rustc/rustup only), the options' additional search
/// directories, every PATH entry, the cargo home <c>bin</c> folder (<c>CARGO_HOME</c> or the
/// options' <see cref="RustWorkbenchOptions.CargoHome"/>), then <c>~/.cargo/bin</c>
/// (<c>%USERPROFILE%\.cargo\bin</c> on Windows). On Windows the <c>.exe</c> suffix is added.
/// </summary>
internal sealed class ExecutableLocator
{
    private readonly RustWorkbenchOptions _options;
    private readonly string _pathVariable;
    private readonly string _homeDirectory;
    private readonly string _cargoHomeVariable;

    /// <summary>
    /// Creates a locator that reads PATH, CARGO_HOME and the home folder from this process.
    /// </summary>
    public ExecutableLocator(RustWorkbenchOptions options)
        : this(
            options,
            System.Environment.GetEnvironmentVariable("PATH"),
            System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile),
            System.Environment.GetEnvironmentVariable("CARGO_HOME"),
            OperatingSystem.IsWindows())
    {
    }

    /// <summary>
    /// Creates a locator over an explicit environment (used by tests).
    /// </summary>
    public ExecutableLocator(RustWorkbenchOptions options, string pathVariable, string homeDirectory, string cargoHomeVariable, bool isWindows)
    {
        _options = options ?? new RustWorkbenchOptions();
        _pathVariable = pathVariable ?? string.Empty;
        _homeDirectory = homeDirectory;
        _cargoHomeVariable = cargoHomeVariable;
        IsWindows = isWindows;
    }

    /// <summary>
    /// True when the locator applies Windows rules (<c>.exe</c> suffix, ';' PATH separator).
    /// </summary>
    public bool IsWindows { get; }

    /// <summary>
    /// The cargo home folder in effect: the options override, else CARGO_HOME, else ~/.cargo.
    /// </summary>
    public string CargoHome
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_options.CargoHome))
            {
                return _options.CargoHome;
            }

            if (!string.IsNullOrWhiteSpace(_cargoHomeVariable))
            {
                return _cargoHomeVariable;
            }

            return string.IsNullOrEmpty(_homeDirectory) ? null : Path.Combine(_homeDirectory, ".cargo");
        }
    }

    /// <summary>
    /// Returns the full path of the executable that runs <paramref name="tool"/>, or <c>null</c>
    /// when it cannot be found. <see cref="RustTool.CCompiler"/> and <see cref="RustTool.Linker"/>
    /// resolve to <c>cc</c> on Unix and to <c>cl</c> / <c>link</c> on Windows.
    /// </summary>
    public string Locate(RustTool tool)
    {
        var overridePath = tool switch
        {
            RustTool.Cargo => _options.CargoPath,
            RustTool.Rustc => _options.RustcPath,
            RustTool.Rustup => _options.RustupPath,
            _ => null,
        };
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return IsExecutableFile(overridePath) ? Path.GetFullPath(overridePath) : null;
        }

        return Locate(GetExecutableName(tool));
    }

    /// <summary>
    /// Returns the full path of the named executable (without extension; <c>.exe</c> is added on
    /// Windows), or <c>null</c> when it cannot be found. A rooted path is checked as it is,
    /// without searching.
    /// </summary>
    public string Locate(string executableName)
    {
        if (string.IsNullOrWhiteSpace(executableName))
        {
            return null;
        }

        if (Path.IsPathRooted(executableName))
        {
            return GetCandidateNames(executableName).Where(IsExecutableFile).Select(Path.GetFullPath).FirstOrDefault();
        }

        foreach (var directory in GetSearchDirectories())
        {
            foreach (var candidateName in GetCandidateNames(executableName))
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, candidateName);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                if (IsExecutableFile(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// The folders searched, in order, with duplicates and blanks removed.
    /// </summary>
    public IReadOnlyList<string> GetSearchDirectories()
    {
        var result = new List<string>();
        var seen = new HashSet<string>(IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

        void Add(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                return;
            }

            var trimmed = directory.Trim().Trim('"');
            if (trimmed.Length > 0 && seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        foreach (var directory in _options.AdditionalSearchDirectories)
        {
            Add(directory);
        }

        var separator = IsWindows ? ';' : ':';
        foreach (var directory in _pathVariable.Split(separator, StringSplitOptions.RemoveEmptyEntries))
        {
            Add(directory);
        }

        var cargoHome = CargoHome;
        if (!string.IsNullOrEmpty(cargoHome))
        {
            Add(Path.Combine(cargoHome, "bin"));
        }

        if (!string.IsNullOrEmpty(_homeDirectory))
        {
            Add(Path.Combine(_homeDirectory, ".cargo", "bin"));
        }

        return result;
    }

    /// <summary>
    /// The base executable name (no extension) used for a tool.
    /// </summary>
    public string GetExecutableName(RustTool tool) => tool switch
    {
        RustTool.Rustup => "rustup",
        RustTool.Cargo => "cargo",
        RustTool.Rustc => "rustc",
        RustTool.Clippy => "cargo-clippy",
        RustTool.Rustfmt => "rustfmt",
        RustTool.Rustdoc => "rustdoc",
        RustTool.CCompiler => IsWindows ? "cl" : "cc",
        RustTool.Linker => IsWindows ? "link" : "cc",
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "Unknown tool."),
    };

    private IEnumerable<string> GetCandidateNames(string executableName)
    {
        if (!IsWindows)
        {
            yield return executableName;
            yield break;
        }

        if (executableName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            || executableName.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            || executableName.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
        {
            yield return executableName;
            yield break;
        }

        yield return executableName + ".exe";
    }

    private bool IsExecutableFile(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            if (IsWindows || OperatingSystem.IsWindows())
            {
                return true;
            }

            const UnixFileMode anyExecute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
            return (File.GetUnixFileMode(path) & anyExecute) != 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the human-readable list of the places searched, for "not found" messages.
    /// </summary>
    public string DescribeSearch() => string.Join(IsWindows ? ";" : ":", GetSearchDirectories().Take(50));
}
