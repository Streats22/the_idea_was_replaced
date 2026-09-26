export type TokenType =
  | "NUMBER"
  | "STRING"
  | "NAME"
  | "KEYWORD"
  | "OP"
  | "NEWLINE"
  | "INDENT"
  | "DEDENT"
  | "EOF";

export interface Token {
  type: TokenType;
  value: string;
  line: number;
  col: number;
}

export class ScriptError extends Error {
  constructor(message: string, public line: number) {
    super(message);
  }
}

export const KEYWORDS = new Set([
  "if", "elif", "else", "while", "for", "in", "def", "return", "break",
  "continue", "pass", "and", "or", "not", "True", "False", "None", "global",
]);

const OPERATORS = [
  "**=", "//=", "==", "!=", "<=", ">=", "+=", "-=", "*=", "/=", "%=", "**", "//",
  "+", "-", "*", "/", "%", "<", ">", "=", "(", ")", "[", "]", ",", ":", ".",
];

export function tokenize(source: string): Token[] {
  const tokens: Token[] = [];
  const indents = [0];
  const lines = source.replace(/\r\n?/g, "\n").split("\n");
  let depth = 0; // bracket nesting; newlines inside brackets are ignored

  for (let li = 0; li < lines.length; li++) {
    const text = lines[li];
    const lineNo = li + 1;
    let i = 0;

    if (depth === 0) {
      let width = 0;
      while (i < text.length && (text[i] === " " || text[i] === "\t")) {
        width += text[i] === "\t" ? 4 : 1;
        i++;
      }
      if (i >= text.length || text[i] === "#") continue;
      const top = indents[indents.length - 1];
      if (width > top) {
        indents.push(width);
        tokens.push({ type: "INDENT", value: "", line: lineNo, col: 0 });
      } else if (width < top) {
        while (indents[indents.length - 1] > width) {
          indents.pop();
          tokens.push({ type: "DEDENT", value: "", line: lineNo, col: 0 });
        }
        if (indents[indents.length - 1] !== width) {
          throw new ScriptError("Indentation does not match any outer block", lineNo);
        }
      }
    }

    while (i < text.length) {
      const c = text[i];
      if (c === " " || c === "\t") { i++; continue; }
      if (c === "#") break;

      if (/[0-9]/.test(c) || (c === "." && /[0-9]/.test(text[i + 1] ?? ""))) {
        const m = /^[0-9]*\.?[0-9]+(?:[eE][-+]?[0-9]+)?|^[0-9]+\.?/.exec(text.slice(i))!;
        tokens.push({ type: "NUMBER", value: m[0], line: lineNo, col: i });
        i += m[0].length;
        continue;
      }

      if (/[A-Za-z_]/.test(c)) {
        const m = /^[A-Za-z_][A-Za-z0-9_]*/.exec(text.slice(i))!;
        const type = KEYWORDS.has(m[0]) ? "KEYWORD" : "NAME";
        tokens.push({ type, value: m[0], line: lineNo, col: i });
        i += m[0].length;
        continue;
      }

      if (c === '"' || c === "'") {
        let j = i + 1;
        let value = "";
        while (j < text.length && text[j] !== c) {
          if (text[j] === "\\" && j + 1 < text.length) {
            const n = text[j + 1];
            value += n === "n" ? "\n" : n === "t" ? "\t" : n;
            j += 2;
          } else {
            value += text[j++];
          }
        }
        if (j >= text.length) throw new ScriptError("Unterminated string", lineNo);
        tokens.push({ type: "STRING", value, line: lineNo, col: i });
        i = j + 1;
        continue;
      }

      const op = OPERATORS.find((o) => text.startsWith(o, i));
      if (!op) throw new ScriptError(`Unexpected character '${c}'`, lineNo);
      if (op === "(" || op === "[") depth++;
      if (op === ")" || op === "]") depth = Math.max(0, depth - 1);
      tokens.push({ type: "OP", value: op, line: lineNo, col: i });
      i += op.length;
    }

    if (depth === 0) {
      const last = tokens[tokens.length - 1];
      if (last && last.type !== "NEWLINE" && last.type !== "INDENT" && last.type !== "DEDENT") {
        tokens.push({ type: "NEWLINE", value: "", line: lineNo, col: text.length });
      }
    }
  }

  const endLine = lines.length;
  if (tokens.length && tokens[tokens.length - 1].type !== "NEWLINE") {
    tokens.push({ type: "NEWLINE", value: "", line: endLine, col: 0 });
  }
  while (indents.length > 1) {
    indents.pop();
    tokens.push({ type: "DEDENT", value: "", line: endLine, col: 0 });
  }
  tokens.push({ type: "EOF", value: "", line: endLine, col: 0 });
  return tokens;
}
