namespace Delvework.Core.Glyph;

public enum VmStatus
{
    /// <summary>The tick's instruction budget ran out mid-program; it continues next tick.</summary>
    Running,
    /// <summary>The program performed an action (or <c>wait</c>) and its turn is over.</summary>
    Yielded,
    /// <summary>The program is inside a multi-tick <c>wait</c>.</summary>
    Blocked,
    /// <summary>The main program finished and no handlers are pending.</summary>
    Done,
    Error,
}

public readonly record struct StepResult(VmStatus Status, int Used, int Line, GlyphError? Error = null, BuiltinDef? Action = null);

public readonly record struct VmFrameInfo(string Function, int Line);

/// <summary>
/// Runs one compiled program. The host calls <see cref="Step"/> once per simulation tick with the
/// golem's instruction budget. Everything is deterministic: no clocks, no hash-order iteration.
/// </summary>
public sealed class Vm
{
    public const int MaxFrames = 64;
    public const int StackSize = 4096;
    private const int InitialStack = 128;
    public const int MaxCollection = 10_000;
    public const int MaxString = 10_000;
    public const int MaxEvents = 16;
    public const int MaxOutputLines = 200;

    private struct Frame
    {
        public FunctionProto Proto;
        public int Pc;
        public int Base;
        public bool IsHandler;
    }

    private readonly record struct PendingEvent(HandlerDef Handler, Value[] Args);

    private readonly Value[] _globals;
    private Value[] _stack;
    private readonly Frame[] _frames;
    private readonly List<PendingEvent> _events = [];
    private readonly List<string> _output = [];
    private int _sp;
    private int _fp;
    private VmStatus _status;
    private GlyphError? _error;
    private long _debt;
    private int _blocked;
    private int _handlerDepth;
    private bool _mainDone;
    private bool _yieldRequested;
    private bool _lastWasAction;
    private long _pendingCharge;
    private int _currentLine;

    public Vm(GlyphProgram program, object? host = null)
    {
        Program = program;
        Host = host;
        _globals = new Value[program.GlobalNames.Length];
        var env = program.Environment.Entries;
        for (var i = 0; i < env.Count; i++) _globals[i] = env[i].Value;
        _stack = new Value[Math.Min(StackSize, Math.Max(InitialStack, program.Main.MaxStack + 16))];
        _frames = new Frame[MaxFrames];
        _stack[_sp++] = Value.None;
        PushFrame(program.Main, false);
        _currentLine = program.Main.FirstLine;
    }

    private Vm(Vm other, object? host)
    {
        Program = other.Program;
        Host = host;
        var map = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
        _globals = new Value[other._globals.Length];
        for (var i = 0; i < _globals.Length; i++) _globals[i] = CloneValue(other._globals[i], map);
        _stack = new Value[other._stack.Length];
        for (var i = 0; i < other._sp; i++) _stack[i] = CloneValue(other._stack[i], map);
        _frames = (Frame[])other._frames.Clone();
        foreach (var e in other._events)
        {
            _events.Add(new PendingEvent(e.Handler, e.Args.Select(a => CloneValue(a, map)).ToArray()));
        }
        _output.AddRange(other._output);
        _sp = other._sp;
        _fp = other._fp;
        _status = other._status;
        _error = other._error;
        _debt = other._debt;
        _blocked = other._blocked;
        _handlerDepth = other._handlerDepth;
        _mainDone = other._mainDone;
        _lastWasAction = other._lastWasAction;
        _currentLine = other._currentLine;
        TotalInstructions = other.TotalInstructions;
        TotalOutputLines = other.TotalOutputLines;
    }

    public GlyphProgram Program { get; }

    /// <summary>The game object this program controls; builtins cast it to what they need.</summary>
    public object? Host { get; set; }

    public VmStatus Status => _status;
    public GlyphError? Error => _error;
    public bool IsFinished => _status is VmStatus.Done or VmStatus.Error;
    public int CurrentLine => _currentLine;
    public long TotalInstructions { get; private set; }
    public long TotalOutputLines { get; private set; }
    public int PendingEventCount => _events.Count;
    public int BlockedTicks => _blocked;
    public IReadOnlyList<string> Output => _output;

    /// <summary>Deep copy for replay keyframes. Heap objects are copied; the program is shared.</summary>
    public Vm Clone(object? newHost) => new(this, newHost);

