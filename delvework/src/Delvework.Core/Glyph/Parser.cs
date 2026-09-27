using System.Globalization;

namespace Delvework.Core.Glyph;

/// <summary>Recursive-descent parser producing the Glyph AST. Ported from the TypeScript prototype.</summary>
public sealed class Parser
{
    public const int MaxNesting = 100;
    public const int MaxChain = 200;

    private static readonly HashSet<string> AugOps = ["+=", "-=", "*=", "/=", "%=", "//=", "**="];
    private static readonly HashSet<string> CompareOps = ["==", "!=", "<", ">", "<=", ">="];

    private readonly List<Token> _tokens;
    private int _pos;
    private int _nesting;

    private Parser(List<Token> tokens) => _tokens = tokens;

    public static List<Stmt> Parse(string source) => new Parser(Lexer.Tokenize(source)).Program();

    private Token Peek(int offset = 0) => _tokens[Math.Min(_pos + offset, _tokens.Count - 1)];

    private Token Next()
    {
        var t = Peek();
        if (_pos < _tokens.Count - 1) _pos++;
        return t;
    }

    private bool Is(TokenType type, string? text = null)
    {
        var t = Peek();
        return t.Type == type && (text is null || t.Text == text);
    }

    private Token? Accept(TokenType type, string? text = null) => Is(type, text) ? Next() : null;

    private Token Expect(TokenType type, string? text = null)
    {
        if (Is(type, text)) return Next();
        var t = Peek();
        var want = text is not null ? $"'{text}'" : type switch
        {
            TokenType.Name => "a name",
            TokenType.Indent => "an indented block",
            TokenType.String => "a string",
            _ => type.ToString().ToLowerInvariant(),
        };
        throw new GlyphError(GlyphErrorKind.Syntax, $"Expected {want} but found {t.Describe()}", t.Line, t.Column);
    }

    private void Enter(int line)
    {
        if (++_nesting > MaxNesting) throw new GlyphError(GlyphErrorKind.Limit, "Code is nested too deeply", line);
    }

    private void Leave() => _nesting--;

    /// <summary>Left-associative chains build deep trees without recursing here, so cap them too.</summary>
    private static void Chain(ref int count, int line)
    {
        if (++count > MaxChain) throw new GlyphError(GlyphErrorKind.Limit, "Expression is too long; split it up with variables", line);
    }

    private List<Stmt> Program()
    {
        var body = new List<Stmt>();
        while (!Is(TokenType.Eof))
        {
            if (Accept(TokenType.Newline) is not null) continue;
            body.AddRange(Statement());
        }
        return body;
    }

    private List<Stmt> Block()
    {
        var colon = Expect(TokenType.Op, ":");
        Enter(colon.Line);
        try
        {
            if (Accept(TokenType.Newline) is null) return SimpleLine();
            Expect(TokenType.Indent);
            var body = new List<Stmt>();
            while (Accept(TokenType.Dedent) is null)
            {
                if (Is(TokenType.Eof)) break;
                body.AddRange(Statement());
            }
            return body;
        }
        finally
        {
            Leave();
        }
    }

    private List<Stmt> Statement()
    {
        var t = Peek();
        if (t.Type == TokenType.Keyword)
        {
            switch (t.Text)
            {
                case "if":
                    return [IfStatement()];
                case "while":
                {
                    Next();
                    var test = Expression();
                    return [new WhileStmt(test, Block(), t.Line)];
                }
                case "for":
                {
                    Next();
                    var name = Expect(TokenType.Name).Text;
                    Expect(TokenType.Keyword, "in");
                    var iter = Expression();
                    return [new ForStmt(name, iter, Block(), t.Line)];
                }
                case "def":
                {
                    Next();
                    var name = Expect(TokenType.Name).Text;
                    var ps = Params();
                    return [new DefStmt(name, ps, Block(), t.Line)];
                }
                case "on":
                {
                    Next();
                    var evt = Expect(TokenType.Name).Text;
                    var key = Accept(TokenType.String)?.Text;
                    var ps = Is(TokenType.Op, "(") ? Params() : [];
                    return [new OnStmt(evt, key, ps, Block(), t.Line)];
                }
            }
        }
        if (t.Type == TokenType.Indent) throw new GlyphError(GlyphErrorKind.Syntax, "Unexpected indentation", t.Line);
        return SimpleLine();
    }

    private List<string> Params()
    {
        Expect(TokenType.Op, "(");
        var ps = new List<string>();
        while (!Is(TokenType.Op, ")"))
        {
            var p = Expect(TokenType.Name);
            if (ps.Contains(p.Text)) throw new GlyphError(GlyphErrorKind.Syntax, $"Duplicate parameter '{p.Text}'", p.Line, p.Column);
            ps.Add(p.Text);
            if (Accept(TokenType.Op, ",") is null) break;
        }
        Expect(TokenType.Op, ")");
        return ps;
    }

