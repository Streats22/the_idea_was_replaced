# Drone Farm

A small browser game inspired by *The Farmer Was Replaced*: you don't farm, you write
Python-like code that drives a farming drone. Harvest, research upgrades, expand the
farm, and optimize your scripts.

## Run

```bash
npm install
npm run dev      # http://localhost:5173
npm test         # interpreter + game logic tests
npm run build    # type-check and production build
```

## How it works

- `src/lang/`: a small Python subset (lexer with indentation, parser, and a
  generator-based interpreter). Every statement yields a time cost, so scripts run
  step by step in sync with the game clock, and `while True:` loops never freeze the page.
  Supports variables, `if/elif/else`, `while`, `for ... in`, `def`/`return`, `global`,
  lists (`append`, `pop`, `insert`, `remove`), `in`, and the usual operators.
- `src/game/world.ts`: the farm grid, plant growth, and harvesting rules.
- `src/game/api.ts`: the builtins your code can call (`move`, `harvest`, `plant`, ...).
- `src/game/unlocks.ts`: the research tree (planting, sensing, carrots, trees,
  pumpkins, speed and farm-size upgrades).
- `src/ui/`: the canvas renderer and code editor.

## Game rules

| Plant   | Grows in | Needs          | Cost            | Yields |
|---------|----------|----------------|-----------------|--------|
| Grass   | 0.5s     | Grassland      | free, regrows   | 1 Hay  |
| Bush    | 4s       | anything       | free            | 1 Wood |
| Tree    | 7s       | anything       | free            | 5 Wood, slower next to trees |
| Carrot  | 6s       | Soil (`till()`) | 1 Hay + 1 Wood | 1 Carrot |
| Pumpkin | 10s      | Soil           | 1 Carrot        | connected group of n gives n × min(n, 10); 20% die |

Actions (`move`, `harvest`, `plant`, `till`) cost 0.2s of game time, reduced by speed upgrades.
Each statement costs a tiny bit of time too, so efficient code matters.
