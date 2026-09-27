# Delvework game (Godot 4 .NET)

The playable game. All rules live in `Delvework.Core` (`../src`): the language, the
simulation, lessons, skill trees and the save profile. This project draws them as low-poly 3D
dioramas and plays the music. A delve is simulated instantly (deterministic for seed + code), then played back.

## Hardware budget

The game is built to stay light on the machine:

- The Mobile renderer, capped at 60 fps with vsync (30 fps on the Low setting, 10 fps while the window is in the background).
- 3D is drawn at half resolution (third on Low) and scaled up; no MSAA.
- One shadow per scene, from the sun. Point lights never cast shadows (each 3D viewport has no shadow atlas for them).
- Dungeon torches are glowing flames; only the six nearest the party carry a real light. Town keeps a pool of six night lamps; buildings use emissive glow only.
- No SSAO or other heavy screen effects. Each 3D view stops rendering while hidden, and screens without 3D only redraw when something changes.
- Settings > Graphics: Low turns off shadows and glow and shrinks 3D further. It is stored in `user://settings.cfg`.

## Running

Needs the .NET 8 SDK and [Godot 4.7 .NET](https://godotengine.org/download) (the "Godot Engine - .NET" build).

```sh
dotnet build                          # from this folder
godot --path .                        # or open project.godot in the Godot editor and press Play
```

In the editor, create the C# solution once if asked (Project > Tools > C# > Create C# solution).
Content is read from `../content`, so edits to lessons, skills, monsters and floors apply on the next start.
Progress saves to `user://profile.json`.

## How a game goes

1. **Delve first.** A new game starts in town. Your golem already runs plain commands like
   `move(East)`, and the Mine Entrance (the cave) is open from the start.
2. **Learn with gold** at the Library: Loops, Decisions, Variables, Functions, Lists, Events and
   Dictionaries, in that order, each with a price. Buying one unlocks it for every golem and pops
   up a short explanation window. The full pages stay in the Codex. Each feature also has an
   optional challenge floor that pays a gold bonus the first time you pass it. Until you learn a
   feature, the compiler tells you which one a keyword needs.
3. **Help while you play:** the "? Help" menu opens small draggable windows for any feature,
   all functions, or how to play. Click a name in your code and the strip under the editor
   explains it. Ctrl+click opens its window, and so do the names in the Functions tab and the
   Library reference.
4. **Materials.** Gold is not the only thing you need. The mine walls hold veins of stone, iron
   ore and old timber: `mine(North)` or `mine(nearest_vein(Ore))` digs one out, and the golem
   carries it home. Village buildings add more every delve: the Woodcutter's Hut brings wood, the
   Quarry stone, the Wheat Fields wheat.
5. **Workshops you program.** The Smelter turns ore into iron and the Bakery turns wheat into
   bread, but only as well as the script you write for them (`stoke()`, `smelt()`, `knead()`,
   `bake()`, `take_out()`...). Each script runs one shift after every delve, and the workshop
   screen shows it working, with a preview on your current stores.
6. **Skill trees**, bought with gold and materials (wood, stone, iron, bread):
   - **Village** (notice board): rebuild Hollowmere. Buildings deliver gold or materials after
     every delve, add golems to the party, open workshops and the other trees. Built lots appear
     in the town.
   - **Equipment** (the Forge): HP, armor, attack, sight, instructions per tick, speed, recall stones.
   - **Arcana** (the Arcane Tower): mana and spells. Each spell is a new function, such as `heal()`, `bolt(enemy)`, `reveal()` or `shield()`.
7. **Delves** (the cave): free runs into the mines with your equipment and your own code. Each
   run is a new dungeon, and everything the party carries home is yours.

## Screens and code

| File | What it is |
|---|---|
| `App.cs` | Root node: content, profile, screen switching, modals, settings |
| `Screens/TitleScreen.cs` | Title with the live town behind it, intro story |
| `Screens/TownScreen.cs` | Town hub: HUD, "next step" hint, clickable places, delve site picker |
| `Help.cs` | Floating help windows (`HelpWindow`) and what goes in them: features, functions, how to play, the "? Help" menu |
| `Screens/CodexScreen.cs` | The Library: Learn buttons with prices, explanation pages, optional challenge card, a clickable reference |
| `Screens/SkillScreen.cs` | The three trees, node details with prices and where to find missing materials, buying |
| `Screens/WorkshopScreen.cs` | Smelter and Bakery: script editor, animated furnace or oven, playback, shift log, preview on your stores |
| `Goods.cs` | Material icons, colours, the stores bar and price rows |
| `Screens/DelveScreen.cs` | Goals, editor per golem (autocomplete only offers what you know), 3D replay, timeline, inspector, log, results |
| `View3D/` | The diorama look: each scene is a floating board seen through a perspective camera. `DungeonView3D` (Kenney floor and wall tiles in chunks, fog, torches with a small light pool, figures, effects, HP bars and damage numbers; right-drag to pan, scroll to zoom), `TownView3D` (the village on its board, houses built from Kenney wall panels and rebuilt as you buy; drag to turn, scroll to zoom), `Figures` (golems and monsters), `Kenney` (loads the models), `Iso` (camera, flat materials, lights, prop helpers) |
| `Graphics.cs` | Frame cap, resolution scale, shadows/glow, omni-light budget |
| `View3D/OmniLightPool.cs` | Shared pool of nearest night/torch lights |

## Models

Low-poly models from [Kenney](https://kenney.nl) (CC0): Fantasy Town Kit, Mini Dungeon and Mini Characters, in
`assets/kenney/` with their licenses. The editor imports them the first time the project is opened; without an
import they are read straight from the `.glb` files. If a model is missing, the game draws a simple stand-in
built from boxes and cylinders instead.
| `Audio/` | Original music composed in code: a town theme with four layers that fade in as the village grows, a dungeon drone with bells, a combat layer when monsters are close, and sound effects |

## Command line

Arguments after `--` are for the game. Command-line runs use a throwaway profile.

```sh
godot --headless --path . -- --smoke     # first delve, every help window, every feature learned and its challenge passed, every skill and site; exit 1 on failure
godot --path . -- --screenshot=out.png --screen=town --progress=8
godot --path . -- --screenshot=out.png --screen=lesson --lesson=variables --solution --tick=52
godot --path . -- --screenshot=out.png --screen=delve --site=deep_mines --progress=8 --tick=120
godot --path . -- --screenshot=out.png --screen=workshop --workshop=smelter --progress=3 --tick=4
```

Screens: `title`, `town`, `codex` (`--page=N`), `reference`, `skills` (`--tree=Village|Equipment|Arcana`), `lesson`, `delve` (`--site=id`),
`workshop` (`--workshop=smelter|bakery`, `--script=file`).
`--progress=N` learns N features, passes their challenges and buys what it can; `--gold=N` sets the gold; `--stock=N` sets every material (default 14);
`--result` counts the run and shows the result popup;
`--windows=loops,move,functions,howto,smelter` opens help windows (a lesson id, a function or keyword, `functions`, `howto` or a workshop).
