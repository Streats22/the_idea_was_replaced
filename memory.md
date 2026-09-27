# Memory

Living project notes for agents. **Update this file** whenever something important is learned, fixed, or decided. Standing rules live in `agents.md`; this file holds facts, incidents, and how we fixed them.

---

## Always before commit / push

Checklist (do not skip):

1. Tests written/updated for the change.
2. `dotnet test` (and game `dotnet build` when client code changed) — **green**.
3. **Code review** of the full relevant diff.
4. **Security review** after the code review.
5. Resolve every issue those reviews raise; re-run tests/build.
6. Commit / push **only when the user asks**, and only after the above is done.

If a review or test fails, fix it and create a **new** commit path — do not push a broken tree.

---

## Project snapshot

- **Name**: Delvework (in `delvework/`), TFWR-inspired programming + delve game.
- **Stack**: Godot **4.7 .NET**, C# / .NET 8, `TreatWarningsAsErrors`.
- **Core**: `delvework/src/Delvework.Core` — language, sim, lessons, skills, profile.
- **Client**: `delvework/game` — diorama 3D (town + dungeon), screens, help, audio.
- **Content**: `delvework/content` (lessons, monsters, floors…).
- **Look**: bright village + darker dungeon, Kenney CC0 low-poly, perspective diorama (not true iso).
- **Save**: `user://profile.json`; graphics settings: `user://settings.cfg`.

### Key client pieces

| Piece | Notes |
|---|---|
| `Graphics` | Low vs Normal, fps, PixelScale, shadows/glow, `MaxOmniLights`, `Changed` event |
| `Iso.Mount` | Shared SubViewport + camera + sun + env bootstrap |
| `OmniLightPool` | Nearest-N omnis for town lamps / dungeon torches |
| `Figures` | Golem/monster factories; accents via `Palette.ForGolem` / `MonsterAccent` |
| `TownView3D` / `DungeonView3D` | Dioramas; subscribe to `Graphics.Changed`, not `Apply` every frame |
| Almanac portraits | **2D only** — never spawn one SubViewport per monster |

---

## Hardware budget (safety)

Owner’s PC has hard-crashed / needed RAM reseating. Crashing is not acceptable.

- Mobile renderer, vsync, max 60 fps (30 Low, 10 background).
- 3D at 1/`PixelScale` (2 Normal, 3 Low), **no MSAA**.
- One directional shadow; positional shadow atlas size 0.
- At most `Graphics.MaxOmniLights` (6) real omnis per view, pooled.
- Glow/shadows off on Low; views stop updating when hidden.
- Prefer emissive meshes over extra lights.

---

## Common issues → resolution

### Unread `_viewport` / CS0414 with TreatWarningsAsErrors

- **Symptom**: build fails on assigned-but-unread fields.
- **Fix**: pass viewport into `Graphics.Apply` from `OnGraphicsChanged` / `_Ready`, or drop the field.

### Full-res 3D + MSAA undercuts safety

- **Symptom**: GPU fill-rate spikes, heat, freezes.
- **Fix**: `Iso.SceneViewport` uses `StretchShrink = Graphics.PixelScale`, `Msaa3D = Disabled`. Refresh scale on `Graphics.Changed`.

### Too many OmniLights (Mobile ~8/mesh)

- **Symptom**: lights drop out or cost spikes at night / in dungeon.
- **Fix**: `OmniLightPool.AssignNearest`; no lights on golems/monsters/stairs; buildings use glow materials.

### Almanac with 8 live 3D SubViewports

- **Symptom**: OwnWorld3D × N updating at once.
- **Fix**: flat 2D portraits (`Figures.MonsterAccent` + glyph); Almanac can use LowProcessorUsageMode.

### Town `Refresh` smoke / flicker races `QueueFree`

- **Symptom**: freed smoke/lights still referenced after lot rebuild.
- **Fix**: parent smoke under `_world`, free smoke list explicitly before clearing lots; register lamp *spots*, not per-building lights that die with lots.

### Screen coords wrong after pixel scale

