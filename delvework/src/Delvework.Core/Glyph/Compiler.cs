namespace Delvework.Core.Glyph;

public sealed class CompileOptions
{
    /// <summary>Highest language tier the program may use (see the Glyph feature ladder).</summary>
    public int Tier { get; init; } = Tiers.Max;

    /// <summary>Resolves <c>import name</c> to module source, or null if there is no such module.</summary>
    public Func<string, string?>? ModuleResolver { get; init; }

    /// <summary>Environment names the player has not unlocked yet, with the message to show when they are used.</summary>
    public IReadOnlyDictionary<string, string>? Locked { get; init; }
}

/// <summary>
/// Compiles Glyph source to bytecode. Every check that can happen before a delve happens here:
/// syntax, unknown names, locked features, and misplaced statements.
/// </summary>
public static class GlyphCompiler
{
    public static GlyphProgram Compile(string source, GlyphEnvironment env, CompileOptions? options = null)
    {
        options ??= new CompileOptions();
        var stmts = Parser.Parse(source);
        new Checker(env, options.Tier, options.Locked).CheckModule(stmts);
        stmts = InlineImports(stmts, env, options, [], 0);
        return new ModuleCompiler(env, options.Tier).Compile(stmts, Fnv.Mix(Fnv.Offset, source));
    }

    private static List<Stmt> InlineImports(List<Stmt> stmts, GlyphEnvironment env, CompileOptions options, HashSet<string> active, int depth)
    {
        if (!stmts.Any(s => s is ImportStmt)) return stmts;
        var result = new List<Stmt>();
        foreach (var s in stmts)
        {
            if (s is not ImportStmt imp)
            {
                result.Add(s);
                continue;
            }
            if (active.Contains(imp.Module))
            {
                throw new GlyphError(GlyphErrorKind.Name, $"Circular import: '{imp.Module}' imports itself", imp.Line);
            }
            var src = options.ModuleResolver?.Invoke(imp.Module)
                ?? throw new GlyphError(GlyphErrorKind.Name, $"No module named '{imp.Module}' in the Archive", imp.Line);
            List<Stmt> module;
            try
            {
                module = Parser.Parse(src);
            }
            catch (GlyphError e)
            {
                throw new GlyphError(e.Kind, $"In module '{imp.Module}', line {e.Line}: {e.Message}", imp.Line);
            }
            foreach (var m in module)
            {
                if (m is not (DefStmt or AssignStmt or ImportStmt or PassStmt))
                {
                    throw new GlyphError(GlyphErrorKind.Syntax, $"Module '{imp.Module}' may only contain functions, constants and imports (line {m.Line})", imp.Line);
                }
            }
            new Checker(env, options.Tier, options.Locked).CheckModule(module);
            active.Add(imp.Module);
            result.AddRange(InlineImports(module, env, options, active, depth + 1));
            active.Remove(imp.Module);
        }
        return result;
    }

    internal static string? DidYouMean(string name, IEnumerable<string> candidates)
    {
        string? best = null;
        var bestDist = int.MaxValue;
        foreach (var c in candidates)
        {
            if (c == name) continue;
            var d = EditDistance(name, c);
            if (d < bestDist || (d == bestDist && string.CompareOrdinal(c, best) < 0))
            {
                bestDist = d;
                best = c;
            }
        }
        var allowed = name.Length <= 3 ? 1 : 2;
        return bestDist <= allowed ? best : null;
    }

    internal static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}

/// <summary>Structural rules and the capability gate, applied before any code is generated.</summary>
internal sealed class Checker(GlyphEnvironment env, int tier, IReadOnlyDictionary<string, string>? locked = null)
{
    private int _loopDepth;
    private bool _inFunction;
    private bool _inHandler;

    private void Require(int needed, string feature, int line)
    {
        if (needed <= tier) return;
        throw new GlyphError(GlyphErrorKind.Capability, Tiers.LockedMessage(feature, needed, tier), line);
    }

