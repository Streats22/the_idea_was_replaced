import type { Expr, Stmt, Target } from "./ast";
import { ScriptError } from "./lexer";

export interface EnumValue {
  type: "enum";
  group: string;
  name: string;
}

export interface Namespace {
  type: "namespace";
  name: string;
  members: Record<string, Value>;
}

export interface UserFunction {
  type: "function";
  name: string;
  params: string[];
  body: Stmt[];
  closure: Scope;
}

/** A builtin yields the game-time cost (in seconds) of each action it performs. */
export type BuiltinImpl = (args: Value[], line: number) => Generator<number, Value, void>;

export interface Builtin {
  type: "builtin";
  name: string;
  call: BuiltinImpl;
}

export type Value = number | string | boolean | null | Value[] | EnumValue | Namespace | UserFunction | Builtin;

export class Scope {
  vars = new Map<string, Value>();
  globalNames = new Set<string>();
  constructor(public parent: Scope | null) {}

  lookup(name: string): Value | undefined {
    for (let s: Scope | null = this; s; s = s.parent) {
      if (s.vars.has(name)) return s.vars.get(name);
    }
    return undefined;
  }
}

class BreakSignal {}
class ContinueSignal {}
class ReturnSignal {
  constructor(public value: Value) {}
}

export const OP_COST = 0.0005;
const MAX_DEPTH = 150;

export function builtin(name: string, fn: (args: Value[], line: number) => Value): Builtin {
  return {
    type: "builtin",
    name,
    // eslint-disable-next-line require-yield
    call: function* (args, line) {
      return fn(args, line);
    },
  };
}

export function makeEnum(group: string, names: string[]): Namespace {
  const members: Record<string, Value> = {};
  for (const name of names) members[name] = { type: "enum", group, name };
  return { type: "namespace", name: group, members };
}

export function typeName(v: Value): string {
  if (v === null) return "None";
  if (typeof v === "number") return Number.isInteger(v) ? "int" : "float";
  if (typeof v === "string") return "str";
  if (typeof v === "boolean") return "bool";
  if (Array.isArray(v)) return "list";
  return v.type;
}

export function repr(v: Value, nested = false): string {
  if (v === null) return "None";
  if (v === true) return "True";
  if (v === false) return "False";
  if (typeof v === "number") {
    if (Number.isInteger(v)) return String(v);
    return String(Math.round(v * 1e6) / 1e6);
  }
  if (typeof v === "string") return nested ? `'${v}'` : v;
  if (Array.isArray(v)) return `[${v.map((x) => repr(x, true)).join(", ")}]`;
  switch (v.type) {
    case "enum": return `${v.group}.${v.name}`;
    case "namespace": return v.name;
    case "function": return `<function ${v.name}>`;
    case "builtin": return `<builtin ${v.name}>`;
  }
}

export function truthy(v: Value): boolean {
  if (v === null || v === false || v === 0 || v === "") return false;
  if (Array.isArray(v)) return v.length > 0;
  return true;
}

export function equals(a: Value, b: Value): boolean {
  if (Array.isArray(a) && Array.isArray(b)) {
    return a.length === b.length && a.every((x, i) => equals(x, b[i]));
  }
  return a === b;
}

export class Interpreter {
  readonly globals: Scope;
  currentLine = 0;
  private depth = 0;

  constructor(private program: Stmt[], builtins: Record<string, Value>) {
    const builtinScope = new Scope(null);
    for (const [k, v] of Object.entries(builtins)) builtinScope.vars.set(k, v);
    this.globals = new Scope(builtinScope);
  }

  *run(): Generator<number, void, void> {
    try {
      yield* this.execBlock(this.program, this.globals);
    } catch (e) {
      if (e instanceof BreakSignal || e instanceof ContinueSignal) {
        throw new ScriptError("'break' or 'continue' outside of a loop", this.currentLine);
      }
      if (e instanceof ReturnSignal) {
        throw new ScriptError("'return' outside of a function", this.currentLine);
      }
      throw e;
    }
  }

  private *execBlock(body: Stmt[], scope: Scope): Generator<number, void, void> {
    for (const stmt of body) yield* this.exec(stmt, scope);
  }

