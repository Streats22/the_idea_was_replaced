# Agents

Read this first. Keep `memory.md` current when something important is learned or fixed.

## Non‑negotiable gates

Before any commit or push:

1. **Code review** on the change (`/code-review` or Bugbot-style review).
2. **Security review** after that (`/review-security`).
3. **Tests** — add or update them for the behavior you touched; they must pass.
4. **Build + tests must have been run successfully** on this machine (or CI) before commit and before push. Do not commit or push red builds.

Order is always: implement → tests → code review → security review → fix issues → re-run tests/build → then commit/push only if the user asked.

## Code quality (always)

- **OOP**: clear types with one job; factories for figures/scenes; pools for shared budgets; no god-class dumps of unrelated logic.
- **DRY**: one source of truth (accents, status labels, viewport mount, light pools, palette). Extract when the same logic appears twice.
- **Readable C#**: short methods, obvious names, `sealed`/`static` where it fits, no dead members, no magic numbers without a name.
- **Few comments**: prefer names over narration. Keep type-level docs only when the *why* is non-obvious (budget, fog, replay scrubbing). Delete comments that restate the code.
- **Match the repo**: Godot 4.7 .NET / C#, `TreatWarningsAsErrors`, existing patterns in `delvework/`.

## Hardware safety (never optional)

This machine has crashed before. Prefer cool and boring over pretty:

- Mobile renderer, fps caps (`Graphics`), half/third-res 3D, no MSAA.
- One sun shadow; omnis from `OmniLightPool` / `Graphics.MaxOmniLights` only.
- No golem/monster point lights; Almanac stays 2D (no portrait SubViewports).
- Do not raise resolution, add MSAA, or spawn many lights without updating `memory.md` and getting explicit approval.

## Layout

| Path | Role |
|---|---|
| `delvework/src/Delvework.Core` | Rules, sim, language, content load, save |
| `delvework/game` | Godot client: screens, View3D, UI, audio |
| `delvework/tests` | Core tests — keep green |
| `memory.md` | Project facts, issue log, agent checklist |
| `agents.md` | This file — standing instructions |

## Memory

- **Always update `memory.md`** when you discover a lasting fact, a recurring bug and its fix, a budget change, or a process change.
- Put durable “how we work” rules here in `agents.md`; put incidents and how-to-fix notes in `memory.md`.
