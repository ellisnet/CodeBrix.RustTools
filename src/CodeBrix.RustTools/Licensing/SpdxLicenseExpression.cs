using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.RustTools.Licensing;

/// <summary>
/// A parsed SPDX license expression, as found in a Cargo.toml <c>license</c> field: license
/// identifiers, the <c>+</c> ("or later") suffix, <c>WITH</c> exceptions, <c>AND</c>,
/// <c>OR</c> and parentheses. Operators are case-insensitive, and the legacy <c>/</c>
/// separator (<c>MIT/Apache-2.0</c>) is read as <c>OR</c>, as cargo documents. Precedence is
/// <c>WITH</c>, then <c>AND</c>, then <c>OR</c>. Each instance is one node of the tree.
/// </summary>
public sealed class SpdxLicenseExpression
{
    private SpdxLicenseExpression()
    {
    }

    /// <summary>The node kind.</summary>
    public SpdxExpressionKind Kind { get; private init; }

    /// <summary>For a <see cref="SpdxExpressionKind.License"/> node: the identifier, in its
    /// canonical SPDX spelling when known (deprecated identifiers are replaced by their current
    /// form), otherwise as written (without any <c>+</c>). <c>null</c> for AND / OR nodes.</summary>
    public string Identifier { get; private init; }

    /// <summary>For a license node: the recognised license, or <c>null</c> when the identifier
    /// is not one of <see cref="OpenSourceLicense"/> (including <c>LicenseRef-</c> identifiers).</summary>
    public OpenSourceLicense? License { get; private init; }

    /// <summary>For a license node: true when a <c>+</c> follows the identifier and was not
    /// already folded into an <c>-or-later</c> identifier.</summary>
    public bool IsOrLater { get; private init; }

    /// <summary>For a license node: the <c>WITH</c> exception, or <c>null</c>.</summary>
    public SpdxLicenseException Exception { get; private init; }

    /// <summary>For AND / OR nodes: the operands (nested operators of the same kind are
    /// flattened). Empty for a license node.</summary>
    public IReadOnlyList<SpdxLicenseExpression> Operands { get; private init; } = Array.Empty<SpdxLicenseExpression>();

    /// <summary>True for a license node whose identifier is recognised.</summary>
    public bool IsKnownLicense => License.HasValue;

    /// <summary>Every license identifier in the expression that is not recognised, in order,
    /// without duplicates.</summary>
    public IReadOnlyList<string> UnknownIdentifiers
        => LicenseNodes().Where(n => !n.IsKnownLicense).Select(n => n.Identifier).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Every recognised license in the expression, in order, without duplicates.</summary>
    public IReadOnlyList<OpenSourceLicense> Licenses
        => LicenseNodes().Where(n => n.IsKnownLicense).Select(n => n.License.GetValueOrDefault()).Distinct().ToList();

    /// <summary>
    /// Parses an expression.
    /// </summary>
    /// <param name="text">The expression text.</param>
    /// <exception cref="FormatException">The text is empty or not a valid expression.</exception>
    public static SpdxLicenseExpression Parse(string text)
        => TryParse(text, out var expression, out var error) ? expression : throw new FormatException(error);

    /// <summary>
    /// Parses an expression, returning <c>false</c> instead of throwing.
    /// </summary>
    /// <param name="text">The expression text.</param>
    /// <param name="expression">The parsed expression, or <c>null</c>.</param>
    public static bool TryParse(string text, out SpdxLicenseExpression expression) => TryParse(text, out expression, out _);

    /// <summary>
    /// Parses an expression, returning <c>false</c> and a description of the problem instead of
    /// throwing.
    /// </summary>
    /// <param name="text">The expression text.</param>
    /// <param name="expression">The parsed expression, or <c>null</c>.</param>
    /// <param name="error">Why parsing failed, or <c>null</c>.</param>
    public static bool TryParse(string text, out SpdxLicenseExpression expression, out string error)
    {
        expression = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "The license expression is empty.";
            return false;
        }

        if (!Tokenize(text, out var tokens, out error))
        {
            return false;
        }

        var parser = new Parser(tokens);
        expression = parser.ParseOr(out error);
        if (expression != null && error == null && !parser.AtEnd)
        {
            error = $"Unexpected '{parser.Current}' in the license expression '{text.Trim()}'.";
        }

