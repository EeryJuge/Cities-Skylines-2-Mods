// @ts-nocheck
/*
 * Transport+ harita çizici çekirdeği.
 * Prototipteki kodun aynısı; DOM'a dokunmaz, sadece SVG metni üretir.
 * Oyunun arayüz motoru (Coherent Gameface) SVG'nin her özelliğini desteklemediği için
 * <style>, clipPath, <use> ve paint-order kullanılmaz: tüm renkler doğrudan öznitelik olarak yazılır.
 */

export type LineType = "subway" | "train" | "tram" | "ship" | "bus";
export interface Palette { land: string; water: string; waterInk: string; ink: string; muted: string; grid: string; frame: string; }

export const DARK: Palette = {
  land: "#16191f", water: "#13283c", waterInk: "#7fb0de", ink: "#e9ecf1",
  muted: "#9aa3b0", grid: "#1e232b", frame: "#3a424e"
};
// Kaydedilen resim için açık tema (istasyonlardaki basılı haritalar gibi)
export const LIGHT: Palette = {
  land: "#ffffff", water: "#d3e7f7", waterInk: "#4a86bd", ink: "#1c2027",
  muted: "#677080", grid: "#e8edf3", frame: "#b9c3cf"
};

export const TYPES = {
  train:  { w: 7.5, rank: 0 },
  subway: { w: 6.5, rank: 1 },
  tram:   { w: 4,   rank: 2 },
  ship:   { w: 2,   rank: 3 },
  bus:    { w: 2.6, rank: 4 }
};
export const TYPE_ORDER: LineType[] = ["subway", "train", "tram", "ship", "bus"];
/** İstasyon adları: 0 = hepsi, 1 = yalnızca aktarma istasyonları, 2 = hiçbiri */
export type LabelLevel = 0 | 1 | 2;
const MAX_NAME = 26;
const shortName = (n: string) => n.length > MAX_NAME ? n.slice(0, MAX_NAME - 1).trimEnd() + "…" : n;

const SC = 0.12, CELL = 120, GAP = 1.4, BR = 10;
const FONT = "Overpass, sans-serif";
const hyp = Math.hypot;
const f1 = p => p[0].toFixed(1) + "," + p[1].toFixed(1);
const esc = s => String(s).replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
const lum = hex => { let h = hex.replace("#", ""); if (h.length === 3) h = h.split("").map(c => c + c).join(""); const n = parseInt(h.slice(0, 6), 16); return (0.299 * (n >> 16 & 255) + 0.587 * (n >> 8 & 255) + 0.114 * (n & 255)) / 255; };
export const chipText = c => lum(c) > 0.62 ? "#1c2027" : "#ffffff";