- **Symptom**: HUD/signs/HP bars offset from 3D.
- **Fix**: always `Iso.ToScreen(camera, world)` (`UnprojectPosition * Graphics.PixelScale`).

### Duplicate accent / status strings

- **Symptom**: UI color ≠ figure accent; “stunned” wording drifts.
- **Fix**: `Palette.ForGolem`, `Figures.MonsterAccent`, `GolemStatus.Append`.

### `Graphics.Apply` every frame

- **Symptom**: pointless work; unclear intent.
- **Fix**: call from `_Ready` + `Graphics.Changed` only.

### Missing `Iso.Wood` (mimic chest)

- **Symptom**: compile break from Figures mimic branch.
- **Fix**: `Iso.Wood(Color grain)` solid material helper.

### `project.godot` shadow atlas / soft shadows

- **Symptom**: root viewport still paid for positional atlas + soft directional filter after SubViewport budget work.
- **Fix**: `positional_shadow/atlas_size=0`, `directional_shadow/soft_shadow_filter_quality=0`.

### Dungeon `Rebuild` double-draw

- **Symptom**: `QueueFree` leaves old MultiMeshes until frame end while new ones are added.
- **Fix**: `FreeChildren` removes + `Free()` immediately before rebuild.

### Prefer not to launch Godot/dotnet on the user’s machine without need

- Owner preference: avoid long editor/game runs that stress the PC. Prefer `dotnet build` / `dotnet test` when verifying; don’t leave Godot open pounding the GPU.

---

## Review expectations

- **Code review**: correctness, DRY/OOP, budget regressions, warnings-as-errors, races.
- **Security review**: no secrets, no unsafe deserialization surprises, path/content trust boundaries, no malicious-use affordances.
- Both reviews happen on the **actual diff** that will ship; fix findings before commit.

---

## Tests

- Core: `delvework/tests/Delvework.Core.Tests`.
- Game smoke (when appropriate): `godot --headless --path delvework/game -- --smoke` (heavy — prefer after client-critical changes, not for every tiny edit if the user asks to go light).
- New behavior in Core → unit tests. Replay/sim edge cases → extend existing suites.
- Never claim “ready to commit” without tests having been run (or an explicit user waiver).

---

## Log (append below)

### 2026-09-27 — Visual overhaul + safety + cleanup

- Switched to TFWR-style dioramas, Kenney assets, Mobile + fps/resolution budget.
- Reviews found unread viewports, full-res/MSAA, Almanac SubViewports, light count, town Refresh races.
- Resolved with `Graphics`/`Iso` scale, 2D Almanac, `OmniLightPool`, smoke under `_world`, shared accents/status.
- Refactor pass: `Iso.Mount`, `OmniLightPool`, `DungeonOverlay` file, fewer comments, DRY accents.

### 2026-09-27 — Commit/push/launch tooling

- User-level installs (no sudo): .NET 8 at `~/.dotnet`, Godot 4.7.2 Mono at `~/Applications/GodotMono-4.7.2/Godot_mono.app`.
- Launch: `~/.dotnet` on PATH, then `Godot_mono.app/.../Godot --path delvework/game`.
- Rebased onto remote “learn by delving” UI; kept brass frames + hanging signs; kept perspective dioramas + light pools. Merged rune tablets / `ShiftPixels`.

### 2026-09-27 — TFWR growth beat (design north star)

Owner loves TFWR’s feeling: **start tiny → expand the space → then make it move/act**.
Not “more UI,” but earned scale: 1 plot → 3×3 → automation/motion.

Delvework already has a farm workshop (`content/workshops/farm.json`, skill `v_farm`) as a fixed **6×4** wraparound drone field — closer to mid-TFWR than to the “one seed” start. Greenhouse (`v_greenhouse`) adds passive yield, not grid growth.

**Idea to keep:** progression should feel like unlocking *space* then *agency*, not only +N resources. Candidates: shrink farm start to 1×1 / 3×3 unlocks; delve “one shaft → wider floors”; second golem as “it moves with you.” No implementation chosen yet.