        if (error != null)
        {
            expression = null;
            error = error.Contains(text.Trim(), StringComparison.Ordinal) ? error : $"{error} (in '{text.Trim()}')";
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks the expression against a policy. A license node is approved when the policy
    /// allows its license (or its <c>WITH</c> exception makes it acceptable); an unknown
    /// identifier is unresolved. <c>AND</c> is approved only when every operand is (rejected if
    /// any is rejected); <c>OR</c> is approved when any operand is (unresolved if none is
    /// approved but one is unresolved).
    /// </summary>
    /// <param name="policy">The policy.</param>
    public LicenseEvaluation Evaluate(LicensePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var (outcome, reason) = EvaluateNode(policy);
        return new LicenseEvaluation { Outcome = outcome, Reason = reason, UnknownIdentifiers = UnknownIdentifiers };
    }

    /// <summary>
    /// Returns the canonical text: upper-case operators, canonical identifiers, single spaces,
    /// and parentheses only where precedence needs them.
    /// </summary>
    public override string ToString()
    {
        switch (Kind)
        {
            case SpdxExpressionKind.License:
                var text = Identifier + (IsOrLater ? "+" : string.Empty);
                return Exception == null ? text : $"{text} WITH {Exception.Id}";
            case SpdxExpressionKind.And:
                return string.Join(" AND ", Operands.Select(o => o.Kind == SpdxExpressionKind.Or ? $"({o})" : o.ToString()));
            default:
                return string.Join(" OR ", Operands.Select(o => o.ToString()));
        }
    }

    private IEnumerable<SpdxLicenseExpression> LicenseNodes()
        => Kind == SpdxExpressionKind.License ? new[] { this } : Operands.SelectMany(o => o.LicenseNodes());

    private (LicenseEvaluationOutcome Outcome, string Reason) EvaluateNode(LicensePolicy policy)
    {
        if (Kind == SpdxExpressionKind.License)
        {
            var display = ToString();
            if (!License.HasValue)
            {
                return (LicenseEvaluationOutcome.Unresolved, $"'{Identifier}' is not a recognised SPDX license identifier");
            }

            if (policy.IsAllowed(License.Value))
            {
                return (LicenseEvaluationOutcome.Approved, $"{display} is allowed");
            }

            if (policy.IsExceptionAllowed(Exception))
            {
                return (LicenseEvaluationOutcome.Approved, $"{display} is allowed because the exception {Exception.Id} is allowed");
            }

            return (LicenseEvaluationOutcome.Rejected, $"{display} is not allowed by the policy");
        }

        var results = Operands.Select(o => (Node: o, Result: o.EvaluateNode(policy))).ToList();
        if (Kind == SpdxExpressionKind.And)
        {
            var rejected = results.Where(r => r.Result.Outcome == LicenseEvaluationOutcome.Rejected).ToList();
            if (rejected.Count > 0)
            {
                return (LicenseEvaluationOutcome.Rejected, string.Join("; ", rejected.Select(r => r.Result.Reason)));
            }

            var unresolved = results.Where(r => r.Result.Outcome == LicenseEvaluationOutcome.Unresolved).ToList();
            if (unresolved.Count > 0)
            {
                return (LicenseEvaluationOutcome.Unresolved, string.Join("; ", unresolved.Select(r => r.Result.Reason)));
            }

            return (LicenseEvaluationOutcome.Approved, $"every license of {this} is allowed");
        }

        var approved = results.FirstOrDefault(r => r.Result.Outcome == LicenseEvaluationOutcome.Approved);
        if (approved.Node != null)
        {
            return (LicenseEvaluationOutcome.Approved, $"{approved.Result.Reason} (chosen from {this})");
        }

        var undecided = results.Where(r => r.Result.Outcome == LicenseEvaluationOutcome.Unresolved).ToList();
        if (undecided.Count > 0)
        {
            return (LicenseEvaluationOutcome.Unresolved, "no choice is allowed and " + string.Join("; ", undecided.Select(r => r.Result.Reason)));
        }

        return (LicenseEvaluationOutcome.Rejected, $"none of the choices in {this} is allowed by the policy");
    }

    private static SpdxLicenseExpression CreateLicense(string token, SpdxLicenseException exception)
    {
        if (OpenSourceLicenseInfo.TryParse(token, out var direct))
        {
            return new SpdxLicenseExpression
            {
                Kind = SpdxExpressionKind.License,
                Identifier = OpenSourceLicenseInfo.ToSpdxId(direct),
                License = direct,
                Exception = exception,
            };
        }

        var plus = token.EndsWith('+');
        var baseId = plus ? token.Substring(0, token.Length - 1) : token;
        if (OpenSourceLicenseInfo.TryParse(baseId, out var license))
        {
            var orLater = plus;
            var id = OpenSourceLicenseInfo.ToSpdxId(license);
            if (plus && id.EndsWith("-only", StringComparison.Ordinal)
                && OpenSourceLicenseInfo.TryParse(id.Substring(0, id.Length - "-only".Length) + "-or-later", out var later))
            {
                license = later;
                orLater = false;
            }

            return new SpdxLicenseExpression
            {
                Kind = SpdxExpressionKind.License,
                Identifier = OpenSourceLicenseInfo.ToSpdxId(license),
                License = license,
                IsOrLater = orLater,
                Exception = exception,
            };
        }

        return new SpdxLicenseExpression
        {
            Kind = SpdxExpressionKind.License,
            Identifier = baseId,
            IsOrLater = plus,
            Exception = exception,
        };
    }

    private static SpdxLicenseExpression Combine(SpdxExpressionKind kind, List<SpdxLicenseExpression> parts)
    {
        if (parts.Count == 1)
        {
            return parts[0];
        }

        var operands = new List<SpdxLicenseExpression>();
        foreach (var part in parts)
        {
            if (part.Kind == kind)
            {
                operands.AddRange(part.Operands);
            }
            else
            {
                operands.Add(part);
            }
        }

        return new SpdxLicenseExpression { Kind = kind, Operands = operands };
    }

    private static bool Tokenize(string text, out List<string> tokens, out string error)
    {
        tokens = new List<string>();
        error = null;
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (char.IsWhiteSpace(c))
            {
                i++;
                continue;
            }

            if (c == '(' || c == ')' || c == '/')
            {
                tokens.Add(c.ToString());
                i++;
                continue;
            }

            if (!IsIdentifierChar(c))
            {
                error = $"Unexpected character '{c}' in the license expression '{text.Trim()}'.";
                return false;
            }

            var start = i;
            while (i < text.Length && IsIdentifierChar(text[i]))
            {
                i++;
            }

            tokens.Add(text.Substring(start, i - start));
        }

        return true;
    }

