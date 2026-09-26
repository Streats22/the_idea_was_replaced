import { KEYWORDS } from "../lang/lexer";

const INDENT = "    ";
const BUILTIN_NAMES = new Set([
  "move", "harvest", "can_harvest", "plant", "till", "do_a_flip", "print", "range", "len", "abs",
  "min", "max", "int", "str", "random", "get_pos_x", "get_pos_y", "get_world_size",
  "get_entity_type", "get_ground_type", "num_items", "get_time",
]);
const CONSTANTS = new Set(["North", "East", "South", "West", "Entities", "Items", "Grounds"]);

function escape(s: string) {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

export function highlight(code: string): string {
  const re = /(#.*$)|("(?:[^"\\\n]|\\.)*"?|'(?:[^'\\\n]|\\.)*'?)|(\b\d+(?:\.\d+)?\b)|([A-Za-z_]\w*)/gm;
  let out = "";
  let last = 0;
  for (const m of code.matchAll(re)) {
    out += escape(code.slice(last, m.index));
    const [text, comment, str, num, word] = m;
    let cls = "";
    if (comment) cls = "tk-comment";
    else if (str) cls = "tk-string";
    else if (num) cls = "tk-number";
    else if (word) {
      if (KEYWORDS.has(word)) cls = "tk-keyword";
      else if (BUILTIN_NAMES.has(word)) cls = "tk-builtin";
      else if (CONSTANTS.has(word)) cls = "tk-const";
    }
    out += cls ? `<span class="${cls}">${escape(text)}</span>` : escape(text);
    last = m.index! + text.length;
  }
  return out + escape(code.slice(last)) + "\n";
}

export class CodeEditor {
  private textarea: HTMLTextAreaElement;
  private highlightEl: HTMLElement;
  private gutter: HTMLElement;
  private activeLine: number | null = null;
  private errorLine: number | null = null;
  private lineCount = 0;
  onChange: () => void = () => {};
  onRun: () => void = () => {};

  constructor(root: HTMLElement, initial: string) {
    root.classList.add("editor");
    root.innerHTML = `
      <div class="editor-gutter"></div>
      <div class="editor-body">
        <pre class="editor-highlight" aria-hidden="true"></pre>
        <textarea class="editor-input" spellcheck="false" autocapitalize="off" autocomplete="off"></textarea>
      </div>`;
    this.gutter = root.querySelector(".editor-gutter")!;
    this.highlightEl = root.querySelector(".editor-highlight")!;
    this.textarea = root.querySelector(".editor-input")!;
    this.textarea.value = initial;

    this.textarea.addEventListener("input", () => {
      this.errorLine = null;
      this.refresh();
      this.onChange();
    });
    this.textarea.addEventListener("scroll", () => this.syncScroll());
    this.textarea.addEventListener("keydown", (e) => this.onKey(e));
    this.refresh();
  }

  get value() {
    return this.textarea.value;
  }

  set value(v: string) {
    this.textarea.value = v;
    this.errorLine = null;
    this.refresh();
    this.onChange();
  }

  setMarkers(active: number | null, error: number | null) {
    if (active === this.activeLine && error === this.errorLine) return;
    this.activeLine = active;
    this.errorLine = error;
    this.renderGutter();
  }

  private refresh() {
    this.highlightEl.innerHTML = highlight(this.textarea.value);
    this.lineCount = this.textarea.value.split("\n").length;
    this.renderGutter();
    this.syncScroll();
  }

  private renderGutter() {
    let html = "";
    for (let i = 1; i <= this.lineCount; i++) {
      const cls = i === this.errorLine ? "error" : i === this.activeLine ? "active" : "";
      html += `<div class="${cls}">${i}</div>`;
    }
    this.gutter.innerHTML = html;
    this.syncScroll();
  }

  private syncScroll() {
    this.highlightEl.scrollTop = this.textarea.scrollTop;
    this.highlightEl.scrollLeft = this.textarea.scrollLeft;
    this.gutter.scrollTop = this.textarea.scrollTop;
  }

  private replaceRange(start: number, end: number, text: string, cursor: number) {
    this.textarea.setRangeText(text, start, end, "end");
    this.textarea.selectionStart = this.textarea.selectionEnd = cursor;
    this.textarea.dispatchEvent(new Event("input"));
  }

  private onKey(e: KeyboardEvent) {
    const ta = this.textarea;
    const { selectionStart: s, selectionEnd: end, value } = ta;

    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) {
      e.preventDefault();
      this.onRun();
      return;
    }

    if (e.key === "Tab") {
      e.preventDefault();
      const lineStart = value.lastIndexOf("\n", s - 1) + 1;
      if (s === end && !e.shiftKey) {
        this.replaceRange(s, end, INDENT, s + INDENT.length);
        return;
      }
      const block = value.slice(lineStart, end);
      const lines = block.split("\n");
      const changed = e.shiftKey
        ? lines.map((l) => l.replace(/^ {1,4}/, ""))
        : lines.map((l) => INDENT + l);
      const text = changed.join("\n");
      ta.setRangeText(text, lineStart, end, "select");
      ta.dispatchEvent(new Event("input"));
      return;
    }

    if (e.key === "Enter") {
      e.preventDefault();
      const lineStart = value.lastIndexOf("\n", s - 1) + 1;
      const line = value.slice(lineStart, s);
      let indent = /^[ \t]*/.exec(line)![0];
      if (/:\s*(#.*)?$/.test(line)) indent += INDENT;
      const text = "\n" + indent;
      this.replaceRange(s, end, text, s + text.length);
      return;
    }

    if (e.key === "Backspace" && s === end && s > 0) {
      const lineStart = value.lastIndexOf("\n", s - 1) + 1;
      const before = value.slice(lineStart, s);
      if (before.length > 0 && /^ +$/.test(before)) {
        e.preventDefault();
        const remove = before.length % 4 === 0 ? 4 : before.length % 4;
        this.replaceRange(s - remove, s, "", s - remove);
      }
    }
  }
}
