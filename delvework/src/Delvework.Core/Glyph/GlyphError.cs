namespace Delvework.Core.Glyph;

public enum GlyphErrorKind
{
    Syntax,
    Name,
    Capability,
    Type,
    Value,
    Limit,
}

/// <summary>
/// A problem in a player's program, reported with a line number and a friendly message.
/// Compile errors are thrown from <see cref="GlyphCompiler"/>; runtime errors are reported
/// through <see cref="Vm.Error"/> and never escape <see cref="Vm.Step"/>.
/// </summary>
public sealed class GlyphError : Exception
{
    public GlyphError(GlyphErrorKind kind, string message, int line, int column = 0)
        : base(message)
    {
        Kind = kind;
        Line = line;
        Column = column;
    }

    public GlyphErrorKind Kind { get; }
    public int Line { get; }
    public int Column { get; }

    public override string ToString() => $"Line {Line}: {Message}";
}