  private *exec(s: Stmt, scope: Scope): Generator<number, void, void> {
    this.currentLine = s.line;
    yield OP_COST;
    switch (s.kind) {
      case "expr":
        yield* this.eval(s.expr, scope);
        return;
      case "assign": {
        const value = yield* this.eval(s.value, scope);
        yield* this.assign(s.target, value, scope);
        return;
      }
      case "augassign": {
        const current = yield* this.eval(s.target, scope);
        const rhs = yield* this.eval(s.value, scope);
        yield* this.assign(s.target, this.binary(s.op, current, rhs, s.line), scope);
        return;
      }
      case "if":
        if (truthy(yield* this.eval(s.test, scope))) yield* this.execBlock(s.body, scope);
        else yield* this.execBlock(s.orelse, scope);
        return;
      case "while":
        while (truthy(yield* this.eval(s.test, scope))) {
          try {
            yield* this.execBlock(s.body, scope);
          } catch (e) {
            if (e instanceof BreakSignal) break;
            if (e instanceof ContinueSignal) continue;
            throw e;
          }
          this.currentLine = s.line;
          yield OP_COST;
        }
        return;
      case "for": {
        const iter = yield* this.eval(s.iter, scope);
        let items: Value[];
        if (Array.isArray(iter)) items = [...iter];
        else if (typeof iter === "string") items = [...iter];
        else throw new ScriptError(`Cannot loop over a ${typeName(iter)}`, s.line);
        for (const item of items) {
          this.setVar(s.name, item, scope);
          try {
            yield* this.execBlock(s.body, scope);
          } catch (e) {
            if (e instanceof BreakSignal) break;
            if (e instanceof ContinueSignal) continue;
            throw e;
          }
          this.currentLine = s.line;
          yield OP_COST;
        }
        return;
      }
      case "def":
        this.setVar(s.name, { type: "function", name: s.name, params: s.params, body: s.body, closure: scope }, scope);
        return;
      case "return":
        throw new ReturnSignal(s.value ? yield* this.eval(s.value, scope) : null);
      case "global":
        for (const n of s.names) scope.globalNames.add(n);
        return;
      case "break":
        throw new BreakSignal();
      case "continue":
        throw new ContinueSignal();
      case "pass":
        return;
    }
  }

  private setVar(name: string, value: Value, scope: Scope) {
    if (scope.globalNames.has(name)) this.globals.vars.set(name, value);
    else scope.vars.set(name, value);
  }

  private *assign(t: Target, value: Value, scope: Scope): Generator<number, void, void> {
    if (t.kind === "name") {
      this.setVar(t.name, value, scope);
      return;
    }
    const target = yield* this.eval(t.target, scope);
    const index = yield* this.eval(t.index, scope);
    if (!Array.isArray(target)) throw new ScriptError(`Cannot assign items of a ${typeName(target)}`, t.line);
    target[this.listIndex(target, index, t.line)] = value;
  }

  private listIndex(list: Value[] | string, index: Value, line: number): number {
    if (typeof index !== "number" || !Number.isInteger(index)) {
      throw new ScriptError("List index must be an integer", line);
    }
    const i = index < 0 ? list.length + index : index;
    if (i < 0 || i >= list.length) throw new ScriptError(`Index ${index} out of range`, line);
    return i;
  }

  private *eval(e: Expr, scope: Scope): Generator<number, Value, void> {
    switch (e.kind) {
      case "num": return e.value;
      case "str": return e.value;
      case "const": return e.value;
      case "name": {
        const scopeToRead = scope.globalNames.has(e.name) ? this.globals : scope;
        const v = scopeToRead.lookup(e.name);
        if (v === undefined) throw new ScriptError(`Name '${e.name}' is not defined`, e.line);
        return v;
      }
      case "list": {
        const out: Value[] = [];
        for (const item of e.items) out.push(yield* this.eval(item, scope));
        return out;
      }
      case "unary": {
        const v = yield* this.eval(e.operand, scope);
        if (e.op === "not") return !truthy(v);
        if (typeof v !== "number") throw new ScriptError(`Bad operand for unary ${e.op}: ${typeName(v)}`, e.line);
        return e.op === "-" ? -v : v;
      }
      case "binary": {
        const l = yield* this.eval(e.left, scope);
        const r = yield* this.eval(e.right, scope);
        return this.binary(e.op, l, r, e.line);
      }
      case "logic": {
        const l = yield* this.eval(e.left, scope);
        if (e.op === "and") return truthy(l) ? yield* this.eval(e.right, scope) : l;
        return truthy(l) ? l : yield* this.eval(e.right, scope);
      }
      case "compare": {
        let left = yield* this.eval(e.operands[0], scope);
        for (let i = 0; i < e.ops.length; i++) {
          const right = yield* this.eval(e.operands[i + 1], scope);
          if (!this.compare(e.ops[i], left, right, e.line)) return false;
          left = right;
        }
        return true;
      }
      case "index": {
        const target = yield* this.eval(e.target, scope);
        const index = yield* this.eval(e.index, scope);
        if (Array.isArray(target) || typeof target === "string") {
          return target[this.listIndex(target, index, e.line)];
        }
        throw new ScriptError(`A ${typeName(target)} cannot be indexed`, e.line);
      }
      case "attr": {
        const target = yield* this.eval(e.target, scope);
        return this.attribute(target, e.name, e.line);
      }
      case "call": {
        const callee = yield* this.eval(e.callee, scope);
        const args: Value[] = [];
        for (const a of e.args) args.push(yield* this.eval(a, scope));
        return yield* this.call(callee, args, e.line);
      }
    }
  }

