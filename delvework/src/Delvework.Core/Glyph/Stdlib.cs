namespace Delvework.Core.Glyph;

/// <summary>The language-level builtins every Glyph environment shares.</summary>
public static class Stdlib
{
    private static readonly Dictionary<string, BuiltinDef> ListMethodTable = BuildListMethods();
    private static readonly Dictionary<string, BuiltinDef> StrMethodTable = BuildStrMethods();
    private static readonly Dictionary<string, BuiltinDef> DictMethodTable = BuildDictMethods();
    private static readonly Dictionary<string, BuiltinDef> Empty = [];

    public static IReadOnlyDictionary<string, BuiltinDef> Methods(ValueKind kind) => kind switch
    {
        ValueKind.List => ListMethodTable,
        ValueKind.Str => StrMethodTable,
        ValueKind.Dict => DictMethodTable,
        _ => Empty,
    };

    public static IEnumerable<BuiltinDef> Functions =>
    [
        new BuiltinDef
        {
            Name = "print", Impl = Print, Tier = Tiers.Variables, MinArgs = 0, MaxArgs = -1,
            Signature = "print(value, ...)", Doc = "Write values to the delve log.",
        },
        new BuiltinDef
        {
            Name = "range", Impl = Range, Tier = Tiers.Lists, MinArgs = 1, MaxArgs = 3,
            Signature = "range(stop) / range(start, stop, step=1)", Doc = "A list of whole numbers.",
        },
        new BuiltinDef
        {
            Name = "len", Impl = Len, Tier = Tiers.Lists, MinArgs = 1, MaxArgs = 1,
            Signature = "len(x)", Doc = "Number of items in a list, dict or string.",
        },
        new BuiltinDef
        {
            Name = "min", Impl = (in BuiltinCall c) => MinMax(c, -1), Tier = Tiers.Variables, MinArgs = 1, MaxArgs = -1,
            Signature = "min(a, b, ...) / min(list)", Doc = "The smallest value.",
        },
        new BuiltinDef
        {
            Name = "max", Impl = (in BuiltinCall c) => MinMax(c, 1), Tier = Tiers.Variables, MinArgs = 1, MaxArgs = -1,
            Signature = "max(a, b, ...) / max(list)", Doc = "The largest value.",
        },
        new BuiltinDef
        {
            Name = "abs", Impl = Abs, Tier = Tiers.Variables, MinArgs = 1, MaxArgs = 1,
            Signature = "abs(x)", Doc = "Distance from zero.",
        },
        new BuiltinDef
        {
            Name = "str", Impl = Str, Tier = Tiers.Variables, MinArgs = 1, MaxArgs = 1,
            Signature = "str(x)", Doc = "Text form of a value.",
        },
        new BuiltinDef
        {
            Name = "int", Impl = Int, Tier = Tiers.Variables, MinArgs = 1, MaxArgs = 1,
            Signature = "int(x)", Doc = "Whole number from a float (rounds toward zero), bool or numeric string.",
        },
        new BuiltinDef
        {
            Name = "wait", Impl = Wait, Tier = Tiers.Calls, MinArgs = 0, MaxArgs = 1,
            Signature = "wait(ticks=1)", Doc = "End this turn and do nothing for the given number of ticks.",
        },
    ];

    /// <summary>Add the shared builtins to an environment.</summary>
    public static GlyphEnvironment AddTo(GlyphEnvironment env)
    {
        foreach (var f in Functions) env.Add(f);
        return env;
    }

    private static Value Print(in BuiltinCall c)
    {
        var parts = new string[c.Count];
        for (var i = 0; i < c.Count; i++) parts[i] = c.Args[i].ToString();
        var line = string.Join(' ', parts);
        c.Vm.Charge(line.Length / 32);
        c.Vm.Print(line);
        return Value.None;
    }

