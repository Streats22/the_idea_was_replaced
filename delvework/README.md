# Delvework

A programming dungeon crawler for Steam: write the AI for a party of golems, send them into
the dungeon, read the monsters' code, and grow the town of Hollowmere with what comes back.

- [Concept](docs/CONCEPT.md): pitch, pillars, party, monsters-as-code, town, language, visual style
- [Roadmap](docs/ROADMAP.md): scope targets, architecture, phases with exit criteria, unlock map, risks, next actions

## Status

Everything is C#: a headless core, and a Godot 4 .NET front end on top of it.

| Phase | State | Where |
|---|---|---|
| 0. Concept validation | Playtest build ready in Godot; [playtests](docs/PLAYTEST.md) still to run | [`game/`](game/) |
| 1. Core foundation (C#) | Built and tested | `src/`, `tests/`, `content/` |
| 2. First playable (Godot) | Playable loop: delve from the start, buy 8 language features with gold (optional challenges), help windows, mining materials (wood, stone, iron ore), programmable Smelter and Bakery, 2.5D town and dungeon, 3 skill trees, music | [`game/`](game/) |

## Layout

| Path | What it is |
|---|---|
| `game/` | Godot 4 .NET game: title, 2.5D town hub, Codex lessons, skill trees, delve screen with editor, 3D replay and inspector, procedural music ([README](game/README.md)) |
| `src/Delvework.Core/Glyph/` | Glyph language: lexer, parser, bytecode compiler, stack VM, standard library |
| `src/Delvework.Core/Sim/` | Deterministic simulation: grid, field of view, A*, combat, statuses, golem and monster APIs |
| `src/Delvework.Core/Dungeon/` | Seeded room-and-corridor generator and hand-made floor parsing |
| `src/Delvework.Core/Replay/` | Replay recorder with keyframes and seeking, and a verifier |
| `src/Delvework.Core/Progress/` | Player profile and material stores, learning features with gold, lesson challenges and their checks, skill trees and prices, rewards |
| `src/Delvework.Core/Village/` | Workshops: the Smelter and Bakery rules, their Glyph functions and the shift runner |
| `src/Delvework.Core/Testing/` | Determinism sweep, Glyph fuzzer and benchmark (shared by tests, CLI and CI) |
| `src/Delvework.Cli/` | Headless command-line front end |
| `content/` | Data-driven content: Codex lessons (`codex/`: pages, challenge, starter and solution code), skill trees (`skills.json`), workshops and their starter scripts (`workshops/`), delve sites (`delves.json`), chassis, monsters and their Glyph scripts, traps, strata, hand-made floors, example parties, sample programs |
| `tests/Delvework.Core.Tests/` | xUnit tests, including the retired TypeScript prototype's interpreter tests ported to C# |

## Building

Requires the .NET 8 SDK (see `global.json`). Analyzers run with warnings as errors.

```sh
dotnet build
dotnet test
```

`DELVEWORK_DETERMINISM_SEEDS=1000 dotnet test` widens the in-test determinism sweep (60 seeds by default).

The game needs Godot 4.7 .NET as well: `cd game && dotnet build && godot --path .` (details in [game/README.md](game/README.md)).

## Command line

```sh
dotnet run --project src/Delvework.Cli -- run --seed 42 --party warden.glyph,seeker.glyph
```

| Command | What it does |
|---|---|
| `run --seed N --party a.glyph,b.glyph` | Plays one generated floor and prints the outcome. `--map`, `--log`, `--replay out.json`, `--chassis`, `--tier`, `--max-ticks` |
| `verify replay.json` | Re-simulates a replay and checks every checkpoint hash |
| `map --seed N` | Prints a generated floor |
| `check file.glyph [--monster] [--tier N]` | Compiles a program and reports errors with line numbers |
| `validate` | Checks the content folder: 200 floors per stratum, every bundled program and workshop |
| `workshop smelter [file.glyph] [--ore N --wood N --wheat N] [--log]` | Runs one workshop shift with a script (the starter by default) and prints what it used and made |
| `lessons [--seeds N]` | Every Codex lesson: the solution must pass and the starter code must fail, on N seeds |
| `determinism --seeds 1000` | Runs each seed twice and prints a combined hash to compare across machines |
| `fuzz --minutes 5` | Feeds random and mutated Glyph to the VM; any host exception is a crash |
| `bench` | 4 golems + 60 monsters at 200 instructions per tick; fails below 1000 ticks/s |

Party entries are file paths, or names of programs in `content/programs/`. The chassis defaults
to the program name when it matches one (`warden`, `seeker`, `striker`).

## Rules the core guarantees

- **Determinism.** No floats in the simulation (`Fix` is Q47.16 fixed point), no hash-ordered
  iteration, and named xoshiro256** RNG streams (dungeon, combat, loot, monsters) derived from
  the run seed. CI compares the 1000-seed hash between Windows and Linux.
- **No program can freeze the game.** Each golem runs up to its budget of instructions per tick
  and resumes where it stopped. Recursion depth, stack, collection and string sizes are capped,
  and every failure becomes a Glyph error with a line number that halts only that golem.
- **Tick order.** Timers and statuses, then golem programs in party order, then monster scripts,
  then actions resolved by initiative (then id), then vision and events. Queries see the state
  at the start of the tick.
- **Events** (`on see(enemy):`, `on hurt(n, source):`, `on signal "go"(data):`, ...) are queued
  (at most 16, oldest dropped) and run only between statements, then the main program resumes.
- **Monsters are Glyph too.** Their scripts in `content/monsters/` use a smaller API, and their
  attack wind-ups are visible to golems through `enemy.intent`.