    /// <summary>Extra instruction cost for the builtin currently running.</summary>
    public void Charge(int instructions) => _pendingCharge += Math.Max(0, instructions);

    /// <summary>Ends the current turn after the running builtin returns.</summary>
    public void RequestYield() => _yieldRequested = true;

    /// <summary>End the turn now and sleep for <paramref name="ticks"/> - 1 further ticks.</summary>
    public void Block(int ticks)
    {
        _blocked = Math.Max(0, ticks - 1);
        _yieldRequested = true;
    }

    /// <summary>Replace the return value of the action that ended the last turn.</summary>
    public void ResolveAction(Value result)
    {
        if (!_lastWasAction || _sp == 0) return;
        _stack[_sp - 1] = result;
        _lastWasAction = false;
    }

    public void Print(string line)
    {
        TotalOutputLines++;
        if (_output.Count >= MaxOutputLines) _output.RemoveAt(0);
        _output.Add(line.Length > 500 ? line[..500] : line);
    }

    public List<string> DrainOutput()
    {
        var lines = new List<string>(_output);
        _output.Clear();
        return lines;
    }

    /// <summary>
    /// Queue an event for the program's matching handlers. Returns false if nothing listens.
    /// When the queue is full the oldest pending event is dropped.
    /// </summary>
    public bool QueueEvent(string evt, string? key, params Value[] args)
    {
        var queued = false;
        foreach (var h in Program.Handlers)
        {
            if (h.Event != evt || (h.Key is not null && h.Key != key)) continue;
            if (_events.Count >= MaxEvents) _events.RemoveAt(0);
            Value[] full = h.Key is null && key is not null ? [Value.Str(key), .. args] : args;
            var fitted = new Value[h.Proto.Arity];
            for (var i = 0; i < fitted.Length; i++) fitted[i] = i < full.Length ? full[i] : Value.None;
            _events.Add(new PendingEvent(h, fitted));
            queued = true;
        }
        return queued;
    }

    public IReadOnlyList<VmFrameInfo> CallStack()
    {
        var list = new List<VmFrameInfo>();
        for (var i = _fp - 1; i >= 0; i--)
        {
            var f = _frames[i];
            list.Add(new VmFrameInfo(f.Proto.Name, LineAt(f.Proto, f.Pc - 1)));
        }
        return list;
    }

    public IReadOnlyList<(string Name, Value Value)> GetGlobals()
    {
        var list = new List<(string, Value)>();
        var start = Program.Environment.Entries.Count;
        for (var i = start; i < _globals.Length; i++)
        {
            if (_globals[i].Kind is ValueKind.Undefined or ValueKind.Function) continue;
            list.Add((Program.GlobalNames[i], _globals[i]));
        }
        return list;
    }

    public IReadOnlyList<(string Name, Value Value)> GetLocals()
    {
        var list = new List<(string, Value)>();
        if (_fp == 0) return list;
        var f = _frames[_fp - 1];
        for (var i = 0; i < f.Proto.LocalCount; i++)
        {
            var v = _stack[f.Base + i];
            if (v.Kind != ValueKind.Undefined) list.Add((f.Proto.LocalNames[i], v));
        }
        return list;
    }

    public Value GetGlobal(string name)
    {
        var i = Array.IndexOf(Program.GlobalNames, name);
        return i < 0 ? Value.Undefined : _globals[i];
    }

    /// <summary>Deterministic hash of all execution state, used to verify replays.</summary>
    public ulong StateHash()
    {
        var h = Fnv.Mix(Fnv.Offset, (ulong)_status);
        h = Fnv.Mix(h, _debt);
        h = Fnv.Mix(h, (long)_blocked);
        h = Fnv.Mix(h, (long)_sp);
        h = Fnv.Mix(h, (long)_fp);
        for (var i = Program.Environment.Entries.Count; i < _globals.Length; i++) h = _globals[i].StableHash(h);
        for (var i = 0; i < _sp; i++) h = _stack[i].StableHash(h);
        for (var i = 0; i < _fp; i++)
        {
            h = Fnv.Mix(h, _frames[i].Proto.Name);
            h = Fnv.Mix(h, (long)_frames[i].Pc);
        }
        foreach (var e in _events)
        {
            h = Fnv.Mix(h, e.Handler.Event);
            foreach (var a in e.Args) h = a.StableHash(h);
        }
        return Fnv.Mix(h, TotalOutputLines);
    }

