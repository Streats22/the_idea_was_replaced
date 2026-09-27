namespace Delvework.Core.Glyph;

/// <summary>Runtime semantics of Glyph operators. All errors are friendly <see cref="GlyphError"/>s.</summary>
internal static class Ops
{
    private static GlyphError TypeError(string message, int line) => new(GlyphErrorKind.Type, message, line);

    private static string Symbol(OpCode op) => op switch
    {
        OpCode.Add => "+",
        OpCode.Sub => "-",
        OpCode.Mul => "*",
        OpCode.Div => "/",
        OpCode.FloorDiv => "//",
        OpCode.Mod => "%",
        OpCode.Pow => "**",
        OpCode.Lt => "<",
        OpCode.Gt => ">",
        OpCode.Le => "<=",
        OpCode.Ge => ">=",
        _ => op.ToString(),
    };

    public static Value Binary(OpCode op, Value a, Value b, int line)
    {
        if (a.Kind == ValueKind.Int && b.Kind == ValueKind.Int)
        {
            long x = a.AsInt, y = b.AsInt;
            switch (op)
            {
                case OpCode.Add: return Value.Int(checked(x + y));
                case OpCode.Sub: return Value.Int(checked(x - y));
                case OpCode.Mul: return Value.Int(checked(x * y));
                case OpCode.Div:
                    if (y == 0) throw new GlyphError(GlyphErrorKind.Value, "Division by zero", line);
                    return Value.Float(Fix.FromInt(x) / Fix.FromInt(y));
                case OpCode.FloorDiv:
                    if (y == 0) throw new GlyphError(GlyphErrorKind.Value, "Division by zero", line);
                    return Value.Int(Arith.FloorDiv(x, y));
                case OpCode.Mod:
                    if (y == 0) throw new GlyphError(GlyphErrorKind.Value, "Modulo by zero", line);
                    return Value.Int(Arith.Mod(x, y));
                case OpCode.Pow:
                    if (y < 0) return Value.Float(Fix.Pow(Fix.FromInt(x), y));
                    return Value.Int(Arith.Pow(x, y));
            }
        }
        if (a.IsNumber && b.IsNumber)
        {
            Fix x = a.AsFix, y = b.AsFix;
            switch (op)
            {
                case OpCode.Add: return Value.Float(x + y);
                case OpCode.Sub: return Value.Float(x - y);
                case OpCode.Mul: return Value.Float(x * y);
                case OpCode.Div:
                    if (y == Fix.Zero) throw new GlyphError(GlyphErrorKind.Value, "Division by zero", line);
                    return Value.Float(x / y);
                case OpCode.FloorDiv:
                    if (y == Fix.Zero) throw new GlyphError(GlyphErrorKind.Value, "Division by zero", line);
                    return Value.Float(Fix.FromInt((x / y).Floor()));
                case OpCode.Mod:
                    if (y == Fix.Zero) throw new GlyphError(GlyphErrorKind.Value, "Modulo by zero", line);
                    return Value.Float(x % y);
                case OpCode.Pow:
                    if (b.Kind == ValueKind.Int) return Value.Float(Fix.Pow(x, b.AsInt));
                    if (y.IsInteger) return Value.Float(Fix.Pow(x, y.Floor()));
                    throw TypeError("Only whole-number exponents are supported", line);
            }
        }
        switch (op)
        {
            case OpCode.Add when a.Kind == ValueKind.Str && b.Kind == ValueKind.Str:
                if (a.AsStr.Length + b.AsStr.Length > Vm.MaxString) throw StringLimit(line);
                return Value.Str(a.AsStr + b.AsStr);
            case OpCode.Add when a.Kind == ValueKind.List && b.Kind == ValueKind.List:
            {
                var x = a.AsList.Items;
                var y = b.AsList.Items;
                if (x.Count + y.Count > Vm.MaxCollection) throw ListLimit(line);
                var result = new ListObj(x);
                result.Items.AddRange(y);
                return Value.List(result);
            }
            case OpCode.Mul when a.Kind is ValueKind.Str or ValueKind.List && b.Kind == ValueKind.Int:
                return Repeat(a, b.AsInt, line);
            case OpCode.Mul when b.Kind is ValueKind.Str or ValueKind.List && a.Kind == ValueKind.Int:
                return Repeat(b, a.AsInt, line);
            case OpCode.Mod when a.Kind == ValueKind.Str:
                throw TypeError("String formatting with % is not supported; use str() and +", line);
            case OpCode.Add when a.Kind == ValueKind.Str || b.Kind == ValueKind.Str:
                throw TypeError($"Cannot add {a.TypeName} and {b.TypeName}; convert with str() first", line);
        }
        throw TypeError($"Cannot use {Symbol(op)} on {a.TypeName} and {b.TypeName}", line);
    }