    public void CheckModule(IReadOnlyList<Stmt> stmts)
    {
        foreach (var s in stmts)
        {
            if (s is OnStmt on)
            {
                Require(Tiers.Events, "Event handlers ('on')", on.Line);
                if (env.Events.Count > 0 && !env.Events.Contains(on.Event))
                {
                    var hint = GlyphCompiler.DidYouMean(on.Event, env.Events);
                    var known = string.Join(", ", env.Events.Order(StringComparer.Ordinal));
                    throw new GlyphError(GlyphErrorKind.Name,
                        hint is null ? $"Unknown event '{on.Event}'. Events: {known}" : $"Unknown event '{on.Event}'. Did you mean '{hint}'?",
                        on.Line);
                }
                _inHandler = true;
                Block(on.Body);
                _inHandler = false;
            }
            else
            {
                Stmt(s);
            }
        }
    }

    private void Block(IReadOnlyList<Stmt> body)
    {
        foreach (var s in body) Stmt(s);
    }

    private void Stmt(Stmt s)
    {
        switch (s)
        {
            case ExprStmt e:
                Expr(e.Expr);
                break;
            case AssignStmt a:
                Require(Tiers.Variables, "Variables", a.Line);
                Target(a.Target);
                Expr(a.Value);
                break;
            case AugAssignStmt a:
                Require(Tiers.Variables, "Variables", a.Line);
                Target(a.Target);
                Expr(a.Value);
                break;
            case IfStmt i:
                Require(Tiers.Conditions, "'if'", i.Line);
                Expr(i.Test);
                Block(i.Body);
                Block(i.Else);
                break;
            case WhileStmt w:
                Require(Tiers.Loops, "'while' loops", w.Line);
                Expr(w.Test);
                _loopDepth++;
                Block(w.Body);
                _loopDepth--;
                break;
            case ForStmt f:
                Require(Tiers.Lists, "'for' loops", f.Line);
                Expr(f.Iter);
                _loopDepth++;
                Block(f.Body);
                _loopDepth--;
                break;
            case DefStmt d:
                Require(Tiers.Functions, "Functions ('def')", d.Line);
                if (_inFunction || _inHandler)
                {
                    throw new GlyphError(GlyphErrorKind.Syntax, "Functions must be defined at the top level, not inside another function or handler", d.Line);
                }
                var outerLoops = _loopDepth;
                _loopDepth = 0;
                _inFunction = true;
                Block(d.Body);
                _inFunction = false;
                _loopDepth = outerLoops;
                break;
            case ReturnStmt r:
                Require(Tiers.Functions, "'return'", r.Line);
                if (!_inFunction && !_inHandler) throw new GlyphError(GlyphErrorKind.Syntax, "'return' outside a function", r.Line);
                if (r.Value is not null) Expr(r.Value);
                break;
            case GlobalStmt g:
                Require(Tiers.Functions, "'global'", g.Line);
                break;
            case BreakStmt b:
                if (_loopDepth == 0) throw new GlyphError(GlyphErrorKind.Syntax, "'break' outside a loop", b.Line);
                break;
            case ContinueStmt c:
                if (_loopDepth == 0) throw new GlyphError(GlyphErrorKind.Syntax, "'continue' outside a loop", c.Line);
                break;
            case OnStmt o:
                throw new GlyphError(GlyphErrorKind.Syntax, "'on' handlers must be at the top level, not inside other code", o.Line);
            case ImportStmt imp:
                Require(Tiers.Modules, "'import'", imp.Line);
                if (_inFunction || _inHandler || _loopDepth > 0)
                {
                    throw new GlyphError(GlyphErrorKind.Syntax, "'import' must be at the top level", imp.Line);
                }
                break;
            case PassStmt:
                break;
        }
    }

    private void Target(Expr t)
    {
        if (t is IndexExpr ix)
        {
            Expr(ix.Target);
            Expr(ix.Index);
            Require(Tiers.Lists, "Indexing with [ ]", ix.Line);
        }
    }