    private IfStmt IfStatement()
    {
        var t = Next();
        var test = Expression();
        var body = Block();
        IReadOnlyList<Stmt> orelse = [];
        if (Is(TokenType.Keyword, "elif"))
        {
            Enter(Peek().Line);
            try
            {
                orelse = [IfStatement()];
            }
            finally
            {
                Leave();
            }
        }
        else if (Accept(TokenType.Keyword, "else") is not null)
        {
            orelse = Block();
        }
        return new IfStmt(test, body, orelse, t.Line);
    }

    private List<Stmt> SimpleLine()
    {
        var stmt = SimpleStatement();
        if (!Is(TokenType.Eof)) Expect(TokenType.Newline);
        return [stmt];
    }

    private Stmt SimpleStatement()
    {
        var t = Peek();
        if (t.Type == TokenType.Keyword)
        {
            switch (t.Text)
            {
                case "pass": Next(); return new PassStmt(t.Line);
                case "break": Next(); return new BreakStmt(t.Line);
                case "continue": Next(); return new ContinueStmt(t.Line);
                case "return":
                {
                    Next();
                    var value = Is(TokenType.Newline) || Is(TokenType.Eof) ? null : Expression();
                    return new ReturnStmt(value, t.Line);
                }
                case "global":
                {
                    Next();
                    var names = new List<string> { Expect(TokenType.Name).Text };
                    while (Accept(TokenType.Op, ",") is not null) names.Add(Expect(TokenType.Name).Text);
                    return new GlobalStmt(names, t.Line);
                }
                case "import":
                {
                    Next();
                    return new ImportStmt(Expect(TokenType.Name).Text, t.Line);
                }
            }
        }

        var expr = Expression();
        if (Accept(TokenType.Op, "=") is not null)
        {
            return new AssignStmt(ToTarget(expr), Expression(), t.Line);
        }
        var aug = Peek();
        if (aug.Type == TokenType.Op && AugOps.Contains(aug.Text))
        {
            Next();
            return new AugAssignStmt(ToTarget(expr), aug.Text[..^1], Expression(), t.Line);
        }
        return new ExprStmt(expr, t.Line);
    }

    private static Expr ToTarget(Expr e) => e is NameExpr or IndexExpr
        ? e
        : throw new GlyphError(GlyphErrorKind.Syntax, "Can only assign to a variable or a list/dict item", e.Line);

    private Expr Expression()
    {
        Enter(Peek().Line);
        try
        {
            return OrExpr();
        }
        finally
        {
            Leave();
        }
    }

    private Expr OrExpr()
    {
        var left = AndExpr();
        var count = 0;
        while (Is(TokenType.Keyword, "or"))
        {
            var line = Next().Line;
            Chain(ref count, line);
            left = new LogicExpr("or", left, AndExpr(), line);
        }
        return left;
    }

    private Expr AndExpr()
    {
        var left = NotExpr();
        var count = 0;
        while (Is(TokenType.Keyword, "and"))
        {
            var line = Next().Line;
            Chain(ref count, line);
            left = new LogicExpr("and", left, NotExpr(), line);
        }
        return left;
    }

    private Expr NotExpr()
    {
        if (!Is(TokenType.Keyword, "not")) return Comparison();
        var line = Next().Line;
        Enter(line);
        try
        {
            return new UnaryExpr("not", NotExpr(), line);
        }
        finally
        {
            Leave();
        }
    }

    private Expr Comparison()
    {
        var first = Arith();
        var ops = new List<string>();
        var operands = new List<Expr> { first };
        while (true)
        {
            var t = Peek();
            if (t.Type == TokenType.Op && CompareOps.Contains(t.Text))
            {
                Next();
                ops.Add(t.Text);
            }
            else if (Is(TokenType.Keyword, "in"))
            {
                Next();
                ops.Add("in");
            }
            else if (Is(TokenType.Keyword, "not") && Peek(1).Type == TokenType.Keyword && Peek(1).Text == "in")
            {
                Next();
                Next();
                ops.Add("not in");
            }
            else
            {
                break;
            }
            if (ops.Count > MaxChain) throw new GlyphError(GlyphErrorKind.Limit, "Comparison chain is too long", t.Line);
            operands.Add(Arith());
        }
        return ops.Count > 0 ? new CompareExpr(ops, operands, first.Line) : first;
    }

    private Expr Arith()
    {
        var left = Term();
        var count = 0;
        while (Is(TokenType.Op, "+") || Is(TokenType.Op, "-"))
        {
            var t = Next();
            Chain(ref count, t.Line);
            left = new BinaryExpr(t.Text, left, Term(), t.Line);
        }
        return left;
    }

