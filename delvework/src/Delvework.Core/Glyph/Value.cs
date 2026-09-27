using System.Globalization;
using System.Text;

namespace Delvework.Core.Glyph;

public enum ValueKind : byte
{
    /// <summary>Internal marker for a global that has not been assigned yet. Never visible to scripts.</summary>
    Undefined,
    None,
    Bool,
    Int,
    Float,
    Str,
    List,
    Dict,
    Function,
    Builtin,
    Enum,
    EnumType,
    Record,
    Iterator,
}

/// <summary>
/// A Glyph value: a small tagged struct. Numbers and booleans live inline; strings, lists,
/// dicts and other objects are references.
/// </summary>
public readonly struct Value : IEquatable<Value>
{
    public readonly ValueKind Kind;
    private readonly long _n;
    private readonly object? _o;

    private Value(ValueKind kind, long n, object? o)
    {
        Kind = kind;
        _n = n;
        _o = o;
    }

    public static readonly Value Undefined = default;
    public static readonly Value None = new(ValueKind.None, 0, null);
    public static readonly Value True = new(ValueKind.Bool, 1, null);
    public static readonly Value False = new(ValueKind.Bool, 0, null);

    public static Value Bool(bool b) => b ? True : False;
    public static Value Int(long n) => new(ValueKind.Int, n, null);
    public static Value Float(Fix f) => new(ValueKind.Float, f.Raw, null);
    public static Value Str(string s) => new(ValueKind.Str, 0, s);
    public static Value List(ListObj l) => new(ValueKind.List, 0, l);
    public static Value List(IEnumerable<Value> items) => new(ValueKind.List, 0, new ListObj(items));
    public static Value Dict(DictObj d) => new(ValueKind.Dict, 0, d);
    public static Value Function(FunctionObj f) => new(ValueKind.Function, 0, f);
    public static Value Builtin(BuiltinObj b) => new(ValueKind.Builtin, 0, b);
    public static Value Enum(EnumValue e) => new(ValueKind.Enum, 0, e);
    public static Value EnumType(EnumType t) => new(ValueKind.EnumType, 0, t);
    public static Value Record(RecordObj r) => new(ValueKind.Record, 0, r);
    public static Value Iterator(IteratorObj it) => new(ValueKind.Iterator, 0, it);

    public bool IsNone => Kind == ValueKind.None;
    public bool IsNumber => Kind is ValueKind.Int or ValueKind.Float;
    public bool AsBool => _n != 0;
    public long AsInt => _n;
    public Fix AsFix => Kind == ValueKind.Float ? Fix.FromRaw(_n) : Fix.FromInt(_n);
    public string AsStr => (string)_o!;
    public ListObj AsList => (ListObj)_o!;
    public DictObj AsDict => (DictObj)_o!;
    public FunctionObj AsFunction => (FunctionObj)_o!;
    public BuiltinObj AsBuiltin => (BuiltinObj)_o!;
    public EnumValue AsEnum => (EnumValue)_o!;
    public EnumType AsEnumType => (EnumType)_o!;
    public RecordObj AsRecord => (RecordObj)_o!;
    public IteratorObj AsIterator => (IteratorObj)_o!;
    internal object? Object => _o;

    public bool IsTruthy => Kind switch
    {
        ValueKind.None or ValueKind.Undefined => false,
        ValueKind.Bool or ValueKind.Int or ValueKind.Float => _n != 0,
        ValueKind.Str => AsStr.Length > 0,
        ValueKind.List => AsList.Items.Count > 0,
        ValueKind.Dict => AsDict.Count > 0,
        _ => true,
    };

    public string TypeName => Kind switch
    {
        ValueKind.Undefined => "undefined",
        ValueKind.None => "None",
        ValueKind.Bool => "bool",
        ValueKind.Int => "int",
        ValueKind.Float => "float",
        ValueKind.Str => "str",
        ValueKind.List => "list",
        ValueKind.Dict => "dict",
        ValueKind.Function => "function",
        ValueKind.Builtin => "builtin",
        ValueKind.Enum => AsEnum.Type.Name,
        ValueKind.EnumType => "enum",
        ValueKind.Record => AsRecord.Shape.Name,
        ValueKind.Iterator => "iterator",
        _ => "?",
    };

    public bool IsHashable => Kind is ValueKind.None or ValueKind.Bool or ValueKind.Int or ValueKind.Float
        or ValueKind.Str or ValueKind.Enum;

    /// <summary>Python-style equality: <c>1 == 1.0</c>; lists and dicts compare by contents.</summary>
    public bool Equals(Value other) => Equal(this, other, 0);

    private static bool Equal(Value a, Value b, int depth)
    {
        if (depth > 64) return false;
        if (a.IsNumber && b.IsNumber)
        {
            if (a.Kind == ValueKind.Int && b.Kind == ValueKind.Int) return a._n == b._n;
            return a.AsFixSafe(out var fa) && b.AsFixSafe(out var fb) && fa == fb;
        }
        if (a.Kind != b.Kind) return false;
        switch (a.Kind)
        {
            case ValueKind.None:
            case ValueKind.Undefined:
                return true;
            case ValueKind.Bool:
                return a._n == b._n;
            case ValueKind.Str:
                return string.Equals(a.AsStr, b.AsStr, StringComparison.Ordinal);
            case ValueKind.List:
            {
                var x = a.AsList.Items;
                var y = b.AsList.Items;
                if (ReferenceEquals(x, y)) return true;
                if (x.Count != y.Count) return false;
                for (var i = 0; i < x.Count; i++)
                {
                    if (!Equal(x[i], y[i], depth + 1)) return false;
                }
                return true;
            }
            case ValueKind.Dict:
            {
                var x = a.AsDict;
                var y = b.AsDict;
                if (ReferenceEquals(x, y)) return true;
                if (x.Count != y.Count) return false;
                for (var i = 0; i < x.Count; i++)
                {
                    if (!y.TryGet(x.KeyAt(i), out var v) || !Equal(x.ValueAt(i), v, depth + 1)) return false;
                }
                return true;
            }
            case ValueKind.Record:
            {
                var x = a.AsRecord;
                var y = b.AsRecord;
                if (ReferenceEquals(x, y)) return true;
                if (x.Shape != y.Shape) return false;
                for (var i = 0; i < x.Values.Length; i++)
                {
                    if (!Equal(x.Values[i], y.Values[i], depth + 1)) return false;
                }
                return true;
            }
            default:
                return ReferenceEquals(a._o, b._o);
        }
    }

    private bool AsFixSafe(out Fix f)
    {
        if (Kind == ValueKind.Float)
        {
            f = Fix.FromRaw(_n);
            return true;
        }
        if (_n > long.MaxValue >> Fix.FracBits || _n < long.MinValue >> Fix.FracBits)
        {
            f = Fix.Zero;
            return false;
        }
        f = Fix.FromInt(_n);
        return true;
    }

    public override bool Equals(object? obj) => obj is Value v && Equals(v);

    public override int GetHashCode() => Kind switch
    {
        ValueKind.Int => Fix.FromRaw(unchecked(_n << Fix.FracBits)).GetHashCode(),
        ValueKind.Float => _n.GetHashCode(),
        ValueKind.Str => StringComparer.Ordinal.GetHashCode(AsStr),
        ValueKind.Bool => _n.GetHashCode() ^ 0x5bd1e995,
        ValueKind.None => 0x1f351,
        ValueKind.Enum => AsEnum.GetHashCode(),
        _ => 0,
    };

    public static bool operator ==(Value a, Value b) => a.Equals(b);
    public static bool operator !=(Value a, Value b) => !a.Equals(b);

    /// <summary>A deterministic hash (unlike <see cref="GetHashCode"/>) for replay verification.</summary>
    public ulong StableHash(ulong h = Fnv.Offset) => StableHash(this, h, 0);

    private static ulong StableHash(Value v, ulong h, int depth)
    {
        h = Fnv.Mix(h, (ulong)v.Kind);
        if (depth > 16) return h;
        switch (v.Kind)
        {
            case ValueKind.Bool:
            case ValueKind.Int:
            case ValueKind.Float:
                return Fnv.Mix(h, unchecked((ulong)v._n));
            case ValueKind.Str:
                return Fnv.Mix(h, v.AsStr);
            case ValueKind.List:
                foreach (var item in v.AsList.Items) h = StableHash(item, h, depth + 1);
                return h;
            case ValueKind.Dict:
            {
                var d = v.AsDict;
                for (var i = 0; i < d.Count; i++)
                {
                    h = StableHash(d.KeyAt(i), h, depth + 1);
                    h = StableHash(d.ValueAt(i), h, depth + 1);
                }
                return h;
            }
            case ValueKind.Enum:
                return Fnv.Mix(Fnv.Mix(h, v.AsEnum.Type.Name), (ulong)v.AsEnum.Ordinal);
            case ValueKind.Record:
                h = Fnv.Mix(h, v.AsRecord.Shape.Name);
                foreach (var item in v.AsRecord.Values) h = StableHash(item, h, depth + 1);
                return h;
            case ValueKind.Function:
                return Fnv.Mix(h, v.AsFunction.Proto.Name);
            case ValueKind.Builtin:
                return Fnv.Mix(h, v.AsBuiltin.Def.Name);
            case ValueKind.EnumType:
                return Fnv.Mix(h, v.AsEnumType.Name);
            case ValueKind.Iterator:
                return Fnv.Mix(h, (ulong)v.AsIterator.Index);
            default:
                return h;
        }
    }

    /// <summary>The <c>str()</c> form: strings print without quotes at the top level.</summary>
    public override string ToString() => Format(this, false, 0);

    /// <summary>The <c>repr()</c> form used inside containers: strings are quoted.</summary>
    public string Repr() => Format(this, true, 0);

    private static string Format(Value v, bool nested, int depth)
    {
        if (depth > 8) return "...";
        switch (v.Kind)
        {
            case ValueKind.Undefined: return "<undefined>";
            case ValueKind.None: return "None";
            case ValueKind.Bool: return v.AsBool ? "True" : "False";
            case ValueKind.Int: return v._n.ToString(CultureInfo.InvariantCulture);
            case ValueKind.Float: return Fix.FromRaw(v._n).ToString();
            case ValueKind.Str: return nested ? "'" + v.AsStr.Replace("'", "\\'", StringComparison.Ordinal) + "'" : v.AsStr;
            case ValueKind.List:
            {
                var items = v.AsList.Items;
                var sb = new StringBuilder("[");
                for (var i = 0; i < items.Count && sb.Length < 2000; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Format(items[i], true, depth + 1));
                }
                return sb.Append(']').ToString();
            }
            case ValueKind.Dict:
            {
                var d = v.AsDict;
                var sb = new StringBuilder("{");
                for (var i = 0; i < d.Count && sb.Length < 2000; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(Format(d.KeyAt(i), true, depth + 1)).Append(": ").Append(Format(d.ValueAt(i), true, depth + 1));
                }
                return sb.Append('}').ToString();
            }
            case ValueKind.Function: return $"<function {v.AsFunction.Proto.Name}>";
            case ValueKind.Builtin: return $"<builtin {v.AsBuiltin.Def.Name}>";
            case ValueKind.Enum: return $"{v.AsEnum.Type.Name}.{v.AsEnum.Name}";
            case ValueKind.EnumType: return v.AsEnumType.Name;
            case ValueKind.Record:
            {
                var r = v.AsRecord;
                var sb = new StringBuilder(r.Shape.Name).Append('(');
                for (var i = 0; i < r.Values.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(r.Shape.Fields[i]).Append('=').Append(Format(r.Values[i], true, depth + 1));
                }
                return sb.Append(')').ToString();
            }
            case ValueKind.Iterator: return "<iterator>";
            default: return "?";
        }
    }
}