    private void Expr(Expr e)
    {
        switch (e)
        {
            case NameExpr n:
                if (env.TryGetIndex(n.Name, out var idx))
                {
                    var entry = env.Entries[idx];
                    var what = entry.Value.Kind == ValueKind.Builtin ? $"{n.Name}()" : $"'{n.Name}'";
                    Require(entry.Tier, what, n.Line);
                    if (locked is not null && locked.TryGetValue(n.Name, out var why))
                    {
                        throw new GlyphError(GlyphErrorKind.Capability, why, n.Line);
                    }
                }
                break;
            case ListExpr l:
                Require(Tiers.Lists, "Lists", l.Line);
                foreach (var i in l.Items) Expr(i);
                break;
            case DictExpr d:
                Require(Tiers.Dicts, "Dicts", d.Line);
                foreach (var k in d.Keys) Expr(k);
                foreach (var v in d.Values) Expr(v);
                break;
            case UnaryExpr u:
                if (u.Op == "not") Require(Tiers.Conditions, "'not'", u.Line);
                else if (u.Operand is not (IntLit or FloatLit)) Require(Tiers.Variables, "Arithmetic", u.Line);
                Expr(u.Operand);
                break;
            case BinaryExpr b:
                Require(Tiers.Variables, "Arithmetic", b.Line);
                Expr(b.Left);
                Expr(b.Right);
                break;
            case CompareExpr c:
                Require(Tiers.Conditions, "Comparisons", c.Line);
                if (c.Ops.Any(o => o is "in" or "not in")) Require(Tiers.Dicts, "'in'", c.Line);
                foreach (var o in c.Operands) Expr(o);
                break;
            case LogicExpr l:
                Require(Tiers.Conditions, $"'{l.Op}'", l.Line);
                Expr(l.Left);
                Expr(l.Right);
                break;
            case CallExpr call:
                if (call.Callee is NameExpr { Name: "wait_until" }) Require(Tiers.Coroutines, "wait_until()", call.Line);
                Expr(call.Callee);
                foreach (var a in call.Args) Expr(a);
                foreach (var k in call.Kwargs) Expr(k.Value);
                break;
            case IndexExpr ix:
                Require(Tiers.Lists, "Indexing with [ ]", ix.Line);
                Expr(ix.Target);
                Expr(ix.Index);
                break;
            case AttrExpr a:
                Expr(a.Target);
                break;
        }
    }
}

internal sealed class ModuleCompiler(GlyphEnvironment env, int tier)
{
    private readonly List<string> _globalNames = [];
    private readonly Dictionary<string, int> _globalIndex = [];
    private readonly List<FunctionProto> _functions = [];
    private readonly List<HandlerDef> _handlers = [];

    public GlyphProgram Compile(List<Stmt> stmts, ulong sourceHash)
    {
        foreach (var e in env.Entries) AddGlobal(e.Name);
        CollectGlobals(stmts);

        var main = new FunctionCompiler(this, "<main>", [], [], FunctionKind.Module, 1);
        main.CompileBody(stmts.Where(s => s is not OnStmt).ToList());

        foreach (var on in stmts.OfType<OnStmt>())
        {
            var name = on.Key is null ? $"on {on.Event}" : $"on {on.Event} \"{on.Key}\"";
            var fc = new FunctionCompiler(this, name, on.Params, on.Params, FunctionKind.Handler, on.Line);
            fc.CompileBody(on.Body);
            _handlers.Add(new HandlerDef(on.Event, on.Key, fc.Build()));
        }

        return new GlyphProgram
        {
            Main = main.Build(),
            Functions = _functions,
            Handlers = _handlers,
            GlobalNames = [.. _globalNames],
            Environment = env,
            Tier = tier,
            SourceHash = sourceHash,
        };
    }

    internal IReadOnlyCollection<string> GlobalNames => _globalNames;

    internal bool TryGlobal(string name, out int slot) => _globalIndex.TryGetValue(name, out slot);

    private void AddGlobal(string name)
    {
        if (_globalIndex.ContainsKey(name)) return;
        _globalIndex[name] = _globalNames.Count;
        _globalNames.Add(name);
    }

