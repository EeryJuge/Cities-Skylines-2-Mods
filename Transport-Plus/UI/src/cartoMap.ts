// @ts-nocheck
/*
 * Rota sekmesinin haritası: açık renkli, kartografik bir altlık (su, yollar) ve üstünde hat güzergâhları.
 * Doğrudan <canvas> 2D ile çizilir (oyunun arayüz motoru satır içi SVG göstermiyor).
 * Dünya koordinatları metre; x doğu, z kuzey. Ekranda kuzey yukarıdadır.
 */
import { tw, chipText } from "./transitMap";

export const CARTO = {
  land: "#f1eee8", water: "#add3e6", minor: "#ffffff", minorCase: "#d6d0c4",
  major: "#fde7a3", majorCase: "#dcbd62", hwy: "#f6ad62", hwyCase: "#cf8436",
  ink: "#20242b", muted: "#5d6673", halo: "#ffffff", dim: "#8d949e"
};
const FONT = "Overpass, sans-serif";

export type View = { cx: number; cz: number; k: number }; // k: metre başına piksel

function polySet(src) {
  // src: [[x,z,x,z,...], ...] → tek dizi + her çoklu çizginin başlangıcı ve sınır kutusu
  const list = Array.isArray(src) ? src.filter(p => Array.isArray(p) && p.length >= 4) : [];
  let total = 0; for (const p of list) total += p.length;
  const pts = new Float32Array(total), off = new Int32Array(list.length + 1), bb = new Float32Array(list.length * 4);
  let o = 0;
  list.forEach((p, i) => {
    off[i] = o;
    let x0 = Infinity, z0 = Infinity, x1 = -Infinity, z1 = -Infinity;
    for (let k = 0; k + 1 < p.length; k += 2) {
      const x = +p[k], z = +p[k + 1];
      pts[o++] = x; pts[o++] = z;
      if (x < x0) x0 = x; if (x > x1) x1 = x; if (z < z0) z0 = z; if (z > z1) z1 = z;
    }
    bb[i * 4] = x0; bb[i * 4 + 1] = z0; bb[i * 4 + 2] = x1; bb[i * 4 + 3] = z1;
  });
  off[list.length] = o;
  return { pts, off, bb, n: list.length };
}

/** Oyundan gelen JSON'u çizime hazır modele çevirir. Geçersiz veride null döner. */
export function parseRoutes(d) {
  if (!d || !Array.isArray(d.lines)) return null;
  const lines = [];
  for (const l of d.lines) {
    if (!l || !Array.isArray(l.stops) || l.stops.length < 2) continue;
    const stops = l.stops.map(s => ({ n: String(s.n ?? ""), x: +s.x || 0, y: +s.y || 0, z: +s.z || 0, w: +s.w || 0, out: !!s.out }));
    const raw = Array.isArray(l.path) && l.path.length >= 4 ? l.path : [].concat(...stops.map(s => [s.x, s.z]), [stops[0].x, stops[0].z]);
    const path = Float32Array.from(raw, Number);
    let x0 = Infinity, z0 = Infinity, x1 = -Infinity, z1 = -Infinity;
    for (let i = 0; i + 1 < path.length; i += 2) { x0 = Math.min(x0, path[i]); x1 = Math.max(x1, path[i]); z0 = Math.min(z0, path[i + 1]); z1 = Math.max(z1, path[i + 1]); }
    // Ad verilmemişse: ilk durak – ona en uzak durak (hat bir döngü olduğu için "son durak" ilkine komşudur)
    let far = 0, fd = -1;
    stops.forEach((s, i) => { const dd = Math.hypot(s.x - stops[0].x, s.z - stops[0].z); if (dd > fd) { fd = dd; far = i; } });
    const color = /^#[0-9a-f]{3}([0-9a-f]{3})?$/i.test(l.color || "") ? l.color : "#888888";
    lines.push({
      id: String(l.id), code: String(l.code ?? ""), type: String(l.type || "bus"), color,
      name: l.name ? String(l.name) : `${stops[0].n} – ${stops[far].n}`,
      len: +l.len || 0, veh: +l.veh || 0, active: l.active !== false, sched: +l.sched || 0,
      stops, path, bb: [x0, z0, x1, z1]
    });
  }
  const roads = [0, 1, 2].map(i => polySet(d.roads && d.roads[i]));
  const water = (Array.isArray(d.water) ? d.water : [])
    .filter(w => w && Array.isArray(w.p) && w.p.length > 2)
    .sort((a, b) => (a.d || 0) - (b.d || 0))
    .map(w => ({ d: w.d || 0, p: Float32Array.from([].concat(...w.p), Number) }));
  return { city: String(d.city || ""), lines, roads, water, diag: d.diag || null };
}

