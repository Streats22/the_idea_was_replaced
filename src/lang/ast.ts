export type Expr =
  | { kind: "num"; value: number; line: number }
  | { kind: "str"; value: string; line: number }
  | { kind: "const"; value: boolean | null; line: number }
  | { kind: "name"; name: string; line: number }
  | { kind: "list"; items: Expr[]; line: number }
  | { kind: "unary"; op: "-" | "+" | "not"; operand: Expr; line: number }
  | { kind: "binary"; op: string; left: Expr; right: Expr; line: number }
  | { kind: "compare"; ops: string[]; operands: Expr[]; line: number }
  | { kind: "logic"; op: "and" | "or"; left: Expr; right: Expr; line: number }
  | { kind: "call"; callee: Expr; args: Expr[]; line: number }
  | { kind: "index"; target: Expr; index: Expr; line: number }
  | { kind: "attr"; target: Expr; name: string; line: number };

export type Target =
  | { kind: "name"; name: string; line: number }
  | { kind: "index"; target: Expr; index: Expr; line: number };

export type Stmt =
  | { kind: "expr"; expr: Expr; line: number }
  | { kind: "assign"; target: Target; value: Expr; line: number }
  | { kind: "augassign"; target: Target; op: string; value: Expr; line: number }
  | { kind: "if"; test: Expr; body: Stmt[]; orelse: Stmt[]; line: number }
  | { kind: "while"; test: Expr; body: Stmt[]; line: number }
  | { kind: "for"; name: string; iter: Expr; body: Stmt[]; line: number }
  | { kind: "def"; name: string; params: string[]; body: Stmt[]; line: number }
  | { kind: "return"; value: Expr | null; line: number }
  | { kind: "global"; names: string[]; line: number }
  | { kind: "break"; line: number }
  | { kind: "continue"; line: number }
  | { kind: "pass"; line: number };