    private static Value Range(in BuiltinCall c)
    {
        long start = 0, stop, step = 1;
        if (c.Count == 1)
        {
            stop = c.Int(0, "range");
        }
        else
        {
            start = c.Int(0, "range");
            stop = c.Int(1, "range");
            if (c.Count == 3) step = c.Int(2, "range");
        }
        if (step == 0) throw c.Error("range() step cannot be zero", GlyphErrorKind.Value);
        var count = step > 0
            ? (stop > start ? (Int128)(stop - (Int128)start + step - 1) / step : 0)
            : (start > stop ? ((Int128)start - stop - step - 1) / -step : 0);
        if (count > Vm.MaxCollection)
        {
            throw c.Error($"range() would make {count} items; lists are limited to {Vm.MaxCollection}", GlyphErrorKind.Limit);
        }
        var n = (int)count;
        c.Vm.Charge(n / 8);
        var list = new ListObj();
        list.Items.Capacity = n;
        for (var i = 0; i < n; i++) list.Items.Add(Value.Int(start + i * step));
        return Value.List(list);
    }

    private static Value Len(in BuiltinCall c)
    {
        var v = c.Arg(0);
        return v.Kind switch
        {
            ValueKind.List => Value.Int(v.AsList.Items.Count),
            ValueKind.Dict => Value.Int(v.AsDict.Count),
            ValueKind.Str => Value.Int(v.AsStr.Length),
            _ => throw c.Error($"len() needs a list, dict or string, got {v.TypeName}"),
        };
    }

    private static Value MinMax(in BuiltinCall c, int sign)
    {
        var name = sign < 0 ? "min" : "max";
        ReadOnlySpan<Value> items = c.Args;
        if (c.Count == 1)
        {
            var only = c.Arg(0);
            if (only.Kind != ValueKind.List) throw c.Error($"{name}() of one value needs a list, got {only.TypeName}");
            items = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(only.AsList.Items);
            if (items.Length == 0) throw c.Error($"{name}() of an empty list", GlyphErrorKind.Value);
        }
        c.Vm.Charge(items.Length / 8);
        var best = items[0];
        for (var i = 1; i < items.Length; i++)
        {
            if (Ops.Compare(items[i], best, sign < 0 ? OpCode.Lt : OpCode.Gt, c.Line) * sign > 0) best = items[i];
        }
        return best;
    }

    private static Value Abs(in BuiltinCall c)
    {
        var v = c.Arg(0);
        return v.Kind switch
        {
            ValueKind.Int => Value.Int(checked(Math.Abs(v.AsInt))),
            ValueKind.Float => Value.Float(Fix.Abs(v.AsFix)),
            _ => throw c.Error($"abs() needs a number, got {v.TypeName}"),
        };
    }

    private static Value Str(in BuiltinCall c)
    {
        var s = c.Arg(0).ToString();
        if (s.Length > Vm.MaxString) throw c.Error($"Strings are limited to {Vm.MaxString} characters", GlyphErrorKind.Limit);
        return Value.Str(s);
    }

    private static Value Int(in BuiltinCall c)
    {
        var v = c.Arg(0);
        switch (v.Kind)
        {
            case ValueKind.Int:
                return v;
            case ValueKind.Float:
                return Value.Int(v.AsFix.Truncate());
            case ValueKind.Bool:
                return Value.Int(v.AsBool ? 1 : 0);
            case ValueKind.Str:
            {
                var s = v.AsStr.Trim();
                if (long.TryParse(s, System.Globalization.NumberStyles.AllowLeadingSign, System.Globalization.CultureInfo.InvariantCulture, out var n))
                {
                    return Value.Int(n);
                }
                throw c.Error($"int() cannot read '{v.AsStr}' as a whole number", GlyphErrorKind.Value);
            }
            default:
                throw c.Error($"int() cannot convert a {v.TypeName}");
        }
    }

    private static Value Wait(in BuiltinCall c)
    {
        var ticks = c.Count == 0 ? 1 : c.Int(0, "wait");
        if (ticks < 1) throw c.Error("wait() needs at least 1 tick", GlyphErrorKind.Value);
        c.Vm.Block((int)Math.Min(ticks, 100_000));
        return Value.None;
    }

    private static BuiltinDef Method(string name, BuiltinImpl impl, int tier, int min, int max, string signature, string doc) => new()
    {
        Name = name,
        Impl = impl,
        Tier = tier,
        MinArgs = min,
        MaxArgs = max,
        Signature = signature,
        Doc = doc,
    };