    private Expr Term()
    {
        var left = Unary();
        var count = 0;
        while (Is(TokenType.Op, "*") || Is(TokenType.Op, "/") || Is(TokenType.Op, "//") || Is(TokenType.Op, "%"))
        {
            var t = Next();
            Chain(ref count, t.Line);
            left = new BinaryExpr(t.Text, left, Unary(), t.Line);
        }
        return left;
    }

    private Expr Unary()
    {
        if (!Is(TokenType.Op, "-") && !Is(TokenType.Op, "+")) return Power();
        var t = Next();
        Enter(t.Line);
        try
        {
            return new UnaryExpr(t.Text, Unary(), t.Line);
        }
        finally
        {
            Leave();
        }
    }

    private Expr Power()
    {
        var b = Postfix();
        if (!Is(TokenType.Op, "**")) return b;
        var t = Next();
        Enter(t.Line);
        try
        {
            return new BinaryExpr("**", b, Unary(), t.Line);
        }
        finally
        {
            Leave();
        }
    }

    private Expr Postfix()
    {
        var e = Atom();
        var count = 0;
        while (true)
        {
            if (Is(TokenType.Op, "(") || Is(TokenType.Op, "[") || Is(TokenType.Op, ".")) Chain(ref count, e.Line);
            if (Accept(TokenType.Op, "(") is not null)
            {
                var args = new List<Expr>();
                var kwargs = new List<KeywordArg>();
                while (!Is(TokenType.Op, ")"))
                {
                    if (Peek().Type == TokenType.Name && Peek(1).Type == TokenType.Op && Peek(1).Text == "=")
                    {
                        var name = Next();
                        Next();
                        if (kwargs.Any(k => k.Name == name.Text))
                        {
                            throw new GlyphError(GlyphErrorKind.Syntax, $"Keyword argument '{name.Text}' given twice", name.Line, name.Column);
                        }
                        kwargs.Add(new KeywordArg(name.Text, Expression()));
                    }
                    else
                    {
                        if (kwargs.Count > 0)
                        {
                            throw new GlyphError(GlyphErrorKind.Syntax, "Positional arguments must come before keyword arguments", Peek().Line);
                        }
                        args.Add(Expression());
                    }
                    if (Accept(TokenType.Op, ",") is null) break;
                }
                Expect(TokenType.Op, ")");
                e = new CallExpr(e, args, kwargs, e.Line);
            }
            else if (Accept(TokenType.Op, "[") is not null)
            {
                var index = Expression();
                Expect(TokenType.Op, "]");
                e = new IndexExpr(e, index, e.Line);
            }
            else if (Accept(TokenType.Op, ".") is not null)
            {
                e = new AttrExpr(e, Expect(TokenType.Name).Text, e.Line);
            }
            else
            {
                return e;
            }
        }
    }

    private Expr Atom()
    {
        var t = Next();
        switch (t.Type)
        {
            case TokenType.Int:
                if (!long.TryParse(t.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                {
                    throw new GlyphError(GlyphErrorKind.Limit, $"Number {t.Text} is too large", t.Line, t.Column);
                }
                return new IntLit(n, t.Line);
            case TokenType.Float:
                if (!Fix.TryParse(t.Text, out var f))
                {
                    throw new GlyphError(GlyphErrorKind.Limit, $"Number {t.Text} is too large", t.Line, t.Column);
                }
                return new FloatLit(f, t.Line);
            case TokenType.String:
                return new StrLit(t.Text, t.Line);
            case TokenType.Name:
                return new NameExpr(t.Text, t.Line);
            case TokenType.Keyword when t.Text == "True":
                return new BoolLit(true, t.Line);
            case TokenType.Keyword when t.Text == "False":
                return new BoolLit(false, t.Line);
            case TokenType.Keyword when t.Text == "None":
                return new NoneLit(t.Line);
            case TokenType.Op when t.Text == "(":
            {
                var e = Expression();
                Expect(TokenType.Op, ")");
                return e;
            }
            case TokenType.Op when t.Text == "[":
            {
                var items = new List<Expr>();
                while (!Is(TokenType.Op, "]"))
                {
                    items.Add(Expression());
                    if (Accept(TokenType.Op, ",") is null) break;
                }
                Expect(TokenType.Op, "]");
                return new ListExpr(items, t.Line);
            }
            case TokenType.Op when t.Text == "{":
            {
                var keys = new List<Expr>();
                var values = new List<Expr>();
                while (!Is(TokenType.Op, "}"))
                {
                    keys.Add(Expression());
                    Expect(TokenType.Op, ":");
                    values.Add(Expression());
                    if (Accept(TokenType.Op, ",") is null) break;
                }
                Expect(TokenType.Op, "}");
                return new DictExpr(keys, values, t.Line);
            }
        }
        throw new GlyphError(GlyphErrorKind.Syntax, $"Unexpected {t.Describe()}", t.Line, t.Column);
    }
}
