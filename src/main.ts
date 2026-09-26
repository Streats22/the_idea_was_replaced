import "./style.css";
import { FUNCTION_DOCS } from "./game/api";
import { Game } from "./game/game";
import { UNLOCKS } from "./game/unlocks";
import type { ItemKind } from "./game/world";
import { DEFAULT_CODE, EXAMPLES } from "./examples";
import { CodeEditor } from "./ui/editor";
import { FarmRenderer } from "./ui/renderer";

const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;

const ITEMS: ItemKind[] = ["Hay", "Wood", "Carrot", "Pumpkin"];
const WARPS = [1, 4, 16, 64];

const game = new Game();
const savedCode = game.load();
const editor = new CodeEditor($("editor"), savedCode ?? DEFAULT_CODE);
const renderer = new FarmRenderer($<HTMLCanvasElement>("farm"));

const runBtn = $<HTMLButtonElement>("run");
const statusEl = $("status");
const consoleEl = $("console");

function toggleRun() {
  if (game.state === "running") game.stop();
  else game.run(editor.value);
  updateRunState();
}

runBtn.addEventListener("click", toggleRun);
editor.onRun = toggleRun;

const exampleSelect = $<HTMLSelectElement>("examples");
for (const name of Object.keys(EXAMPLES)) {
  exampleSelect.add(new Option(name, name));
}
exampleSelect.addEventListener("change", () => {
  const code = EXAMPLES[exampleSelect.value];
  if (code) {
    game.stop();
    editor.value = code;
  }
  exampleSelect.value = "";
});

const warpEl = $("warp");
function renderWarp() {
  warpEl.innerHTML = WARPS.map(
    (w) => `<button class="${w === game.timeWarp ? "on" : ""}" data-warp="${w}">${w}×</button>`,
  ).join("");
}
warpEl.addEventListener("click", (e) => {
  const w = (e.target as HTMLElement).dataset.warp;
  if (w) {
    game.timeWarp = Number(w);
    renderWarp();
  }
});

$("clear").addEventListener("click", () => {
  game.logs = [];
  renderConsole();
});

$("reset").addEventListener("click", () => {
  if (!confirm("Reset all progress? Your code will be kept.")) return;
  game.reset();
  renderAll();
});

$("win-close").addEventListener("click", () => $("win").classList.add("hidden"));

$("upgrades").addEventListener("click", (e) => {
  const id = (e.target as HTMLElement).closest<HTMLElement>("[data-buy]")?.dataset.buy;
  if (id && game.buy(id)) {
    game.save(editor.value);
    if (id === "automation") $("win").classList.remove("hidden");
    renderAll();
  }
});

function costHtml(cost: Partial<Record<ItemKind, number>>) {
  return Object.entries(cost)
    .map(([item, n]) => {
      const have = game.world.inventory[item as ItemKind] >= n!;
      return `<span class="cost ${have ? "" : "short"}"><i class="swatch ${item.toLowerCase()}"></i>${n} ${item}</span>`;
    })
    .join("");
}

let upgradesKey: string | null = null;
function renderUpgrades() {
  const key = JSON.stringify([game.world.inventory, [...game.unlocked]]);
  if (key === upgradesKey) return;
  upgradesKey = key;

  const available = UNLOCKS.filter((u) => game.isAvailable(u));
  const locked = UNLOCKS.filter((u) => !game.unlocked.has(u.id) && !game.isAvailable(u));
  const done = UNLOCKS.filter((u) => game.unlocked.has(u.id));

  const name = (id: string) => UNLOCKS.find((u) => u.id === id)!.name;
  let html = available
    .map((u) => {
      const afford = game.canAfford(u);
      return `<div class="upgrade ${afford ? "ready" : ""}">
        <div class="upgrade-top"><strong>${u.name}</strong>
          <button class="btn small ${afford ? "primary" : ""}" data-buy="${u.id}" ${afford ? "" : "disabled"}>Research</button>
        </div>
        <p>${u.description}</p>
        <div class="costs">${costHtml(u.cost)}</div>
      </div>`;
    })
    .join("");
  if (locked.length) {
    html += `<div class="locked-list">${locked
      .map((u) => `<span title="Requires ${u.requires.map(name).join(", ")}">${u.name}</span>`)
      .join("")}</div>`;
  }
  if (done.length) {
    html += `<div class="done-list">${done.map((u) => `<span>✓ ${u.name}</span>`).join("")}</div>`;
  }
  $("upgrades").innerHTML = html;
  $("upgrade-meta").textContent = `${done.length}/${UNLOCKS.length} researched`;
}

