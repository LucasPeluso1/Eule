import { useEffect, useRef } from "react";
import gsap from "gsap";
import type { Risk } from "./fixtures";

/**
 * Painel de azulejos ao fundo da aplicação, no espírito da azulejaria modernista de Brasília.
 * Os motivos giram devagar, um de cada vez; quando chega um resultado, uma onda de giros parte
 * do score e tinge os azulejos próximos com a cor da classificação de risco.
 */

const TILE = 76;
const GROUND = "#06102a";
const FACE = "#0a1834";
const COBALT = "47, 99, 214";
const AZULEJO = "214, 226, 245";

const riskInk: Record<Risk, string> = {
  CRÍTICO: "255, 61, 99",
  ALTO: "242, 88, 91",
  MÉDIO: "240, 184, 74",
  BAIXO: "63, 196, 134",
};

interface Tile {
  col: number;
  row: number;
  motif: number;
  angle: number;
  alpha: number;
  light: boolean;
  tint: number;
  busy: boolean;
}

export interface TilePulse {
  key: number;
  risk: Risk;
}

function drawMotif(ctx: CanvasRenderingContext2D, motif: number, h: number, fill: string, stroke: string) {
  ctx.fillStyle = fill;
  ctx.strokeStyle = stroke;
  ctx.lineWidth = 2;
  ctx.beginPath();
  switch (motif) {
    case 0: // quarto de círculo cheio no canto
      ctx.moveTo(-h, -h);
      ctx.arc(-h, -h, h * 1.25, 0, Math.PI / 2);
      ctx.closePath();
      ctx.fill();
      break;
    case 1: // meia diagonal
      ctx.moveTo(-h, -h);
      ctx.lineTo(h, -h);
      ctx.lineTo(-h, h);
      ctx.closePath();
      ctx.fill();
      break;
    case 2: // semicírculo na borda com ponto
      ctx.arc(0, -h, h * 0.72, 0, Math.PI);
      ctx.fill();
      ctx.beginPath();
      ctx.arc(0, h * 0.42, h * 0.16, 0, Math.PI * 2);
      ctx.fill();
      break;
    case 3: // faixa com arco
      ctx.rect(-h, -h * 0.22, h * 2, h * 0.44);
      ctx.fill();
      ctx.beginPath();
      ctx.arc(h, h, h * 0.62, Math.PI, Math.PI * 1.5);
      ctx.stroke();
      break;
    case 4: // arcos concêntricos
      for (const r of [0.55, 1.05, 1.55]) {
        ctx.beginPath();
        ctx.arc(-h, h, h * r, -Math.PI / 2, 0);
        ctx.stroke();
      }
      break;
    default: // azulejo liso com quadrado deslocado
      ctx.rect(-h * 0.1, -h * 0.1, h * 0.9, h * 0.9);
      ctx.stroke();
  }
}