    private static GlyphError ListLimit(int line) => new(GlyphErrorKind.Limit, $"Lists and dicts are limited to {Vm.MaxCollection} items", line);

    private static GlyphError StringLimit(int line) => new(GlyphErrorKind.Limit, $"Strings are limited to {Vm.MaxString} characters", line);

    private static Value Repeat(Value seq, long times, int line)
    {
        var length = seq.Kind == ValueKind.Str ? seq.AsStr.Length : seq.AsList.Items.Count;
        if (times <= 0 || length == 0) return seq.Kind == ValueKind.Str ? Value.Str("") : Value.List(new ListObj());
        if (seq.Kind == ValueKind.Str)
        {
            if (times > Vm.MaxString / length) throw StringLimit(line);
            return Value.Str(string.Concat(Enumerable.Repeat(seq.AsStr, (int)times)));
        }
        var items = seq.AsList.Items;
        if (times > Vm.MaxCollection / length) throw ListLimit(line);
        var result = new ListObj();
        for (var i = 0; i < times; i++) result.Items.AddRange(items);
        return Value.List(result);
    }

    public static Value Negate(Value v, int line) => v.Kind switch
    {
        ValueKind.Int => Value.Int(checked(-v.AsInt)),
        ValueKind.Float => Value.Float(-v.AsFix),
        _ => throw TypeError($"Cannot negate a {v.TypeName}", line),
    };

    public static int Compare(Value a, Value b, OpCode op, int line)
    {
        if (a.Kind == ValueKind.Int && b.Kind == ValueKind.Int) return a.AsInt.CompareTo(b.AsInt);
        if (a.IsNumber && b.IsNumber)
        {
            // Compare int against float exactly, even when the int is outside the Fix range.
            if (a.Kind == ValueKind.Int && !FitsFix(a.AsInt)) return a.AsInt < 0 ? -1 : 1;
            if (b.Kind == ValueKind.Int && !FitsFix(b.AsInt)) return b.AsInt < 0 ? 1 : -1;
            return a.AsFix.CompareTo(b.AsFix);
        }
        if (a.Kind == ValueKind.Str && b.Kind == ValueKind.Str) return Math.Sign(string.CompareOrdinal(a.AsStr, b.AsStr));
        if (a.Kind == ValueKind.Bool && b.Kind == ValueKind.Bool) return a.AsBool.CompareTo(b.AsBool);
        if (a.Kind == ValueKind.List && b.Kind == ValueKind.List)
        {
            var x = a.AsList.Items;
            var y = b.AsList.Items;
            for (var i = 0; i < Math.Min(x.Count, y.Count); i++)
            {
                if (x[i].Equals(y[i])) continue;
                return Compare(x[i], y[i], op, line);
            }
            return x.Count.CompareTo(y.Count);
        }
        throw TypeError($"Cannot compare {a.TypeName} {Symbol(op)} {b.TypeName}", line);
    }

    private static bool FitsFix(long n) => n <= long.MaxValue >> Fix.FracBits && n >= long.MinValue >> Fix.FracBits;

    public static bool Contains(Value container, Value item, int line)
    {
        switch (container.Kind)
        {
            case ValueKind.List:
                foreach (var v in container.AsList.Items)
                {
                    if (v.Equals(item)) return true;
                }
                return false;
            case ValueKind.Dict:
                return item.IsHashable && container.AsDict.ContainsKey(item);
            case ValueKind.Str:
                if (item.Kind != ValueKind.Str) throw TypeError($"'in <str>' needs a string on the left, got {item.TypeName}", line);
                return container.AsStr.Contains(item.AsStr, StringComparison.Ordinal);
            default:
                throw TypeError($"Cannot use 'in' with a {container.TypeName}", line);
        }
    }

    public static void CheckKey(Value key, int line)
    {
        if (!key.IsHashable) throw TypeError($"A {key.TypeName} cannot be a dict key", line);
    }