    public StepResult Step(int budget)
    {
        if (_status == VmStatus.Error) return new StepResult(VmStatus.Error, 0, _currentLine, _error);
        _lastWasAction = false;
        if (_debt >= budget)
        {
            _debt -= budget;
            return Finish(VmStatus.Running, 0);
        }
        var limit = (int)(budget - _debt);
        _debt = 0;

        if (_handlerDepth == 0 && _events.Count == 0)
        {
            if (_mainDone) return Finish(VmStatus.Done, 0);
            if (_blocked > 0)
            {
                _blocked--;
                return Finish(VmStatus.Blocked, 0);
            }
        }
        // Otherwise handlers start at the next statement boundary inside Run.
        if (_handlerDepth == 0 && _events.Count > 0 && (_mainDone || _blocked > 0) && !TryDispatch(out var dispatchError))
        {
            return Fail(dispatchError!, 0);
        }
        _status = VmStatus.Running;
        return Run(limit);
    }

    private StepResult Finish(VmStatus status, int used, BuiltinDef? action = null)
    {
        _status = status;
        return new StepResult(status, used, _currentLine, null, action);
    }

    private StepResult Fail(GlyphError error, int used)
    {
        _status = VmStatus.Error;
        _error = error.Line > 0 ? error : new GlyphError(error.Kind, error.Message, _currentLine, error.Column);
        return new StepResult(VmStatus.Error, used, _error.Line, _error);
    }

    private static int LineAt(FunctionProto p, int pc) => p.Lines.Length == 0 ? p.FirstLine : p.Lines[Math.Clamp(pc, 0, p.Lines.Length - 1)];

    private bool PushFrame(FunctionProto proto, bool isHandler)
    {
        if (_fp >= MaxFrames) return false;
        var basePtr = _sp - proto.Arity;
        _frames[_fp++] = new Frame { Proto = proto, Pc = 0, Base = basePtr, IsHandler = isHandler };
        for (var i = proto.Arity; i < proto.LocalCount; i++) _stack[basePtr + i] = Value.Undefined;
        _sp = basePtr + proto.LocalCount;
        return true;
    }

    private bool TryDispatch(out GlyphError? error)
    {
        error = null;
        var ev = _events[0];
        _events.RemoveAt(0);
        var proto = ev.Handler.Proto;
        if (!EnsureStack(_sp + 1 + proto.LocalCount + proto.MaxStack + 8))
        {
            error = new GlyphError(GlyphErrorKind.Limit, "Out of stack space", _currentLine);
            return false;
        }
        _stack[_sp++] = Value.None;
        foreach (var a in ev.Args) _stack[_sp++] = a;
        if (!PushFrame(proto, true))
        {
            error = new GlyphError(GlyphErrorKind.Limit, $"Too many nested calls (max {MaxFrames})", _currentLine);
            return false;
        }
        _handlerDepth++;
        return true;
    }

    /// <summary>Grow the value stack to hold <paramref name="needed"/> slots, up to <see cref="StackSize"/>.</summary>
    private bool EnsureStack(int needed)
    {
        if (needed <= _stack.Length) return true;
        if (needed > StackSize) return false;
        Array.Resize(ref _stack, Math.Min(StackSize, Math.Max(needed, _stack.Length * 2)));
        return true;
    }

