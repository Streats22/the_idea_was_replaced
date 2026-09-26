import type { Expr, Stmt, Target } from "./ast";
import { ScriptError, tokenize, type Token } from "./lexer";

const AUG_OPS = ["+=", "-=", "*=", "/=", "%=", "//=", "**="];
const COMPARE_OPS = ["==", "!=", "<", ">", "<=", ">="];

export function parse(source: string): Stmt[] {
  return new Parser(tokenize(source)).program();
}

class Parser {
  private pos = 0;
  constructor(private tokens: Token[]) {}

  private peek(offset = 0): Token {
    return this.tokens[Math.min(this.pos + offset, this.tokens.length - 1)];
  }

  private next(): Token {
    return this.tokens[this.pos++];
  }

  private is(type: Token["type"], value?: string): boolean {
    const t = this.peek();
    return t.type === type && (value === undefined || t.value === value);
  }

  private accept(type: Token["type"], value?: string): Token | null {
    return this.is(type, value) ? this.next() : null;
  }

  private expect(type: Token["type"], value?: string): Token {
    const t = this.peek();
    if (!this.is(type, value)) {
      const want = value ? `'${value}'` : type.toLowerCase();
      const got = t.type === "NEWLINE" ? "end of line" : t.type === "EOF" ? "end of file" : `'${t.value}'`;
      throw new ScriptError(`Expected ${want} but found ${got}`, t.line);
    }
    return this.next();
  }

  program(): Stmt[] {
    const body: Stmt[] = [];
    while (!this.is("EOF")) body.push(...this.statement());
    return body;
  }

  private block(): Stmt[] {
    this.expect("OP", ":");
    if (!this.accept("NEWLINE")) return this.simpleLine();
    this.expect("INDENT");
    const body: Stmt[] = [];
    while (!this.accept("DEDENT")) {
      if (this.is("EOF")) break;
      body.push(...this.statement());
    }
    return body;
  }

  private statement(): Stmt[] {
    const t = this.peek();
    if (t.type === "KEYWORD") {
      switch (t.value) {
        case "if": return [this.ifStmt()];
        case "while": {
          this.next();
          const test = this.expr();
          return [{ kind: "while", test, body: this.block(), line: t.line }];
        }
        case "for": {
          this.next();
          const name = this.expect("NAME").value;
          this.expect("KEYWORD", "in");
          const iter = this.expr();
          return [{ kind: "for", name, iter, body: this.block(), line: t.line }];
        }
        case "def": {
          this.next();
          const name = this.expect("NAME").value;
          this.expect("OP", "(");
          const params: string[] = [];
          while (!this.is("OP", ")")) {
            params.push(this.expect("NAME").value);
            if (!this.accept("OP", ",")) break;
          }
          this.expect("OP", ")");
          return [{ kind: "def", name, params, body: this.block(), line: t.line }];
        }
      }
    }
    if (t.type === "INDENT") throw new ScriptError("Unexpected indentation", t.line);
    return this.simpleLine();
  }

  private ifStmt(): Stmt {
    const t = this.next(); // 'if' or 'elif'
    const test = this.expr();
    const body = this.block();
    let orelse: Stmt[] = [];
    if (this.is("KEYWORD", "elif")) {
      orelse = [this.ifStmt()];
    } else if (this.accept("KEYWORD", "else")) {
      orelse = this.block();
    }
    return { kind: "if", test, body, orelse, line: t.line };
  }

  private simpleLine(): Stmt[] {
    const stmt = this.simpleStatement();
    if (!this.is("EOF")) this.expect("NEWLINE");
    return [stmt];
  }

  private simpleStatement(): Stmt {
    const t = this.peek();
    if (t.type === "KEYWORD") {
      switch (t.value) {
        case "pass": this.next(); return { kind: "pass", line: t.line };
        case "break": this.next(); return { kind: "break", line: t.line };
        case "continue": this.next(); return { kind: "continue", line: t.line };
        case "return": {
          this.next();
          const value = this.is("NEWLINE") || this.is("EOF") ? null : this.expr();
          return { kind: "return", value, line: t.line };
        }
        case "global": {
          this.next();
          const names = [this.expect("NAME").value];
          while (this.accept("OP", ",")) names.push(this.expect("NAME").value);
          return { kind: "global", names, line: t.line };
        }
      }
    }

    const expr = this.expr();
    if (this.accept("OP", "=")) {
      return { kind: "assign", target: this.toTarget(expr), value: this.expr(), line: t.line };
    }
    const aug = this.peek();
    if (aug.type === "OP" && AUG_OPS.includes(aug.value)) {
      this.next();
      const op = aug.value.slice(0, -1);
      return { kind: "augassign", target: this.toTarget(expr), op, value: this.expr(), line: t.line };
    }
    return { kind: "expr", expr, line: t.line };
  }