  private attribute(target: Value, name: string, line: number): Value {
    if (target !== null && typeof target === "object" && !Array.isArray(target) && target.type === "namespace") {
      if (name in target.members) return target.members[name];
      throw new ScriptError(`${target.name} has no member '${name}'`, line);
    }
    if (Array.isArray(target)) {
      const list = target;
      switch (name) {
        case "append": return builtin("append", ([x]) => { list.push(x); return null; });
        case "pop": return builtin("pop", ([i], l) => {
          if (!list.length) throw new ScriptError("pop from empty list", l);
          return list.splice(i === undefined ? list.length - 1 : this.listIndex(list, i, l), 1)[0];
        });
        case "insert": return builtin("insert", ([i, x], l) => {
          if (typeof i !== "number") throw new ScriptError("insert index must be a number", l);
          list.splice(i, 0, x);
          return null;
        });
        case "remove": return builtin("remove", ([x], l) => {
          const i = list.findIndex((y) => equals(x, y));
          if (i < 0) throw new ScriptError("value not in list", l);
          list.splice(i, 1);
          return null;
        });
      }
    }
    throw new ScriptError(`A ${typeName(target)} has no attribute '${name}'`, line);
  }

  private *call(callee: Value, args: Value[], line: number): Generator<number, Value, void> {
    if (callee === null || typeof callee !== "object" || Array.isArray(callee)) {
      throw new ScriptError(`A ${typeName(callee)} is not callable`, line);
    }
    if (callee.type === "builtin") {
      return yield* callee.call(args, line);
    }
    if (callee.type !== "function") throw new ScriptError(`${repr(callee)} is not callable`, line);
    if (args.length !== callee.params.length) {
      throw new ScriptError(`${callee.name}() takes ${callee.params.length} argument(s) but got ${args.length}`, line);
    }
    if (this.depth >= MAX_DEPTH) throw new ScriptError("Maximum recursion depth exceeded", line);
    const local = new Scope(callee.closure);
    callee.params.forEach((p, i) => local.vars.set(p, args[i]));
    this.depth++;
    try {
      yield* this.execBlock(callee.body, local);
    } catch (e) {
      if (e instanceof ReturnSignal) return e.value;
      throw e;
    } finally {
      this.depth--;
      this.currentLine = line;
    }
    return null;
  }

  private binary(op: string, l: Value, r: Value, line: number): Value {
    if (typeof l === "number" && typeof r === "number") {
      switch (op) {
        case "+": return l + r;
        case "-": return l - r;
        case "*": return l * r;
        case "**": return l ** r;
        case "/":
          if (r === 0) throw new ScriptError("Division by zero", line);
          return l / r;
        case "//":
          if (r === 0) throw new ScriptError("Division by zero", line);
          return Math.floor(l / r);
        case "%":
          if (r === 0) throw new ScriptError("Modulo by zero", line);
          return ((l % r) + r) % r;
      }
    }
    if (op === "+" && typeof l === "string" && typeof r === "string") return l + r;
    if (op === "+" && Array.isArray(l) && Array.isArray(r)) return [...l, ...r];
    if (op === "*") {
      const [seq, n] = typeof r === "number" ? [l, r] : [r, l];
      if (typeof n === "number" && typeof seq === "string") return seq.repeat(Math.max(0, n));
      if (typeof n === "number" && Array.isArray(seq)) {
        return Array.from({ length: Math.max(0, n) }, () => seq).flat();
      }
    }
    throw new ScriptError(`Unsupported operands for ${op}: ${typeName(l)} and ${typeName(r)}`, line);
  }

  private compare(op: string, l: Value, r: Value, line: number): boolean {
    switch (op) {
      case "==": return equals(l, r);
      case "!=": return !equals(l, r);
      case "in":
      case "not in": {
        let found: boolean;
        if (Array.isArray(r)) found = r.some((x) => equals(x, l));
        else if (typeof r === "string" && typeof l === "string") found = r.includes(l);
        else throw new ScriptError(`Cannot use 'in' with a ${typeName(r)}`, line);
        return op === "in" ? found : !found;
      }
    }
    const comparable =
      (typeof l === "number" && typeof r === "number") || (typeof l === "string" && typeof r === "string");
    if (!comparable) throw new ScriptError(`Cannot compare ${typeName(l)} and ${typeName(r)} with ${op}`, line);
    const a = l as number | string;
    const b = r as number | string;
    switch (op) {
      case "<": return a < b;
      case ">": return a > b;
      case "<=": return a <= b;
      default: return a >= b;
    }
  }
}
