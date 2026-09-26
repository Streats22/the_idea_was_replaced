export type EntityKind = "Grass" | "Bush" | "Tree" | "Carrot" | "Pumpkin" | "Dead_Pumpkin";
export type GroundKind = "Grassland" | "Soil";
export type ItemKind = "Hay" | "Wood" | "Carrot" | "Pumpkin";
export type Direction = "North" | "East" | "South" | "West";

export interface Plant {
  kind: EntityKind;
  /** 0..1; harvestable at 1 */
  growth: number;
}

export interface Tile {
  ground: GroundKind;
  plant: Plant | null;
}

export interface PlantSpec {
  growTime: number;
  needsSoil: boolean;
  cost: Partial<Record<ItemKind, number>>;
  yields: Partial<Record<ItemKind, number>>;
}

export const PLANTS: Record<Exclude<EntityKind, "Dead_Pumpkin">, PlantSpec> = {
  Grass: { growTime: 0.5, needsSoil: false, cost: {}, yields: { Hay: 1 } },
  Bush: { growTime: 4, needsSoil: false, cost: {}, yields: { Wood: 1 } },
  Tree: { growTime: 7, needsSoil: false, cost: {}, yields: { Wood: 5 } },
  Carrot: { growTime: 6, needsSoil: true, cost: { Hay: 1, Wood: 1 }, yields: { Carrot: 1 } },
  Pumpkin: { growTime: 10, needsSoil: true, cost: { Carrot: 1 }, yields: { Pumpkin: 1 } },
};

export const PUMPKIN_DEATH_CHANCE = 0.2;

const DIRS: Record<Direction, [number, number]> = {
  North: [0, 1],
  East: [1, 0],
  South: [0, -1],
  West: [-1, 0],
};

export class World {
  size: number;
  tiles: Tile[][] = [];
  drone = { x: 0, y: 0 };
  time = 0;
  inventory: Record<ItemKind, number> = { Hay: 0, Wood: 0, Carrot: 0, Pumpkin: 0 };
  flips = 0;

  constructor(size: number, private rng: () => number = Math.random) {
    this.size = size;
    this.tiles = World.makeTiles(size);
  }

  private static makeTiles(size: number, prev?: Tile[][]): Tile[][] {
    return Array.from({ length: size }, (_, x) =>
      Array.from({ length: size }, (_, y) => prev?.[x]?.[y] ?? { ground: "Grassland" as GroundKind, plant: { kind: "Grass" as EntityKind, growth: 0 } }),
    );
  }

  resize(size: number) {
    if (size === this.size) return;
    this.tiles = World.makeTiles(size, this.tiles);
    this.size = size;
    this.drone.x %= size;
    this.drone.y %= size;
  }

  tileAt(x: number, y: number): Tile {
    const s = this.size;
    return this.tiles[((x % s) + s) % s][((y % s) + s) % s];
  }

  get here(): Tile {
    return this.tileAt(this.drone.x, this.drone.y);
  }

  private adjacentTrees(x: number, y: number): number {
    let n = 0;
    for (const [dx, dy] of Object.values(DIRS)) {
      const nx = x + dx;
      const ny = y + dy;
      if (nx < 0 || ny < 0 || nx >= this.size || ny >= this.size) continue;
      if (this.tiles[nx][ny].plant?.kind === "Tree") n++;
    }
    return n;
  }

  advance(dt: number) {
    if (dt <= 0) return;
    this.time += dt;
    for (let x = 0; x < this.size; x++) {
      for (let y = 0; y < this.size; y++) {
        const tile = this.tiles[x][y];
        if (!tile.plant && tile.ground === "Grassland") tile.plant = { kind: "Grass", growth: 0 };
        const p = tile.plant;
        if (!p) continue;
        if (p.kind === "Dead_Pumpkin" || p.growth >= 1) continue;
        let rate = 1 / PLANTS[p.kind].growTime;
        if (p.kind === "Tree") rate /= 1 + this.adjacentTrees(x, y);
        p.growth = Math.min(1, p.growth + rate * dt);
        if (p.growth >= 1 && p.kind === "Pumpkin" && this.rng() < PUMPKIN_DEATH_CHANCE) {
          tile.plant = { kind: "Dead_Pumpkin", growth: 1 };
        }
      }
    }
  }

  move(dir: Direction) {
    const [dx, dy] = DIRS[dir];
    this.drone.x = (this.drone.x + dx + this.size) % this.size;
    this.drone.y = (this.drone.y + dy + this.size) % this.size;
  }

  canHarvest(): boolean {
    const p = this.here.plant;
    return !!p && p.kind !== "Dead_Pumpkin" && p.growth >= 1;
  }

  /** Returns false if there was nothing to harvest. Unripe plants are destroyed without yield. */
  harvest(): boolean {
    const tile = this.here;
    const p = tile.plant;
    if (!p) return false;
    if (p.kind === "Pumpkin" && p.growth >= 1) {
      const group = this.pumpkinGroup(this.drone.x, this.drone.y);
      for (const [x, y] of group) this.tiles[x][y].plant = null;
      this.inventory.Pumpkin += group.length * Math.min(group.length, 10);
      return true;
    }
    tile.plant = null;
    if (p.kind !== "Dead_Pumpkin" && p.growth >= 1) {
      for (const [item, n] of Object.entries(PLANTS[p.kind].yields)) {
        this.inventory[item as ItemKind] += n!;
      }
    }
    return true;
  }

  /** Connected ripe pumpkins; harvesting a group of n yields n * min(n, 10). */
  pumpkinGroup(sx: number, sy: number): [number, number][] {
    const seen = new Set<string>();
    const out: [number, number][] = [];
    const stack: [number, number][] = [[sx, sy]];
    while (stack.length) {
      const [x, y] = stack.pop()!;
      const key = `${x},${y}`;
      if (seen.has(key) || x < 0 || y < 0 || x >= this.size || y >= this.size) continue;
      seen.add(key);
      const p = this.tiles[x][y].plant;
      if (p?.kind !== "Pumpkin" || p.growth < 1) continue;
      out.push([x, y]);
      for (const [dx, dy] of Object.values(DIRS)) stack.push([x + dx, y + dy]);
    }
    return out;
  }

  /** Returns an error message, or null on success. */
  plant(kind: Exclude<EntityKind, "Dead_Pumpkin">): string | null {
    const tile = this.here;
    const spec = PLANTS[kind];
    if (tile.plant && tile.plant.kind !== "Grass") return `there is already a ${tile.plant.kind} here`;
    if (spec.needsSoil && tile.ground !== "Soil") return `${kind} needs Soil (use till())`;
    if (kind === "Grass" && tile.ground !== "Grassland") return "Grass needs Grassland";
    for (const [item, n] of Object.entries(spec.cost)) {
      if (this.inventory[item as ItemKind] < n!) return `not enough ${item} (needs ${n})`;
    }
    for (const [item, n] of Object.entries(spec.cost)) this.inventory[item as ItemKind] -= n!;
    tile.plant = { kind, growth: 0 };
    return null;
  }

  till() {
    const tile = this.here;
    tile.ground = tile.ground === "Soil" ? "Grassland" : "Soil";
    tile.plant = null;
  }
}