    private static int NormalizeIndex(Value index, int count, string what, int line)
    {
        if (index.Kind != ValueKind.Int) throw TypeError($"{what} indices must be whole numbers, got {index.TypeName}", line);
        var i = index.AsInt;
        if (i < 0) i += count;
        if (i < 0 || i >= count)
        {
            throw new GlyphError(GlyphErrorKind.Value, $"Index {index.AsInt} is out of range for a {what.ToLowerInvariant()} of length {count}", line);
        }
        return (int)i;
    }

    public static Value Index(Value target, Value index, int line)
    {
        switch (target.Kind)
        {
            case ValueKind.List:
                return target.AsList.Items[NormalizeIndex(index, target.AsList.Items.Count, "List", line)];
            case ValueKind.Str:
                return Value.Str(target.AsStr[NormalizeIndex(index, target.AsStr.Length, "String", line)].ToString());
            case ValueKind.Dict:
                CheckKey(index, line);
                if (target.AsDict.TryGet(index, out var v)) return v;
                throw new GlyphError(GlyphErrorKind.Value, $"Key {index.Repr()} is not in the dict (use .get() for a default)", line);
            default:
                throw TypeError($"A {target.TypeName} cannot be indexed with [ ]", line);
        }
    }

    public static void StoreIndex(Value target, Value index, Value value, int line)
    {
        switch (target.Kind)
        {
            case ValueKind.List:
                target.AsList.Items[NormalizeIndex(index, target.AsList.Items.Count, "List", line)] = value;
                return;
            case ValueKind.Dict:
            {
                CheckKey(index, line);
                var d = target.AsDict;
                if (d.Count >= Vm.MaxCollection && !d.ContainsKey(index)) throw ListLimit(line);
                d.Set(index, value);
                return;
            }
            case ValueKind.Str:
                throw TypeError("Strings cannot be changed in place", line);
            default:
                throw TypeError($"A {target.TypeName} does not support item assignment", line);
        }
    }

    public static Value Attr(Vm vm, Value target, string name, int line)
    {
        switch (target.Kind)
        {
            case ValueKind.Record:
            {
                var r = target.AsRecord;
                if (r.Shape.TryIndex(name, out var i)) return r.Values[i];
                var hint = GlyphCompiler.DidYouMean(name, r.Shape.Fields);
                throw new GlyphError(GlyphErrorKind.Name,
                    $"{r.Shape.Name} has no field '{name}'." + (hint is null ? $" Fields: {string.Join(", ", r.Shape.Fields)}" : $" Did you mean '{hint}'?"),
                    line);
            }
            case ValueKind.EnumType:
            {
                var t = target.AsEnumType;
                if (t.TryGet(name, out var member)) return member.AsValue();
                var hint = GlyphCompiler.DidYouMean(name, t.Members.Select(m => m.Name));
                throw new GlyphError(GlyphErrorKind.Name, $"{t.Name} has no member '{name}'" + (hint is null ? "" : $". Did you mean '{hint}'?"), line);
            }
            case ValueKind.Enum when name == "name":
                return Value.Str(target.AsEnum.Name);
            case ValueKind.List:
            case ValueKind.Str:
            case ValueKind.Dict:
            {
                var table = Stdlib.Methods(target.Kind);
                if (table.TryGetValue(name, out var def))
                {
                    if (def.Tier > vm.Program.Tier)
                    {
                        throw new GlyphError(GlyphErrorKind.Capability, Tiers.LockedMessage($".{name}()", def.Tier, vm.Program.Tier), line);
                    }
                    return Value.Builtin(new BuiltinObj(def, target));
                }
                var hint = GlyphCompiler.DidYouMean(name, table.Keys);
                throw new GlyphError(GlyphErrorKind.Name, $"A {target.TypeName} has no method '{name}'" + (hint is null ? "" : $". Did you mean '{hint}'?"), line);
            }
            default:
                throw new GlyphError(GlyphErrorKind.Name, $"A {target.TypeName} has no attribute '{name}'", line);
        }
    }

    public static Value Iter(Value v, int line) => v.Kind switch
    {
        ValueKind.List => Value.Iterator(new IteratorObj([.. v.AsList.Items])),
        ValueKind.Str => Value.Iterator(new IteratorObj(v.AsStr.Select(c => Value.Str(c.ToString())).ToArray())),
        ValueKind.Dict => Value.Iterator(new IteratorObj(Enumerable.Range(0, v.AsDict.Count).Select(v.AsDict.KeyAt).ToArray())),
        ValueKind.Iterator => v,
        _ => throw TypeError($"Cannot loop over a {v.TypeName}", line),
    };
}