export default function TileField({ pulse }: { pulse: TilePulse | null }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const field = useRef<{ tiles: Tile[]; cols: number; ink: string; reduced: boolean; draw: (t: Tile) => void } | null>(null);

  useEffect(() => {
    const el = canvas.current;
    const ctx = el?.getContext("2d");
    if (!el || !ctx) return;

    const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    const state = { tiles: [] as Tile[], cols: 0, ink: riskInk.ALTO, reduced, draw: (_: Tile) => {} };
    field.current = state;
    const h = TILE / 2;

    state.draw = (tile: Tile) => {
      const x = tile.col * TILE;
      const y = tile.row * TILE;
      ctx.save();
      ctx.fillStyle = GROUND;
      ctx.fillRect(x, y, TILE, TILE);
      ctx.beginPath();
      ctx.rect(x + 1, y + 1, TILE - 2, TILE - 2);
      ctx.clip();
      ctx.fillStyle = FACE;
      ctx.fill();
      ctx.translate(x + h, y + h);
      ctx.rotate(tile.angle);
      const base = tile.light ? AZULEJO : COBALT;
      drawMotif(ctx, tile.motif, h, `rgba(${base}, ${tile.alpha})`, `rgba(${base}, ${tile.alpha * 1.3})`);
      if (tile.tint > 0.01) {
        drawMotif(ctx, tile.motif, h, `rgba(${state.ink}, ${tile.tint * 0.55})`, `rgba(${state.ink}, ${tile.tint * 0.7})`);
      }
      ctx.restore();
    };

    function build() {
      if (!el || !ctx) return;
      const dpr = Math.min(window.devicePixelRatio || 1, 2);
      const w = window.innerWidth;
      const hgt = window.innerHeight;
      el.width = Math.round(w * dpr);
      el.height = Math.round(hgt * dpr);
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      const cols = Math.ceil(w / TILE);
      const rows = Math.ceil(hgt / TILE);
      const anteriores = state.tiles;
      state.cols = cols;
      state.tiles = [];
      gsap.killTweensOf(anteriores);
      for (let row = 0; row < rows; row++) {
        for (let col = 0; col < cols; col++) {
          // Composição aleatória, como Athos Bulcão deixava os azulejistas fazerem.
          const motif = Math.floor(Math.random() * 6);
          state.tiles.push({
            col,
            row,
            motif,
            angle: (Math.PI / 2) * Math.floor(Math.random() * 4),
            alpha: motif === 4 || motif === 5 ? 0.22 + Math.random() * 0.12 : 0.1 + Math.random() * 0.14,
            light: Math.random() < 0.14,
            tint: 0,
            busy: false,
          });
        }
      }
      ctx.fillStyle = GROUND;
      ctx.fillRect(0, 0, w, hgt);
      state.tiles.forEach(state.draw);
    }

    build();

    let resizeTimer = 0;
    const onResize = () => {
      window.clearTimeout(resizeTimer);
      resizeTimer = window.setTimeout(build, 150);
    };
    window.addEventListener("resize", onResize);

    let idle: gsap.core.Tween | null = null;
    const turnOne = () => {
      const livres = state.tiles.filter((t) => !t.busy);
      const tile = livres[Math.floor(Math.random() * livres.length)];
      if (tile) {
        tile.busy = true;
        gsap.to(tile, {
          angle: tile.angle + (Math.random() < 0.5 ? -1 : 1) * (Math.PI / 2),
          duration: 1.6,
          ease: "power3.inOut",
          onUpdate: () => state.draw(tile),
          onComplete: () => { tile.busy = false; },
        });
      }
      idle = gsap.delayedCall(0.18 + Math.random() * 0.35, turnOne);
    };
    if (!reduced) turnOne();

    return () => {
      window.removeEventListener("resize", onResize);
      window.clearTimeout(resizeTimer);
      idle?.kill();
      gsap.killTweensOf(state.tiles);
      field.current = null;
    };
  }, []);

  useEffect(() => {
    const state = field.current;
    if (!state) return;

    if (!pulse) {
      state.tiles.forEach((tile) => {
        if (tile.tint === 0) return;
        gsap.to(tile, { tint: 0, duration: state.reduced ? 0 : 0.8, ease: "power2.out", overwrite: "auto", onUpdate: () => state.draw(tile) });
      });
      return;
    }

    state.ink = riskInk[pulse.risk];
    const anchor = document.getElementById("score-anchor")?.getBoundingClientRect();
    const ox = anchor ? anchor.left + anchor.width / 2 : window.innerWidth / 2;
    const oy = anchor ? anchor.top + anchor.height / 2 : window.innerHeight / 3;
    const alcance = Math.hypot(window.innerWidth, window.innerHeight);

    state.tiles.forEach((tile) => {
      const d = Math.hypot(tile.col * TILE + TILE / 2 - ox, tile.row * TILE + TILE / 2 - oy);
      const tint = Math.max(0, 1 - d / 560) ** 1.4;
      if (state.reduced) {
        tile.tint = tint;
        state.draw(tile);
        return;
      }
      const delay = (d / alcance) * 1.1;
      tile.busy = true;
      gsap.to(tile, {
        angle: Math.round(tile.angle / (Math.PI / 2)) * (Math.PI / 2) + Math.PI / 2,
        tint,
        duration: 0.9,
        delay,
        ease: "power2.inOut",
        overwrite: "auto",
        onUpdate: () => state.draw(tile),
        onComplete: () => { tile.busy = false; },
      });
    });
  }, [pulse?.key, pulse?.risk]);

  return (
    <div className="tile-field" aria-hidden="true">
      <canvas ref={canvas} />
    </div>
  );
}