    private StepResult Run(int limit)
    {
        var used = 0;
        ref var frame = ref _frames[_fp - 1];
        var code = frame.Proto.Code;
        var consts = frame.Proto.Constants;
        var stack = _stack;
        var pc = frame.Pc;
        try
        {
            while (true)
            {
                if (used >= limit)
                {
                    _debt = used - limit;
                    frame.Pc = pc;
                    return Finish(VmStatus.Running, used);
                }
                if (_events.Count > 0 && _handlerDepth == 0 && frame.Proto.StatementStarts[pc])
                {
                    frame.Pc = pc;
                    if (!TryDispatch(out var err)) throw err!;
                    stack = _stack;
                    frame = ref _frames[_fp - 1];
                    code = frame.Proto.Code;
                    consts = frame.Proto.Constants;
                    pc = 0;
                }
                var ins = code[pc++];
                used++;
                TotalInstructions++;
                switch (ins.Op)
                {
                    case OpCode.Nop:
                        break;
                    case OpCode.Const:
                        stack[_sp++] = consts[ins.A];
                        break;
                    case OpCode.PushNone:
                        stack[_sp++] = Value.None;
                        break;
                    case OpCode.PushTrue:
                        stack[_sp++] = Value.True;
                        break;
                    case OpCode.PushFalse:
                        stack[_sp++] = Value.False;
                        break;
                    case OpCode.Pop:
                        _sp--;
                        break;
                    case OpCode.Dup:
                        stack[_sp] = stack[_sp - 1];
                        _sp++;
                        break;
                    case OpCode.Dup2:
                        stack[_sp] = stack[_sp - 2];
                        stack[_sp + 1] = stack[_sp - 1];
                        _sp += 2;
                        break;
                    case OpCode.Swap:
                        (stack[_sp - 1], stack[_sp - 2]) = (stack[_sp - 2], stack[_sp - 1]);
                        break;
                    case OpCode.Rot3:
                    {
                        var top = stack[_sp - 1];
                        stack[_sp - 1] = stack[_sp - 2];
                        stack[_sp - 2] = stack[_sp - 3];
                        stack[_sp - 3] = top;
                        break;
                    }
                    case OpCode.LoadLocal:
                    {
                        var v = stack[frame.Base + ins.A];
                        if (v.Kind == ValueKind.Undefined)
                        {
                            throw new GlyphError(GlyphErrorKind.Name, $"Local variable '{frame.Proto.LocalNames[ins.A]}' is used before it is assigned", LineAt(frame.Proto, pc - 1));
                        }
                        stack[_sp++] = v;
                        break;
                    }
                    case OpCode.StoreLocal:
                        stack[frame.Base + ins.A] = stack[--_sp];
                        break;
                    case OpCode.LoadGlobal:
                    {
                        var v = _globals[ins.A];
                        if (v.Kind == ValueKind.Undefined)
                        {
                            throw new GlyphError(GlyphErrorKind.Name, $"Name '{Program.GlobalNames[ins.A]}' has no value yet", LineAt(frame.Proto, pc - 1));
                        }
                        stack[_sp++] = v;
                        break;
                    }
                    case OpCode.StoreGlobal:
                        _globals[ins.A] = stack[--_sp];
                        break;
                    case OpCode.Add:
                    case OpCode.Sub:
                    case OpCode.Mul:
                    case OpCode.Div:
                    case OpCode.FloorDiv:
                    case OpCode.Mod:
                    case OpCode.Pow:
                    {
                        var b = stack[--_sp];
                        var a = stack[_sp - 1];
                        stack[_sp - 1] = Ops.Binary(ins.Op, a, b, LineAt(frame.Proto, pc - 1));
                        break;
                    }
                    case OpCode.Neg:
                        stack[_sp - 1] = Ops.Negate(stack[_sp - 1], LineAt(frame.Proto, pc - 1));
                        break;
                    case OpCode.Pos:
                        if (!stack[_sp - 1].IsNumber) throw new GlyphError(GlyphErrorKind.Type, $"Unary + needs a number, got {stack[_sp - 1].TypeName}", LineAt(frame.Proto, pc - 1));
                        break;
                    case OpCode.Not:
                        stack[_sp - 1] = Value.Bool(!stack[_sp - 1].IsTruthy);
                        break;
                    case OpCode.Eq:
                    {
                        var b = stack[--_sp];
                        stack[_sp - 1] = Value.Bool(stack[_sp - 1].Equals(b));
                        break;
                    }
                    case OpCode.Ne:
                    {
                        var b = stack[--_sp];
                        stack[_sp - 1] = Value.Bool(!stack[_sp - 1].Equals(b));
                        break;
                    }
                    case OpCode.Lt:
                    case OpCode.Gt:
                    case OpCode.Le:
                    case OpCode.Ge:
                    {
                        var b = stack[--_sp];
                        var c = Ops.Compare(stack[_sp - 1], b, ins.Op, LineAt(frame.Proto, pc - 1));
                        stack[_sp - 1] = Value.Bool(ins.Op switch
                        {
                            OpCode.Lt => c < 0,
                            OpCode.Gt => c > 0,
                            OpCode.Le => c <= 0,
                            _ => c >= 0,
                        });
                        break;
                    }
                    case OpCode.In:
                    case OpCode.NotIn:
                    {
                        var container = stack[--_sp];
                        var found = Ops.Contains(container, stack[_sp - 1], LineAt(frame.Proto, pc - 1));
                        stack[_sp - 1] = Value.Bool(found == (ins.Op == OpCode.In));
                        break;
                    }
                    case OpCode.Jump:
                        pc = ins.A;
                        break;
                    case OpCode.JumpIfFalse:
                        if (!stack[--_sp].IsTruthy) pc = ins.A;
                        break;
                    case OpCode.JumpIfFalseKeep:
                        if (!stack[_sp - 1].IsTruthy) pc = ins.A;
                        else _sp--;
                        break;
                    case OpCode.JumpIfTrueKeep:
                        if (stack[_sp - 1].IsTruthy) pc = ins.A;
                        else _sp--;
                        break;
                    case OpCode.BuildList:
                    {
                        var items = new ListObj();
                        items.Items.Capacity = ins.A;
                        for (var i = _sp - ins.A; i < _sp; i++) items.Items.Add(stack[i]);
                        _sp -= ins.A;
                        stack[_sp++] = Value.List(items);
                        break;
                    }
                    case OpCode.BuildDict:
                    {
                        var d = new DictObj();
                        var line = LineAt(frame.Proto, pc - 1);
                        for (var i = _sp - 2 * ins.A; i < _sp; i += 2)
                        {
                            Ops.CheckKey(stack[i], line);
                            d.Set(stack[i], stack[i + 1]);
                        }
                        _sp -= 2 * ins.A;
                        stack[_sp++] = Value.Dict(d);
                        break;
                    }
                    case OpCode.Index:
                    {
                        var index = stack[--_sp];
                        stack[_sp - 1] = Ops.Index(stack[_sp - 1], index, LineAt(frame.Proto, pc - 1));
                        break;
                    }
                    case OpCode.StoreIndex:
                    {
                        var index = stack[--_sp];
                        var target = stack[--_sp];
                        var value = stack[--_sp];
                        Ops.StoreIndex(target, index, value, LineAt(frame.Proto, pc - 1));
                        break;
                    }
                    case OpCode.Attr:
                        stack[_sp - 1] = Ops.Attr(this, stack[_sp - 1], consts[ins.A].AsStr, LineAt(frame.Proto, pc - 1));
                        break;
                    case OpCode.Call:
                    case OpCode.CallKw:
                    {
                        var argc = ins.A;
                        var line = LineAt(frame.Proto, pc - 1);
                        _currentLine = line;
                        var callee = stack[_sp - argc - 1];
                        var kwNames = ins.Op == OpCode.CallKw ? KwNames(consts[ins.B]) : [];
                        if (callee.Kind == ValueKind.Function)
                        {
                            var proto = callee.AsFunction.Proto;
                            if (kwNames.Length > 0) BindKeywords(proto, argc, kwNames, line);
                            else if (argc != proto.Arity)
                            {
                                throw new GlyphError(GlyphErrorKind.Type, $"{proto.Name}() takes {proto.Arity} argument{(proto.Arity == 1 ? "" : "s")} but got {argc}", line);
                            }
                            if (!EnsureStack(_sp + proto.LocalCount + proto.MaxStack + 8))
                            {
                                throw new GlyphError(GlyphErrorKind.Limit, "Out of stack space", line);
                            }
                            stack = _stack;
                            frame.Pc = pc;
                            if (!PushFrame(proto, false))
                            {
                                throw new GlyphError(GlyphErrorKind.Limit, $"Recursion too deep (max {MaxFrames} nested calls)", line);
                            }
                            frame = ref _frames[_fp - 1];
                            code = proto.Code;
                            consts = proto.Constants;
                            pc = 0;
                            break;
                        }
                        if (callee.Kind != ValueKind.Builtin)
                        {
                            throw new GlyphError(GlyphErrorKind.Type, $"A {callee.TypeName} is not something you can call", line);
                        }
                        var bobj = callee.AsBuiltin;
                        var def = bobj.Def;
                        if (def.Tier > Program.Tier)
                        {
                            throw new GlyphError(GlyphErrorKind.Capability, Tiers.LockedMessage($"{def.Name}()", def.Tier, Program.Tier), line);
                        }
                        var positional = argc - kwNames.Length;
                        CheckBuiltinArgs(def, positional, kwNames, line);
                        frame.Pc = pc;
                        _pendingCharge = 0;
                        _yieldRequested = false;
                        var args = new ReadOnlySpan<Value>(stack, _sp - argc, positional);
                        var kwValues = new ReadOnlySpan<Value>(stack, _sp - kwNames.Length, kwNames.Length);
                        var result = def.Impl(new BuiltinCall(this, args, kwValues, kwNames, bobj.Self, line));
                        _sp -= argc + 1;
                        stack[_sp++] = result;
                        used += def.Cost - 1 + (int)Math.Min(_pendingCharge, 1_000_000);
                        _pendingCharge = 0;
                        if (_status == VmStatus.Error) return new StepResult(VmStatus.Error, used, line, _error);
                        if (def.IsAction || _yieldRequested)
                        {
                            _yieldRequested = false;
                            _lastWasAction = def.IsAction;
                            frame.Pc = pc;
                            if (used > limit) _debt = used - limit;
                            return Finish(VmStatus.Yielded, used, def.IsAction ? def : null);
                        }
                        break;
                    }
                    case OpCode.Return:
                    {
                        var result = stack[--_sp];
                        var done = _frames[--_fp];
                        _sp = done.Base - 1;
                        if (done.IsHandler) _handlerDepth--;
                        if (!done.IsHandler && _fp > 0)
                        {
                            stack[_sp++] = result;
                        }
                        else
                        {
                            if (_fp == 0) _mainDone = true;
                            if (_handlerDepth == 0)
                            {
                                if (_events.Count > 0)
                                {
                                    if (!TryDispatch(out var err)) throw err!;
                                    stack = _stack;
                                }
                                else if (_fp == 0)
                                {
                                    _sp = 0;
                                    if (used > limit) _debt = used - limit;
                                    return Finish(VmStatus.Done, used);
                                }
                                else if (_blocked > 0)
                                {
                                    _blocked--;
                                    frame = ref _frames[_fp - 1];
                                    return Finish(VmStatus.Blocked, used);
                                }
                            }
                        }
                        frame = ref _frames[_fp - 1];
                        code = frame.Proto.Code;
                        consts = frame.Proto.Constants;
                        pc = frame.Pc;
                        break;
                    }
                    case OpCode.GetIter:
                        stack[_sp - 1] = Ops.Iter(stack[_sp - 1], LineAt(frame.Proto, pc - 1));
                        break;
                    case OpCode.ForIter:
                    {
                        var it = stack[_sp - 1].AsIterator;
                        if (it.Index < it.Items.Length)
                        {
                            stack[_sp++] = it.Items[it.Index++];
                        }
                        else
                        {
                            _sp--;
                            pc = ins.A;
                        }
                        break;
                    }
                    default:
                        throw new InvalidOperationException($"Unknown opcode {ins.Op}");
                }
                if (_sp + 8 >= stack.Length)
                {
                    if (!EnsureStack(_sp + 16)) throw new GlyphError(GlyphErrorKind.Limit, "Out of stack space", LineAt(frame.Proto, pc - 1));
                    stack = _stack;
                }
            }
        }
        catch (GlyphError e)
        {
            SyncLine(pc);
            return Fail(e, used);
        }
        catch (OverflowException)
        {
            SyncLine(pc);
            return Fail(new GlyphError(GlyphErrorKind.Value, "Number too large", _currentLine), used);
        }
        catch (DivideByZeroException)
        {
            SyncLine(pc);
            return Fail(new GlyphError(GlyphErrorKind.Value, "Division by zero", _currentLine), used);
        }
        finally
        {
            if (_fp > 0 && _status != VmStatus.Error)
            {
                var top = _frames[_fp - 1];
                _currentLine = LineAt(top.Proto, top.Pc);
            }
        }
    }

