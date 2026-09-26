import type { Plant, World } from "../game/world";

export class FarmRenderer {
  private ctx: CanvasRenderingContext2D;
  private droneVis = { x: 0, y: 0 };
  private lastFlips = 0;
  private flipStart = -1;
  private t = 0;

  constructor(private canvas: HTMLCanvasElement) {
    this.ctx = canvas.getContext("2d")!;
  }

  private resize(): number {
    const rect = this.canvas.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const px = Math.round(rect.width * dpr);
    if (this.canvas.width !== px || this.canvas.height !== px) {
      this.canvas.width = px;
      this.canvas.height = px;
    }
    return px;
  }

  draw(world: World, dt: number) {
    this.t += dt;
    const px = this.resize();
    const ctx = this.ctx;
    const n = world.size;
    const pad = px * 0.02;
    const cell = (px - pad * 2) / n;

    ctx.clearRect(0, 0, px, px);

    for (let x = 0; x < n; x++) {
      for (let y = 0; y < n; y++) {
        const tile = world.tiles[x][y];
        const cx = pad + x * cell;
        const cy = pad + (n - 1 - y) * cell;
        this.drawGround(cx, cy, cell, tile.ground === "Soil", (x + y) % 2 === 0);
        if (tile.plant) this.drawPlant(tile.plant, cx + cell / 2, cy + cell / 2, cell, x * 7 + y * 13);
      }
    }

    // Snap when wrapping across the edge; otherwise glide toward the target tile.
    const d = world.drone;
    if (Math.abs(d.x - this.droneVis.x) > 1.01 || Math.abs(d.y - this.droneVis.y) > 1.01) {
      this.droneVis = { x: d.x, y: d.y };
    }
    const k = 1 - Math.exp(-dt * 18);
    this.droneVis.x += (d.x - this.droneVis.x) * k;
    this.droneVis.y += (d.y - this.droneVis.y) * k;

    if (world.flips !== this.lastFlips) {
      this.lastFlips = world.flips;
      this.flipStart = this.t;
    }
    const flipT = this.flipStart >= 0 ? (this.t - this.flipStart) / 0.8 : 1;

    const dx = pad + this.droneVis.x * cell + cell / 2;
    const dy = pad + (n - 1 - this.droneVis.y) * cell + cell / 2;
    this.drawDrone(dx, dy, cell, flipT < 1 ? flipT : -1);
  }

  private drawGround(x: number, y: number, s: number, soil: boolean, alt: boolean) {
    const ctx = this.ctx;
    const inset = s * 0.03;
    const r = s * 0.12;
    ctx.fillStyle = soil ? (alt ? "#7a5236" : "#734c31") : alt ? "#6fae4f" : "#67a549";
    roundRect(ctx, x + inset, y + inset, s - inset * 2, s - inset * 2, r);
    ctx.fill();
    if (soil) {
      ctx.strokeStyle = "rgba(0,0,0,0.18)";
      ctx.lineWidth = Math.max(1, s * 0.025);
      for (let i = 1; i < 4; i++) {
        const ly = y + (s * i) / 4;
        ctx.beginPath();
        ctx.moveTo(x + s * 0.15, ly);
        ctx.lineTo(x + s * 0.85, ly);
        ctx.stroke();
      }
    }
  }

