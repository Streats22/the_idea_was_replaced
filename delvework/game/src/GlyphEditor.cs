using Delvework.Core.Glyph;
using Delvework.Core.Sim;
using Godot;

namespace Delvework.Game;

/// <summary>
/// A Glyph code editor: syntax colours, autocomplete for what the player has learned, the
/// replay's current line in the gutter, and error lines tinted red.
/// </summary>
public partial class GlyphEditor : CodeEdit
{
    /// <summary>Keywords and the Codex tier that teaches them.</summary>
    private static readonly (string Word, int Tier)[] Keywords =
    [
        ("while", Tiers.Loops), ("break", Tiers.Loops), ("continue", Tiers.Loops),
        ("if", Tiers.Conditions), ("elif", Tiers.Conditions), ("else", Tiers.Conditions),
        ("and", Tiers.Conditions), ("or", Tiers.Conditions), ("not", Tiers.Conditions),
        ("def", Tiers.Functions), ("return", Tiers.Functions), ("global", Tiers.Functions), ("pass", Tiers.Functions),
        ("for", Tiers.Lists), ("in", Tiers.Lists),
        ("on", Tiers.Events),
        ("import", Tiers.Modules),
    ];
    private static readonly string[] Constants = ["True", "False", "None"];

    private int _errorLine = -1;
    private int _executingLine = -1;
    private GlyphEnvironment _environment = GolemApi.Environment;

    /// <summary>The functions and enums the code runs against: golems by default, or a workshop.</summary>
    public GlyphEnvironment Environment
    {
        get => _environment;
        set
        {
            _environment = value;
            SyntaxHighlighter = BuildHighlighter(value);
        }
    }

    /// <summary>Autocomplete only offers what this tier knows.</summary>
    public int Tier { get; set; } = Tiers.Max;

    /// <summary>Names that are hidden from autocomplete (spells not learned yet).</summary>
    public IReadOnlySet<string> HiddenNames { get; set; } = new HashSet<string>();

    /// <summary>A one-line explanation of a word, shown as a hover tooltip; null for words it doesn't know.</summary>
    public Func<string, string?>? Describe { get; set; }

    /// <summary>Ctrl+click on a word <see cref="Describe"/> knows.</summary>
    public event Action<string>? WordClicked;

    /// <summary>The Codex tier that teaches a keyword, or null if it isn't one.</summary>
    public static int? KeywordTier(string word)
    {
        foreach (var (k, t) in Keywords)
        {
            if (k == word) return t;
        }
        return Constants.Contains(word) ? Tiers.Conditions : null;
    }
    public GlyphEditor()
    {
        SizeFlagsVertical = SizeFlags.ExpandFill;
        SizeFlagsHorizontal = SizeFlags.ExpandFill;
        GuttersDrawLineNumbers = true;
        GuttersDrawExecutingLines = true;
        GuttersZeroPadLineNumbers = false;
        IndentUseSpaces = true;
        IndentSize = 4;
        IndentAutomatic = true;
        AutoBraceCompletionEnabled = true;
        AutoBraceCompletionHighlightMatching = true;
        CodeCompletionEnabled = true;
        HighlightCurrentLine = true;
        HighlightAllOccurrences = true;
        ScrollPastEndOfFile = true;
        CaretBlink = true;
        DelimiterComments = ["#"];
        DelimiterStrings = ["\" \"", "' '"];
        SyntaxHighlighter = BuildHighlighter();
        TextChanged += OnTextChanged;
        SymbolLookupOnClick = true;
        SymbolValidate += word => SetSymbolLookupWordAsValid(Describe?.Invoke(word) is not null);
        SymbolLookup += (word, _, _) => WordClicked?.Invoke(word);
    }

    public override string _GetTooltip(Vector2 atPosition)
    {
        var word = GetWordAtPos(atPosition);
        return word.Length == 0 ? "" : Describe?.Invoke(word) ?? "";
    }

    /// <summary>The name the caret is on or right after, outside comments.</summary>
    public string WordAtCaret()
    {
        var line = GetLine(GetCaretLine());
        var col = Math.Min(GetCaretColumn(), line.Length);
        if (IsInComment(line, col)) return "";
        static bool IsWord(char ch) => char.IsLetterOrDigit(ch) || ch == '_';
        var (s, e) = (col, col);
        while (s > 0 && IsWord(line[s - 1])) s--;
        while (e < line.Length && IsWord(line[e])) e++;
        return line[s..e];
    }

    public static CodeHighlighter BuildHighlighter(GlyphEnvironment? env = null)
    {
        env ??= GolemApi.Environment;
        var h = new CodeHighlighter
        {
            NumberColor = Color.FromHtml("#f78c6c"),
            SymbolColor = Color.FromHtml("#89ddff"),
            FunctionColor = Color.FromHtml("#82aaff"),
            MemberVariableColor = Color.FromHtml("#c3a6ff"),
        };
        foreach (var (k, _) in Keywords) h.AddKeywordColor(k, Color.FromHtml("#c792ea"));
        foreach (var k in Constants) h.AddKeywordColor(k, Color.FromHtml("#f78c6c"));
        foreach (var b in env.Builtins) h.AddKeywordColor(b.Name, Palette.Accent);
        foreach (var (name, _, _) in env.Entries.Where(e => e.Value.Kind is ValueKind.Enum or ValueKind.EnumType))
        {
            h.AddKeywordColor(name, Color.FromHtml("#ffcb6b"));
        }
        h.AddColorRegion("#", "", Color.FromHtml("#7f7466"), lineOnly: true);
        h.AddColorRegion("\"", "\"", Color.FromHtml("#c3e88d"));
        h.AddColorRegion("'", "'", Color.FromHtml("#c3e88d"));
        return h;
    }