  private toTarget(e: Expr): Target {
    if (e.kind === "name" || e.kind === "index") return e;
    throw new ScriptError("Can only assign to a variable or list item", e.line);
  }

  private expr(): Expr {
    return this.orExpr();
  }

  private orExpr(): Expr {
    let left = this.andExpr();
    while (this.is("KEYWORD", "or")) {
      const line = this.next().line;
      left = { kind: "logic", op: "or", left, right: this.andExpr(), line };
    }
    return left;
  }

  private andExpr(): Expr {
    let left = this.notExpr();
    while (this.is("KEYWORD", "and")) {
      const line = this.next().line;
      left = { kind: "logic", op: "and", left, right: this.notExpr(), line };
    }
    return left;
  }

  private notExpr(): Expr {
    if (this.is("KEYWORD", "not")) {
      const line = this.next().line;
      return { kind: "unary", op: "not", operand: this.notExpr(), line };
    }
    return this.comparison();
  }

  private comparison(): Expr {
    const first = this.arith();
    const ops: string[] = [];
    const operands: Expr[] = [first];
    for (;;) {
      const t = this.peek();
      if (t.type === "OP" && COMPARE_OPS.includes(t.value)) {
        this.next();
        ops.push(t.value);
      } else if (this.is("KEYWORD", "in")) {
        this.next();
        ops.push("in");
      } else if (this.is("KEYWORD", "not") && this.peek(1).type === "KEYWORD" && this.peek(1).value === "in") {
        this.next();
        this.next();
        ops.push("not in");
      } else {
        break;
      }
      operands.push(this.arith());
    }
    return ops.length ? { kind: "compare", ops, operands, line: first.line } : first;
  }

  private arith(): Expr {
    let left = this.term();
    while (this.is("OP", "+") || this.is("OP", "-")) {
      const t = this.next();
      left = { kind: "binary", op: t.value, left, right: this.term(), line: t.line };
    }
    return left;
  }

  private term(): Expr {
    let left = this.unary();
    while (["*", "/", "//", "%"].some((o) => this.is("OP", o))) {
      const t = this.next();
      left = { kind: "binary", op: t.value, left, right: this.unary(), line: t.line };
    }
    return left;
  }

  private unary(): Expr {
    if (this.is("OP", "-") || this.is("OP", "+")) {
      const t = this.next();
      return { kind: "unary", op: t.value as "-" | "+", operand: this.unary(), line: t.line };
    }
    return this.power();
  }

  private power(): Expr {
    const base = this.postfix();
    if (this.is("OP", "**")) {
      const t = this.next();
      return { kind: "binary", op: "**", left: base, right: this.unary(), line: t.line };
    }
    return base;
  }

  private postfix(): Expr {
    let e = this.atom();
    for (;;) {
      if (this.accept("OP", "(")) {
        const args: Expr[] = [];
        while (!this.is("OP", ")")) {
          args.push(this.expr());
          if (!this.accept("OP", ",")) break;
        }
        this.expect("OP", ")");
        e = { kind: "call", callee: e, args, line: e.line };
      } else if (this.accept("OP", "[")) {
        const index = this.expr();
        this.expect("OP", "]");
        e = { kind: "index", target: e, index, line: e.line };
      } else if (this.accept("OP", ".")) {
        const name = this.expect("NAME").value;
        e = { kind: "attr", target: e, name, line: e.line };
      } else {
        return e;
      }
    }
  }

  private atom(): Expr {
    const t = this.next();
    switch (t.type) {
      case "NUMBER": return { kind: "num", value: Number(t.value), line: t.line };
      case "STRING": return { kind: "str", value: t.value, line: t.line };
      case "NAME": return { kind: "name", name: t.value, line: t.line };
      case "KEYWORD":
        if (t.value === "True") return { kind: "const", value: true, line: t.line };
        if (t.value === "False") return { kind: "const", value: false, line: t.line };
        if (t.value === "None") return { kind: "const", value: null, line: t.line };
        break;
      case "OP":
        if (t.value === "(") {
          const e = this.expr();
          this.expect("OP", ")");
          return e;
        }
        if (t.value === "[") {
          const items: Expr[] = [];
          while (!this.is("OP", "]")) {
            items.push(this.expr());
            if (!this.accept("OP", ",")) break;
          }
          this.expect("OP", "]");
          return { kind: "list", items, line: t.line };
        }
    }
    const shown = t.type === "NEWLINE" ? "end of line" : t.type === "EOF" ? "end of file" : `'${t.value}'`;
    throw new ScriptError(`Unexpected ${shown}`, t.line);
  }
}