/** Verilen hatların (şehir dışı duraklar hariç) duraklarını çevreleyen kutu. Durak yoksa yolların kutusu. */
export function boundsOf(M, lines) {
  let x0 = Infinity, z0 = Infinity, x1 = -Infinity, z1 = -Infinity;
  for (const l of lines) for (const s of l.stops) {
    if (s.out) continue;
    x0 = Math.min(x0, s.x); x1 = Math.max(x1, s.x); z0 = Math.min(z0, s.z); z1 = Math.max(z1, s.z);
  }
  if (x0 === Infinity) for (const r of M.roads) for (let i = 0; i < r.n; i++) {
    x0 = Math.min(x0, r.bb[i * 4]); z0 = Math.min(z0, r.bb[i * 4 + 1]); x1 = Math.max(x1, r.bb[i * 4 + 2]); z1 = Math.max(z1, r.bb[i * 4 + 3]);
  }
  if (x0 === Infinity) return null;
  return [x0, z0, x1, z1];
}

export function fitView(b, W, H, pad = 60): View {
  const w = Math.max(b[2] - b[0], 200), h = Math.max(b[3] - b[1], 200);
  const k = Math.min((W - 2 * pad) / w, (H - 2 * pad) / h);
  return { cx: (b[0] + b[2]) / 2, cz: (b[1] + b[3]) / 2, k: Math.max(0.004, Math.min(3, k)) };
}

export const toScreen = (v: View, W, H, x, z) => [W / 2 + (x - v.cx) * v.k, H / 2 - (z - v.cz) * v.k];
export const toWorld = (v: View, W, H, px, py) => [v.cx + (px - W / 2) / v.k, v.cz - (py - H / 2) / v.k];

function strokeSet(ctx, set, v, W, H, vb, minLen) {
  // Görünen çoklu çizgileri tek yolda toplar; tek stroke çağrısı binlerce yol parçasını hızlı çizer
  const { pts, off, bb, n } = set, k = v.k, ox = W / 2 - v.cx * k, oy = H / 2 + v.cz * k;
  ctx.beginPath();
  for (let i = 0; i < n; i++) {
    const j = i * 4;
    if (bb[j + 2] < vb[0] || bb[j] > vb[2] || bb[j + 3] < vb[1] || bb[j + 1] > vb[3]) continue;
    if (minLen > 0 && bb[j + 2] - bb[j] < minLen && bb[j + 3] - bb[j + 1] < minLen) continue;
    const a = off[i], e = off[i + 1];
    ctx.moveTo(ox + pts[a] * k, oy - pts[a + 1] * k);
    for (let p = a + 2; p < e; p += 2) ctx.lineTo(ox + pts[p] * k, oy - pts[p + 1] * k);
  }
  ctx.stroke();
}

function tracePath(ctx, path, v, W, H) {
  const k = v.k, ox = W / 2 - v.cx * k, oy = H / 2 + v.cz * k;
  ctx.beginPath();
  ctx.moveTo(ox + path[0] * k, oy - path[1] * k);
  for (let p = 2; p + 1 < path.length; p += 2) ctx.lineTo(ox + path[p] * k, oy - path[p + 1] * k);
}

const NICE = [20, 50, 100, 200, 500, 1000, 2000, 5000, 10000];

/**
 * Haritayı çizer.
 * lines: görünen hatlar; sel: seçili hat (ya da null); selStop: seçili durağın sırası (ya da -1);
 * u: arayüz ölçeği (1080p'de 1); names: durak adları yazılsın mı.
 */
