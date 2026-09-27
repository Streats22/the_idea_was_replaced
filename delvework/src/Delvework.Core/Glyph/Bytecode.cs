namespace Delvework.Core.Glyph;

public enum OpCode : byte
{
    Nop,
    Const,
    PushNone,
    PushTrue,
    PushFalse,
    Pop,
    Dup,
    /// <summary>[a, b] becomes [a, b, a, b].</summary>
    Dup2,
    Swap,
    /// <summary>Move the top value under the next two: [a, b, c] becomes [c, a, b].</summary>
    Rot3,
    LoadLocal,
    StoreLocal,
    LoadGlobal,
    StoreGlobal,
    Add,
    Sub,
    Mul,
    Div,
    FloorDiv,
    Mod,
    Pow,
    Neg,
    Pos,
    Not,
    Eq,
    Ne,
    Lt,
    Gt,
    Le,
    Ge,
    In,
    NotIn,
    Jump,
    /// <summary>Pop; jump if falsy.</summary>
    JumpIfFalse,
    /// <summary>If the top is falsy jump and keep it, otherwise pop it and fall through.</summary>
    JumpIfFalseKeep,
    /// <summary>If the top is truthy jump and keep it, otherwise pop it and fall through.</summary>
    JumpIfTrueKeep,
    BuildList,
    BuildDict,
    Index,
    /// <summary>Stack: value, target, index.</summary>
    StoreIndex,
    Attr,
    Call,
    /// <summary>A = total argument count, B = constant index of the keyword-name list.</summary>
    CallKw,
    Return,
    GetIter,
    /// <summary>Push the next item, or pop the iterator and jump to A when exhausted.</summary>
    ForIter,
}

public readonly record struct Instr(OpCode Op, int A = 0, int B = 0);

public sealed class FunctionProto
{
    public required string Name { get; init; }
    public required int Arity { get; init; }
    public required string[] LocalNames { get; init; }
    public required Instr[] Code { get; init; }
    public required int[] Lines { get; init; }
    public required Value[] Constants { get; init; }
    public required int MaxStack { get; init; }
    public required int FirstLine { get; init; }
    /// <summary>True where a statement (or loop iteration) begins: the only places handlers may interrupt.</summary>
    public required bool[] StatementStarts { get; init; }
    public int LocalCount => LocalNames.Length;
}

public sealed record HandlerDef(string Event, string? Key, FunctionProto Proto);

/// <summary>A compiled Glyph program. Immutable and shareable between many <see cref="Vm"/>s.</summary>
public sealed class GlyphProgram
{
    public required FunctionProto Main { get; init; }
    public required IReadOnlyList<FunctionProto> Functions { get; init; }
    public required IReadOnlyList<HandlerDef> Handlers { get; init; }
    /// <summary>Names of every global slot: environment names first, then the program's own.</summary>
    public required string[] GlobalNames { get; init; }
    public required GlyphEnvironment Environment { get; init; }
    public required int Tier { get; init; }
    public required ulong SourceHash { get; init; }

    public bool Listens(string evt) => Handlers.Any(h => h.Event == evt);
}
