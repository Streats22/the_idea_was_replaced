import type { ItemKind } from "./world";

export interface Unlock {
  id: string;
  name: string;
  description: string;
  cost: Partial<Record<ItemKind, number>>;
  requires: string[];
}

export const UNLOCKS: Unlock[] = [
  { id: "plant", name: "Planting", description: "plant(Entities.Bush) grows bushes that give Wood.", cost: { Hay: 15 }, requires: [] },
  { id: "senses", name: "Senses", description: "get_pos_x(), get_pos_y(), get_world_size(), get_entity_type(), get_ground_type(), num_items().", cost: { Hay: 25 }, requires: [] },
  { id: "expand1", name: "Expand I", description: "Farm grows to 4×4.", cost: { Hay: 30, Wood: 10 }, requires: ["plant"] },
  { id: "speed1", name: "Speed I", description: "Drone actions are 1.5× faster.", cost: { Hay: 50, Wood: 25 }, requires: ["plant"] },
  { id: "carrots", name: "Carrots", description: "till() turns Grassland into Soil. plant(Entities.Carrot) costs 1 Hay + 1 Wood.", cost: { Wood: 50 }, requires: ["expand1"] },
  { id: "trees", name: "Trees", description: "plant(Entities.Tree) gives 5 Wood, but grows slower next to other trees.", cost: { Wood: 60, Carrot: 20 }, requires: ["carrots"] },
  { id: "expand2", name: "Expand II", description: "Farm grows to 6×6.", cost: { Wood: 150, Carrot: 50 }, requires: ["carrots", "senses"] },
  { id: "speed2", name: "Speed II", description: "Drone actions are 2.5× faster.", cost: { Hay: 300, Carrot: 100 }, requires: ["speed1", "carrots"] },
  { id: "pumpkins", name: "Pumpkins", description: "plant(Entities.Pumpkin) costs 1 Carrot. Some die when grown. Harvest a connected group of n ripe pumpkins for n × min(n, 10).", cost: { Wood: 200, Carrot: 150 }, requires: ["expand2"] },
  { id: "expand3", name: "Expand III", description: "Farm grows to 8×8.", cost: { Wood: 500, Pumpkin: 100 }, requires: ["pumpkins"] },
  { id: "speed3", name: "Speed III", description: "Drone actions are 4× faster.", cost: { Carrot: 600, Pumpkin: 300 }, requires: ["speed2", "pumpkins"] },
  { id: "expand4", name: "Expand IV", description: "Farm grows to 10×10.", cost: { Wood: 2000, Pumpkin: 1000 }, requires: ["expand3"] },
  { id: "automation", name: "Full Automation", description: "The farmer has officially been replaced. You win!", cost: { Hay: 5000, Wood: 5000, Carrot: 3000, Pumpkin: 5000 }, requires: ["expand4", "speed3"] },
];

export function worldSizeFor(unlocked: Set<string>): number {
  if (unlocked.has("expand4")) return 10;
  if (unlocked.has("expand3")) return 8;
  if (unlocked.has("expand2")) return 6;
  if (unlocked.has("expand1")) return 4;
  return 3;
}

export function speedFor(unlocked: Set<string>): number {
  if (unlocked.has("speed3")) return 4;
  if (unlocked.has("speed2")) return 2.5;
  if (unlocked.has("speed1")) return 1.5;
  return 1;
}