    internal FunctionProto CompileDef(DefStmt d)
    {
        var declaredGlobal = new HashSet<string>();
        Walk(d.Body, s =>
        {
            if (s is GlobalStmt g) declaredGlobal.UnionWith(g.Names);
        });
        var locals = new List<string>(d.Params);
        foreach (var n in AssignedNames(d.Body))
        {
            if (!declaredGlobal.Contains(n) && !locals.Contains(n)) locals.Add(n);
        }
        var fc = new FunctionCompiler(this, d.Name, d.Params, locals, FunctionKind.Function, d.Line);
        fc.CompileBody(d.Body);
        var proto = fc.Build();
        _functions.Add(proto);
        return proto;
    }

    private void CollectGlobals(List<Stmt> stmts)
    {
        foreach (var n in AssignedNames(stmts.Where(s => s is not (OnStmt or DefStmt)).ToList())) AddGlobal(n);
        foreach (var s in stmts)
        {
            switch (s)
            {
                case OnStmt on:
                    foreach (var n in AssignedNames(on.Body))
                    {
                        if (!on.Params.Contains(n)) AddGlobal(n);
                    }
                    break;
                case DefStmt d:
                    AddGlobal(d.Name);
                    Walk(d.Body, x =>
                    {
                        if (x is GlobalStmt g) foreach (var n in g.Names) AddGlobal(n);
                    });
                    break;
            }
        }
        // Conditionally defined functions (def inside an if at module level) are globals too.
        Walk(stmts.Where(s => s is not (OnStmt or DefStmt)).ToList(), x =>
        {
            if (x is DefStmt d) AddGlobal(d.Name);
        });
    }

    /// <summary>Names bound by assignment or a for loop, in first-seen order, without entering nested defs.</summary>
    private static List<string> AssignedNames(IReadOnlyList<Stmt> body)
    {
        var names = new List<string>();
        Walk(body, s =>
        {
            switch (s)
            {
                case AssignStmt { Target: NameExpr n } when !names.Contains(n.Name):
                    names.Add(n.Name);
                    break;
                case AugAssignStmt { Target: NameExpr n } when !names.Contains(n.Name):
                    names.Add(n.Name);
                    break;
                case ForStmt f when !names.Contains(f.Name):
                    names.Add(f.Name);
                    break;
            }
        });
        return names;
    }

    private static void Walk(IReadOnlyList<Stmt> body, Action<Stmt> visit)
    {
        foreach (var s in body)
        {
            visit(s);
            switch (s)
            {
                case IfStmt i:
                    Walk(i.Body, visit);
                    Walk(i.Else, visit);
                    break;
                case WhileStmt w:
                    Walk(w.Body, visit);
                    break;
                case ForStmt f:
                    Walk(f.Body, visit);
                    break;
            }
        }
    }

    internal IEnumerable<string> VisibleNames() => _globalNames;

    internal GlyphEnvironment Env => env;
}

internal enum FunctionKind
{
    Module,
    Function,
    Handler,
}

internal sealed class FunctionCompiler
{
    private readonly ModuleCompiler _module;
    private readonly string _name;
    private readonly int _arity;
    private readonly List<string> _locals;
    private readonly FunctionKind _kind;
    private readonly int _firstLine;
    private readonly List<Instr> _code = [];
    private readonly List<int> _lines = [];
    private readonly List<Value> _constants = [];
    private readonly Dictionary<(ValueKind, long, string?), int> _constIndex = [];
    private readonly List<Loop> _loops = [];
    private readonly List<int> _statementStarts = [];
    private int _line;
    private int _depth;
    private int _maxDepth;

    private sealed class Loop(int continueTarget, bool isFor)
    {
        public int ContinueTarget { get; } = continueTarget;
        public bool IsFor { get; } = isFor;
        public List<int> Breaks { get; } = [];
    }

    public FunctionCompiler(ModuleCompiler module, string name, IReadOnlyList<string> parameters, IReadOnlyList<string> locals, FunctionKind kind, int firstLine)
    {
        _module = module;
        _name = name;
        _arity = parameters.Count;
        _locals = [.. locals];
        _kind = kind;
        _firstLine = firstLine;
        _line = firstLine;
    }