    private static void Add(Dictionary<string, BuiltinDef> table, BuiltinDef def) => table[def.Name] = def;

    private static List<Value> Items(in BuiltinCall c) => c.Self.AsList.Items;

    private static void CheckGrow(in BuiltinCall c, int by)
    {
        if (Items(c).Count + by > Vm.MaxCollection)
        {
            throw c.Error($"Lists are limited to {Vm.MaxCollection} items", GlyphErrorKind.Limit);
        }
    }

    private static Dictionary<string, BuiltinDef> BuildListMethods()
    {
        var t = new Dictionary<string, BuiltinDef>();
        Add(t, Method("append", (in BuiltinCall c) =>
        {
            CheckGrow(c, 1);
            Items(c).Add(c.Arg(0));
            return Value.None;
        }, Tiers.Lists, 1, 1, "list.append(x)", "Add x to the end."));
        Add(t, Method("extend", (in BuiltinCall c) =>
        {
            var other = c.Arg(0);
            if (other.Kind != ValueKind.List) throw c.Error($"extend() needs a list, got {other.TypeName}");
            CheckGrow(c, other.AsList.Items.Count);
            c.Vm.Charge(other.AsList.Items.Count / 8);
            Items(c).AddRange([.. other.AsList.Items]);
            return Value.None;
        }, Tiers.Lists, 1, 1, "list.extend(other)", "Add every item of another list."));
        Add(t, Method("pop", (in BuiltinCall c) =>
        {
            var items = Items(c);
            if (items.Count == 0) throw c.Error("pop() from an empty list", GlyphErrorKind.Value);
            var i = c.Count == 0 ? items.Count - 1 : c.Int(0, "pop");
            if (i < 0) i += items.Count;
            if (i < 0 || i >= items.Count) throw c.Error("pop() index out of range", GlyphErrorKind.Value);
            var v = items[(int)i];
            items.RemoveAt((int)i);
            return v;
        }, Tiers.Lists, 0, 1, "list.pop(index=-1)", "Remove and return an item."));
        Add(t, Method("insert", (in BuiltinCall c) =>
        {
            CheckGrow(c, 1);
            var items = Items(c);
            var i = c.Int(0, "insert");
            if (i < 0) i += items.Count;
            items.Insert((int)Math.Clamp(i, 0, items.Count), c.Arg(1));
            return Value.None;
        }, Tiers.Lists, 2, 2, "list.insert(index, x)", "Insert x before index."));
        Add(t, Method("remove", (in BuiltinCall c) =>
        {
            var items = Items(c);
            var target = c.Arg(0);
            for (var i = 0; i < items.Count; i++)
            {
                if (!items[i].Equals(target)) continue;
                items.RemoveAt(i);
                return Value.None;
            }
            throw c.Error($"remove(): {target.Repr()} is not in the list", GlyphErrorKind.Value);
        }, Tiers.Lists, 1, 1, "list.remove(x)", "Remove the first item equal to x."));
        Add(t, Method("index", (in BuiltinCall c) =>
        {
            var items = Items(c);
            var target = c.Arg(0);
            c.Vm.Charge(items.Count / 8);
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].Equals(target)) return Value.Int(i);
            }
            throw c.Error($"index(): {target.Repr()} is not in the list", GlyphErrorKind.Value);
        }, Tiers.Lists, 1, 1, "list.index(x)", "Position of the first item equal to x."));
        Add(t, Method("count", (in BuiltinCall c) =>
        {
            var items = Items(c);
            var target = c.Arg(0);
            c.Vm.Charge(items.Count / 8);
            var n = 0;
            foreach (var v in items)
            {
                if (v.Equals(target)) n++;
            }
            return Value.Int(n);
        }, Tiers.Lists, 1, 1, "list.count(x)", "How many items equal x."));
        Add(t, Method("clear", (in BuiltinCall c) =>
        {
            Items(c).Clear();
            return Value.None;
        }, Tiers.Lists, 0, 0, "list.clear()", "Remove every item."));
        Add(t, Method("copy", (in BuiltinCall c) =>
        {
            c.Vm.Charge(Items(c).Count / 8);
            return Value.List(new ListObj(Items(c)));
        }, Tiers.Lists, 0, 0, "list.copy()", "A shallow copy."));
        Add(t, Method("reverse", (in BuiltinCall c) =>
        {
            Items(c).Reverse();
            return Value.None;
        }, Tiers.Lists, 0, 0, "list.reverse()", "Reverse in place."));
        Add(t, new BuiltinDef
        {
            Name = "sort",
            Tier = Tiers.Lists,
            MinArgs = 0,
            MaxArgs = 0,
            Keywords = ["reverse"],
            Signature = "list.sort(reverse=False)",
            Doc = "Sort in place (stable).",
            Impl = SortImpl,
        });
        return t;
    }

    private static Value SortImpl(in BuiltinCall c)
    {
        {
            var items = Items(c);
            var line = c.Line;
            c.Vm.Charge(items.Count);
            var sorted = items.OrderBy(v => v, Comparer<Value>.Create((a, b) => Ops.Compare(a, b, OpCode.Lt, line))).ToList();
            if (c.Get("reverse", 0, Value.False).IsTruthy) sorted.Reverse();
            items.Clear();
            items.AddRange(sorted);
            return Value.None;
        }
    }

    private static string Self(in BuiltinCall c) => c.Self.AsStr;

    private static Dictionary<string, BuiltinDef> BuildStrMethods()
    {
        var t = new Dictionary<string, BuiltinDef>();
        Add(t, Method("upper", (in BuiltinCall c) => Value.Str(Self(c).ToUpperInvariant()), Tiers.Dicts, 0, 0, "str.upper()", "Upper-case copy."));
        Add(t, Method("lower", (in BuiltinCall c) => Value.Str(Self(c).ToLowerInvariant()), Tiers.Dicts, 0, 0, "str.lower()", "Lower-case copy."));
        Add(t, Method("strip", (in BuiltinCall c) => Value.Str(Self(c).Trim()), Tiers.Dicts, 0, 0, "str.strip()", "Copy without surrounding spaces."));
        Add(t, Method("startswith", (in BuiltinCall c) => Value.Bool(Self(c).StartsWith(c.Str(0, "startswith"), StringComparison.Ordinal)), Tiers.Dicts, 1, 1, "str.startswith(prefix)", "True if the string starts with prefix."));
        Add(t, Method("endswith", (in BuiltinCall c) => Value.Bool(Self(c).EndsWith(c.Str(0, "endswith"), StringComparison.Ordinal)), Tiers.Dicts, 1, 1, "str.endswith(suffix)", "True if the string ends with suffix."));
        Add(t, Method("find", (in BuiltinCall c) => Value.Int(Self(c).IndexOf(c.Str(0, "find"), StringComparison.Ordinal)), Tiers.Dicts, 1, 1, "str.find(sub)", "Position of sub, or -1."));
        Add(t, Method("replace", (in BuiltinCall c) =>
        {
            var old = c.Str(0, "replace");
            if (old.Length == 0) throw c.Error("replace() needs a non-empty string to find", GlyphErrorKind.Value);
            var self = Self(c);
            var with = c.Str(1, "replace");
            long hits = 0;
            for (var at = self.IndexOf(old, StringComparison.Ordinal); at >= 0; at = self.IndexOf(old, at + old.Length, StringComparison.Ordinal)) hits++;
            if (self.Length + hits * (with.Length - old.Length) > Vm.MaxString)
            {
                throw c.Error($"Strings are limited to {Vm.MaxString} characters", GlyphErrorKind.Limit);
            }
            return Value.Str(self.Replace(old, with, StringComparison.Ordinal));
        }, Tiers.Dicts, 2, 2, "str.replace(old, new)", "Copy with every old replaced by new."));
        Add(t, Method("split", (in BuiltinCall c) =>
        {
            var s = Self(c);
            string[] parts;
            if (c.Count == 0 || c.Arg(0).IsNone)
            {
                parts = s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            }
            else
            {
                var sep = c.Str(0, "split");
                if (sep.Length == 0) throw c.Error("split() separator cannot be empty", GlyphErrorKind.Value);
                parts = s.Split(sep);
            }
            c.Vm.Charge(parts.Length / 8);
            return Value.List(parts.Select(Value.Str));
        }, Tiers.Dicts, 0, 1, "str.split(sep=None)", "List of pieces."));
        Add(t, Method("join", (in BuiltinCall c) =>
        {
            var list = c.Arg(0);
            if (list.Kind != ValueKind.List) throw c.Error($"join() needs a list of strings, got {list.TypeName}");
            var parts = new List<string>();
            var sep = Self(c);
            long length = 0;
            foreach (var v in list.AsList.Items)
            {
                if (v.Kind != ValueKind.Str) throw c.Error($"join() needs strings, found a {v.TypeName}; use str() first");
                length += v.AsStr.Length + (parts.Count > 0 ? sep.Length : 0);
                if (length > Vm.MaxString) throw c.Error($"Strings are limited to {Vm.MaxString} characters", GlyphErrorKind.Limit);
                parts.Add(v.AsStr);
            }
            c.Vm.Charge(parts.Count / 8);
            return Value.Str(string.Join(sep, parts));
        }, Tiers.Dicts, 1, 1, "sep.join(list)", "Join strings with sep between them."));
        return t;
    }

    private static DictObj Dict(in BuiltinCall c) => c.Self.AsDict;

    private static Dictionary<string, BuiltinDef> BuildDictMethods()
    {
        var t = new Dictionary<string, BuiltinDef>();
        Add(t, Method("get", (in BuiltinCall c) =>
        {
            var key = c.Arg(0);
            return key.IsHashable && Dict(c).TryGet(key, out var v) ? v : c.Arg(1);
        }, Tiers.Dicts, 1, 2, "dict.get(key, default=None)", "Value for key, or default."));
        Add(t, Method("keys", (in BuiltinCall c) =>
        {
            var d = Dict(c);
            c.Vm.Charge(d.Count / 8);
            return Value.List(Enumerable.Range(0, d.Count).Select(d.KeyAt));
        }, Tiers.Dicts, 0, 0, "dict.keys()", "List of keys in insertion order."));
        Add(t, Method("values", (in BuiltinCall c) =>
        {
            var d = Dict(c);
            c.Vm.Charge(d.Count / 8);
            return Value.List(Enumerable.Range(0, d.Count).Select(d.ValueAt));
        }, Tiers.Dicts, 0, 0, "dict.values()", "List of values in insertion order."));
        Add(t, Method("items", (in BuiltinCall c) =>
        {
            var d = Dict(c);
            c.Vm.Charge(d.Count / 4);
            return Value.List(Enumerable.Range(0, d.Count).Select(i => Value.List([d.KeyAt(i), d.ValueAt(i)])));
        }, Tiers.Dicts, 0, 0, "dict.items()", "List of [key, value] pairs."));
        Add(t, Method("pop", (in BuiltinCall c) =>
        {
            var key = c.Arg(0);
            if (key.IsHashable && Dict(c).Remove(key, out var v)) return v;
            if (c.Count >= 2) return c.Arg(1);
            throw c.Error($"pop(): key {key.Repr()} is not in the dict", GlyphErrorKind.Value);
        }, Tiers.Dicts, 1, 2, "dict.pop(key, default)", "Remove key and return its value."));
        Add(t, Method("clear", (in BuiltinCall c) =>
        {
            Dict(c).Clear();
            return Value.None;
        }, Tiers.Dicts, 0, 0, "dict.clear()", "Remove every entry."));
        Add(t, Method("copy", (in BuiltinCall c) =>
        {
            var d = Dict(c);
            var copy = new DictObj();
            for (var i = 0; i < d.Count; i++) copy.Set(d.KeyAt(i), d.ValueAt(i));
            c.Vm.Charge(d.Count / 8);
            return Value.Dict(copy);
        }, Tiers.Dicts, 0, 0, "dict.copy()", "A shallow copy."));
        return t;
    }
}