    private void SyncLine(int pc)
    {
        if (_fp == 0) return;
        ref var f = ref _frames[_fp - 1];
        _currentLine = LineAt(f.Proto, pc - 1);
    }

    private static string[] KwNames(Value list)
    {
        var items = list.AsList.Items;
        var names = new string[items.Count];
        for (var i = 0; i < names.Length; i++) names[i] = items[i].AsStr;
        return names;
    }

    /// <summary>Reorder keyword arguments on the stack into parameter order.</summary>
    private void BindKeywords(FunctionProto proto, int argc, string[] kwNames, int line)
    {
        var positional = argc - kwNames.Length;
        if (argc > proto.Arity)
        {
            throw new GlyphError(GlyphErrorKind.Type, $"{proto.Name}() takes {proto.Arity} arguments but got {argc}", line);
        }
        var start = _sp - argc;
        var bound = new Value[proto.Arity];
        var set = new bool[proto.Arity];
        for (var i = 0; i < positional; i++)
        {
            bound[i] = _stack[start + i];
            set[i] = true;
        }
        for (var k = 0; k < kwNames.Length; k++)
        {
            var idx = Array.IndexOf(proto.LocalNames, kwNames[k], 0, proto.Arity);
            if (idx < 0) throw new GlyphError(GlyphErrorKind.Type, $"{proto.Name}() has no parameter named '{kwNames[k]}'", line);
            if (set[idx]) throw new GlyphError(GlyphErrorKind.Type, $"{proto.Name}() got two values for '{kwNames[k]}'", line);
            bound[idx] = _stack[start + positional + k];
            set[idx] = true;
        }
        for (var i = 0; i < proto.Arity; i++)
        {
            if (!set[i]) throw new GlyphError(GlyphErrorKind.Type, $"{proto.Name}() is missing '{proto.LocalNames[i]}'", line);
            _stack[start + i] = bound[i];
        }
        _sp = start + proto.Arity;
    }

