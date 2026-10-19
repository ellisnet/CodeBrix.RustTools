using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CodeBrix.RustTools.CrateBuilder.Facade.Planning;

/// <summary>
/// Evaluates the extractor's verbatim <c>cfg</c> predicates against one CrateBuilder target,
/// three-valued: a predicate the planner cannot decide (a feature that was not requested,
/// <c>debug_assertions</c>, a custom cfg) is UNKNOWN and never rules an item out. When the
/// crate's feature names are known, a feature that is one of them but not enabled is OFF.
/// </summary>
internal static class CfgEvaluator
{
    /// <summary>Whether every predicate holds (<c>true</c>), one fails (<c>false</c>) or it
    /// cannot be decided (<c>null</c>) for <paramref name="target"/>.</summary>
    /// <param name="predicates">The verbatim predicates.</param>
    /// <param name="target">The target.</param>
    /// <param name="enabledFeatures">The features that are on.</param>
    /// <param name="knownFeatures">Every feature name of the crate; <c>null</c> = not known
    /// (a feature that is not enabled is then unknown).</param>
    public static bool? EvaluateAll(IEnumerable<string> predicates, CrateTarget target, ICollection<string> enabledFeatures, ICollection<string> knownFeatures = null)
    {
        bool? result = true;
        foreach (var predicate in predicates ?? Enumerable.Empty<string>())
        {
            result = And(result, Evaluate(predicate, target, enabledFeatures, knownFeatures));
        }

        return result;
    }

    /// <summary>Evaluates one predicate (<c>target_arch = "wasm32"</c>,
    /// <c>all(unix, not(test))</c>); text that does not parse is unknown.</summary>
    /// <param name="predicate">The verbatim predicate.</param>
    /// <param name="target">The target.</param>
    /// <param name="enabledFeatures">The features that are on.</param>
    /// <param name="knownFeatures">Every feature name of the crate; <c>null</c> = not
    /// known.</param>
    public static bool? Evaluate(string predicate, CrateTarget target, ICollection<string> enabledFeatures, ICollection<string> knownFeatures = null)
    {
        try
        {
            var tokens = Tokenize(predicate ?? string.Empty);
            var position = 0;
            var node = Parse(tokens, ref position);
            if (position != tokens.Count)
            {
                return null;
            }

            return node.Evaluate(target, enabledFeatures ?? Array.Empty<string>(), knownFeatures);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>The value of a <c>key = "value"</c> fact for a target, or <c>null</c> when the
    /// key is not a target fact the planner knows.</summary>
    public static string TargetFact(string key, CrateTarget target)
    {
        var windows = CrateTargets.IsWindows(target);
        var mac = CrateTargets.IsMacOS(target);
        var android = CrateTargets.IsAndroid(target);
        switch (key)
        {
            case "target_arch":
                return target switch
                {
                    CrateTarget.WinX64 or CrateTarget.OsxX64 or CrateTarget.LinuxX64 or CrateTarget.AndroidX64 => "x86_64",
                    CrateTarget.LinuxRiscV64 => "riscv64",
                    _ => "aarch64",
                };
            case "target_os":
                return windows ? "windows" : mac ? "macos" : android ? "android" : "linux";
            case "target_family":
                return windows ? "windows" : "unix";
            case "target_env":
                return windows || CrateTargets.IsLinux(target) ? "gnu" : string.Empty;
            case "target_abi":
                return windows ? "llvm" : string.Empty;
            case "target_vendor":
                return windows ? "pc" : mac ? "apple" : "unknown";
            case "target_pointer_width":
                return "64";
            case "target_endian":
                return "little";
            default:
                return null;
        }
    }

    private static bool? And(bool? a, bool? b) => a == false || b == false ? false : a == true && b == true ? true : null;

    private static bool? Or(bool? a, bool? b) => a == true || b == true ? true : a == false && b == false ? false : null;

    private static List<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
            }
            else if (c == '(' || c == ')' || c == ',' || c == '=')
            {
                tokens.Add(c.ToString());
                i++;
            }
            else if (c == '"')
            {
                var sb = new StringBuilder("\"");
                i++;
                while (i < text.Length && text[i] != '"')
                {
                    if (text[i] == '\\' && i + 1 < text.Length)
                    {
                        i++;
                    }

                    sb.Append(text[i++]);
                }

                if (i >= text.Length)
                {
                    throw new FormatException("Unterminated string.");
                }

                i++;
                tokens.Add(sb.ToString());
            }
            else if (char.IsLetterOrDigit(c) || c == '_' || c == ':')
            {
                var start = i;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_' || text[i] == ':'))
                {
                    i++;
                }

                tokens.Add(text.Substring(start, i - start));
            }
            else
            {
                throw new FormatException("Unexpected character.");
            }
        }

        return tokens;
    }

    private static Node Parse(List<string> tokens, ref int position)
    {
        if (position >= tokens.Count || tokens[position].StartsWith('"') || "(),=".Contains(tokens[position], StringComparison.Ordinal))
        {
            throw new FormatException("Identifier expected.");
        }

        var name = tokens[position++];
        if (position < tokens.Count && tokens[position] == "=")
        {
            position++;
            if (position >= tokens.Count || !tokens[position].StartsWith('"'))
            {
                throw new FormatException("String expected.");
            }

            return new Node { Name = name, Value = tokens[position++].Substring(1) };
        }

        if (position < tokens.Count && tokens[position] == "(")
        {
            position++;
            var node = new Node { Name = name, Children = new List<Node>() };
            while (position < tokens.Count && tokens[position] != ")")
            {
                node.Children.Add(Parse(tokens, ref position));
                if (position < tokens.Count && tokens[position] == ",")
                {
                    position++;
                }
            }

            if (position >= tokens.Count)
            {
                throw new FormatException("')' expected.");
            }

            position++;
            return node;
        }

        return new Node { Name = name };
    }

    private sealed class Node
    {
        public string Name { get; set; }

        public string Value { get; set; }

        public List<Node> Children { get; set; }

        public bool? Evaluate(CrateTarget target, ICollection<string> features, ICollection<string> known)
        {
            if (Children != null)
            {
                switch (Name)
                {
                    case "all":
                        return Children.Aggregate((bool?)true, (acc, c) => And(acc, c.Evaluate(target, features, known)));
                    case "any":
                        return Children.Aggregate((bool?)false, (acc, c) => Or(acc, c.Evaluate(target, features, known)));
                    case "not":
                        if (Children.Count != 1)
                        {
                            return null;
                        }

                        var inner = Children[0].Evaluate(target, features, known);
                        return inner == null ? null : !inner;
                    default:
                        return null;
                }
            }

            if (Value != null)
            {
                if (Name == "feature")
                {
                    return features.Contains(Value) ? true : known != null && known.Contains(Value) ? false : null;
                }

                if (Name == "target_has_atomic")
                {
                    return Value is "8" or "16" or "32" or "64" or "ptr" ? true : null;
                }

                var fact = TargetFact(Name, target);
                return fact == null ? null : string.Equals(fact, Value, StringComparison.Ordinal);
            }

            switch (Name)
            {
                case "unix":
                    return !CrateTargets.IsWindows(target);
                case "windows":
                    return CrateTargets.IsWindows(target);
                case "test":
                case "doc":
                case "doctest":
                case "docsrs":
                case "miri":
                    return false;
                default:
                    return null;
            }
        }
    }
}