/// <summary>FNV-1a 64-bit, used for stable state hashes.</summary>
public static class Fnv
{
    public const ulong Offset = 14695981039346656037;
    private const ulong Prime = 1099511628211;

    public static ulong Mix(ulong h, ulong v)
    {
        for (var i = 0; i < 8; i++)
        {
            h ^= (v >> (i * 8)) & 0xff;
            h *= Prime;
        }
        return h;
    }

    public static ulong Mix(ulong h, long v) => Mix(h, unchecked((ulong)v));

    public static ulong Mix(ulong h, string s)
    {
        foreach (var c in s)
        {
            h ^= c;
            h *= Prime;
        }
        return Mix(h, (ulong)s.Length);
    }
}

public sealed class ListObj
{
    public ListObj() => Items = [];
    public ListObj(IEnumerable<Value> items) => Items = new List<Value>(items);
    public List<Value> Items { get; }
}

/// <summary>An insertion-ordered dict. Iteration order never depends on hash codes.</summary>
public sealed class DictObj
{
    private readonly List<Value> _keys = [];
    private readonly List<Value> _values = [];
    private readonly Dictionary<Value, int> _index = [];

    public int Count => _keys.Count;
    public Value KeyAt(int i) => _keys[i];
    public Value ValueAt(int i) => _values[i];