    /// <summary>A read-only, highlighted, auto-sized code sample for explanation pages.</summary>
    public static CodeEdit Snippet(string code, GlyphEnvironment? env = null)
    {
        var lines = code.Split('\n').Length;
        var e = new CodeEdit
        {
            Text = code,
            Editable = false,
            SyntaxHighlighter = BuildHighlighter(env),
            GuttersDrawLineNumbers = lines > 2,
            ScrollFitContentHeight = true,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            ContextMenuEnabled = false,
            HighlightCurrentLine = false,
            CaretBlink = false,
            MouseDefaultCursorShape = CursorShape.Arrow,
            FocusMode = FocusModeEnum.Click,
        };
        e.AddThemeFontSizeOverride("font_size", 14);
        return e;
    }

    /// <summary>Line numbers are 1-based like Glyph errors; 0 clears.</summary>
    public void SetMarkers(int executingLine, int errorLine)
    {
        if (_executingLine >= 0 && _executingLine < GetLineCount()) SetLineAsExecuting(_executingLine, false);
        if (_errorLine >= 0 && _errorLine < GetLineCount()) SetLineBackgroundColor(_errorLine, new Color(0, 0, 0, 0));
        _executingLine = executingLine - 1;
        _errorLine = errorLine - 1;
        if (_executingLine >= 0 && _executingLine < GetLineCount())
        {
            SetLineAsExecuting(_executingLine, true);
            SetLineBackgroundColor(_executingLine, new Color(Palette.Accent, 0.12f));
        }
        if (_errorLine >= 0 && _errorLine < GetLineCount()) SetLineBackgroundColor(_errorLine, new Color(Palette.Danger, 0.22f));
    }

    public void ReplaceText(string text)
    {
        _executingLine = _errorLine = -1;
        Text = text;
        ClearUndoHistory();
    }

    private void OnTextChanged()
    {
        var line = GetLine(GetCaretLine());
        var col = GetCaretColumn();
        if (col > 0 && col <= line.Length && (char.IsLetter(line[col - 1]) || line[col - 1] == '_') && !IsInComment(line, col))
        {
            RequestCodeCompletion();
        }
    }

    private static bool IsInComment(string line, int col)
    {
        var hash = line.IndexOf('#', StringComparison.Ordinal);
        return hash >= 0 && hash < col;
    }

    public override void _RequestCodeCompletion(bool force)
    {
        foreach (var b in _environment.Builtins)
        {
            if (b.Tier > Tier || HiddenNames.Contains(b.Name) || string.IsNullOrEmpty(b.Signature)) continue;
            AddCodeCompletionOption(CodeCompletionKind.Function, b.Name, b.MaxArgs == 0 ? b.Name + "()" : b.Name + "(", Palette.Accent);
        }
        foreach (var (name, value, tier) in _environment.Entries.Where(e => e.Value.Kind is ValueKind.Enum or ValueKind.EnumType))
        {
            if (tier > Tier) continue;
            AddCodeCompletionOption(value.Kind == ValueKind.EnumType ? CodeCompletionKind.Class : CodeCompletionKind.Constant, name, name);
        }
        foreach (var (k, tier) in Keywords)
        {
            if (tier <= Tier) AddCodeCompletionOption(CodeCompletionKind.PlainText, k, k);
        }
        if (Tier >= Tiers.Conditions)
        {
            foreach (var k in Constants) AddCodeCompletionOption(CodeCompletionKind.PlainText, k, k);
        }
        if (Tier >= Tiers.Variables)
        {
            foreach (var name in AssignedNames()) AddCodeCompletionOption(CodeCompletionKind.Variable, name, name);
        }
        UpdateCodeCompletionOptions(force);
    }

    private IEnumerable<string> AssignedNames()
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < GetLineCount(); i++)
        {
            var line = GetLine(i).Trim();
            var eq = line.IndexOf('=', StringComparison.Ordinal);
            if (eq > 0 && eq + 1 < line.Length && line[eq + 1] != '=' && "<>!".IndexOf(line[eq - 1]) < 0)
            {
                var lhs = line[..eq].TrimEnd('+', '-', '*', '/', '%', ' ');
                if (lhs.Length > 0 && lhs.All(ch => char.IsLetterOrDigit(ch) || ch == '_')) names.Add(lhs);
            }
            if (line.StartsWith("def ", StringComparison.Ordinal))
            {
                var paren = line.IndexOf('(', StringComparison.Ordinal);
                if (paren > 4) names.Add(line[4..paren].Trim());
            }
        }
        return names;
    }
}
