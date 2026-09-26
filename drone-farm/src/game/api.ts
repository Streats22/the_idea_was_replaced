import { ScriptError } from "../lang/lexer";
import {
  builtin, makeEnum, repr, typeName,
  type Builtin, type BuiltinImpl, type Value,
} from "../lang/interpreter";
import { speedFor } from "./unlocks";
import type { Direction, EntityKind, ItemKind, World } from "./world";

export const ACTION_COST = 0.2;
const SENSE_COST = 0.002;

export const Entities = makeEnum("Entities", ["Grass", "Bush", "Tree", "Carrot", "Pumpkin", "Dead_Pumpkin"]);
export const Items = makeEnum("Items", ["Hay", "Wood", "Carrot", "Pumpkin"]);
export const Grounds = makeEnum("Grounds", ["Grassland", "Soil"]);
const DIRECTIONS = makeEnum("Directions", ["North", "East", "South", "West"]);

const ENTITY_UNLOCK: Partial<Record<EntityKind, string>> = {
  Bush: "plant", Carrot: "carrots", Tree: "trees", Pumpkin: "pumpkins",
};

export interface FunctionDoc {
  signature: string;
  description: string;
  unlock?: string;
}

export const FUNCTION_DOCS: FunctionDoc[] = [
  { signature: "move(North | East | South | West)", description: "Fly one tile. The farm wraps around at the edges." },
  { signature: "harvest()", description: "Harvest the plant under the drone. Unripe plants are destroyed for nothing." },
  { signature: "can_harvest()", description: "True if the plant under the drone is ripe." },
  { signature: "do_a_flip()", description: "Very important. Takes 1 second." },
  { signature: "print(value, ...)", description: "Write to the output console." },
  { signature: "plant(Entities.X)", description: "Plant a seed on this tile (replaces grass).", unlock: "plant" },
  { signature: "get_pos_x() / get_pos_y()", description: "Drone coordinates. (0, 0) is bottom-left.", unlock: "senses" },
  { signature: "get_world_size()", description: "Width of the (square) farm.", unlock: "senses" },
  { signature: "get_entity_type()", description: "Entities.X under the drone, or None.", unlock: "senses" },
  { signature: "get_ground_type()", description: "Grounds.Grassland or Grounds.Soil.", unlock: "senses" },
  { signature: "num_items(Items.X)", description: "How many of an item you own.", unlock: "senses" },
  { signature: "till()", description: "Toggle the ground between Grassland and Soil (clears the tile).", unlock: "carrots" },
  { signature: "range(n) / range(a, b[, step])", description: "List of numbers, like Python." },
  { signature: "len, abs, min, max, int, str, random()", description: "Utility builtins." },
  { signature: "get_time()", description: "Game time in seconds since the start." },
];

export interface ApiContext {
  world: World;
  unlocked: Set<string>;
  log: (text: string) => void;
  /** Called before any builtin that reads or changes the farm, so plant growth is up to date. */
  sync: () => void;
}

function asEnum(v: Value, group: string, fn: string, line: number): string {
  if (v !== null && typeof v === "object" && !Array.isArray(v) && v.type === "enum" && v.group === group) {
    return v.name;
  }
  throw new ScriptError(`${fn}() expects a ${group} value, got ${repr(v, true)}`, line);
}

function num(v: Value, fn: string, line: number): number {
  if (typeof v !== "number") throw new ScriptError(`${fn}() expects a number, got ${typeName(v)}`, line);
  return v;
}