    public bool TryGet(Value key, out Value value)
    {
        if (_index.TryGetValue(key, out var i))
        {
            value = _values[i];
            return true;
        }
        value = Value.None;
        return false;
    }

    public void Set(Value key, Value value)
    {
        if (_index.TryGetValue(key, out var i))
        {
            _values[i] = value;
            return;
        }
        _index[key] = _keys.Count;
        _keys.Add(key);
        _values.Add(value);
    }

    public bool Remove(Value key, out Value value)
    {
        if (!_index.TryGetValue(key, out var i))
        {
            value = Value.None;
            return false;
        }
        value = _values[i];
        _keys.RemoveAt(i);
        _values.RemoveAt(i);
        _index.Clear();
        for (var k = 0; k < _keys.Count; k++) _index[_keys[k]] = k;
        return true;
    }

    public bool ContainsKey(Value key) => _index.ContainsKey(key);

    public void Clear()
    {
        _keys.Clear();
        _values.Clear();
        _index.Clear();
    }
}

public sealed class EnumType
{
    private readonly Dictionary<string, EnumValue> _members = [];

    public EnumType(string name, IEnumerable<string> members)
    {
        Name = name;
        var i = 0;
        foreach (var m in members) _members[m] = new EnumValue(this, m, i++);
        Members = _members.Values.OrderBy(v => v.Ordinal).ToArray();
    }