    private static bool IsIdentifierChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-' || c == '+' || c == ':' || c == '_';

    private static bool IsOperator(string token, string name) => string.Equals(token, name, StringComparison.OrdinalIgnoreCase);

    private sealed class Parser
    {
        private readonly List<string> _tokens;
        private int _position;

        public Parser(List<string> tokens)
        {
            _tokens = tokens;
        }

        public bool AtEnd => _position >= _tokens.Count;

        public string Current => AtEnd ? "end of text" : _tokens[_position];

        public SpdxLicenseExpression ParseOr(out string error)
        {
            var parts = new List<SpdxLicenseExpression>();
            var first = ParseAnd(out error);
            if (first == null)
            {
                return null;
            }

            parts.Add(first);
            while (!AtEnd && (IsOperator(Current, "OR") || Current == "/"))
            {
                _position++;
                var next = ParseAnd(out error);
                if (next == null)
                {
                    return null;
                }

                parts.Add(next);
            }

            return Combine(SpdxExpressionKind.Or, parts);
        }

        private SpdxLicenseExpression ParseAnd(out string error)
        {
            var parts = new List<SpdxLicenseExpression>();
            var first = ParseWith(out error);
            if (first == null)
            {
                return null;
            }

            parts.Add(first);
            while (!AtEnd && IsOperator(Current, "AND"))
            {
                _position++;
                var next = ParseWith(out error);
                if (next == null)
                {
                    return null;
                }

                parts.Add(next);
            }

            return Combine(SpdxExpressionKind.And, parts);
        }

        private SpdxLicenseExpression ParseWith(out string error)
        {
            error = null;
            if (AtEnd)
            {
                error = "The license expression ends where a license identifier was expected.";
                return null;
            }

            var token = Current;
            if (token == "(")
            {
                _position++;
                var inner = ParseOr(out error);
                if (inner == null)
                {
                    return null;
                }

                if (AtEnd || Current != ")")
                {
                    error = "A '(' in the license expression is not closed.";
                    return null;
                }

                _position++;
                return inner;
            }

            if (token == ")" || token == "/" || IsOperator(token, "AND") || IsOperator(token, "OR") || IsOperator(token, "WITH"))
            {
                error = $"Expected a license identifier but found '{token}'.";
                return null;
            }

            _position++;
            SpdxLicenseException exception = null;
            if (!AtEnd && IsOperator(Current, "WITH"))
            {
                _position++;
                if (AtEnd || Current == "(" || Current == ")" || Current == "/" || IsOperator(Current, "AND") || IsOperator(Current, "OR") || IsOperator(Current, "WITH"))
                {
                    error = "'WITH' must be followed by a license exception identifier.";
                    return null;
                }

                exception = SpdxLicenseException.Parse(Current);
                _position++;
            }

            return CreateLicense(token, exception);
        }
    }
}