export function createBuiltins(ctx: ApiContext): Record<string, Value> {
  const { world, unlocked } = ctx;
  const cost = () => ACTION_COST / speedFor(unlocked);

  const gate = (fn: string, unlock: string, line: number) => {
    if (!unlocked.has(unlock)) {
      throw new ScriptError(`${fn}() is locked. Research it in the Upgrades panel first.`, line);
    }
  };

  const action = (name: string, unlock: string | null, impl: (args: Value[], line: number) => Value, weight = 1): Builtin => ({
    type: "builtin",
    name,
    call: function* (args, line) {
      if (unlock) gate(name, unlock, line);
      ctx.sync();
      const result = impl(args, line);
      yield cost() * weight;
      return result;
    },
  });

  const sense = (name: string, unlock: string | null, impl: (args: Value[], line: number) => Value): Builtin => ({
    type: "builtin",
    name,
    call: function* (args, line) {
      if (unlock) gate(name, unlock, line);
      ctx.sync();
      yield SENSE_COST;
      return impl(args, line);
    },
  });

  const flip: BuiltinImpl = function* () {
    world.flips++;
    yield 1;
    return null;
  };

  const out: Record<string, Value> = {
    Entities, Items, Grounds,
    ...DIRECTIONS.members,

    move: action("move", null, ([d], line) => {
      world.move(asEnum(d, "Directions", "move", line) as Direction);
      return true;
    }),
    harvest: action("harvest", null, () => world.harvest()),
    can_harvest: sense("can_harvest", null, () => world.canHarvest()),
    plant: action("plant", "plant", ([e], line) => {
      const kind = asEnum(e, "Entities", "plant", line) as EntityKind;
      if (kind === "Dead_Pumpkin") throw new ScriptError("You can't plant a dead pumpkin", line);
      const needed = ENTITY_UNLOCK[kind];
      if (needed && !unlocked.has(needed)) throw new ScriptError(`${kind} is locked. Research it first.`, line);
      const err = world.plant(kind);
      if (err) ctx.log(`plant(${kind}) failed: ${err}`);
      return err === null;
    }),
    till: action("till", "carrots", () => { world.till(); return null; }),
    do_a_flip: { type: "builtin", name: "do_a_flip", call: flip },

    get_pos_x: sense("get_pos_x", "senses", () => world.drone.x),
    get_pos_y: sense("get_pos_y", "senses", () => world.drone.y),
    get_world_size: sense("get_world_size", "senses", () => world.size),
    get_entity_type: sense("get_entity_type", "senses", () => {
      const p = world.here.plant;
      return p ? Entities.members[p.kind] : null;
    }),
    get_ground_type: sense("get_ground_type", "senses", () => Grounds.members[world.here.ground]),
    num_items: sense("num_items", "senses", ([i], line) => world.inventory[asEnum(i, "Items", "num_items", line) as ItemKind]),
    get_time: builtin("get_time", () => { ctx.sync(); return world.time; }),

    print: builtin("print", (args) => {
      ctx.log(args.map((a) => repr(a)).join(" "));
      return null;
    }),
    range: builtin("range", (args, line) => {
      const nums = args.map((a) => num(a, "range", line));
      const [start, stop, step] = nums.length === 1 ? [0, nums[0], 1] : [nums[0], nums[1], nums[2] ?? 1];
      if (step === 0) throw new ScriptError("range() step must not be zero", line);
      const outList: Value[] = [];
      for (let i = start; step > 0 ? i < stop : i > stop; i += step) {
        outList.push(i);
        if (outList.length > 100_000) throw new ScriptError("range() is too large", line);
      }
      return outList;
    }),
    len: builtin("len", ([v], line) => {
      if (Array.isArray(v) || typeof v === "string") return v.length;
      throw new ScriptError(`len() of a ${typeName(v)}`, line);
    }),
    abs: builtin("abs", ([v], line) => Math.abs(num(v, "abs", line))),
    min: builtin("min", (args, line) => {
      const list = args.length === 1 && Array.isArray(args[0]) ? args[0] : args;
      if (!list.length) throw new ScriptError("min() of empty sequence", line);
      return Math.min(...list.map((v) => num(v, "min", line)));
    }),
    max: builtin("max", (args, line) => {
      const list = args.length === 1 && Array.isArray(args[0]) ? args[0] : args;
      if (!list.length) throw new ScriptError("max() of empty sequence", line);
      return Math.max(...list.map((v) => num(v, "max", line)));
    }),
    int: builtin("int", ([v], line) => {
      if (typeof v === "string" && v.trim() !== "" && !isNaN(Number(v))) return Math.trunc(Number(v));
      if (typeof v === "boolean") return v ? 1 : 0;
      return Math.trunc(num(v, "int", line));
    }),
    str: builtin("str", ([v]) => repr(v)),
    random: builtin("random", () => Math.random()),
  };
  return out;
}