    public void CompileBody(IReadOnlyList<Stmt> body)
    {
        foreach (var s in body) Stmt(s);
        Emit(OpCode.PushNone);
        Emit(OpCode.Return);
    }

    public FunctionProto Build()
    {
        var starts = new bool[_code.Count + 1];
        foreach (var s in _statementStarts) starts[Math.Min(s, _code.Count)] = true;
        return new FunctionProto
        {
            Name = _name,
            Arity = _arity,
            LocalNames = [.. _locals],
            Code = [.. _code],
            Lines = [.. _lines],
            Constants = [.. _constants],
            MaxStack = _maxDepth + 4,
            FirstLine = _firstLine,
            StatementStarts = starts,
        };
    }

    private int Emit(OpCode op, int a = 0, int b = 0)
    {
        _code.Add(new Instr(op, a, b));
        _lines.Add(_line);
        _depth += StackEffect(op, a);
        if (_depth > _maxDepth) _maxDepth = _depth;
        return _code.Count - 1;
    }

    private static int StackEffect(OpCode op, int a) => op switch
    {
        OpCode.Const or OpCode.PushNone or OpCode.PushTrue or OpCode.PushFalse or OpCode.Dup
            or OpCode.LoadLocal or OpCode.LoadGlobal or OpCode.ForIter => 1,
        OpCode.Dup2 => 2,
        OpCode.Pop or OpCode.StoreLocal or OpCode.StoreGlobal or OpCode.JumpIfFalse
            or OpCode.JumpIfFalseKeep or OpCode.JumpIfTrueKeep or OpCode.Index or OpCode.Return => -1,
        OpCode.Add or OpCode.Sub or OpCode.Mul or OpCode.Div or OpCode.FloorDiv or OpCode.Mod or OpCode.Pow
            or OpCode.Eq or OpCode.Ne or OpCode.Lt or OpCode.Gt or OpCode.Le or OpCode.Ge
            or OpCode.In or OpCode.NotIn => -1,
        OpCode.BuildList => 1 - a,
        OpCode.BuildDict => 1 - 2 * a,
        OpCode.StoreIndex => -3,
        OpCode.Call or OpCode.CallKw => -a,
        _ => 0,
    };

    private int Here => _code.Count;

    private void Patch(int at, int target)
    {
        var i = _code[at];
        _code[at] = i with { A = target };
    }

    private int Constant(Value v)
    {
        (ValueKind, long, string?) key = v.Kind switch
        {
            ValueKind.Int => (v.Kind, v.AsInt, null),
            ValueKind.Float => (v.Kind, v.AsFix.Raw, null),
            ValueKind.Str => (v.Kind, 0, v.AsStr),
            _ => (ValueKind.Undefined, _constants.Count, null),
        };
        if (key.Item1 != ValueKind.Undefined && _constIndex.TryGetValue(key, out var existing)) return existing;
        _constants.Add(v);
        if (key.Item1 != ValueKind.Undefined) _constIndex[key] = _constants.Count - 1;
        return _constants.Count - 1;
    }

