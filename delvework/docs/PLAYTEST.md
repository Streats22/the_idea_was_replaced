# Phase 0 playtest protocol

From the roadmap: 5 people, at least two of them non-programmers. Watch them without helping.

## Setup

1. Build and start the game (see [`game/README.md`](../game/README.md)). Delete
   `%APPDATA%\Godot\app_userdata\Delvework\playtest.json` (Linux: `~/.local/share/godot/app_userdata/Delvework/`)
   so every tester starts fresh: seed 1, floor "The Old Mines", example "1. Just explore".
2. Say only: *"You're writing the brain for these golems. Get them down the stairs with as much
   gold as you can. The rune reference lists what they can do."*
3. Record the screen. Note the time of every Delve press and every code edit.
4. Stop after 30 minutes, or earlier if the tester asks to stop.

## Observe (tick when it happens, with a timestamp)

| # | Observation | T1 | T2 | T3 | T4 | T5 |
|---|---|---|---|---|---|---|
| 1 | Loses a golem to a Skeleton for the first time | | | | | |
| 2 | Edits code *after* that loss without being prompted | | | | | |
| 3 | Scrubs the replay timeline back to find out what went wrong | | | | | |
| 4 | Reads a monster's intent icon, or uses `enemy.intent` in code | | | | | |
| 5 | Counter-programs the Skeleton ("aha" moment); write down what they said | | | | | |
| 6 | Uses a second golem with `signal` or `ally()` | | | | | |
| 7 | Uses fast-forward or Skip to result | | | | | |
| 8 | Gets stuck on a language error for more than 2 minutes (write down the error) | | | | | |
| 9 | Opens the Inspector to read a variable while scrubbing | | | | | |

## Ask afterwards (open questions, don't lead)

- What was the most satisfying moment?
- When did you feel lost?
- Was watching the delve fun, boring, or stressful? At what speed?
- How did you figure out why a golem died?

## Exit criteria (from the roadmap)

- [ ] At least 4 of 5 testers voluntarily edit their code to beat the Skeleton after losing to it once (row 2).
- [ ] Testers describe the replay as useful without being prompted (row 3 plus interview).
- [ ] At least one "aha" moment is observed (row 5).
- [ ] Decision recorded: turn-based ticks versus near-real-time ticks (below).

## Tick model decision

The build uses the roadmap's default: **10 ticks per second, actions take 1–5 ticks,
instant simulation with 1×/4×/16× playback.** Record the decision here after the playtests:

- Decision:
- Evidence (rows 7 and "watching" answers):
- Date:

**Kill or pivot signal:** if watching isn't fun even with fast-forward, pivot to turn-by-turn
stepping where every tick is a puzzle (a Zachtronics-style design).