    private static void CheckBuiltinArgs(BuiltinDef def, int positional, string[] kwNames, int line)
    {
        foreach (var k in kwNames)
        {
            if (Array.IndexOf(def.Keywords, k) < 0)
            {
                throw new GlyphError(GlyphErrorKind.Type, $"{def.Name}() has no option named '{k}'", line);
            }
        }
        var total = positional + kwNames.Length;
        if (positional > def.MaxArgs && def.MaxArgs >= 0)
        {
            throw new GlyphError(GlyphErrorKind.Type, $"{def.Name}() takes at most {def.MaxArgs} argument{(def.MaxArgs == 1 ? "" : "s")} but got {positional}", line);
        }
        if (total < def.MinArgs)
        {
            var sig = def.Signature.Length > 0 ? $" Usage: {def.Signature}" : "";
            throw new GlyphError(GlyphErrorKind.Type, $"{def.Name}() needs at least {def.MinArgs} argument{(def.MinArgs == 1 ? "" : "s")}.{sig}", line);
        }
    }

    private Value CloneValue(Value v, Dictionary<object, object> map)
    {
        switch (v.Kind)
        {
            case ValueKind.List:
            {
                if (map.TryGetValue(v.AsList, out var existing)) return Value.List((ListObj)existing);
                var copy = new ListObj();
                map[v.AsList] = copy;
                foreach (var item in v.AsList.Items) copy.Items.Add(CloneValue(item, map));
                return Value.List(copy);
            }
            case ValueKind.Dict:
            {
                if (map.TryGetValue(v.AsDict, out var existing)) return Value.Dict((DictObj)existing);
                var src = v.AsDict;
                var copy = new DictObj();
                map[src] = copy;
                for (var i = 0; i < src.Count; i++) copy.Set(src.KeyAt(i), CloneValue(src.ValueAt(i), map));
                return Value.Dict(copy);
            }
            case ValueKind.Iterator:
            {
                if (map.TryGetValue(v.AsIterator, out var existing)) return Value.Iterator((IteratorObj)existing);
                var src = v.AsIterator;
                var copy = new IteratorObj(src.Items.Select(x => CloneValue(x, map)).ToArray()) { Index = src.Index };
                map[src] = copy;
                return Value.Iterator(copy);
            }
            case ValueKind.Builtin when v.AsBuiltin.Self.Kind != ValueKind.None:
                return Value.Builtin(new BuiltinObj(v.AsBuiltin.Def, CloneValue(v.AsBuiltin.Self, map)));
            default:
                return v;
        }
    }
}