    private void Stmt(Stmt s)
    {
        _line = s.Line;
        _statementStarts.Add(Here);
        switch (s)
        {
            case ExprStmt e:
                Expr(e.Expr);
                Emit(OpCode.Pop);
                break;
            case AssignStmt a:
                if (a.Target is NameExpr n)
                {
                    Expr(a.Value);
                    Store(n.Name, a.Line);
                }
                else
                {
                    var ix = (IndexExpr)a.Target;
                    Expr(a.Value);
                    Expr(ix.Target);
                    Expr(ix.Index);
                    _line = a.Line;
                    Emit(OpCode.StoreIndex);
                }
                break;
            case AugAssignStmt a:
                if (a.Target is NameExpr name)
                {
                    Load(name.Name, a.Line);
                    Expr(a.Value);
                    _line = a.Line;
                    Emit(BinaryOp(a.Op));
                    Store(name.Name, a.Line);
                }
                else
                {
                    var ix = (IndexExpr)a.Target;
                    Expr(ix.Target);
                    Expr(ix.Index);
                    _line = a.Line;
                    Emit(OpCode.Dup2);
                    Emit(OpCode.Index);
                    Expr(a.Value);
                    _line = a.Line;
                    Emit(BinaryOp(a.Op));
                    Emit(OpCode.Rot3);
                    Emit(OpCode.StoreIndex);
                }
                break;
            case IfStmt i:
            {
                if (i.Test is BoolLit { Value: true })
                {
                    foreach (var b in i.Body) Stmt(b);
                    break;
                }
                Expr(i.Test);
                _line = i.Line;
                var toElse = Emit(OpCode.JumpIfFalse);
                foreach (var b in i.Body) Stmt(b);
                if (i.Else.Count > 0)
                {
                    var toEnd = Emit(OpCode.Jump);
                    Patch(toElse, Here);
                    foreach (var b in i.Else) Stmt(b);
                    Patch(toEnd, Here);
                }
                else
                {
                    Patch(toElse, Here);
                }
                break;
            }
            case WhileStmt w:
            {
                var start = Here;
                int? exit = null;
                if (w.Test is not BoolLit { Value: true })
                {
                    Expr(w.Test);
                    _line = w.Line;
                    exit = Emit(OpCode.JumpIfFalse);
                }
                var loop = new Loop(start, false);
                _loops.Add(loop);
                foreach (var b in w.Body) Stmt(b);
                _loops.RemoveAt(_loops.Count - 1);
                _line = w.Line;
                Emit(OpCode.Jump, start);
                if (exit is int e) Patch(e, Here);
                foreach (var br in loop.Breaks) Patch(br, Here);
                break;
            }
            case ForStmt f:
            {
                Expr(f.Iter);
                _line = f.Line;
                Emit(OpCode.GetIter);
                var start = Here;
                _statementStarts.Add(start);
                var forIter = Emit(OpCode.ForIter);
                Store(f.Name, f.Line);
                var loop = new Loop(start, true);
                _loops.Add(loop);
                foreach (var b in f.Body) Stmt(b);
                _loops.RemoveAt(_loops.Count - 1);
                _line = f.Line;
                Emit(OpCode.Jump, start);
                // The iterator is still on the stack here. Breaks land on this Pop; exhaustion
                // (ForIter) has already popped it and jumps past.
                var breakTarget = Here;
                if (loop.Breaks.Count > 0)
                {
                    Emit(OpCode.Pop);
                    foreach (var br in loop.Breaks) Patch(br, breakTarget);
                }
                else
                {
                    _depth--;
                }
                Patch(forIter, Here);
                break;
            }
            case DefStmt d:
            {
                var proto = _module.CompileDef(d);
                _line = d.Line;
                Emit(OpCode.Const, Constant(Value.Function(new FunctionObj(proto))));
                Store(d.Name, d.Line);
                break;
            }
            case ReturnStmt r:
                if (r.Value is null) Emit(OpCode.PushNone);
                else Expr(r.Value);
                _line = r.Line;
                Emit(OpCode.Return);
                break;
            case BreakStmt:
            {
                var loop = _loops[^1];
                loop.Breaks.Add(Emit(OpCode.Jump));
                break;
            }
            case ContinueStmt:
                Emit(OpCode.Jump, _loops[^1].ContinueTarget);
                break;
            case GlobalStmt:
            case PassStmt:
            case OnStmt:
            case ImportStmt:
                break;
        }
    }

    private bool IsLocal(string name, out int slot)
    {
        slot = _kind == FunctionKind.Module ? -1 : _locals.IndexOf(name);
        return slot >= 0;
    }

    private void Load(string name, int line)
    {
        if (IsLocal(name, out var slot))
        {
            Emit(OpCode.LoadLocal, slot);
            return;
        }
        if (_module.TryGlobal(name, out var g))
        {
            Emit(OpCode.LoadGlobal, g);
            return;
        }
        var candidates = _locals.Concat(_module.VisibleNames());
        var hint = GlyphCompiler.DidYouMean(name, candidates);
        throw new GlyphError(GlyphErrorKind.Name,
            hint is null ? $"Name '{name}' is not defined" : $"Name '{name}' is not defined. Did you mean '{hint}'?",
            line);
    }