  private drawPlant(p: Plant, cx: number, cy: number, s: number, seed: number) {
    const ctx = this.ctx;
    const ripe = p.growth >= 1;
    const g = 0.35 + 0.65 * p.growth;
    const sway = Math.sin(this.t * 2 + seed) * 0.04;
    ctx.save();
    ctx.translate(cx, cy + s * 0.18);
    ctx.rotate(sway);
    ctx.scale(g, g);
    const u = s / 100;

    switch (p.kind) {
      case "Grass": {
        ctx.strokeStyle = ripe ? "#e2cf6a" : "#9ed46b";
        ctx.lineWidth = 4 * u;
        ctx.lineCap = "round";
        for (let i = -2; i <= 2; i++) {
          ctx.beginPath();
          ctx.moveTo(i * 7 * u, 0);
          ctx.quadraticCurveTo(i * 9 * u, -20 * u, i * 14 * u, -(34 - Math.abs(i) * 5) * u);
          ctx.stroke();
        }
        break;
      }
      case "Bush": {
        ctx.fillStyle = "#2f7a3a";
        for (const [bx, by, br] of [[-14, -14, 16], [14, -14, 16], [0, -26, 18]] as const) {
          circle(ctx, bx * u, by * u, br * u);
        }
        ctx.fillStyle = "#3f9a4a";
        circle(ctx, -4 * u, -24 * u, 9 * u);
        if (ripe) {
          ctx.fillStyle = "#e0485a";
          circle(ctx, -12 * u, -16 * u, 3.5 * u);
          circle(ctx, 10 * u, -22 * u, 3.5 * u);
          circle(ctx, 4 * u, -10 * u, 3.5 * u);
        }
        break;
      }
      case "Tree": {
        ctx.fillStyle = "#6b4226";
        ctx.fillRect(-5 * u, -22 * u, 10 * u, 24 * u);
        ctx.fillStyle = "#1f6b35";
        circle(ctx, 0, -40 * u, 26 * u);
        ctx.fillStyle = "#2c8545";
        circle(ctx, -8 * u, -46 * u, 14 * u);
        break;
      }
      case "Carrot": {
        if (ripe) {
          ctx.fillStyle = "#f08a24";
          ctx.beginPath();
          ctx.moveTo(-9 * u, -14 * u);
          ctx.lineTo(9 * u, -14 * u);
          ctx.lineTo(0, 8 * u);
          ctx.closePath();
          ctx.fill();
        }
        ctx.strokeStyle = "#4fae3c";
        ctx.lineWidth = 5 * u;
        ctx.lineCap = "round";
        for (const a of [-0.5, 0, 0.5]) {
          ctx.beginPath();
          ctx.moveTo(0, -14 * u);
          ctx.lineTo(Math.sin(a) * 22 * u, -14 * u - Math.cos(a) * 22 * u);
          ctx.stroke();
        }
        break;
      }
      case "Pumpkin":
      case "Dead_Pumpkin": {
        const dead = p.kind === "Dead_Pumpkin";
        ctx.fillStyle = dead ? "#6e6450" : ripe ? "#f07b1d" : "#b9c24a";
        for (const [ox, rx] of [[-11, 13], [11, 13], [0, 15]] as const) {
          ctx.beginPath();
          ctx.ellipse(ox * u, -14 * u, rx * u, 17 * u, 0, 0, Math.PI * 2);
          ctx.fill();
        }
        ctx.strokeStyle = "rgba(0,0,0,0.18)";
        ctx.lineWidth = 2 * u;
        ctx.beginPath();
        ctx.moveTo(-6 * u, -28 * u);
        ctx.lineTo(-6 * u, 0);
        ctx.moveTo(6 * u, -28 * u);
        ctx.lineTo(6 * u, 0);
        ctx.stroke();
        ctx.fillStyle = dead ? "#4a4235" : "#4a7a2a";
        ctx.fillRect(-2.5 * u, -38 * u, 5 * u, 10 * u);
        break;
      }
    }
    ctx.restore();

    if (ripe && p.kind !== "Dead_Pumpkin") {
      const a = 0.5 + 0.5 * Math.sin(this.t * 4 + seed);
      ctx.fillStyle = `rgba(255,255,220,${0.35 + 0.5 * a})`;
      circle(ctx, cx + s * 0.32, cy - s * 0.32, s * 0.035);
    }
  }

  private drawDrone(x: number, y: number, s: number, flip: number) {
    const ctx = this.ctx;
    const u = s / 100;
    const hover = Math.sin(this.t * 3) * 3 * u;

    ctx.fillStyle = "rgba(0,0,0,0.22)";
    ctx.beginPath();
    ctx.ellipse(x, y + 30 * u, 24 * u, 7 * u, 0, 0, Math.PI * 2);
    ctx.fill();

    ctx.save();
    ctx.translate(x, y - 12 * u + hover - (flip >= 0 ? Math.sin(flip * Math.PI) * 18 * u : 0));
    if (flip >= 0) ctx.rotate(flip * Math.PI * 2);

    ctx.strokeStyle = "#2b2f3a";
    ctx.lineWidth = 5 * u;
    ctx.beginPath();
    ctx.moveTo(-22 * u, -16 * u); ctx.lineTo(22 * u, 16 * u);
    ctx.moveTo(22 * u, -16 * u); ctx.lineTo(-22 * u, 16 * u);
    ctx.stroke();

    const spin = this.t * 30;
    for (const [rx, ry] of [[-22, -16], [22, -16], [-22, 16], [22, 16]] as const) {
      ctx.fillStyle = "rgba(220,230,255,0.35)";
      ctx.beginPath();
      ctx.ellipse(rx * u, ry * u, 13 * u, 4 * u * Math.abs(Math.cos(spin + rx)), 0, 0, Math.PI * 2);
      ctx.fill();
      ctx.fillStyle = "#2b2f3a";
      circle(ctx, rx * u, ry * u, 3.5 * u);
    }

    ctx.fillStyle = "#f4f6fb";
    roundRect(ctx, -14 * u, -11 * u, 28 * u, 22 * u, 8 * u);
    ctx.fill();
    ctx.fillStyle = "#3b82f6";
    circle(ctx, 0, 0, 6 * u);
    ctx.fillStyle = "#bfdbfe";
    circle(ctx, -2 * u, -2 * u, 2 * u);
    ctx.restore();
  }
}

function circle(ctx: CanvasRenderingContext2D, x: number, y: number, r: number) {
  ctx.beginPath();
  ctx.arc(x, y, r, 0, Math.PI * 2);
  ctx.fill();
}

function roundRect(ctx: CanvasRenderingContext2D, x: number, y: number, w: number, h: number, r: number) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}