let inventoryKey: string | null = null;
function renderInventory() {
  const inv = game.world.inventory;
  const key = ITEMS.map((i) => inv[i]).join(",");
  if (key === inventoryKey) return;
  inventoryKey = key;
  $("inventory").innerHTML = ITEMS.map(
    (i) => `<div class="item"><i class="swatch ${i.toLowerCase()}"></i><span>${i}</span><b>${formatNum(inv[i])}</b></div>`,
  ).join("");
}

function formatNum(n: number) {
  return n >= 10_000 ? `${(n / 1000).toFixed(1)}k` : String(n);
}

let docsKey: string | null = null;
function renderDocs() {
  const key = [...game.unlocked].join(",");
  if (key === docsKey) return;
  docsKey = key;
  $("docs").innerHTML = FUNCTION_DOCS.map((d) => {
    const locked = d.unlock && !game.unlocked.has(d.unlock);
    const unlockName = d.unlock ? UNLOCKS.find((u) => u.id === d.unlock)?.name : "";
    return `<div class="doc ${locked ? "locked" : ""}">
      <code>${d.signature}</code>
      <p>${d.description}${locked ? ` <em>Unlock: ${unlockName}</em>` : ""}</p>
    </div>`;
  }).join("");
}

let logCount = -1;
let lastLog: unknown = null;
function renderConsole() {
  const last = game.logs[game.logs.length - 1] ?? null;
  if (game.logs.length === logCount && last === lastLog) return;
  logCount = game.logs.length;
  lastLog = last;
  consoleEl.innerHTML = game.logs.length
    ? game.logs.map((l) => `<div class="log ${l.kind}">${escapeHtml(l.text)}</div>`).join("")
    : `<div class="log info">Press Run to start your drone.</div>`;
  consoleEl.scrollTop = consoleEl.scrollHeight;
}

function escapeHtml(s: string) {
  return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;");
}

function updateRunState() {
  const running = game.state === "running";
  runBtn.textContent = running ? "Stop" : "Run";
  runBtn.classList.toggle("danger", running);
  runBtn.classList.toggle("primary", !running);
  const labels = { idle: "", running: "running", error: "error", done: "finished" };
  statusEl.textContent = labels[game.state];
  statusEl.className = `status ${game.state}`;
}

function renderAll() {
  upgradesKey = inventoryKey = docsKey = null;
  renderInventory();
  renderUpgrades();
  renderDocs();
  renderConsole();
  renderWarp();
  updateRunState();
}

let saveTimer: number | undefined;
editor.onChange = () => {
  clearTimeout(saveTimer);
  saveTimer = window.setTimeout(() => game.save(editor.value), 500);
};
setInterval(() => game.save(editor.value), 5000);
window.addEventListener("beforeunload", () => game.save(editor.value));

let last = performance.now();
let hudTimer = 0;
function frame(now: number) {
  const dt = Math.min(0.1, (now - last) / 1000);
  last = now;
  const prevState = game.state;
  game.tick(dt);
  renderer.draw(game.world, dt);
  editor.setMarkers(game.currentLine, game.errorLine);
  if (game.state !== prevState) updateRunState();

  hudTimer += dt;
  if (hudTimer > 0.1) {
    hudTimer = 0;
    renderInventory();
    renderUpgrades();
    renderDocs();
    renderConsole();
    const w = game.world;
    $("farm-meta").textContent = `${w.size}×${w.size} · drone at (${w.drone.x}, ${w.drone.y}) · t=${w.time.toFixed(0)}s`;
  }
  requestAnimationFrame(frame);
}

renderAll();
requestAnimationFrame(frame);
