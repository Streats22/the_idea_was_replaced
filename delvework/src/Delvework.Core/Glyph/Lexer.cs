namespace Delvework.Core.Glyph;

public enum TokenType
{
    Int,
    Float,
    String,
    Name,
    Keyword,
    Op,
    Newline,
    Indent,
    Dedent,
    Eof,
}

public readonly record struct Token(TokenType Type, string Text, int Line, int Column)
{
    public string Describe() => Type switch
    {
        TokenType.Newline => "end of line",
        TokenType.Eof => "end of file",
        TokenType.Indent => "indentation",
        TokenType.Dedent => "end of block",
        _ => $"'{Text}'",
    };
}

/// <summary>Indentation-aware tokenizer, ported from the TypeScript prototype.</summary>
public static class Lexer
{
    public const int MaxIndentDepth = 40;
    public const int MaxSourceLength = 200_000;

    public static readonly HashSet<string> Keywords =
    [
        "if", "elif", "else", "while", "for", "in", "def", "return", "break", "continue", "pass",
        "and", "or", "not", "True", "False", "None", "global", "on", "import",
    ];

    // Longest first, so "**=" wins over "**" and "*".
    private static readonly string[] Operators =
    [
        "**=", "//=", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=", "**", "//",
        "+", "-", "*", "/", "%", "<", ">", "=", "(", ")", "[", "]", "{", "}", ",", ":", ".",
    ];

    public static List<Token> Tokenize(string source)
    {
        if (source.Length > MaxSourceLength)
        {
            throw new GlyphError(GlyphErrorKind.Limit, $"Program is too long (over {MaxSourceLength} characters)", 1);
        }

        var tokens = new List<Token>();
        var indents = new Stack<int>();
        indents.Push(0);
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        var depth = 0;

        for (var li = 0; li < lines.Length; li++)
        {
            var text = lines[li];
            var lineNo = li + 1;
            var i = 0;

            if (depth == 0)
            {
                var width = 0;
                while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                {
                    width += text[i] == '\t' ? 4 : 1;
                    i++;
                }
                if (i >= text.Length || text[i] == '#') continue;
                var top = indents.Peek();
                if (width > top)
                {
                    if (indents.Count >= MaxIndentDepth)
                    {
                        throw new GlyphError(GlyphErrorKind.Limit, "Blocks are nested too deeply", lineNo);
                    }
                    indents.Push(width);
                    tokens.Add(new Token(TokenType.Indent, "", lineNo, 0));
                }
                else if (width < top)
                {
                    while (indents.Peek() > width)
                    {
                        indents.Pop();
                        tokens.Add(new Token(TokenType.Dedent, "", lineNo, 0));
                    }
                    if (indents.Peek() != width)
                    {
                        throw new GlyphError(GlyphErrorKind.Syntax, "Indentation does not match any outer block", lineNo);
                    }
                }
            }

            while (i < text.Length)
            {
                var c = text[i];
                if (c == ' ' || c == '\t')
                {
                    i++;
                    continue;
                }
                if (c == '#') break;

                if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
                {
                    var start = i;
                    while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                    var isFloat = false;
                    if (i < text.Length && text[i] == '.' && !(i + 1 < text.Length && char.IsAsciiLetter(text[i + 1])))
                    {
                        isFloat = true;
                        i++;
                        while (i < text.Length && char.IsAsciiDigit(text[i])) i++;
                    }
                    if (i < text.Length && (char.IsAsciiLetter(text[i]) || text[i] == '_'))
                    {
                        throw new GlyphError(GlyphErrorKind.Syntax, $"Invalid number '{text[start..(i + 1)]}'", lineNo, start + 1);
                    }
                    tokens.Add(new Token(isFloat ? TokenType.Float : TokenType.Int, text[start..i], lineNo, start + 1));
                    continue;
                }

                if (char.IsAsciiLetter(c) || c == '_')
                {
                    var start = i;
                    while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_')) i++;
                    var word = text[start..i];
                    tokens.Add(new Token(Keywords.Contains(word) ? TokenType.Keyword : TokenType.Name, word, lineNo, start + 1));
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    var start = i;
                    var j = i + 1;
                    var sb = new System.Text.StringBuilder();
                    while (j < text.Length && text[j] != c)
                    {
                        if (text[j] == '\\' && j + 1 < text.Length)
                        {
                            var n = text[j + 1];
                            sb.Append(n switch { 'n' => '\n', 't' => '\t', _ => n });
                            j += 2;
                        }
                        else
                        {
                            sb.Append(text[j++]);
                        }
                    }
                    if (j >= text.Length) throw new GlyphError(GlyphErrorKind.Syntax, "Unterminated string", lineNo, start + 1);
                    tokens.Add(new Token(TokenType.String, sb.ToString(), lineNo, start + 1));
                    i = j + 1;
                    continue;
                }

                string? op = null;
                foreach (var candidate in Operators)
                {
                    if (string.CompareOrdinal(text, i, candidate, 0, candidate.Length) == 0)
                    {
                        op = candidate;
                        break;
                    }
                }
                if (op is null)
                {
                    var shown = char.IsControl(c) ? $"\\u{(int)c:X4}" : c.ToString();
                    throw new GlyphError(GlyphErrorKind.Syntax, $"Unexpected character '{shown}'", lineNo, i + 1);
                }
                if (op is "(" or "[" or "{") depth++;
                if (op is ")" or "]" or "}") depth = Math.Max(0, depth - 1);
                tokens.Add(new Token(TokenType.Op, op, lineNo, i + 1));
                i += op.Length;
            }

            if (depth == 0 && tokens.Count > 0)
            {
                var last = tokens[^1].Type;
                if (last is not (TokenType.Newline or TokenType.Indent or TokenType.Dedent))
                {
                    tokens.Add(new Token(TokenType.Newline, "", lineNo, text.Length + 1));
                }
            }
        }

        var endLine = lines.Length;
        if (tokens.Count > 0 && tokens[^1].Type != TokenType.Newline && tokens[^1].Type != TokenType.Dedent)
        {
            tokens.Add(new Token(TokenType.Newline, "", endLine, 0));
        }
        while (indents.Count > 1)
        {
            indents.Pop();
            tokens.Add(new Token(TokenType.Dedent, "", endLine, 0));
        }
        tokens.Add(new Token(TokenType.Eof, "", endLine, 0));
        return tokens;
    }
}
