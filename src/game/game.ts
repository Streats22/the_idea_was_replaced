import { ScriptError } from "../lang/lexer";
import { Interpreter } from "../lang/interpreter";
import { parse } from "../lang/parser";
import { createBuiltins } from "./api";
import { UNLOCKS, worldSizeFor, type Unlock } from "./unlocks";
import { World, type ItemKind } from "./world";

export type RunState = "idle" | "running" | "error" | "done";

const MAX_STEPS_PER_FRAME = 40_000;
const SAVE_KEY = "drone-farm-save-v1";

export interface LogLine {
  text: string;
  kind: "out" | "error" | "info";
}

export class Game {
  world = new World(3);
  unlocked = new Set<string>();
  state: RunState = "idle";
  logs: LogLine[] = [];
  errorLine: number | null = null;
  timeWarp = 1;

  private interpreter: Interpreter | null = null;
  private program: Generator<number, void, void> | null = null;
  /** Script time spent but not yet applied to the world. */
  private pending = 0;
  private listeners = new Set<() => void>();

  get currentLine(): number | null {
    return this.state === "running" && this.interpreter ? this.interpreter.currentLine : null;
  }

  onChange(fn: () => void) {
    this.listeners.add(fn);
  }

  private emit() {
    for (const fn of this.listeners) fn();
  }

  log(text: string, kind: LogLine["kind"] = "out") {
    this.logs.push({ text, kind });
    if (this.logs.length > 300) this.logs.splice(0, this.logs.length - 300);
    this.emit();
  }

  run(source: string) {
    this.stop();
    this.errorLine = null;
    try {
      const ast = parse(source);
      const builtins = createBuiltins({
        world: this.world,
        unlocked: this.unlocked,
        log: (t) => this.log(t),
        sync: () => this.flush(),
      });
      this.interpreter = new Interpreter(ast, builtins);
      this.program = this.interpreter.run();
      this.state = "running";
      this.log("▶ Running", "info");
    } catch (e) {
      this.fail(e);
    }
    this.emit();
  }

  stop() {
    if (this.state === "running") this.log("■ Stopped", "info");
    this.program = null;
    this.interpreter = null;
    if (this.state === "running") this.state = "idle";
    this.emit();
  }

  private fail(e: unknown) {
    this.program = null;
    this.state = "error";
    if (e instanceof ScriptError) {
      this.errorLine = e.line;
      this.log(`Line ${e.line}: ${e.message}`, "error");
    } else {
      this.log(`Internal error: ${e instanceof Error ? e.message : String(e)}`, "error");
      console.error(e);
    }
  }

  private flush() {
    this.world.advance(this.pending);
    this.pending = 0;
  }

  /** Advance the simulation by `realDt` seconds of wall-clock time. */
  tick(realDt: number) {
    const budget = realDt * this.timeWarp;
    if (!this.program) {
      this.world.advance(budget);
      return;
    }
    let spent = 0;
    let steps = 0;
    try {
      while (spent < budget && steps < MAX_STEPS_PER_FRAME) {
        const r = this.program.next();
        if (r.done) {
          this.program = null;
          this.state = "done";
          this.log("✓ Program finished", "info");
          break;
        }
        spent += r.value;
        this.pending += r.value;
        steps++;
      }
    } catch (e) {
      this.fail(e);
    }
    this.flush();
    if (!this.program && spent < budget) this.world.advance(budget - spent);
    this.emit();
  }

  canAfford(u: Unlock): boolean {
    return Object.entries(u.cost).every(([item, n]) => this.world.inventory[item as ItemKind] >= n!);
  }

  isAvailable(u: Unlock): boolean {
    return !this.unlocked.has(u.id) && u.requires.every((r) => this.unlocked.has(r));
  }

  buy(id: string): boolean {
    const u = UNLOCKS.find((x) => x.id === id);
    if (!u || !this.isAvailable(u) || !this.canAfford(u)) return false;
    for (const [item, n] of Object.entries(u.cost)) this.world.inventory[item as ItemKind] -= n!;
    this.unlocked.add(u.id);
    this.world.resize(worldSizeFor(this.unlocked));
    this.log(`Researched ${u.name}!`, "info");
    this.emit();
    return true;
  }

  save(code: string) {
    const data = {
      code,
      inventory: this.world.inventory,
      unlocked: [...this.unlocked],
      time: this.world.time,
    };
    localStorage.setItem(SAVE_KEY, JSON.stringify(data));
  }

  load(): string | null {
    const raw = localStorage.getItem(SAVE_KEY);
    if (!raw) return null;
    try {
      const data = JSON.parse(raw);
      this.unlocked = new Set(data.unlocked ?? []);
      this.world = new World(worldSizeFor(this.unlocked));
      Object.assign(this.world.inventory, data.inventory ?? {});
      this.world.time = data.time ?? 0;
      return typeof data.code === "string" ? data.code : null;
    } catch {
      return null;
    }
  }

  reset() {
    this.stop();
    localStorage.removeItem(SAVE_KEY);
    this.unlocked = new Set();
    this.world = new World(3);
    this.logs = [];
    this.state = "idle";
    this.emit();
  }
}
