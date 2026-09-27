namespace Delvework.Core.Glyph;

public abstract record Node(int Line);

public abstract record Expr(int Line) : Node(Line);

public sealed record IntLit(long Value, int Line) : Expr(Line);
public sealed record FloatLit(Fix Value, int Line) : Expr(Line);
public sealed record StrLit(string Value, int Line) : Expr(Line);
public sealed record BoolLit(bool Value, int Line) : Expr(Line);
public sealed record NoneLit(int Line) : Expr(Line);
public sealed record NameExpr(string Name, int Line) : Expr(Line);
public sealed record ListExpr(IReadOnlyList<Expr> Items, int Line) : Expr(Line);
public sealed record DictExpr(IReadOnlyList<Expr> Keys, IReadOnlyList<Expr> Values, int Line) : Expr(Line);

/// <summary>Operator is one of <c>-</c>, <c>+</c>, <c>not</c>.</summary>
public sealed record UnaryExpr(string Op, Expr Operand, int Line) : Expr(Line);
public sealed record BinaryExpr(string Op, Expr Left, Expr Right, int Line) : Expr(Line);

/// <summary>A (possibly chained) comparison: <c>a &lt; b &lt;= c</c>.</summary>
public sealed record CompareExpr(IReadOnlyList<string> Ops, IReadOnlyList<Expr> Operands, int Line) : Expr(Line);

/// <summary>Short-circuit <c>and</c> / <c>or</c>.</summary>
public sealed record LogicExpr(string Op, Expr Left, Expr Right, int Line) : Expr(Line);
public sealed record KeywordArg(string Name, Expr Value);
public sealed record CallExpr(Expr Callee, IReadOnlyList<Expr> Args, IReadOnlyList<KeywordArg> Kwargs, int Line) : Expr(Line);
public sealed record IndexExpr(Expr Target, Expr Index, int Line) : Expr(Line);
public sealed record AttrExpr(Expr Target, string Name, int Line) : Expr(Line);

public abstract record Stmt(int Line) : Node(Line);

public sealed record ExprStmt(Expr Expr, int Line) : Stmt(Line);

/// <summary>Target is a <see cref="NameExpr"/> or an <see cref="IndexExpr"/>.</summary>
public sealed record AssignStmt(Expr Target, Expr Value, int Line) : Stmt(Line);
public sealed record AugAssignStmt(Expr Target, string Op, Expr Value, int Line) : Stmt(Line);
public sealed record IfStmt(Expr Test, IReadOnlyList<Stmt> Body, IReadOnlyList<Stmt> Else, int Line) : Stmt(Line);
public sealed record WhileStmt(Expr Test, IReadOnlyList<Stmt> Body, int Line) : Stmt(Line);
public sealed record ForStmt(string Name, Expr Iter, IReadOnlyList<Stmt> Body, int Line) : Stmt(Line);
public sealed record DefStmt(string Name, IReadOnlyList<string> Params, IReadOnlyList<Stmt> Body, int Line) : Stmt(Line);
public sealed record ReturnStmt(Expr? Value, int Line) : Stmt(Line);
public sealed record GlobalStmt(IReadOnlyList<string> Names, int Line) : Stmt(Line);
public sealed record BreakStmt(int Line) : Stmt(Line);
public sealed record ContinueStmt(int Line) : Stmt(Line);
public sealed record PassStmt(int Line) : Stmt(Line);

/// <summary><c>on see(enemy):</c> or <c>on signal "help"(data):</c>.</summary>
public sealed record OnStmt(string Event, string? Key, IReadOnlyList<string> Params, IReadOnlyList<Stmt> Body, int Line) : Stmt(Line);
public sealed record ImportStmt(string Module, int Line) : Stmt(Line);