// Yazı genişliği tahmini. Canvas measureText oyunun arayüz motorunda güvenilir değil.
export const tw = (t, fs, wt) => {
  let w = 0;
  for (const ch of t) w += /[ilIjtf.,:;'|!·]/.test(ch) ? 0.3 : /[mwMWÇĞŞÖÜ@]/.test(ch) ? 0.82 : /[A-ZÂÎÛ0-9]/.test(ch) ? 0.64 : ch === " " ? 0.28 : 0.53;
  return w * fs * (wt >= 600 ? 1.05 : 1);
};

function densify(poly, maxLen) {
  const out = [];
  for (let i = 0; i < poly.length; i++) {
    const a = poly[i], b = poly[(i + 1) % poly.length];
    const n = Math.max(1, Math.ceil(hyp(b[0] - a[0], b[1] - a[1]) / maxLen));
    for (let k = 0; k < n; k++) out.push([a[0] + (b[0] - a[0]) * k / n, a[1] + (b[1] - a[1]) * k / n]);
  }
  return out;
}

export class TransitMap {
  M: any = null; G: any = null; FULL: any = null;
  anchor = 0.06; eq = 0.5;
  hidden = new Set<string>();
  palette: Palette = DARK;

  /* ---------------------------------------------------------------- veri */
  load(d) {
    if (!d || !Array.isArray(d.stations) || !Array.isArray(d.lines)) { this.M = null; return; }
    const S = new Map();
    for (const s of d.stations) {
      if (!s || s.id == null) continue;
      const x = +s.x, z = +s.z;
      if (!isFinite(x) || !isFinite(z)) continue;
      S.set(String(s.id), { id: String(s.id), name: String(s.name ?? s.id), out: !!s.out, wx: x, wy: +s.y || 0, wz: z, gx: x * SC, gy: -z * SC, sx: x * SC, sy: -z * SC });
    }
    pullInOutside(S);
    const lines = [], taken = new Set();
    d.lines.forEach((l, i) => {
      const T = TYPES[l && l.type];
      if (!T) return;
      let stops = (l.stops || []).map(String).filter(id => S.has(id));
      if (l.loop && stops.length > 2 && stops[0] !== stops[stops.length - 1]) stops.push(stops[0]);
      stops = stops.filter((id, k, a) => k === 0 || a[k - 1] !== id);
      if (stops.length < 2) return;
      const color = /^#[0-9a-f]{3}([0-9a-f]{3})?$/i.test(l.color || "") ? l.color : "#888888";
      // code: rozette yazan ad (iki hatta aynı olabilir). id: benzersiz anahtar.
      const code = String(l.id);
      let id = code; for (let k = 2; taken.has(id); k++) id = code + "#" + k;
      taken.add(id);
      lines.push({ id, code, type: l.type, name: l.name || `${S.get(stops[0]).name} – ${S.get(stops[stops.length - 1]).name}`, color, stops, w: T.w, rank: T.rank, order: i });
    });
    const used = new Set(); lines.forEach(l => l.stops.forEach(id => used.add(id)));
    for (const id of Array.from(S.keys())) if (!used.has(id)) S.delete(id);
    if (!S.size) { this.M = null; return; }
    // Su: [[x,z],...] (derinlik 0) ya da oyundan gelen { d: iç içelik, p: [[x,z],...] }. Tek derinlik = ada.
    const wsrc = (d.water || []).map(w => Array.isArray(w) ? { d: 0, p: w } : w)
      .filter(w => w && Array.isArray(w.p) && w.p.length > 2)
      .sort((a, b) => (a.d || 0) - (b.d || 0));
    const waterD = wsrc.map(w => w.d || 0);
    const water = wsrc.map(w => densify(w.p.map(([x, z]) => [x * SC, -z * SC]), 28));
    const labels = (d.labels || []).filter(l => l && l.text).map(l => ({ text: String(l.text), gx: l.x * SC, gy: -l.z * SC, rot: +l.rot || 0 }));
    // Aktarma: farklı hatlardaki yakın iki istasyon (ör. tren istasyonu + yanındaki metro)
    const transfers = (d.transfers || []).map(t => ({ a: String(t.a), b: String(t.b), m: +t.m || 0 }))
      .filter(t => t.a !== t.b && S.has(t.a) && S.has(t.b));
    this.M = { city: String(d.city || ""), S, lines, water, waterD, waterS: water, labels, transfers, diag: d.diag || null };
    this.layout();
  }

  /* ---------------------------------------------------------------- şematik yerleşim */
  layout() {
    const M = this.M; if (!M) return;
    const ids = Array.from(M.S.keys()), n = ids.length, ix = new Map(ids.map((id, i) => [id, i]));
    const gx = ids.map(id => M.S.get(id).gx), gy = ids.map(id => M.S.get(id).gy);
    const px = Float64Array.from(gx), py = Float64Array.from(gy);
    const rail = new Array(n).fill(false), seen = new Set(), E = [];
    // Yalnızca görünen türler yerleşime girer: gizli otobüs hatları ne haritayı büyütür ne de yerleşimi yavaşlatır
    const used = new Array(n).fill(false);
    for (const l of M.lines) {
      if (this.hidden.has(l.type)) continue;
      for (const id of l.stops) used[ix.get(id)] = true;
      if (l.type === "ship") continue;
      for (let k = 0; k < l.stops.length - 1; k++) {
        const u = ix.get(l.stops[k]), v = ix.get(l.stops[k + 1]);
        rail[u] = rail[v] = true;
        const key = u < v ? u + "_" + v : v + "_" + u;
        if (!seen.has(key)) { seen.add(key); E.push([u, v]); }
      }
    }
    // Aktarma çiftleri yerleşimde birbirine yakın tutulur, birbirini itmez
    const TR = [], trKey = new Set();
    for (const t of M.transfers || []) {
      const u = ix.get(t.a), v = ix.get(t.b);
      if (u == null || v == null || !rail[u] || !rail[v]) continue;
      TR.push([u, v]); trKey.add(Math.min(u, v) * n + Math.max(u, v));
    }
    const nb = Array.from({ length: n }, () => new Set());
    for (const [u, v] of E) { nb[u].add(v); nb[v].add(u); }
    const glen = E.map(([u, v]) => hyp(gx[v] - gx[u], gy[v] - gy[u]));
    const lens = glen.slice().sort((a, b) => a - b);
    const L = lens.length ? lens[lens.length >> 1] : 60;
    // Hedef uzunluk coğrafi uzunluğa yakın kalır: çok kısa aralıklar okunabilir olacak kadar uzar, çok uzunlar biraz kısalır.
    // (0.2'ye kadar her aralık ortancaya çekiliyordu; kısa tramvay hatları şişip başka hatların öbür yanına kayıyordu.)
    const Lmin = Math.max(L * 0.4, 18), Lmax = Math.max(L * 1.8, Lmin * 2);
    const tlen = glen.map(g => g < Lmin ? g + (Lmin - g) * this.eq * 1.6 : g > Lmax ? Lmax + (g - Lmax) * 0.4 : g);
    const deg2 = []; nb.forEach((s, i) => { if (s.size === 2) { const [a, b] = Array.from(s); deg2.push([i, a, b]); } });

    // Kesişme koruması: coğrafyada kesişmeyen iki hat parçası şematikte de kesişemez.
    // Böylece bir istasyon ya da hat başka bir hattın öbür yanına geçemez; hatların birbirine göre konumu korunur.
    const NE = E.length, inc = Array.from({ length: n }, () => []);
    E.forEach(([u, v], e) => { inc[u].push(e); inc[v].push(e); });
    const geoCross = new Set();
    for (let a = 0; a < NE; a++) for (let b = a + 1; b < NE; b++) {
      const [p, q] = E[a], [r, t] = E[b];
      if (p === r || p === t || q === r || q === t) continue;
      if (cross(gx[p], gy[p], gx[q], gy[q], gx[r], gy[r], gx[t], gy[t])) geoCross.add(a * NE + b);
    }
    // Izgara: her parça kendi sınır kutusunun hücrelerine girer. Kesişen iki parça mutlaka ortak bir hücrededir;
    // sorgu, bu turda yerinden oynamış olabilecek parçalar için en büyük adım kadar pay bırakır.
    const GS = Math.max(L * 2, 40), grid = new Map<number, number[]>();
    let pad = 0;
    const buildGrid = () => {
      grid.clear();
      for (let e = 0; e < NE; e++) {
        const u = E[e][0], v = E[e][1];
        const i0 = Math.floor(Math.min(px[u], px[v]) / GS), i1 = Math.floor(Math.max(px[u], px[v]) / GS);
        const j0 = Math.floor(Math.min(py[u], py[v]) / GS), j1 = Math.floor(Math.max(py[u], py[v]) / GS);
        for (let i = i0; i <= i1; i++) for (let j = j0; j <= j1; j++) {
          const key = i * 92821 + j; let c = grid.get(key); if (!c) grid.set(key, c = []); c.push(e);
        }
      }
    };
    const stamp = new Int32Array(NE); let tick = 0;
    // i düğümü (x, y) noktasına giderse yeni bir kesişme oluşur mu?
    const blocked = (i, x, y) => {
      for (const e of inc[i]) {
        const o = E[e][0] === i ? E[e][1] : E[e][0], ox = px[o], oy = py[o];
        tick++;
        const i0 = Math.floor((Math.min(x, ox) - pad) / GS), i1 = Math.floor((Math.max(x, ox) + pad) / GS);
        const j0 = Math.floor((Math.min(y, oy) - pad) / GS), j1 = Math.floor((Math.max(y, oy) + pad) / GS);
        for (let ci = i0; ci <= i1; ci++) for (let cj = j0; cj <= j1; cj++) {
          const c = grid.get(ci * 92821 + cj); if (!c) continue;
          for (let k = 0; k < c.length; k++) {
            const f = c[k];
            if (f === e || stamp[f] === tick) continue;
            stamp[f] = tick;
            const r = E[f][0], t = E[f][1];
            if (r === i || t === i || r === o || t === o) continue;
            if (geoCross.has(Math.min(e, f) * NE + Math.max(e, f))) continue;
            if (cross(x, y, ox, oy, px[r], py[r], px[t], py[t])) return true;
          }
        }
      }
      return false;
    };

    const fx = new Float64Array(n), fy = new Float64Array(n), cnt = new Float64Array(n);
    let nr = 0; for (let i = 0; i < n; i++) if (rail[i]) nr++;
    const IT = nr > 900 ? 120 : nr > 450 ? 220 : nr > 150 ? 400 : 700, Q = Math.PI / 4, Dmin = Math.min(L * 0.5, Lmin * 1.2);
    let calm = 0;
    for (let it = 0; it < IT; it++) {
      const prog = it / IT, cool = 1 - 0.8 * prog, anc = this.anchor * (prog > 0.75 ? 0.5 : 1);
      fx.fill(0); fy.fill(0); cnt.fill(0);
      E.forEach(([u, v], e) => {
        const dx = px[v] - px[u], dy = py[v] - py[u], len = hyp(dx, dy) || 1e-6;
        const a = Math.round(Math.atan2(dy, dx) / Q) * Q, tl = tlen[e];
        const cx = (Math.cos(a) * tl - dx) * 0.5, cy = (Math.sin(a) * tl - dy) * 0.5;
        fx[u] -= cx; fy[u] -= cy; fx[v] += cx; fy[v] += cy; cnt[u]++; cnt[v]++;
      });
      for (let i = 0; i < n; i++) if (cnt[i]) { fx[i] /= cnt[i]; fy[i] /= cnt[i]; }
      for (const [i, a, b] of deg2) {
        const ax = px[a] - px[i], ay = py[a] - py[i], bx = px[b] - px[i], by = py[b] - py[i];
        const c = (ax * bx + ay * by) / ((hyp(ax, ay) * hyp(bx, by)) || 1);
        if (c < -0.55) { fx[i] += ((px[a] + px[b]) / 2 - px[i]) * 0.15; fy[i] += ((py[a] + py[b]) / 2 - py[i]) * 0.15; }
      }
      const Lt = Math.min(L * 0.35, 22);
      for (const [u, v] of TR) {
        const dx = px[v] - px[u], dy = py[v] - py[u], d = hyp(dx, dy) || 1e-6;
        if (d > Lt) { const k = (d - Lt) / d * 0.2; fx[u] += dx * k; fy[u] += dy * k; fx[v] -= dx * k; fy[v] -= dy * k; }
      }
      for (let i = 0; i < n; i++) if (rail[i]) { fx[i] += (gx[i] - px[i]) * anc; fy[i] += (gy[i] - py[i]) * anc; }
      for (let i = 0; i < n; i++) {
        if (!rail[i]) continue;
        for (let j = i + 1; j < n; j++) {
          if (!rail[j]) continue;
          const dx = px[j] - px[i], dy = py[j] - py[i], d = hyp(dx, dy);
          if (d < Dmin && !trKey.has(i * n + j)) { const k = (Dmin - d) / (d || 1) * 0.25; fx[i] -= dx * k; fy[i] -= dy * k; fx[j] += dx * k; fy[j] += dy * k; }
        }
      }
      // Düğümler tek tek taşınır; yeni bir kesişme yaratacak adım önce yarıya iner, yine olmazsa atlanır
      buildGrid();
      pad = 0;
      for (let i = 0; i < n; i++) if (rail[i]) pad = Math.max(pad, Math.abs(fx[i]), Math.abs(fy[i]));
      pad = pad * cool + 1;
      for (let i = 0; i < n; i++) {
        if (!rail[i]) continue;
        let sx = fx[i] * cool, sy = fy[i] * cool;
        for (let tries = 0; tries < 3; tries++, sx *= 0.5, sy *= 0.5) {
          if (sx * sx + sy * sy < 1e-6) break;
          if (!blocked(i, px[i] + sx, py[i] + sy)) { px[i] += sx; py[i] += sy; break; }
        }
      }
      // Yerleşim oturduysa erken bitir (en büyük adım ortanca aralığın binde birinden küçük)
      if (it > IT * 0.4) { calm = pad - 1 < L * 0.001 ? calm + 1 : 0; if (calm > 15) break; }
    }
    const disp = (x, y) => {
      let sw = 0, sx = 0, sy = 0;
      for (let i = 0; i < n; i++) {
        if (!rail[i]) continue;
        const dx = x - gx[i], dy = y - gy[i], d2 = dx * dx + dy * dy + 25, w = 1 / (d2 * Math.sqrt(d2));
        sw += w; sx += w * (px[i] - gx[i]); sy += w * (py[i] - gy[i]);
      }
      return sw ? [sx / sw, sy / sw] : [0, 0];
    };
    for (let i = 0; i < n; i++) if (!rail[i]) { const [dx, dy] = disp(gx[i], gy[i]); px[i] = gx[i] + dx; py[i] = gy[i] + dy; }
    ids.forEach((id, i) => { const s = M.S.get(id); s.sx = px[i]; s.sy = py[i]; });
    M.waterS = M.water.map(poly => poly.map(([x, y]) => { const [dx, dy] = disp(x, y); return [x + dx, y + dy]; }));
    M.labels.forEach(l => { const [dx, dy] = disp(l.gx, l.gy); l.sx = l.gx + dx; l.sy = l.gy + dy; });

    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    ids.forEach((id, i) => { if (!used[i]) return; const s = M.S.get(id); for (const [x, y] of [[s.gx, s.gy], [s.sx, s.sy]]) { x0 = Math.min(x0, x); x1 = Math.max(x1, x); y0 = Math.min(y0, y); y1 = Math.max(y1, y); } });
    if (x0 === Infinity) { x0 = y0 = -100; x1 = y1 = 100; }
    x0 -= 90; x1 += 140; y0 -= 60; y1 += 60;
    const cols = Math.max(3, Math.ceil((x1 - x0) / CELL)), rows = Math.max(3, Math.ceil((y1 - y0) / CELL));
    const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
    M.frame = { x: cx - cols * CELL / 2, y: cy - rows * CELL / 2, w: cols * CELL, h: rows * CELL, cols, rows };
    this.FULL = { x: M.frame.x - 36, y: M.frame.y - 66, w: M.frame.w + 72, h: M.frame.h + 104 };
  }

  /* ---------------------------------------------------------------- geometri */
  geometry(t, final) {
    const M = this.M; if (!M) { this.G = null; return; }
    const mode = final && t >= 1 ? "octi" : "straight";
    const P = new Map();
    M.S.forEach(s => P.set(s.id, [s.gx + (s.sx - s.gx) * t, s.gy + (s.sy - s.gy) * t]));
    const vis = M.lines.filter(l => !this.hidden.has(l.type));
    const rail = vis.filter(l => l.type !== "ship").sort((a, b) => a.rank - b.rank || a.order - b.order);
    const ships = vis.filter(l => l.type === "ship");
    const E = new Map();
    const ekey = (a, b) => a < b ? a + "\u0001" + b : b + "\u0001" + a;
    for (const l of rail) for (let k = 0; k < l.stops.length - 1; k++) {
      const a = l.stops[k], b = l.stops[k + 1], key = ekey(a, b);
      let e = E.get(key); if (!e) { e = { a: a < b ? a : b, b: a < b ? b : a, lines: [] }; E.set(key, e); }
      if (e.lines.indexOf(l) < 0) e.lines.push(l);
    }
    E.forEach(e => {
      e.lines.sort((x, y) => x.rank - y.rank || x.order - y.order);
      const tot = e.lines.reduce((s, l) => s + l.w, 0) + GAP * (e.lines.length - 1);
      let c = -tot / 2; e.off = new Map();
      for (const l of e.lines) { e.off.set(l.id, c + l.w / 2); c += l.w + GAP; }
      e.route = route(P.get(e.a), P.get(e.b), mode);
    });
    const LG = [], FG = [], segs = [], SP = new Map();
    const sp = id => { let o = SP.get(id); if (!o) { o = { pts: [], ships: [] }; SP.set(id, o); } return o; };
    for (const l of rail) {
      const P0 = [], offs = [], sv = [];
      for (let k = 0; k < l.stops.length - 1; k++) {
        const a = l.stops[k], e = E.get(ekey(a, l.stops[k + 1]));
        let r = e.route, o = e.off.get(l.id);
        if (e.a !== a) { r = r.slice().reverse(); o = -o; }
        if (k === 0) { P0.push(r[0]); sv.push(0); }
        for (let j = 1; j < r.length; j++) { P0.push(r[j]); offs.push(o); }
        sv.push(P0.length - 1);
      }
      const { out, vmap } = offsetLine(P0, offs);
      LG.push({ l, pts: out });
      for (let i = 0; i < out.length - 1; i++) segs.push([out[i][0], out[i][1], out[i + 1][0], out[i + 1][1], l.w / 2]);
      l.stops.forEach((id, k) => {
        const vi = sv[k], ps = vmap[vi].map(j => out[j]);
        const p = [ps.reduce((s, q) => s + q[0], 0) / ps.length, ps.reduce((s, q) => s + q[1], 0) / ps.length];
        const a = P0[Math.max(vi - 1, 0)], b = P0[Math.min(vi + 1, P0.length - 1)];
        const tx = b[0] - a[0], ty = b[1] - a[1], tl = hyp(tx, ty) || 1;
        sp(id).pts.push({ x: p[0], y: p[1], tx: tx / tl, ty: ty / tl, l, k });
      });
    }
    for (const l of ships) {
      const pts = l.stops.map(id => P.get(id));
      FG.push({ l, pts });
      for (let i = 0; i < pts.length - 1; i++) segs.push([pts[i][0], pts[i][1], pts[i + 1][0], pts[i + 1][1], 1.5]);
      l.stops.forEach(id => { const o = sp(id); if (o.ships.indexOf(l) < 0) o.ships.push(l); });
    }
    const ST = [], STm = new Map();
    SP.forEach((o, id) => {
      const s = M.S.get(id), pos = P.get(id);
      const lines = Array.from(new Set(o.pts.map(p => p.l)));
      const kind = lines.length >= 2 ? "hub" : lines.length === 1 ? "stop" : "pier";
      let p1 = null, p2 = null, cx = pos[0], cy = pos[1], r = 5;
      if (o.pts.length) {
        let best = -1;
        for (const a of o.pts) for (const b of o.pts) { const d = hyp(a.x - b.x, a.y - b.y); if (d > best) { best = d; p1 = a; p2 = b; } }
        cx = (p1.x + p2.x) / 2; cy = (p1.y + p2.y) / 2;
        r = kind === "hub" ? best / 2 + 7 : lines[0].w / 2 + 5;
      }
      const e = { s, id, kind, cx, cy, r, p1, p2, pts: o.pts, lines, ships: o.ships,
        tx: o.pts[0] ? o.pts[0].tx : 1, ty: o.pts[0] ? o.pts[0].ty : 0, side: 1, lab: null,
        busOnly: lines.length > 0 && !o.ships.length && lines.every(l => l.type === "bus"),
        ids: lines.concat(o.ships).map(l => l.id) };
      ST.push(e); STm.set(id, e);
    });
    // Aktarma bağlantıları
    const XF = [];
    for (const t of M.transfers || []) {
      const A = STm.get(t.a), B = STm.get(t.b);
      if (!A || !B) continue;
      XF.push({ A, B, m: t.m });
      (A.xfer = A.xfer || []).push({ other: B, m: t.m });
      (B.xfer = B.xfer || []).push({ other: A, m: t.m });
      segs.push([A.cx, A.cy, B.cx, B.cy, 3.5]);
    }
    for (const e of ST) if (e.xfer && e.kind === "stop") e.r = Math.max(e.r, 9);
    this.G = { t, mode, LG, FG, ST, STm, segs, badges: [], final, XF };
    if (final) { this.placeBadges(); this.placeLabels(); }
  }

  placeBadges() {
    const { LG, STm, ST, segs, badges } = this.G;
    for (const { l } of LG) {
      const ends = l.stops[0] === l.stops[l.stops.length - 1] ? [0] : [0, l.stops.length - 1];
      for (const end of ends) {
        const S0 = STm.get(l.stops[end]); if (!S0) continue;
        const pt = S0.pts.find(p => p.l === l && p.k === end); if (!pt) continue;
        const dx = end ? pt.tx : -pt.tx, dy = end ? pt.ty : -pt.ty;
        const base = S0.kind === "hub" ? [S0.cx, S0.cy] : [pt.x, pt.y];
        const dist = (S0.kind === "hub" ? S0.r : l.w / 2) + BR + 4;
        let pos = null;
        outer: for (const ang of [0, 40, -40, 80, -80, 130, -130, 180]) {
          const a = ang * Math.PI / 180, ux = dx * Math.cos(a) - dy * Math.sin(a), uy = dx * Math.sin(a) + dy * Math.cos(a);
          for (const mult of [1, 1.9]) {
            const c = [base[0] + ux * dist * mult, base[1] + uy * dist * mult];
            if (badges.every(b => hyp(b.x - c[0], b.y - c[1]) > 2 * BR + 2)
              && ST.every(o => o === S0 || hyp(o.cx - c[0], o.cy - c[1]) > o.r + BR + 2)
              && !segs.some(sg => segDist(sg, c) < BR + sg[4] + 1)) { pos = c; break outer; }
          }
        }
        if (!pos) pos = [base[0] + dx * dist, base[1] + dy * dist];
        badges.push({ x: pos[0], y: pos[1], l });
      }
    }
  }

  placeLabels() {
    const { ST, segs, badges } = this.G, F = this.M.frame, placed = [];
    const bBoxes = badges.map(b => ({ x: b.x - BR, y: b.y - BR, w: 2 * BR, h: 2 * BR }));
    const order = ST.slice().sort((a, b) => (+(b.kind === "hub")) - (+(a.kind === "hub")) || b.pts.length - a.pts.length);
    for (const s of order) {
      const bold = s.kind === "hub", fs = 12;
      const textW = tw(shortName(s.s.name), fs, bold ? 600 : 400), w = textW + (s.ships.length ? 17 : 0), h = fs * 1.12;
      let best = null;
      for (const [ux, uy, pref] of DIRS) {
        const d = s.r + 3, ax = s.cx + ux * d, ay = s.cy + uy * d;
        const box = { x: ux > 0.3 ? ax : ux < -0.3 ? ax - w : ax - w / 2, y: uy > 0.3 ? ay : uy < -0.3 ? ay - h : ay - h / 2, w, h };
        let score = pref * 2 + 12 * Math.abs(ux * s.tx + uy * s.ty);
        for (const b of placed) if (overlap(box, b)) score += 1000;
        for (const b of bBoxes) if (overlap(box, b)) score += 400;
        for (const o of ST) if (o !== s && overlap(box, { x: o.cx - o.r, y: o.cy - o.r, w: 2 * o.r, h: 2 * o.r })) score += 300;
        for (const sg of segs) { const m = sg[4] + 1; if (segRect(sg[0], sg[1], sg[2], sg[3], box.x - m, box.y - m, box.x + box.w + m, box.y + box.h + m)) score += 60; }
        if (box.x < F.x || box.y < F.y || box.x + box.w > F.x + F.w || box.y + box.h > F.y + F.h) score += 500;
        if (!best || score < best.score) best = { x: box.x, y: box.y, w, h, score, ux, uy, fs, bold, textW };
      }
      // Başka bir ada ya da istasyon simgesine binen ad "çakışıyor" sayılır: Otomatik kipte yazılmaz ve başkalarına yer açar
      best.clash = best.score >= 300;
      if (!best.clash) placed.push(best);
      s.lab = best;
      s.side = (-s.ty * best.ux + s.tx * best.uy) < 0 ? -1 : 1;
    }
  }

  gridRef(x, y) {
    const F = this.M.frame;
    const c = Math.min(F.cols, Math.max(1, Math.floor((x - F.x) / CELL) + 1));
    const r = Math.min(F.rows - 1, Math.max(0, Math.floor((y - F.y) / CELL)));
    return String.fromCharCode(65 + r) + c;
  }

  /* ---------------------------------------------------------------- resim olarak kaydet */
  /**
   * Tüm haritayı (yakınlaştırmadan bağımsız) ve sağında bir hat lejantıyla tek bir SVG belgesi olarak verir.
   * labels: lejant başlığı ve tür adları, arayüzün dilinde.
   */
  /** Kaydedilecek resim. labelLevel / declutter ekrandaki "Adlar" seçimiyle aynı: Otomatik'te çakışan adlar yazılmaz. */
  exportSVG(palette: Palette, labels: { lines: string; types: Record<string, string>; footer: string }, scale = 2, labelLevel: LabelLevel = 0, declutter = true) {
    const M = this.M, G = this.G; if (!M || !G) return null;
    const saved = this.palette; this.palette = palette;
    const body = this.markup(null, null, labelLevel, declutter, true);
    this.palette = saved;
    const C = palette, FULL = this.FULL, F = M.frame;
    const LW = 300, lx = FULL.x + FULL.w + 4, ly = F.y;
    const rows = [];
    let y = ly + 34;
    const vis = M.lines.filter(l => !this.hidden.has(l.type));
    for (const type of TYPE_ORDER) {
      const group = vis.filter(l => l.type === type);
      if (!group.length) continue;
      y += 10;
      rows.push(`<text x="${lx + 16}" y="${y}" fill="${C.muted}" font-family="${FONT}" font-weight="700" font-size="11" letter-spacing="1">${esc((labels.types[type] || type).toUpperCase())}</text>`);
      y += 12;
      for (const l of group) {
        const cw = Math.max(30, l.code.length * 8 + 12), cx = lx + 16, name = l.name.length > 32 ? l.name.slice(0, 31) + "…" : l.name;
        rows.push(`<rect x="${cx}" y="${y}" width="${cw}" height="20" rx="${l.type === "ship" ? 4 : 10}" fill="${l.color}"/>`
          + `<text x="${cx + cw / 2}" y="${y + 14}" text-anchor="middle" fill="${chipText(l.color)}" font-family="${FONT}" font-weight="700" font-size="11">${esc(l.code)}</text>`
          + `<text x="${cx + cw + 10}" y="${y + 14.5}" fill="${C.ink}" font-family="${FONT}" font-size="13">${esc(name)}</text>`);
        y += 26;
      }
    }
    const lh = Math.max(y - ly + 30, 120);
    const legend = `<rect x="${lx}" y="${ly}" width="${LW}" height="${lh}" rx="8" fill="${C.land}" stroke="${C.frame}" stroke-width="1.2"/>`
      + `<text x="${lx + 16}" y="${ly + 26}" fill="${C.ink}" font-family="${FONT}" font-weight="700" font-size="17">${esc(labels.lines)}</text>`
      + rows.join("")
      + `<text x="${lx + 16}" y="${ly + lh - 12}" fill="${C.muted}" font-family="${FONT}" font-size="10.5">${esc(labels.footer)}</text>`;
    const vx = FULL.x, vy = FULL.y, vw = FULL.w + LW + 40, vh = Math.max(FULL.h, ly - FULL.y + lh + 30);
    const width = Math.round(vw * scale), height = Math.round(vh * scale);
    const inner = `<rect x="${vx}" y="${vy}" width="${vw}" height="${vh}" fill="${C.land}"/>` + body + legend;
    const svg = `<svg xmlns="http://www.w3.org/2000/svg" width="${width}" height="${height}" viewBox="${vx} ${vy} ${vw} ${vh}">` + inner + `</svg>`;
    // inner + view: aynı resmi canvas'a çizip PNG yapmak için
    return { svg, width, height, inner, view: { x: vx, y: vy, w: vw, h: vh }, bg: C.land };
  }

  /* ---------------------------------------------------------------- SVG metni */
  /**
   * labelLevel: 0 hepsi, 1 yalnızca aktarma istasyonları, 2 hiçbiri.
   * declutter: çakışan adları yazma. busNames: yalnızca otobüs geçen durakların adları yazılsın mı.
   * Seçili istasyon ve vurgulanan hattın durakları her durumda yazılır.
   */
  markup(focus: string | null, sel: string | null, labelLevel: LabelLevel = 0, declutter = false, busNames = true) {
    const M = this.M, G = this.G, C = this.palette; if (!M || !G) return "";
    const F = M.frame, t = G.t, out = [], FULL = this.FULL;
    const dim = ids => focus && ids.indexOf(focus) < 0 ? ' opacity="0.12"' : "";
    out.push(`<rect x="${FULL.x - 4000}" y="${FULL.y - 4000}" width="${FULL.w + 8000}" height="${FULL.h + 8000}" fill="${C.land}"/>`);
    M.water.forEach((poly, i) => {
      const ps = M.waterS[i], pts = poly.map((p, j) => [p[0] + (ps[j][0] - p[0]) * t, p[1] + (ps[j][1] - p[1]) * t]);
      out.push(`<path d="${closedSmooth(pts)}" fill="${M.waterD[i] % 2 ? C.land : C.water}"/>`);
    });
    // çerçeve dışını kara rengiyle örterek suyu kırp (clipPath yerine)
    const big = 6000;
    // dört dikdörtgen: evenodd dolgu her motorda desteklenmiyor
    out.push(`<rect x="${F.x - big}" y="${F.y - big}" width="${F.w + 2 * big}" height="${big}" fill="${C.land}"/>`
      + `<rect x="${F.x - big}" y="${F.y + F.h}" width="${F.w + 2 * big}" height="${big}" fill="${C.land}"/>`
      + `<rect x="${F.x - big}" y="${F.y}" width="${big}" height="${F.h}" fill="${C.land}"/>`
      + `<rect x="${F.x + F.w}" y="${F.y}" width="${big}" height="${F.h}" fill="${C.land}"/>`);
    let gl = "";
    for (let c = 1; c < F.cols; c++) gl += `M${F.x + c * CELL},${F.y}v${F.h}`;
    for (let r = 1; r < F.rows; r++) gl += `M${F.x},${F.y + r * CELL}h${F.w}`;
    out.push(`<path d="${gl}" fill="none" stroke="${C.grid}" stroke-width="1"/>`);
    for (const l of M.labels) {
      const x = l.gx + (l.sx - l.gx) * t, y = l.gy + (l.sy - l.gy) * t;
      out.push(`<text text-anchor="middle" transform="translate(${x.toFixed(1)},${y.toFixed(1)}) rotate(${l.rot})" fill="${C.waterInk}" font-family="${FONT}" font-style="italic" font-size="17">${esc(l.text)}</text>`);
    }
    out.push(`<rect x="${F.x}" y="${F.y}" width="${F.w}" height="${F.h}" fill="none" stroke="${C.frame}" stroke-width="1.2"/>`);
    const gn = (x, y, s) => `<text x="${x}" y="${y}" text-anchor="middle" fill="${C.muted}" font-family="${FONT}" font-weight="600" font-size="11">${s}</text>`;
    for (let c = 0; c < F.cols; c++) { const x = F.x + (c + .5) * CELL; out.push(gn(x, F.y - 8, c + 1) + gn(x, F.y + F.h + 17, c + 1)); }
    for (let r = 0; r < F.rows; r++) { const y = F.y + (r + .5) * CELL + 4, ch = String.fromCharCode(65 + r); out.push(gn(F.x - 14, y, ch) + gn(F.x + F.w + 14, y, ch)); }
    if (M.city) out.push(`<text x="${F.x}" y="${F.y - 30}" fill="${C.ink}" font-family="${FONT}" font-weight="700" font-size="24">${esc(M.city)}</text>`);

    for (const { l, pts } of G.FG)
      out.push(`<path data-line="${esc(l.id)}" d="${smoothPath(pts)}" fill="none" stroke="${l.color}" stroke-width="2" stroke-dasharray="6 4" stroke-linecap="round"${dim([l.id])}/>`);
    const rad = G.mode === "octi" ? 12 : (t === 0 ? 28 : 10);
    for (const { l, pts } of G.LG) {
      const d = rounded(pts, rad), o = dim([l.id]);
      out.push(`<path d="${d}" fill="none" stroke="${C.land}" stroke-width="${(l.w + 2.6).toFixed(1)}" stroke-linejoin="round"${o}/>`);
      out.push(`<path data-line="${esc(l.id)}" d="${d}" fill="none" stroke="${l.color}" stroke-width="${l.w}" stroke-linejoin="round"${o}/>`);
    }
    for (const s of G.ST) {
      if (s.kind !== "stop" || s.xfer) continue;
      for (const p of s.pts) {
        const nx = -p.ty * s.side, ny = p.tx * s.side, w = p.l.w;
        out.push(`<line data-st="${esc(s.id)}" x1="${(p.x + nx * (w / 2 - 0.5)).toFixed(1)}" y1="${(p.y + ny * (w / 2 - 0.5)).toFixed(1)}" x2="${(p.x + nx * (w / 2 + 4.5)).toFixed(1)}" y2="${(p.y + ny * (w / 2 + 4.5)).toFixed(1)}" stroke="${p.l.color}" stroke-width="${p.l.type === "tram" ? 2.6 : 3}"${dim([p.l.id])}/>`);
      }
    }
    // aktarma bağlantısı: istasyon simgelerinin altında, ince beyaz geçit
    for (const x of G.XF || []) {
      const o = dim(x.A.ids.concat(x.B.ids));
      const c = `x1="${x.A.cx.toFixed(1)}" y1="${x.A.cy.toFixed(1)}" x2="${x.B.cx.toFixed(1)}" y2="${x.B.cy.toFixed(1)}"`;
      out.push(`<line ${c} stroke="${C.ink}" stroke-width="7.4" stroke-linecap="round"${o}/><line ${c} stroke="${C.land}" stroke-width="3.6" stroke-linecap="round"${o}/>`);
    }
    for (const s of G.ST) {
      const o = dim(s.ids);
      if (s.kind === "stop" && s.xfer) {
        out.push(`<circle data-st="${esc(s.id)}" cx="${s.cx.toFixed(1)}" cy="${s.cy.toFixed(1)}" r="6" fill="${C.land}" stroke="${C.ink}" stroke-width="2.2"${o}/>`);
      } else if (s.kind === "hub") {
        const d = hyp(s.p2.x - s.p1.x, s.p2.y - s.p1.y);
        if (d < 0.6) out.push(`<circle data-st="${esc(s.id)}" cx="${s.cx.toFixed(1)}" cy="${s.cy.toFixed(1)}" r="6" fill="${C.land}" stroke="${C.ink}" stroke-width="2.2"${o}/>`);
        else {
          const c = `x1="${s.p1.x.toFixed(1)}" y1="${s.p1.y.toFixed(1)}" x2="${s.p2.x.toFixed(1)}" y2="${s.p2.y.toFixed(1)}"`;
          out.push(`<line data-st="${esc(s.id)}" ${c} stroke="${C.ink}" stroke-width="14.4" stroke-linecap="round"${o}/><line data-st="${esc(s.id)}" ${c} stroke="${C.land}" stroke-width="10" stroke-linecap="round"${o}/>`);
        }
      } else if (s.kind === "pier") {
        out.push(`<circle data-st="${esc(s.id)}" cx="${s.cx.toFixed(1)}" cy="${s.cy.toFixed(1)}" r="4.2" fill="${C.land}" stroke="${s.ships[0].color}" stroke-width="2"${o}/>`);
      }
    }
    for (const b of G.badges) {
      const code = b.l.code, lid = b.l.id, fs = code.length > 2 ? 8.5 : 10, o = dim([lid]);
      out.push(`<circle data-line="${esc(lid)}" cx="${b.x.toFixed(1)}" cy="${b.y.toFixed(1)}" r="${BR}" fill="${b.l.color}"${o}/><text data-line="${esc(lid)}" x="${b.x.toFixed(1)}" y="${(b.y + fs * 0.36).toFixed(1)}" text-anchor="middle" font-family="${FONT}" font-weight="700" font-size="${fs}" fill="${chipText(b.l.color)}"${o}>${esc(code)}</text>`);
    }
    if (G.final && labelLevel < 2) for (const s of G.ST) {
      const L = s.lab; if (!L) continue;
      // Uzaktan bakarken yalnızca aktarma istasyonları ve seçili / vurgulanan hattın durakları yazılır
      if (s.id !== sel && !(focus && s.ids.indexOf(focus) >= 0)) {
        if (labelLevel === 1 && s.kind !== "hub" && !s.xfer) continue;
        if (declutter && (L.clash || (s.busOnly && !busNames))) continue;
      }
      const o = dim(s.ids), x = L.x.toFixed(1), y = (L.y + L.fs * 0.86).toFixed(1), wt = L.bold ? 600 : 400, name = esc(shortName(s.s.name));
      // önce kara rengiyle kalın kontur (hale), sonra yazı
      out.push(`<text data-st="${esc(s.id)}" x="${x}" y="${y}" font-family="${FONT}" font-size="12" font-weight="${wt}" fill="${C.land}" stroke="${C.land}" stroke-width="3" stroke-linejoin="round"${o}>${name}</text>`);
      out.push(`<text data-st="${esc(s.id)}" x="${x}" y="${y}" font-family="${FONT}" font-size="12" font-weight="${wt}" fill="${C.ink}"${o}>${name}</text>`);
      if (s.ships.length) {
        const bx = L.x + L.textW + 3, by = L.y + L.h / 2 - 5;
        out.push(`<path transform="translate(${bx.toFixed(1)},${by.toFixed(1)}) scale(0.82)" d="M1 6.5h14l-2.6 4.3H3.6z M4.5 2.4h6.6v3.2H4.5z" fill="${s.ships[0].color}"${o}/>`);
      }
    }
    if (sel) {
      const s = G.STm.get(sel);
      if (s) out.push(`<circle cx="${s.cx.toFixed(1)}" cy="${s.cy.toFixed(1)}" r="${(s.r + 6).toFixed(1)}" fill="none" stroke="#5ea6ec" stroke-width="3"/>`);
    }
    return out.join("");
  }
}

/**
 * Şehir dışı bağlantıları (harita kenarındaki tren, gemi ve otobüs çıkışları) şehirden kilometrelerce uzaktadır;
 * gerçek yerlerinde çizilirlerse şehir haritanın köşesinde küçücük kalır. Bu yüzden şehrin hemen dışına çekilirler.
 */
function pullInOutside(S) {
  let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity, any = false, anyOut = false;
  S.forEach(s => { if (s.out) { anyOut = true; return; } any = true; x0 = Math.min(x0, s.gx); x1 = Math.max(x1, s.gx); y0 = Math.min(y0, s.gy); y1 = Math.max(y1, s.gy); });
  if (!any || !anyOut) return;
  const cx = (x0 + x1) / 2, cy = (y0 + y1) / 2, pad = Math.max(70, Math.max(x1 - x0, y1 - y0) * 0.14);
  const hx = (x1 - x0) / 2 + pad, hy = (y1 - y0) / 2 + pad;
  S.forEach(s => {
    if (!s.out) return;
    const dx = s.gx - cx, dy = s.gy - cy;
    const t = Math.min(1, hx / (Math.abs(dx) || 1e-6), hy / (Math.abs(dy) || 1e-6));
    s.gx = s.sx = cx + dx * t; s.gy = s.sy = cy + dy * t;
  });
}

/* ---------------------------------------------------------------- geometri yardımcıları */

/** İki doğru parçası birbirini gerçekten kesiyor mu (uç uca değme ve üst üste binme sayılmaz). */
function cross(ax, ay, bx, by, cx, cy, dx, dy) {
  const d1 = (dx - cx) * (ay - cy) - (dy - cy) * (ax - cx), d2 = (dx - cx) * (by - cy) - (dy - cy) * (bx - cx);
  if (!((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))) return false;
  const d3 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax), d4 = (bx - ax) * (dy - ay) - (by - ay) * (dx - ax);
  return (d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0);
}
const DIRS = [[1, 0, 0], [-1, 0, 1], [0.7071, -0.7071, 3], [0.7071, 0.7071, 3], [-0.7071, -0.7071, 4], [-0.7071, 0.7071, 4], [0, -1, 5], [0, 1, 5]];
const overlap = (a, b) => a.x < b.x + b.w && b.x < a.x + a.w && a.y < b.y + b.h && b.y < a.y + a.h;

