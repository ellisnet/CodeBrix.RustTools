using System;
using System.Text;

namespace CodeBrix.RustTools.Docker.Internal;

/// <summary>
/// Turns a stream of UTF-8 byte chunks into lines, coping with chunks that end in the middle of a
/// line or of a multi-byte character. Line terminators (<c>\n</c>, with an optional preceding
/// <c>\r</c>) are removed. Also keeps the whole decoded text.
/// </summary>
internal sealed class LineSplitter
{
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly StringBuilder _pending = new StringBuilder();
    private readonly StringBuilder _all = new StringBuilder();
    private readonly Action<string> _onLine;

    public LineSplitter(Action<string> onLine)
    {
        _onLine = onLine;
    }

    /// <summary>Everything decoded so far.</summary>
    public string Text => _all.ToString();

    public void Append(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return;
        }

        var chars = new char[_decoder.GetCharCount(bytes, flush: false)];
        var count = _decoder.GetChars(bytes, chars, flush: false);
        Emit(chars.AsSpan(0, count));
    }

    /// <summary>Decodes anything buffered and reports the last line when it had no
    /// terminator.</summary>
    public void Complete()
    {
        var chars = new char[_decoder.GetCharCount(ReadOnlySpan<byte>.Empty, flush: true)];
        var count = _decoder.GetChars(ReadOnlySpan<byte>.Empty, chars, flush: true);
        Emit(chars.AsSpan(0, count));
        if (_pending.Length > 0)
        {
            Report(_pending.ToString());
            _pending.Clear();
        }
    }

    private void Emit(ReadOnlySpan<char> chars)
    {
        _all.Append(chars);
        foreach (var c in chars)
        {
            if (c == '\n')
            {
                if (_pending.Length > 0 && _pending[_pending.Length - 1] == '\r')
                {
                    _pending.Length--;
                }

                Report(_pending.ToString());
                _pending.Clear();
            }
            else
            {
                _pending.Append(c);
            }
        }
    }

    private void Report(string line) => _onLine?.Invoke(line);
}