    private void Store(string name, int line)
    {
        if (IsLocal(name, out var slot))
        {
            Emit(OpCode.StoreLocal, slot);
            return;
        }
        if (_module.TryGlobal(name, out var g))
        {
            Emit(OpCode.StoreGlobal, g);
            return;
        }
        throw new GlyphError(GlyphErrorKind.Name, $"Cannot assign to '{name}' here", line);
    }

    private static OpCode BinaryOp(string op) => op switch
    {
        "+" => OpCode.Add,
        "-" => OpCode.Sub,
        "*" => OpCode.Mul,
        "/" => OpCode.Div,
        "//" => OpCode.FloorDiv,
        "%" => OpCode.Mod,
        "**" => OpCode.Pow,
        _ => throw new InvalidOperationException(op),
    };

    private static OpCode CompareOp(string op) => op switch
    {
        "==" => OpCode.Eq,
        "!=" => OpCode.Ne,
        "<" => OpCode.Lt,
        ">" => OpCode.Gt,
        "<=" => OpCode.Le,
        ">=" => OpCode.Ge,
        "in" => OpCode.In,
        "not in" => OpCode.NotIn,
        _ => throw new InvalidOperationException(op),
    };

    private static Value? Fold(Expr e)
    {
        switch (e)
        {
            case IntLit i:
                return Value.Int(i.Value);
            case FloatLit f:
                return Value.Float(f.Value);
            case UnaryExpr { Op: "-" } u when Fold(u.Operand) is { } v:
                try
                {
                    return v.Kind == ValueKind.Int ? Value.Int(checked(-v.AsInt)) : Value.Float(-v.AsFix);
                }
                catch (OverflowException)
                {
                    return null;
                }
            case BinaryExpr b when b.Op is "+" or "-" or "*" or "//" or "%"
                && Fold(b.Left) is { Kind: ValueKind.Int } l && Fold(b.Right) is { Kind: ValueKind.Int } r:
                try
                {
                    return b.Op switch
                    {
                        "+" => Value.Int(checked(l.AsInt + r.AsInt)),
                        "-" => Value.Int(checked(l.AsInt - r.AsInt)),
                        "*" => Value.Int(checked(l.AsInt * r.AsInt)),
                        "//" when r.AsInt != 0 => Value.Int(Arith.FloorDiv(l.AsInt, r.AsInt)),
                        "%" when r.AsInt != 0 => Value.Int(Arith.Mod(l.AsInt, r.AsInt)),
                        _ => null,
                    };
                }
                catch (OverflowException)
                {
                    return null;
                }
            default:
                return null;
        }
    }