function route(A, B, mode) {
  if (mode !== "octi") return [A, B];
  const dx = B[0] - A[0], dy = B[1] - A[1], Q = Math.PI / 4, ang = Math.atan2(dy, dx);
  if (Math.abs(ang - Math.round(ang / Q) * Q) < 0.08) return [A, B];
  const ax = Math.abs(dx), ay = Math.abs(dy), sx = Math.sign(dx), sy = Math.sign(dy);
  let P1, P2;
  if (ax >= ay) { const s = (ax - ay) / 2; P1 = [A[0] + sx * s, A[1]]; P2 = [P1[0] + sx * ay, P1[1] + sy * ay]; }
  else { const s = (ay - ax) / 2; P1 = [A[0], A[1] + sy * s]; P2 = [P1[0] + sx * ax, P1[1] + sy * ax]; }
  return [A, P1, P2, B].filter((p, i, a) => i === 0 || hyp(p[0] - a[i - 1][0], p[1] - a[i - 1][1]) > 0.3);
}
function offsetLine(P, offs) {
  const m = P.length - 1, N = [], D = [];
  for (let i = 0; i < m; i++) { const dx = P[i + 1][0] - P[i][0], dy = P[i + 1][1] - P[i][1], l = hyp(dx, dy) || 1; D.push([dx / l, dy / l]); N.push([-dy / l, dx / l]); }
  const out = [], vmap = [];
  vmap[0] = [0]; out.push([P[0][0] + N[0][0] * offs[0], P[0][1] + N[0][1] * offs[0]]);
  for (let i = 1; i < m; i++) {
    const o1 = offs[i - 1], o2 = offs[i];
    const q1 = [P[i][0] + N[i - 1][0] * o1, P[i][1] + N[i - 1][1] * o1], q2 = [P[i][0] + N[i][0] * o2, P[i][1] + N[i][1] * o2];
    const d1 = D[i - 1], d2 = D[i], cr = d1[0] * d2[1] - d1[1] * d2[0];
    if (Math.abs(cr) < 0.02) {
      if (hyp(q1[0] - q2[0], q1[1] - q2[1]) < 0.05) { vmap[i] = [out.length]; out.push(q2); }
      else { vmap[i] = [out.length, out.length + 1]; out.push(q1, q2); }
    } else {
      const t = ((q2[0] - q1[0]) * d2[1] - (q2[1] - q1[1]) * d2[0]) / cr;
      const X = [q1[0] + d1[0] * t, q1[1] + d1[1] * t];
      if (hyp(X[0] - P[i][0], X[1] - P[i][1]) > 2.5 * Math.max(Math.abs(o1), Math.abs(o2)) + 3) { vmap[i] = [out.length, out.length + 1]; out.push(q1, q2); }
      else { vmap[i] = [out.length]; out.push(X); }
    }
  }
  vmap[m] = [out.length]; out.push([P[m][0] + N[m - 1][0] * offs[m - 1], P[m][1] + N[m - 1][1] * offs[m - 1]]);
  return { out, vmap };
}
function segRect(x1, y1, x2, y2, rx0, ry0, rx1, ry1) {
  let t0 = 0, t1 = 1; const dx = x2 - x1, dy = y2 - y1;
  const p = [-dx, dx, -dy, dy], q = [x1 - rx0, rx1 - x1, y1 - ry0, ry1 - y1];
  for (let i = 0; i < 4; i++) {
    if (p[i] === 0) { if (q[i] < 0) return false; }
    else { const r = q[i] / p[i]; if (p[i] < 0) { if (r > t1) return false; if (r > t0) t0 = r; } else { if (r < t0) return false; if (r < t1) t1 = r; } }
  }
  return true;
}
function segDist(sg, c) {
  const [x1, y1, x2, y2] = sg, dx = x2 - x1, dy = y2 - y1, L2 = dx * dx + dy * dy || 1;
  let t = ((c[0] - x1) * dx + (c[1] - y1) * dy) / L2; t = Math.max(0, Math.min(1, t));
  return hyp(x1 + dx * t - c[0], y1 + dy * t - c[1]);
}
function smoothPath(pts) {
  if (pts.length < 2) return "";
  const n = pts.length, g = i => pts[Math.max(0, Math.min(n - 1, i))];
  let d = "M" + f1(pts[0]);
  for (let i = 0; i < n - 1; i++) {
    const p0 = g(i - 1), p1 = g(i), p2 = g(i + 1), p3 = g(i + 2);
    d += "C" + f1([p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6]) + " " + f1([p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6]) + " " + f1(p2);
  }
  return d;
}
function closedSmooth(pts) {
  const n = pts.length, g = i => pts[(i + n) % n];
  let d = "M" + f1(pts[0]);
  for (let i = 0; i < n; i++) {
    const p0 = g(i - 1), p1 = g(i), p2 = g(i + 1), p3 = g(i + 2);
    d += "C" + f1([p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6]) + " " + f1([p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6]) + " " + f1(p2);
  }
  return d + "Z";
}
function rounded(pts, r) {
  let d = "M" + f1(pts[0]);
  for (let i = 1; i < pts.length - 1; i++) {
    const a = pts[i - 1], b = pts[i], c = pts[i + 1], l1 = hyp(b[0] - a[0], b[1] - a[1]), l2 = hyp(c[0] - b[0], c[1] - b[1]);
    if (l1 < 0.01 || l2 < 0.01) { d += "L" + f1(b); continue; }
    const rr = Math.min(r, l1 / 2, l2 / 2);
    d += "L" + f1([b[0] + (a[0] - b[0]) / l1 * rr, b[1] + (a[1] - b[1]) / l1 * rr]) + "Q" + f1(b) + " " + f1([b[0] + (c[0] - b[0]) / l2 * rr, b[1] + (c[1] - b[1]) / l2 * rr]);
  }
  return d + "L" + f1(pts[pts.length - 1]);
}
