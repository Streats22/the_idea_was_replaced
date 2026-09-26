import { describe, expect, it } from "vitest";
import { builtin, Interpreter, repr, type Value } from "../src/lang/interpreter";
import { ScriptError } from "../src/lang/lexer";
import { parse } from "../src/lang/parser";
import { EXAMPLES } from "../src/examples";

function run(source: string, maxSteps = 100_000): string[] {
  const out: string[] = [];
  const interp = new Interpreter(parse(source), {
    print: builtin("print", (args: Value[]) => {
      out.push(args.map((a) => repr(a)).join(" "));
      return null;
    }),
    range: builtin("range", ([n]) => Array.from({ length: n as number }, (_, i) => i)),
    len: builtin("len", ([v]) => (v as Value[]).length),
  });
  const gen = interp.run();
  for (let i = 0; i < maxSteps; i++) if (gen.next().done) return out;
  throw new Error("did not finish");
}

describe("interpreter", () => {
  it("handles arithmetic with python semantics", () => {
    expect(run("print(7 // 2, -7 // 2, -7 % 3, 2 ** 10, 1 / 4)")).toEqual(["3 -4 2 1024 0.25"]);
  });

  it("runs if/elif/else and while loops", () => {
    const src = `
i = 0
while i < 5:
    if i == 0:
        print("zero")
    elif i % 2 == 0:
        print("even", i)
    else:
        pass
    i += 1
`;
    expect(run(src)).toEqual(["zero", "even 2", "even 4"]);
  });

  it("supports functions, recursion, and globals", () => {
    const src = `
count = 0
def fib(n):
    global count
    count += 1
    if n < 2:
        return n
    return fib(n - 1) + fib(n - 2)
print(fib(10), count)
`;
    expect(run(src)).toEqual(["55 177"]);
  });

  it("supports lists, indexing, methods, and for loops with break/continue", () => {
    const src = `
xs = [3, 1, 4]
xs.append(1)
xs[0] = 9
total = 0
for x in xs:
    if x == 4:
        continue
    total += x
for i in range(100):
    if i == 3:
        break
print(xs, total, i, xs[-1], len(xs), 4 in xs, 7 not in xs)
`;
    expect(run(src)).toEqual(["[9, 1, 4, 1] 11 3 1 4 True True"]);
  });

  it("supports chained comparisons and short-circuit logic", () => {
    expect(run("print(1 < 2 < 3, 3 > 2 > 2, None or 5, 0 and boom)")).toEqual(["True False 5 0"]);
  });

  it("reports errors with line numbers", () => {
    try {
      run("x = 1\ny = x + undefined_thing");
      expect.unreachable();
    } catch (e) {
      expect(e).toBeInstanceOf(ScriptError);
      expect((e as ScriptError).line).toBe(2);
      expect((e as ScriptError).message).toMatch(/undefined_thing/);
    }
  });

  it("reports syntax errors", () => {
    expect(() => parse("if True\n    pass")).toThrow(/Expected ':'/);
    expect(() => parse("x = (1 +\n")).toThrow(ScriptError);
  });

  it("yields on every iteration so infinite loops can be paused", () => {
    const interp = new Interpreter(parse("while True:\n    pass"), {});
    const gen = interp.run();
    for (let i = 0; i < 1000; i++) expect(gen.next().done).toBe(false);
  });

  it("parses every bundled example", () => {
    for (const code of Object.values(EXAMPLES)) expect(() => parse(code)).not.toThrow();
  });

  it("allows one-line blocks and multi-line brackets", () => {
    const src = `
xs = [1,
      2,
      3]
if len(xs) == 3: print("ok")
`;
    expect(run(src)).toEqual(["ok"]);
  });
});