    private void Expr(Expr e)
    {
        _line = e.Line;
        if (e is UnaryExpr or BinaryExpr && Fold(e) is { } folded)
        {
            Emit(OpCode.Const, Constant(folded));
            return;
        }
        switch (e)
        {
            case IntLit i:
                Emit(OpCode.Const, Constant(Value.Int(i.Value)));
                break;
            case FloatLit f:
                Emit(OpCode.Const, Constant(Value.Float(f.Value)));
                break;
            case StrLit s:
                Emit(OpCode.Const, Constant(Value.Str(s.Value)));
                break;
            case BoolLit b:
                Emit(b.Value ? OpCode.PushTrue : OpCode.PushFalse);
                break;
            case NoneLit:
                Emit(OpCode.PushNone);
                break;
            case NameExpr n:
                Load(n.Name, n.Line);
                break;
            case ListExpr l:
                foreach (var item in l.Items) Expr(item);
                _line = l.Line;
                Emit(OpCode.BuildList, l.Items.Count);
                break;
            case DictExpr d:
                for (var k = 0; k < d.Keys.Count; k++)
                {
                    Expr(d.Keys[k]);
                    Expr(d.Values[k]);
                }
                _line = d.Line;
                Emit(OpCode.BuildDict, d.Keys.Count);
                break;
            case UnaryExpr u:
                Expr(u.Operand);
                _line = u.Line;
                Emit(u.Op switch { "-" => OpCode.Neg, "+" => OpCode.Pos, _ => OpCode.Not });
                break;
            case BinaryExpr b:
                Expr(b.Left);
                Expr(b.Right);
                _line = b.Line;
                Emit(BinaryOp(b.Op));
                break;
            case LogicExpr l:
            {
                Expr(l.Left);
                _line = l.Line;
                var jump = Emit(l.Op == "and" ? OpCode.JumpIfFalseKeep : OpCode.JumpIfTrueKeep);
                Expr(l.Right);
                Patch(jump, Here);
                break;
            }
            case CompareExpr c:
                CompileCompare(c);
                break;
            case CallExpr call:
                CompileCall(call);
                break;
            case IndexExpr ix:
                Expr(ix.Target);
                Expr(ix.Index);
                _line = ix.Line;
                Emit(OpCode.Index);
                break;
            case AttrExpr a:
                Expr(a.Target);
                _line = a.Line;
                Emit(OpCode.Attr, Constant(Value.Str(a.Name)));
                break;
        }
    }

    private void CompileCompare(CompareExpr c)
    {
        Expr(c.Operands[0]);
        if (c.Ops.Count == 1)
        {
            Expr(c.Operands[1]);
            _line = c.Line;
            Emit(CompareOp(c.Ops[0]));
            return;
        }
        // a < b < c: keep the middle operand for the next comparison, bail out on the first False.
        var cleanups = new List<int>();
        for (var i = 0; i < c.Ops.Count; i++)
        {
            Expr(c.Operands[i + 1]);
            _line = c.Line;
            if (i < c.Ops.Count - 1)
            {
                Emit(OpCode.Dup);
                Emit(OpCode.Rot3);
                Emit(CompareOp(c.Ops[i]));
                cleanups.Add(Emit(OpCode.JumpIfFalseKeep));
            }
            else
            {
                Emit(CompareOp(c.Ops[i]));
            }
        }
        var toEnd = Emit(OpCode.Jump);
        var cleanup = Here;
        _depth++;
        Emit(OpCode.Swap);
        Emit(OpCode.Pop);
        foreach (var at in cleanups) Patch(at, cleanup);
        Patch(toEnd, Here);
    }

    private void CompileCall(CallExpr call)
    {
        if (call.Callee is NameExpr { Name: "wait_until" })
        {
            CompileWaitUntil(call);
            return;
        }
        Expr(call.Callee);
        foreach (var a in call.Args) Expr(a);
        foreach (var k in call.Kwargs) Expr(k.Value);
        _line = call.Line;
        var total = call.Args.Count + call.Kwargs.Count;
        if (call.Kwargs.Count == 0)
        {
            Emit(OpCode.Call, total);
        }
        else
        {
            var names = Value.List(call.Kwargs.Select(k => Value.Str(k.Name)));
            Emit(OpCode.CallKw, total, Constant(names));
        }
    }

    /// <summary><c>wait_until(cond)</c> compiles to <c>while not cond: wait(1)</c>, re-checking every tick.</summary>
    private void CompileWaitUntil(CallExpr call)
    {
        if (call.Args.Count != 1 || call.Kwargs.Count != 0)
        {
            throw new GlyphError(GlyphErrorKind.Type, "wait_until() takes exactly one condition", call.Line);
        }
        var start = Here;
        Expr(call.Args[0]);
        _line = call.Line;
        var exit = Emit(OpCode.JumpIfFalse);
        var skip = Emit(OpCode.Jump);
        Patch(exit, Here);
        Load("wait", call.Line);
        Emit(OpCode.Const, Constant(Value.Int(1)));
        Emit(OpCode.Call, 1);
        Emit(OpCode.Pop);
        Emit(OpCode.Jump, start);
        Patch(skip, Here);
        Emit(OpCode.PushNone);
    }
}