export function drawCarto(ctx, M, v: View, W, H, o: { lines: any[]; sel: any; selStop: number; hover: string | null; u: number; names: boolean }) {
  const u = o.u || 1, C = CARTO, k = v.k;
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.globalAlpha = 1;
  ctx.fillStyle = C.land;
  ctx.fillRect(0, 0, W, H);
  const a = toWorld(v, W, H, 0, H), b = toWorld(v, W, H, W, 0);
  const vb = [a[0], a[1], b[0], b[1]];
  ctx.lineJoin = "round"; ctx.lineCap = "round";

  // su: çift derinlik su, tek derinlik ada
  for (const w of M.water) {
    tracePath(ctx, w.p, v, W, H);
    ctx.closePath();
    ctx.fillStyle = w.d % 2 ? C.land : C.water;
    ctx.fill();
  }

  // yollar: önce kenar çizgisi (yalnızca yakından), sonra dolgu; sokak → cadde → otoyol
  const wMinor = Math.max(1, Math.min(7 * u, 9 * k)), wMajor = Math.max(1.6 * u, Math.min(12 * u, 18 * k)), wHwy = Math.max(2 * u, Math.min(14 * u, 22 * k));
  const showMinor = k > 0.045, casing = k > 0.16;
  const minLen = k < 0.09 ? 3 / k : 0; // uzaktan bakarken birkaç pikselden kısa parçaları atla
  if (showMinor) {
    if (casing) { ctx.strokeStyle = C.minorCase; ctx.lineWidth = wMinor + 2 * u; strokeSet(ctx, M.roads[2], v, W, H, vb, 0); }
    ctx.strokeStyle = casing ? C.minor : "#ffffff"; ctx.lineWidth = wMinor; strokeSet(ctx, M.roads[2], v, W, H, vb, minLen);
  }
  ctx.strokeStyle = C.majorCase; ctx.lineWidth = wMajor + 2 * u; strokeSet(ctx, M.roads[1], v, W, H, vb, 0);
  ctx.strokeStyle = C.hwyCase; ctx.lineWidth = wHwy + 2 * u; strokeSet(ctx, M.roads[0], v, W, H, vb, 0);
  ctx.strokeStyle = C.major; ctx.lineWidth = wMajor; strokeSet(ctx, M.roads[1], v, W, H, vb, 0);
  ctx.strokeStyle = C.hwy; ctx.lineWidth = wHwy; strokeSet(ctx, M.roads[0], v, W, H, vb, 0);

  // hatlar: seçili olmayanlar ince (seçim varken soluk), seçili olan en üstte kalın
  const sel = o.sel;
  const visible = l => !(l.bb[2] < vb[0] || l.bb[0] > vb[2] || l.bb[3] < vb[1] || l.bb[1] > vb[3]);
  for (const l of o.lines) {
    if (l === sel || !visible(l)) continue;
    const hot = o.hover === l.id;
    ctx.globalAlpha = sel ? (hot ? 0.75 : 0.28) : 1;
    tracePath(ctx, l.path, v, W, H);
    ctx.strokeStyle = C.halo; ctx.lineWidth = (hot ? 6.5 : 5) * u; ctx.stroke();
    ctx.strokeStyle = l.color; ctx.lineWidth = (hot ? 4.5 : 3) * u; ctx.stroke();
  }
  ctx.globalAlpha = 1;
  if (!sel && k > 0.07) {
    for (const l of o.lines) {
      if (!visible(l)) continue;
      ctx.fillStyle = C.halo; ctx.strokeStyle = l.color; ctx.lineWidth = 1.6 * u;
      for (const s of l.stops) {
        const [x, y] = toScreen(v, W, H, s.x, s.z);
        if (x < -10 || y < -10 || x > W + 10 || y > H + 10) continue;
        ctx.beginPath(); ctx.arc(x, y, 3 * u, 0, Math.PI * 2); ctx.fill(); ctx.stroke();
      }
    }
  }

  if (sel) {
    tracePath(ctx, sel.path, v, W, H);
    ctx.strokeStyle = C.halo; ctx.lineWidth = 9 * u; ctx.stroke();
    ctx.strokeStyle = sel.color; ctx.lineWidth = 5.5 * u; ctx.stroke();

    // duraklar: sıra numaralı iğneler. Üst üste binenler küçük nokta olur.
    const R = 9 * u, pins = [], boxes = [];
    const ink = chipText(sel.color);
    sel.stops.forEach((s, i) => {
      const [x, y] = toScreen(v, W, H, s.x, s.z);
      const on = x > -40 && y > -40 && x < W + 40 && y < H + 40;
      const big = on && (i === o.selStop || pins.every(p => !p.big || Math.hypot(p.x - x, p.y - y) > R * 2.1));
      pins.push({ x, y, i, s, big, on });
    });
    for (const p of pins) {
      if (!p.on || p.big) continue;
      ctx.beginPath(); ctx.arc(p.x, p.y, 3.4 * u, 0, Math.PI * 2);
      ctx.fillStyle = C.halo; ctx.fill(); ctx.strokeStyle = sel.color; ctx.lineWidth = 2 * u; ctx.stroke();
    }
    ctx.textAlign = "center"; ctx.textBaseline = "alphabetic";
    for (const p of pins) {
      if (!p.on || !p.big) continue;
      const cur = p.i === o.selStop;
      if (cur) { ctx.beginPath(); ctx.arc(p.x, p.y, R + 5 * u, 0, Math.PI * 2); ctx.fillStyle = C.halo; ctx.fill(); ctx.strokeStyle = C.ink; ctx.lineWidth = 2 * u; ctx.stroke(); }
      ctx.beginPath(); ctx.arc(p.x, p.y, R, 0, Math.PI * 2);
      ctx.fillStyle = sel.color; ctx.fill(); ctx.strokeStyle = C.halo; ctx.lineWidth = 2 * u; ctx.stroke();
      const t = String(p.i + 1), fs = (t.length > 2 ? 7.5 : 9.5) * u;
      ctx.font = `700 ${fs}px ${FONT}`; ctx.fillStyle = ink;
      ctx.fillText(t, p.x, p.y + fs * 0.36);
      boxes.push({ x: p.x - R, y: p.y - R, w: 2 * R, h: 2 * R });
    }
    if (o.names) {
      const fs = 12 * u;
      ctx.textAlign = "left";
      const order = pins.filter(p => p.on && p.big).sort((p, q) => (q.i === o.selStop ? 1 : 0) - (p.i === o.selStop ? 1 : 0));
      const done = new Set();
      for (const p of order) {
        const name = p.s.n.length > 28 ? p.s.n.slice(0, 27) + "…" : p.s.n;
        if (!name || (done.has(name) && p.i !== o.selStop)) continue; // caddenin iki yanındaki aynı adlı durak bir kez yazılır
        const bold = p.i === o.selStop, w = tw(name, fs, bold ? 700 : 500) + 4 * u, h = fs * 1.25, g = R + 4 * u;
        const tries = [[p.x + g, p.y - h / 2], [p.x - g - w, p.y - h / 2], [p.x - w / 2, p.y - g - h], [p.x - w / 2, p.y + g]];
        let box = null;
        for (const [x, y] of tries) {
          const c = { x, y, w, h };
          if (boxes.every(q => c.x > q.x + q.w || q.x > c.x + c.w || c.y > q.y + q.h || q.y > c.y + c.h)) { box = c; break; }
        }
        if (!box) { if (!bold) continue; box = { x: tries[0][0], y: tries[0][1], w, h }; }
        boxes.push(box); done.add(name);
        ctx.font = `${bold ? 700 : 500} ${fs}px ${FONT}`;
        ctx.strokeStyle = C.halo; ctx.lineWidth = 3.5 * u; ctx.lineJoin = "round";
        ctx.strokeText(name, box.x + 2 * u, box.y + fs);
        ctx.fillStyle = C.ink; ctx.fillText(name, box.x + 2 * u, box.y + fs);
      }
    }
  }

  // ölçek çubuğu
  const target = 110 * u / k;
  let m = NICE[NICE.length - 1]; for (const n of NICE) if (n >= target * 0.6) { m = n; break; }
  const bw = m * k, bx = 14 * u, by = H - 16 * u;
  ctx.strokeStyle = C.halo; ctx.lineWidth = 5 * u; ctx.lineCap = "butt";
  ctx.beginPath(); ctx.moveTo(bx, by); ctx.lineTo(bx + bw, by); ctx.stroke();
  ctx.strokeStyle = C.ink; ctx.lineWidth = 2 * u;
  ctx.beginPath(); ctx.moveTo(bx, by - 4 * u); ctx.lineTo(bx, by); ctx.lineTo(bx + bw, by); ctx.lineTo(bx + bw, by - 4 * u); ctx.stroke();
  const label = m >= 1000 ? (m / 1000) + " km" : m + " m";
  ctx.font = `600 ${11 * u}px ${FONT}`; ctx.textAlign = "left";
  ctx.strokeStyle = C.halo; ctx.lineWidth = 3 * u; ctx.strokeText(label, bx + 4 * u, by - 6 * u);
  ctx.fillStyle = C.ink; ctx.fillText(label, bx + 4 * u, by - 6 * u);
}