    public string Name { get; }
    public IReadOnlyList<EnumValue> Members { get; }
    public EnumValue this[string name] => _members[name];
    public bool TryGet(string name, out EnumValue value) => _members.TryGetValue(name, out value!);
}

public sealed class EnumValue
{
    internal EnumValue(EnumType type, string name, int ordinal)
    {
        Type = type;
        Name = name;
        Ordinal = ordinal;
    }

    public EnumType Type { get; }
    public string Name { get; }
    public int Ordinal { get; }
    public Value AsValue() => Value.Enum(this);
    public override int GetHashCode() => HashCode.Combine(Type.Name, Ordinal);
}

/// <summary>Field layout shared by all records of one kind (e.g. every enemy view).</summary>
public sealed class RecordShape
{
    private readonly Dictionary<string, int> _index;

    public RecordShape(string name, params string[] fields)
    {
        Name = name;
        Fields = fields;
        _index = fields.Select((f, i) => (f, i)).ToDictionary(p => p.f, p => p.i);
    }

    public string Name { get; }
    public IReadOnlyList<string> Fields { get; }
    public bool TryIndex(string field, out int index) => _index.TryGetValue(field, out index);
}

/// <summary>A read-only snapshot passed to scripts, such as an enemy or a tile.</summary>
public sealed class RecordObj
{
    public RecordObj(RecordShape shape, params Value[] values)
    {
        if (values.Length != shape.Fields.Count) throw new ArgumentException("Field count mismatch", nameof(values));
        Shape = shape;
        Values = values;
    }

    public RecordShape Shape { get; }
    public Value[] Values { get; }

    public Value Get(string field) => Shape.TryIndex(field, out var i) ? Values[i] : Value.None;
}

public sealed class FunctionObj(FunctionProto proto)
{
    public FunctionProto Proto { get; } = proto;
}

/// <summary>A builtin, optionally bound to a receiver (for methods such as <c>list.append</c>).</summary>
public sealed class BuiltinObj(BuiltinDef def, Value self)
{
    public BuiltinDef Def { get; } = def;
    public Value Self { get; } = self;
}

/// <summary>Iterates over a snapshot of a list, so mutating the list inside the loop is safe.</summary>
public sealed class IteratorObj(Value[] items)
{
    public Value[] Items { get; } = items;
    public int Index { get; set; }
}
