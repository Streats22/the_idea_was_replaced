import { beforeEach, describe, expect, it, vi } from "vitest";
import { Game } from "../src/game/game";
import { World } from "../src/game/world";

beforeEach(() => {
  const store = new Map<string, string>();
  vi.stubGlobal("localStorage", {
    getItem: (k: string) => store.get(k) ?? null,
    setItem: (k: string, v: string) => store.set(k, v),
    removeItem: (k: string) => store.delete(k),
  });
});

function runFor(game: Game, seconds: number) {
  for (let t = 0; t < seconds; t += 0.1) game.tick(0.1);
}

describe("world", () => {
  it("grows grass and harvests hay only when ripe", () => {
    const w = new World(3, () => 1);
    expect(w.canHarvest()).toBe(false);
    w.harvest();
    expect(w.inventory.Hay).toBe(0);
    w.advance(1);
    expect(w.canHarvest()).toBe(true);
    w.harvest();
    expect(w.inventory.Hay).toBe(1);
  });

  it("wraps the drone around edges", () => {
    const w = new World(3);
    w.move("West");
    w.move("South");
    expect(w.drone).toEqual({ x: 2, y: 2 });
  });

  it("requires soil and resources for carrots", () => {
    const w = new World(3);
    expect(w.plant("Carrot")).toMatch(/Soil/);
    w.till();
    expect(w.plant("Carrot")).toMatch(/not enough/);
    w.inventory.Hay = 1;
    w.inventory.Wood = 1;
    expect(w.plant("Carrot")).toBeNull();
    expect(w.inventory).toMatchObject({ Hay: 0, Wood: 0 });
  });

  it("harvests connected pumpkins as a group", () => {
    const w = new World(3, () => 1);
    w.inventory.Carrot = 9;
    for (let x = 0; x < 3; x++) {
      for (let y = 0; y < 3; y++) {
        w.drone = { x, y };
        w.till();
        w.plant("Pumpkin");
      }
    }
    w.advance(20);
    w.harvest();
    expect(w.inventory.Pumpkin).toBe(9 * 9);
    expect(w.tiles.flat().every((t) => t.plant === null)).toBe(true);
  });
});

describe("game", () => {
  it("runs a script that farms hay", () => {
    const game = new Game();
    game.run(`
while True:
    if can_harvest():
        harvest()
    move(North)
`);
    runFor(game, 10);
    expect(game.state).toBe("running");
    expect(game.world.inventory.Hay).toBeGreaterThan(10);
  });

  it("blocks locked functions until researched", () => {
    const game = new Game();
    game.run("plant(Entities.Bush)");
    runFor(game, 1);
    expect(game.state).toBe("error");
    expect(game.errorLine).toBe(1);

    game.world.inventory.Hay = 100;
    expect(game.buy("plant")).toBe(true);
    game.run("harvest()\nplant(Entities.Bush)\nprint(1)");
    runFor(game, 2);
    expect(game.state).toBe("done");
    expect(game.world.here.plant?.kind).toBe("Bush");
  });

  it("expands the farm on research and persists progress", () => {
    const game = new Game();
    game.world.inventory.Hay = 100;
    game.world.inventory.Wood = 100;
    game.buy("plant");
    game.buy("expand1");
    expect(game.world.size).toBe(4);
    game.save("print(1)");

    const loaded = new Game();
    expect(loaded.load()).toBe("print(1)");
    expect(loaded.world.size).toBe(4);
    expect(loaded.unlocked.has("expand1")).toBe(true);
  });
});