/** Seçili hattın (px, py) noktasına en yakın durağı; yoksa -1. */
export function hitStop(line, v: View, W, H, px, py, r) {
  let best = -1, bd = r;
  line.stops.forEach((s, i) => {
    const [x, y] = toScreen(v, W, H, s.x, s.z), d = Math.hypot(x - px, y - py);
    if (d < bd) { bd = d; best = i; }
  });
  return best;
}

/** (px, py) noktasının r piksel yakınından geçen hat; yoksa null. */
export function hitLine(lines, v: View, W, H, px, py, r) {
  const [wx, wz] = toWorld(v, W, H, px, py), rw = r / v.k;
  let best = null, bd = rw;
  for (const l of lines) {
    if (wx < l.bb[0] - rw || wx > l.bb[2] + rw || wz < l.bb[1] - rw || wz > l.bb[3] + rw) continue;
    const p = l.path;
    for (let i = 0; i + 3 < p.length; i += 2) {
      const ax = p[i], az = p[i + 1], dx = p[i + 2] - ax, dz = p[i + 3] - az, L2 = dx * dx + dz * dz || 1;
      const t = Math.max(0, Math.min(1, ((wx - ax) * dx + (wz - az) * dz) / L2));
      const d = Math.hypot(ax + dx * t - wx, az + dz * t - wz);
      if (d < bd) { bd = d; best = l; }
    }
  }
  return best;
}
