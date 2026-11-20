import { useEffect, useMemo, useRef, useState, CSSProperties, MouseEvent } from "react";
import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { isOpen$, setOpen, state$, setFace, pickCenter, setTyping } from "./bindings";
import { ErrorBoundary } from "./ErrorBoundary";
import { STD_SIGNS } from "./stdsigns";

/* ---------------------------------------------------------------- veri */
/** kind: 0 yazılı panel, 1 standart işaret, 2 ek levha */
type Face = { id: string; w: number; h: number; cat: number; spec: string; kind?: number };
type State = { city: string; count: number; sel: { asset: string; key: string; faces: Face[] } | null };
type Slot = "pre" | "post" | "right";
/** Satır içi öğe: s = sembol, n = numara kutusu, ok = ok */
export type Item = { kind: "s" | "n" | "ok"; v: string; k?: boolean; slot: Slot };
/** block: -1 = blok yok, 0-5 = kategori; size: yazı boyu çarpanı; align: 0 sol, 1 orta, 2 sağ; rtext: sağa yaslı yazı (mesafe) */
export type Row = { text: string; block: number; size: number; align: number; items: Item[]; rtext: string };
export type Model = { cat: number; rows: Row[]; arrow: string };   // arrow: büyük ok, "" = yok

const ICON = "coui://ui-mods/SignsPlus-icon.svg";
const ARROW = "coui://ui-mods/SignsPlus-arrow.svg";
const ARROW_DARK = "coui://ui-mods/SignsPlus-arrow-dark.svg";
const symSrc = (id: string, dark: boolean) => `coui://ui-mods/SignsPlus-sym-${id}${dark ? "-dark" : ""}.svg`;

// Data/Categories.cs ile aynı sıra ve renkler
const CATS = [
  { tag: "otoyol", bg: "#007a4d", fg: "#f2f2ec", key: "Cat0", fb: "Otoyol" },
  { tag: "karayolu", bg: "#0e52a0", fg: "#f2f2ec", key: "Cat1", fb: "Karayolu" },
  { tag: "normal", bg: "#f2f2ec", fg: "#1c1c1c", key: "Cat2", fb: "Normal" },
  { tag: "turistik", bg: "#6e3428", fg: "#f2f2ec", key: "Cat3", fb: "Turistik" },
  { tag: "sinirdisi", bg: "#f6be00", fg: "#1c1c1c", key: "Cat4", fb: "Sınır dışı" },
  { tag: "kgm", bg: "#e85c28", fg: "#1c1c1c", key: "Cat5", fb: "KGM" }
];
const DARK = "#1c1c1c";

// tools/make_symbols.py ile aynı sıra
const SYMS: [string, string][] = [
  ["ucak", "Havalimanı"], ["kopru", "Köprü"], ["feribot", "Feribot"], ["liman", "Liman"], ["tren", "Tren / gar"],
  ["otobus", "Otobüs terminali"], ["araba", "Araba"], ["kamyon", "Kamyon"], ["otoyol", "Otoyol"], ["cikis", "Çıkış"],
  ["tunel", "Tünel"], ["otopark", "Otopark"], ["benzin", "Akaryakıt"], ["dinlenme", "Dinlenme tesisi"], ["merkez", "Şehir merkezi"],
  ["hastane", "Hastane"], ["ilkyardim", "İlk yardım"], ["sanayi", "Sanayi"], ["stadyum", "Stadyum"], ["cami", "Cami"],
  ["plaj", "Plaj"], ["yaris", "Yarış pisti"], ["kamera", "Kamera"]
];
const SYM_IDS = new Set(SYMS.map(s => s[0]));
// make_symbols.py'deki genişlik oranları (önizlemede simge genişliği için)
const SYM_RATIO: Record<string, number> = { kopru: 1.3, feribot: 1.25, tren: 0.8, otobus: 1.5, araba: 1.6, kamyon: 1.6, otoyol: 1.2, tunel: 1.2, otopark: 0.8, benzin: 0.85, dinlenme: 1.2, sanayi: 1.3, stadyum: 1.5, cami: 1.2, plaj: 1.1, kamera: 1.2 };

// Ok kodları (SignComposer'ın ASCII adları) ve döndürme açısı
const ARROWS: Record<string, number> = { yukari: 0, sagust: 45, sag: 90, sagalt: 135, asagi: 180, solalt: 225, sol: 270, solust: 315 };
const ARROW_GRID = [["solust", "yukari", "sagust"], ["sol", "", "sag"], ["solalt", "asagi", "sagalt"]];
const ARROW_ALIASES: Record<string, string> = {
  "↑": "yukari", "yukarı": "yukari", "↓": "asagi", "aşağı": "asagi", "←": "sol", "→": "sag", "sağ": "sag",
  "↗": "sagust", "sağüst": "sagust", "↖": "solust", "solüst": "solust", "↘": "sagalt", "sağalt": "sagalt", "↙": "solalt"
};
const arrowCode = (s: string) => { const k = s.trim().toLowerCase(); return ARROWS[k] !== undefined ? k : (ARROW_ALIASES[k] || ""); };
const TAG_ALIASES: Record<string, number> = {
  otoyol: 0, yesil: 0, "yeşil": 0, karayolu: 1, mavi: 1, normal: 2, beyaz: 2, turistik: 3, kahverengi: 3,
  sinirdisi: 4, "sınırdışı": 4, sari: 4, "sarı": 4, kgm: 5, turuncu: 5
};
const SIZES = [1, 0.85, 0.7, 1.25];

/** Numara kutusu renkleri: SignComposer.DrawNumberBox ile aynı kural */
function numberColors(v: string, ctxBg: string, ctxFg: string) {
  const c = (v.trim()[0] || "").toUpperCase();
  if (c === "O" || c === "K") return { bg: CATS[5].bg, fg: CATS[5].fg };
  if (c === "E") return { bg: CATS[0].bg, fg: CATS[0].fg };
  if (c === "D") return { bg: CATS[2].bg, fg: CATS[2].fg };
  return { bg: ctxFg, fg: ctxBg };
}

/* ---------------------------------------------------------------- yazı biçimi (Rendering/SignComposer.cs) */
const clean = (s: string) => (s || "").replace(/[|{}\[\]]/g, "");

function parseToken(tok: string): Omit<Item, "slot"> | null {
  const i = tok.indexOf(":");
  if (i < 0) { const a = arrowCode(tok); return a ? { kind: "ok", v: a } : null; }
  const key = tok.substring(0, i).trim().toLowerCase(), val = tok.substring(i + 1).trim();
  if (key === "s") {
    const [id, flag] = val.split(":");
    return SYM_IDS.has(id.trim()) ? { kind: "s", v: id.trim(), k: (flag || "").trim().toLowerCase() === "k" } : null;
  }
  if (key === "n") return val ? { kind: "n", v: val } : null;
  if (key === "ok") { const a = arrowCode(val); return a ? { kind: "ok", v: a } : null; }
  return null;
}

export function parse(face: Face): Model {
  const rows: Row[] = [];
  let arrow = "";
  for (const raw of (face.spec || "").split("|")) {
    let s = raw.trim();
    if (!s) continue;
    let block = -1, size = 1, align = 0;
    const o = s.match(/^\[([^\]]*)\]/);
    if (o) {
      let any = false;
      for (const opt of o[1].split(",")) {
        const t = opt.trim().toLowerCase();
        if (TAG_ALIASES[t] !== undefined) { block = TAG_ALIASES[t]; any = true; }
        else if (/^x[0-9.]+$/.test(t)) { size = Math.max(0.4, Math.min(2, parseFloat(t.substring(1)) || 1)); any = true; }
        else if (t === "orta") { align = 1; any = true; }
        else if (t === "sag" || t === "sağ") { align = 2; any = true; }
      }
      if (any) s = s.substring(o[0].length).trim();
    }
    const big = s.match(/^\{([^{}]+)\}$/);
    if (big && big[1].indexOf(":") < 0 && arrowCode(big[1])) { arrow = arrowCode(big[1]); continue; }

    const row: Row = { text: "", block, size, align, items: [], rtext: "" };
    const texts: string[] = [], rtexts: string[] = [];
    let right = false, textSeen = false;
    const re = /\{([^}]*)\}/g;
    let last = 0, m: RegExpExecArray | null;
    const addText = (t: string) => { t = t.trim(); if (!t) return; if (right) rtexts.push(t); else { texts.push(t); textSeen = true; } };
    while ((m = re.exec(s))) {
      addText(s.substring(last, m.index));
      last = re.lastIndex;
      const tok = m[1].trim();
      if (tok === ">") { right = true; continue; }
      const it = parseToken(tok);
      if (it) row.items.push({ ...it, slot: right ? "right" : textSeen ? "post" : "pre" });
    }
    addText(s.substring(last));
    row.text = texts.join(" ");
    row.rtext = rtexts.join(" ");
    rows.push(row);
  }
  return { cat: face.cat, rows, arrow };
}

const tokenOf = (it: Item) => it.kind === "s" ? `{s:${it.v}${it.k ? ":k" : ""}}` : it.kind === "n" ? `{n:${clean(it.v)}}` : `{ok:${it.v}}`;

export function serialize(m: Model): string {
  const parts: string[] = [];
  for (const r of m.rows) {
    const text = clean(r.text).trim(), rtext = clean(r.rtext).trim();
    const pre = r.items.filter(i => i.slot === "pre"), post = r.items.filter(i => i.slot === "post"), rit = r.items.filter(i => i.slot === "right");
    if (!text && !rtext && r.items.length === 0) continue;
    const opts: string[] = [];
    if (r.block >= 0) opts.push(CATS[r.block].tag);
    if (r.size !== 1) opts.push("x" + r.size);
    if (r.align === 1) opts.push("orta");
    else if (r.align === 2) opts.push("sag");
    let line = (opts.length ? `[${opts.join(",")}]` : "") + pre.map(tokenOf).join(" ") + (pre.length && text ? " " : "") + text;
    if (post.length) line += " " + post.map(tokenOf).join(" ");
    if (rtext || rit.length) line += "{>}" + rtext + (rtext && rit.length ? " " : "") + rit.map(tokenOf).join(" ");
    parts.push(line.trim());
  }
  if (m.arrow) parts.push(`{${m.arrow}}`);
  return parts.join("|");
}

/* ---------------------------------------------------------------- şablonlar */
const TEMPLATES: { id: string; fb: string; cat: number; spec: string }[] = [
  { id: "Motorway", fb: "Otoyol yön levhası", cat: 0, spec: "{n:E 80} {n:O-3}|Edirne|Çorlu|{sagust}" },
  { id: "Junction", fb: "Kavşak ön bilgi", cat: 1, spec: "[kgm]{n:K 11} Çıkış{>}2000 m|Ankara {n:D-100}|{s:ucak}Havalimanı|{sagust}" },
  { id: "Distance", fb: "Mesafe levhası", cat: 1, spec: "Bergama{>}25 km|Balıkesir{>}102 km|İzmir{>}105 km|Ankara{>}580 km" },
  { id: "City", fb: "Şehir içi yön", cat: 2, spec: "{s:merkez}Şehir Merkezi{>}{ok:yukari}|{s:hastane:k}Devlet Hastanesi{>}{ok:sag}|[turistik]{s:cami}Selimiye Camii{>}{ok:sol}" },
  { id: "Tourist", fb: "Turistik yer", cat: 3, spec: "{s:yaris}İstanbul Park{>}{ok:sagust}" },
  { id: "Border", fb: "Sınır kapısı", cat: 1, spec: "Kapıkule|Gümrük|[sinirdisi]Bulgaristan{>}{ok:yukari}" }
];

/* ---------------------------------------------------------------- pencere */
export const SignsWindow = () => {
  const open = useValue(isOpen$);
  return open ? <ErrorBoundary onError={() => setOpen(false)}><SignsWindowInner /></ErrorBoundary> : null;
};

type Rect = { x: number; y: number; w: number; h: number };
let savedRect: Rect | null = null;
const MIN_W = 520, MIN_H = 460;
function defaultRect(): Rect {
  const W = window.innerWidth, H = window.innerHeight;
  const w = Math.max(MIN_W, Math.min(Math.round(W * 0.32), 660)), h = Math.max(MIN_H, Math.round(H * 0.78));
  return { x: W - w - Math.round(W * 0.03), y: Math.round(H * 0.08), w, h };
}
function clampRect(r: Rect): Rect {
  const W = window.innerWidth, H = window.innerHeight;
  return { ...r, x: Math.min(Math.max(r.x, 120 - r.w), W - 120), y: Math.min(Math.max(r.y, 0), H - 48) };
}

/* ---------------------------------------------------------------- stiller (Transport+ / Demand+ ile aynı koyu tema) */
const C = { panel: "rgba(24,28,35,0.97)", rule: "#2d343f", ink: "#e9ecf1", muted: "#9aa3b0", hover: "#262c36", accent: "#5ea6ec", field: "rgba(12,14,18,0.6)" };
const S: Record<string, CSSProperties> = {
  win: { position: "absolute", display: "flex", flexDirection: "column", background: C.panel, border: `1px solid ${C.rule}`, borderRadius: "8rem", color: C.ink, pointerEvents: "auto", overflow: "hidden", boxShadow: "0 12rem 40rem rgba(0,0,0,0.45)", userSelect: "none" },
  head: { display: "flex", alignItems: "center", padding: "8rem 8rem 8rem 14rem", background: "rgba(12,14,18,0.92)", borderBottom: `1px solid ${C.rule}` },
  title: { flex: 1, display: "flex", alignItems: "center", fontSize: "18rem", fontWeight: 700, alignSelf: "stretch", cursor: "move" },
  icon: { width: "22rem", height: "22rem", marginRight: "10rem" },
  close: { width: "30rem", height: "30rem", marginLeft: "10rem", borderRadius: "6rem", display: "flex", alignItems: "center", justifyContent: "center", fontSize: "22rem", color: C.muted, cursor: "pointer" },
  grip: { position: "absolute", right: 0, bottom: 0, width: "18rem", height: "18rem", cursor: "nwse-resize", borderRight: `3rem solid ${C.muted}`, borderBottom: `3rem solid ${C.muted}`, opacity: 0.5 },
  body: { flex: 1, padding: "12rem 16rem 16rem", overflowY: "auto", position: "relative" },
  eyebrow: { fontSize: "11rem", color: C.muted, fontWeight: 600, textTransform: "uppercase", letterSpacing: "1rem", marginTop: "14rem", marginBottom: "6rem" },
  seg: { display: "flex", border: `1px solid ${C.rule}`, borderRadius: "16rem", padding: "2rem", alignSelf: "flex-start" },
  segBtn: { padding: "5rem 14rem", borderRadius: "14rem", fontSize: "13rem", fontWeight: 600, color: C.ink, cursor: "pointer" },
  btn: { padding: "6rem 12rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", fontSize: "13rem", fontWeight: 600, color: C.ink, cursor: "pointer" },
  swatches: { display: "flex", flexWrap: "wrap" },
  swatch: { display: "flex", alignItems: "center", padding: "5rem 10rem 5rem 6rem", borderRadius: "6rem", border: `1px solid ${C.rule}`, marginRight: "6rem", marginBottom: "6rem", fontSize: "13rem", cursor: "pointer" },
  dot: { width: "16rem", height: "16rem", borderRadius: "4rem", marginRight: "8rem", border: "1px solid rgba(0,0,0,0.4)" },
  rowBox: { borderBottom: `1px solid ${C.rule}`, paddingBottom: "6rem", marginBottom: "8rem" },
  row: { display: "flex", alignItems: "center", marginBottom: "5rem", position: "relative" },
  chip: { width: "30rem", height: "30rem", borderRadius: "6rem", border: `1px solid ${C.rule}`, display: "flex", alignItems: "center", justifyContent: "center", fontSize: "14rem", color: C.muted, cursor: "pointer", marginRight: "6rem" },
  input: { flex: 1, height: "30rem", padding: "0 10rem", fontSize: "15rem", color: C.ink, background: C.field, border: `1px solid ${C.rule}`, borderRadius: "6rem" },
  rinput: { width: "86rem", height: "30rem", padding: "0 8rem", fontSize: "14rem", color: C.ink, background: C.field, border: `1px solid ${C.rule}`, borderRadius: "6rem", marginLeft: "6rem", textAlign: "right" },
  step: { width: "28rem", height: "28rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", display: "flex", alignItems: "center", justifyContent: "center", fontSize: "14rem", fontWeight: 700, cursor: "pointer", marginLeft: "4rem" },
  items: { display: "flex", flexWrap: "wrap", alignItems: "center", marginLeft: "72rem", position: "relative" },
  item: { display: "flex", alignItems: "center", height: "26rem", padding: "0 6rem", borderRadius: "5rem", border: `1px solid ${C.rule}`, marginRight: "5rem", marginBottom: "4rem", cursor: "pointer", fontSize: "12rem" },
  slotTag: { fontSize: "10rem", color: C.muted, marginLeft: "5rem", textTransform: "uppercase" },
  add: { height: "26rem", padding: "0 8rem", borderRadius: "5rem", border: `1px dashed ${C.rule}`, display: "flex", alignItems: "center", fontSize: "12rem", color: C.muted, cursor: "pointer", marginBottom: "4rem" },
  pop: { position: "absolute", top: "34rem", left: 0, width: "200rem", background: "rgba(16,19,24,0.98)", border: `1px solid ${C.rule}`, borderRadius: "8rem", padding: "6rem", zIndex: 5, boxShadow: "0 10rem 30rem rgba(0,0,0,0.5)" },
  popWide: { position: "absolute", top: "30rem", left: 0, width: "340rem", background: "rgba(16,19,24,0.98)", border: `1px solid ${C.rule}`, borderRadius: "8rem", padding: "8rem", zIndex: 6, boxShadow: "0 10rem 30rem rgba(0,0,0,0.5)" },
  popItem: { display: "flex", alignItems: "center", padding: "6rem 8rem", borderRadius: "6rem", cursor: "pointer", fontSize: "13rem" },
  tabs: { display: "flex", marginBottom: "8rem" },
  tab: { padding: "4rem 10rem", borderRadius: "12rem", fontSize: "12rem", fontWeight: 600, cursor: "pointer", marginRight: "4rem" },
  symGrid: { display: "flex", flexWrap: "wrap" },
  symCell: { width: "46rem", height: "40rem", borderRadius: "6rem", border: `1px solid ${C.rule}`, display: "flex", alignItems: "center", justifyContent: "center", margin: "0 4rem 4rem 0", cursor: "pointer" },
  grid: { display: "flex", flexDirection: "column", alignSelf: "flex-start" },
  gridRow: { display: "flex" },
  cell: { width: "38rem", height: "38rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", display: "flex", alignItems: "center", justifyContent: "center", margin: "0 4rem 4rem 0", cursor: "pointer", fontSize: "12rem", color: C.muted },
  small: { width: "28rem", height: "28rem" },
  preview: { position: "relative", borderRadius: "6rem", overflow: "hidden", marginTop: "8rem" },
  note: { fontSize: "12rem", color: C.muted, lineHeight: 1.45, marginTop: "14rem" },
  empty: { padding: "30rem 10rem", textAlign: "center", color: C.muted, fontSize: "14rem", lineHeight: 1.5 }
};

const stop = (e: MouseEvent) => e.stopPropagation();

/* ---------------------------------------------------------------- küçük çizim parçaları (önizleme ve çipler) */
const ArrowImg = ({ code, dark, size }: { code: string; dark: boolean; size: string }) =>
  <img src={dark ? ARROW_DARK : ARROW} style={{ width: size, height: size, transform: `rotate(${ARROWS[code] || 0}deg)` }} />;

/** Bir öğeyi verilen yükseklikte çizer (rem). bg/fg: bulunduğu yerin renkleri */
const ItemView = ({ it, h, bg, fg }: { it: Item; h: number; bg: string; fg: string }) => {
  if (it.kind === "ok") return <ArrowImg code={it.v} dark={fg === DARK} size={h * 0.95 + "rem"} />;
  if (it.kind === "n") {
    const c = numberColors(it.v, bg, fg);
    return <div style={{ height: h + "rem", padding: `0 ${h * 0.25}rem`, background: c.bg, color: c.fg, border: `${Math.max(1, h * 0.07)}rem solid ${c.fg}`, borderRadius: h * 0.12 + "rem", fontSize: h * 0.62 + "rem", fontWeight: 800, display: "flex", alignItems: "center", whiteSpace: "nowrap" }}>{it.v}</div>;
  }
  const ratio = SYM_RATIO[it.v] || 1;
  if (it.k) return (
    <div style={{ height: h + "rem", width: (ratio * h * 0.72 + h * 0.28) + "rem", background: fg, borderRadius: h * 0.12 + "rem", display: "flex", alignItems: "center", justifyContent: "center" }}>
      <img src={symSrc(it.v, bg === DARK)} style={{ height: h * 0.72 + "rem", width: ratio * h * 0.72 + "rem" }} />
    </div>
  );
  return <img src={symSrc(it.v, fg === DARK)} style={{ height: h + "rem", width: ratio * h + "rem" }} />;
};

/** Üç çizgili hiza simgesi (sol / orta / sağ) */
const AlignIcon = ({ a, on, dark }: { a: number; on: boolean; dark?: boolean }) => {
  const col = dark ? "#0d0f13" : on ? C.accent : C.muted;
  return (
    <div style={{ display: "flex", flexDirection: "column", alignItems: a === 1 ? "center" : a === 2 ? "flex-end" : "flex-start", width: "14rem" }}>
      {[14, 9, 12].map((w, k) => <div key={k} style={{ width: w + "rem", height: "2rem", background: col, marginBottom: k < 2 ? "2rem" : 0 }} />)}
    </div>
  );
};

/* ---------------------------------------------------------------- önizleme (levhadaki çizimin kaba bir kopyası) */
const Preview = ({ m, w, h }: { m: Model; w: number; h: number }) => {
  const base = CATS[m.cat] || CATS[2];
  const ratio = Math.min(0.7, h / w);
  const rows = m.rows.filter(r => r.text.trim() || r.rtext.trim() || r.items.length);
  let units = 0;
  for (const r of rows) units += (r.items.length ? 1.9 : 1.75) * r.size;
  if (m.arrow) units += 2.3;
  // Önizleme kutusu yaklaşık 480rem genişliğinde; harf yüksekliği rem cinsinden
  const boxH = 480 * ratio * 0.84;
  const cap = units > 0 ? Math.min(boxH / units, 30) : 20;
  const arrowLeft = m.arrow.startsWith("sol") ? "6%" : m.arrow === "yukari" || m.arrow === "asagi" ? "45%" : "84%";
  return (
    <div style={{ ...S.preview, width: "100%", paddingTop: `${ratio * 100}%`, background: base.bg }}>
      <div style={{ position: "absolute", left: "1.2%", top: "1.2%", right: "1.2%", bottom: "1.2%", border: `2rem solid ${base.fg}`, borderRadius: "5rem" }} />
      <div style={{ position: "absolute", left: "5%", right: "5%", top: "7%", bottom: "7%", display: "flex", flexDirection: "column", justifyContent: "center", overflow: "hidden" }}>
        {rows.map((r, i) => {
          const st = r.block >= 0 ? CATS[r.block] : null;
          const bg = st ? st.bg : base.bg, fg = st ? st.fg : base.fg;
          const c = cap * r.size, ih = c * 1.4;
          const part = (items: Item[]) => items.map((it, k) => <div key={k} style={{ marginRight: c * 0.35 + "rem", display: "flex" }}><ItemView it={it} h={ih} bg={bg} fg={fg} /></div>);
          const right = r.items.filter(x => x.slot === "right");
          return (
            <div key={i} style={{ display: "flex", alignItems: "center", height: c * (r.items.length ? 1.9 : 1.75) + "rem", padding: st ? `0 ${c * 0.4}rem` : 0, background: st ? st.bg : "transparent", color: fg, borderRadius: "2rem", alignSelf: right.length || r.rtext ? "stretch" : r.align === 1 ? "center" : r.align === 2 ? "flex-end" : "flex-start", fontWeight: m.cat === 0 || r.block === 0 ? 800 : 700, fontSize: c * 1.38 + "rem", whiteSpace: "nowrap" }}>
              {part(r.items.filter(x => x.slot === "pre"))}
              {r.text ? <div style={{ marginRight: c * 0.35 + "rem" }}>{r.text}</div> : null}
              {part(r.items.filter(x => x.slot === "post"))}
              {right.length || r.rtext ? <div style={{ flex: 1 }} /> : null}
              {r.rtext ? <div style={{ marginRight: right.length ? c * 0.35 + "rem" : 0 }}>{r.rtext}</div> : null}
              {part(right)}
            </div>
          );
        })}
        {m.arrow ? <div style={{ height: cap * 2.3 + "rem" }} /> : null}
      </div>
      {m.arrow ? <div style={{ position: "absolute", bottom: "8%", left: arrowLeft }}><ArrowImg code={m.arrow} dark={base.fg === DARK} size={cap * 2 + "rem"} /></div> : null}
    </div>
  );
};

/* ---------------------------------------------------------------- iç pencere */
type Tr = (k: string, fb: string) => string;

const SignsWindowInner = () => {
  const raw = useValue(state$);
  const { translate } = useLocalization();
  const tr: Tr = (k, fb) => translate("SignsPlus.UI." + k, fb) || fb;

  const st: State = useMemo(() => {
    try { const s = JSON.parse(raw || "{}"); return { city: s.city || "", count: s.count || 0, sel: s.sel || null }; }
    catch (e) { return { city: "", count: 0, sel: null }; }
  }, [raw]);

  const [rect, setRect] = useState<Rect>(() => clampRect(savedRect || defaultRect()));
  useEffect(() => { savedRect = rect; }, [rect]);

  function startFrame(e: MouseEvent, kind: "move" | "size") {
    e.stopPropagation();
    const sx = e.clientX, sy = e.clientY, r0 = { ...rect };
    const mm = (ev: any) => {
      const dx = ev.clientX - sx, dy = ev.clientY - sy;
      if (kind === "move") setRect(clampRect({ ...r0, x: r0.x + dx, y: r0.y + dy }));
      else setRect({ ...r0, w: Math.max(MIN_W, r0.w + dx), h: Math.max(MIN_H, r0.h + dy) });
    };
    const up = () => { document.removeEventListener("mousemove", mm); document.removeEventListener("mouseup", up); };
    document.addEventListener("mousemove", mm);
    document.addEventListener("mouseup", up);
  }

  const assetName = st.sel ? tr("Asset." + st.sel.asset, st.sel.asset) : "";

  return (
    <div style={{ ...S.win, left: rect.x + "px", top: rect.y + "px", width: rect.w + "px", height: rect.h + "px" }}>
      <div style={S.head}>
        <div style={S.title} onMouseDown={e => startFrame(e, "move")}>
          <img src={ICON} style={S.icon} />
          <span>{tr("Title", "Signs+")}</span>
          {assetName ? <span style={{ color: C.muted, fontWeight: 400, marginLeft: "8rem" }}>{assetName}</span> : null}
        </div>
        <div style={S.close} onClick={() => setOpen(false)}>×</div>
      </div>
      <div style={S.body}>
        {st.sel ? (st.sel.faces.some(f => f.kind === 1) ? <StdEditor key={st.sel.key} sel={st.sel} tr={tr} /> : <Editor key={st.sel.key} sel={st.sel} tr={tr} />) : (
          <div style={S.empty}>
            <div style={{ fontSize: "16rem", fontWeight: 700, color: C.ink, marginBottom: "8rem" }}>{tr("NoSign", "No sign selected")}</div>
            <div>{tr("NoSignHint", "Click a Signs+ sign in the city, or hover the mouse over one and press Shift+L.")}</div>
            <div style={{ ...S.btn, display: "inline-block", marginTop: "14rem" }} onClick={() => pickCenter()}>{tr("PickCenter", "Pick the sign in the middle of the screen")}</div>
          </div>
        )}
      </div>
      <div style={S.grip} onMouseDown={e => startFrame(e, "size")} />
    </div>
  );
};

/* ---------------------------------------------------------------- seçili levhanın düzenleyicisi */
type Pop = { type: "block" | "item" | "add" | "templates"; row: number; idx?: number } | null;
type EditorProps = { sel: { asset: string; key: string; faces: Face[] }; tr: Tr };

const Editor = ({ sel, tr }: EditorProps) => {
  const [faceIdx, setFaceIdx] = useState(0);
  const face = sel.faces[Math.min(faceIdx, sel.faces.length - 1)];
  // Her yüzün yerel modeli; sunucudan gelen değer yalnızca ilk açılışta okunur (yazarken geri sarmasın)
  const [models, setModels] = useState<Record<string, Model>>(() => {
    const m: Record<string, Model> = {};
    for (const f of sel.faces) m[f.id] = parse(f);
    return m;
  });
  const modelsRef = useRef(models);
  modelsRef.current = models;
  const model = models[face.id] || parse(face);
  const [pop, setPop] = useState<Pop>(null);
  const [undo, setUndo] = useState<{ id: string; m: Model } | null>(null);

  // Değişiklik 200 ms sonra gönderilir; levhanın dokusu her tuşta yeniden çizilmesin
  const timers = useRef<Record<string, any>>({});
  const send = (id: string, m: Model) => {
    if (timers.current[id]) clearTimeout(timers.current[id]);
    timers.current[id] = setTimeout(() => { setFace(id, m.cat, serialize(m)); timers.current[id] = null; }, 200);
  };
  useEffect(() => () => {
    // Pencere kapanırken bekleyen değişiklikler hemen gönderilir
    for (const id of Object.keys(timers.current)) if (timers.current[id]) { clearTimeout(timers.current[id]); const m = modelsRef.current[id]; if (m) setFace(id, m.cat, serialize(m)); }
    setTyping(false);
  }, []);

  const update = (m: Model, id = face.id) => {
    setModels(prev => ({ ...prev, [id]: m }));
    send(id, m);
  };
  /** Bütün levhayı değiştiren işlemler (şablon, temizle) geri alınabilir */
  const replaceAll = (m: Model) => { setUndo({ id: face.id, m: model }); update(m); };

  const setRow = (i: number, r: Partial<Row>) => update({ ...model, rows: model.rows.map((x, j) => j === i ? { ...x, ...r } : x) });
  const moveRow = (i: number, d: number) => {
    const j = i + d;
    if (j < 0 || j >= model.rows.length) return;
    const rows = model.rows.slice();
    const t = rows[i]; rows[i] = rows[j]; rows[j] = t;
    update({ ...model, rows });
  };
  const delRow = (i: number) => update({ ...model, rows: model.rows.filter((_, j) => j !== i) });
  // Yeni satır bir önceki satırın hizasını alır
  const addRow = () => update({ ...model, rows: [...model.rows, { text: "", block: -1, size: 1, align: model.rows.length ? model.rows[model.rows.length - 1].align : 0, items: [], rtext: "" }] });
  const alignAll = (a: number) => update({ ...model, rows: model.rows.map(r => ({ ...r, align: a })) });
  const allAlign = model.rows.length && model.rows.every(r => r.align === model.rows[0].align) ? model.rows[0].align : -1;
  const setItem = (i: number, k: number, it: Partial<Item> | null) => {
    const row = model.rows[i];
    const items = it === null ? row.items.filter((_, j) => j !== k) : row.items.map((x, j) => j === k ? { ...x, ...it } : x);
    setRow(i, { items });
  };
  const addItem = (i: number, it: Item) => { setRow(i, { items: [...model.rows[i].items, it] }); setPop(null); };

  const faceLabel = (i: number) => sel.faces.length === 2 ? `${tr("Face", "Panel")} ${i + 1} (${i === 0 ? tr("FaceLeft", "left") : tr("FaceRight", "right")})` : `${tr("Face", "Panel")} ${i + 1}`;
  const other = sel.faces.length > 1 ? sel.faces[(faceIdx + 1) % sel.faces.length] : null;
  const typingProps = { onFocus: () => setTyping(true), onBlur: () => setTyping(false) };
  const slotLabel = (s: Slot) => s === "pre" ? tr("SlotPre", "start") : s === "post" ? tr("SlotPost", "after") : tr("SlotRight", "right");
  const symName = (id: string) => tr("Sym." + id, (SYMS.find(s => s[0] === id) || [id, id])[1]);

  return (
    <div onMouseDown={() => pop && setPop(null)}>
      <div style={{ display: "flex", alignItems: "center", position: "relative" }}>
        {sel.faces.length > 1 ? (
          <div style={S.seg}>
            {sel.faces.map((f, i) => (
              <div key={f.id} style={{ ...S.segBtn, background: i === faceIdx ? C.accent : "transparent", color: i === faceIdx ? "#0d0f13" : C.ink }}
                onClick={() => { setFaceIdx(i); setPop(null); }}>{faceLabel(i)}</div>
            ))}
          </div>
        ) : null}
        <div style={{ flex: 1 }} />
        <div style={{ position: "relative" }} onMouseDown={stop}>
          <div style={S.btn} onClick={() => setPop(pop && pop.type === "templates" ? null : { type: "templates", row: -1 })}>{tr("Templates", "Templates")} ▾</div>
          {pop && pop.type === "templates" ? (
            <div style={{ ...S.pop, left: "auto", right: 0, width: "230rem" }}>
              {TEMPLATES.map(t => (
                <div key={t.id} style={S.popItem} onClick={() => { replaceAll(parse({ ...face, cat: t.cat, spec: t.spec })); setPop(null); }}>
                  <div style={{ ...S.dot, background: CATS[t.cat].bg }} />{tr("Tpl." + t.id, t.fb)}
                </div>
              ))}
            </div>
          ) : null}
        </div>
      </div>

      <Preview m={model} w={face.w} h={face.h} />

      <div style={S.eyebrow}>{tr("Background", "Sign color")}</div>
      <div style={S.swatches}>
        {CATS.map((c, i) => (
          <div key={c.tag} style={{ ...S.swatch, background: model.cat === i ? C.hover : "transparent", borderColor: model.cat === i ? C.accent : C.rule }}
            onClick={() => update({ ...model, cat: i })}>
            <div style={{ ...S.dot, background: c.bg }} />{tr(c.key, c.fb)}
          </div>
        ))}
      </div>

      <div style={{ display: "flex", alignItems: "flex-end" }}>
        <div style={{ ...S.eyebrow, flex: 1 }}>{tr("Lines", "Lines")}</div>
        {model.rows.length ? (
          <div style={{ display: "flex", alignItems: "center", marginBottom: "4rem" }}>
            <span style={{ fontSize: "11rem", color: C.muted, marginRight: "6rem" }}>{tr("AlignAll", "Align all")}</span>
            {[0, 1, 2].map(a => (
              <div key={a} style={{ ...S.chip, width: "26rem", height: "24rem", marginRight: "4rem", background: allAlign === a ? C.accent : "transparent", borderColor: allAlign === a ? C.accent : C.rule }}
                onClick={() => alignAll(a)}><AlignIcon a={a} on={false} dark={allAlign === a} /></div>
            ))}
          </div>
        ) : null}
      </div>
      {model.rows.map((r, i) => {
        const b = r.block >= 0 ? CATS[r.block] : null;
        const ctx = b || CATS[model.cat] || CATS[2];
        return (
          <div key={i} style={S.rowBox}>
            <div style={S.row}>
              <div style={{ ...S.chip, background: b ? b.bg : "transparent", color: b ? b.fg : C.muted }} title={tr("Block", "Block")}
                onMouseDown={stop} onClick={() => setPop(pop && pop.type === "block" && pop.row === i ? null : { type: "block", row: i })}>{b ? "■" : "–"}</div>
              <div style={{ ...S.chip, width: "36rem", fontSize: "11rem", fontWeight: 700, color: r.size === 1 ? C.muted : C.accent }} title={tr("Size", "Text size")}
                onClick={() => setRow(i, { size: SIZES[(SIZES.indexOf(r.size) + 1) % SIZES.length] })}>%{Math.round(r.size * 100)}</div>
              <div style={{ ...S.chip, borderColor: r.align ? C.accent : C.rule }} title={tr("Align", "Alignment (click to change)")}
                onClick={() => setRow(i, { align: (r.align + 1) % 3 })}><AlignIcon a={r.align} on={r.align !== 0} /></div>
              <input style={S.input} value={r.text} placeholder={tr("LinePlaceholder", "Place name")} {...typingProps}
                onChange={(e: any) => setRow(i, { text: clean(e.target.value) })} />
              <input style={S.rinput} value={r.rtext} placeholder={tr("RightPlaceholder", "25 km")} title={tr("RightHint", "Right-aligned text, e.g. distance")} {...typingProps}
                onChange={(e: any) => setRow(i, { rtext: clean(e.target.value) })} />
              <div style={S.step} title={tr("MoveUp", "Move up")} onClick={() => moveRow(i, -1)}>▲</div>
              <div style={S.step} title={tr("MoveDown", "Move down")} onClick={() => moveRow(i, 1)}>▼</div>
              <div style={S.step} title={tr("Delete", "Delete")} onClick={() => delRow(i)}>×</div>
              {pop && pop.type === "block" && pop.row === i ? (
                <div style={S.pop} onMouseDown={stop}>
                  <div style={S.popItem} onClick={() => { setRow(i, { block: -1 }); setPop(null); }}>
                    <div style={{ ...S.dot, background: "transparent", borderColor: C.rule }} />{tr("NoBlock", "No block")}
                  </div>
                  {CATS.map((c, k) => (
                    <div key={c.tag} style={{ ...S.popItem, background: r.block === k ? C.hover : "transparent" }} onClick={() => { setRow(i, { block: k }); setPop(null); }}>
                      <div style={{ ...S.dot, background: c.bg }} />{tr(c.key, c.fb)}
                    </div>
                  ))}
                </div>
              ) : null}
            </div>

            <div style={S.items} onMouseDown={stop}>
              {r.items.map((it, k) => {
                const open = pop && pop.type === "item" && pop.row === i && pop.idx === k;
                return (
                  <div key={k} style={{ position: "relative" }}>
                    <div style={{ ...S.item, borderColor: open ? C.accent : C.rule, background: ctx.bg }} title={it.kind === "s" ? symName(it.v) : it.v}
                      onClick={() => setPop(open ? null : { type: "item", row: i, idx: k })}>
                      <ItemView it={it} h={18} bg={ctx.bg} fg={ctx.fg} />
                      <span style={{ ...S.slotTag, color: ctx.fg }}>{slotLabel(it.slot)}</span>
                    </div>
                    {open ? <ItemPop it={it} tr={tr} typingProps={typingProps} onChange={p => setItem(i, k, p)} onDelete={() => { setItem(i, k, null); setPop(null); }} /> : null}
                  </div>
                );
              })}
              <div style={{ position: "relative" }}>
                <div style={S.add} onClick={() => setPop(pop && pop.type === "add" && pop.row === i ? null : { type: "add", row: i })}>{tr("AddItem", "+ Symbol / number / arrow")}</div>
                {pop && pop.type === "add" && pop.row === i ? <AddPop tr={tr} typingProps={typingProps} symName={symName} onAdd={it => addItem(i, it)} /> : null}
              </div>
            </div>
          </div>
        );
      })}
      <div style={{ ...S.btn, display: "inline-block" }} onClick={addRow}>{tr("AddLine", "+ Add line")}</div>

      <div style={S.eyebrow}>{tr("BigArrow", "Large arrow")}</div>
      <div style={S.grid}>
        {ARROW_GRID.map((row, ri) => (
          <div key={ri} style={S.gridRow}>
            {row.map(code => {
              const on = model.arrow === code;
              return (
                <div key={code || "none"} style={{ ...S.cell, background: on ? C.accent : "transparent", borderColor: on ? C.accent : C.rule }}
                  onClick={() => update({ ...model, arrow: code })}>
                  {code ? <ArrowImg code={code} dark={on} size="20rem" /> : <span style={{ color: on ? "#0d0f13" : C.muted }}>{tr("NoArrow", "None")}</span>}
                </div>
              );
            })}
          </div>
        ))}
      </div>

      <div style={{ display: "flex", marginTop: "14rem" }}>
        {other ? <div style={{ ...S.btn, marginRight: "8rem" }} onClick={() => update({ ...model }, other.id)}>{tr("CopyToOther", "Copy to the other panel")}</div> : null}
        <div style={{ ...S.btn, marginRight: "8rem" }} onClick={() => replaceAll({ cat: model.cat, rows: [], arrow: "" })}>{tr("Clear", "Clear")}</div>
        {undo && undo.id === face.id ? <div style={S.btn} onClick={() => { update(undo.m); setUndo(null); }}>{tr("Undo", "Undo")}</div> : null}
      </div>
      <div style={S.note}>{tr("Saved", "Changes show on the sign right away and are saved for this city.")}</div>
    </div>
  );
};

/* ---------------------------------------------------------------- öğe ayarları */
type TypingProps = { onFocus: () => void; onBlur: () => void };

const ItemPop = ({ it, tr, typingProps, onChange, onDelete }: { it: Item; tr: Tr; typingProps: TypingProps; onChange: (p: Partial<Item>) => void; onDelete: () => void }) => {
  const slots: Slot[] = ["pre", "post", "right"];
  const label = (s: Slot) => s === "pre" ? tr("SlotPreLong", "Before the text") : s === "post" ? tr("SlotPostLong", "After the text") : tr("SlotRightLong", "Right edge");
  return (
    <div style={{ ...S.pop, top: "30rem", width: "220rem" }} onMouseDown={stop}>
      {slots.map(s => (
        <div key={s} style={{ ...S.popItem, background: it.slot === s ? C.hover : "transparent", color: it.slot === s ? C.accent : C.ink }} onClick={() => onChange({ slot: s })}>{label(s)}</div>
      ))}
      {it.kind === "s" ? (
        <div style={{ ...S.popItem, color: it.k ? C.accent : C.ink }} onClick={() => onChange({ k: !it.k })}>{it.k ? "☑ " : "☐ "}{tr("Boxed", "In a box")}</div>
      ) : null}
      {it.kind === "n" ? (
        <input style={{ ...S.input, width: "100%", margin: "4rem 0" }} value={it.v} {...typingProps} onChange={(e: any) => onChange({ v: clean(e.target.value) })} />
      ) : null}
      {it.kind === "ok" ? (
        <div style={{ ...S.grid, margin: "4rem 0" }}>
          {ARROW_GRID.map((row, ri) => (
            <div key={ri} style={S.gridRow}>
              {row.map(code => code ? (
                <div key={code} style={{ ...S.cell, ...S.small, background: it.v === code ? C.accent : "transparent" }} onClick={() => onChange({ v: code })}>
                  <ArrowImg code={code} dark={it.v === code} size="16rem" />
                </div>
              ) : <div key="c" style={{ ...S.small, margin: "0 4rem 4rem 0" }} />)}
            </div>
          ))}
        </div>
      ) : null}
      <div style={{ ...S.popItem, color: "#e36d6d" }} onClick={onDelete}>{tr("Delete", "Delete")}</div>
    </div>
  );
};

const AddPop = ({ tr, typingProps, symName, onAdd }: { tr: Tr; typingProps: TypingProps; symName: (id: string) => string; onAdd: (it: Item) => void }) => {
  const [tab, setTab] = useState<"s" | "n" | "ok">("s");
  const [num, setNum] = useState("O-");
  const tabs: [typeof tab, string, string][] = [["s", "TabSymbol", "Symbol"], ["n", "TabNumber", "Number box"], ["ok", "TabArrow", "Arrow"]];
  return (
    <div style={S.popWide} onMouseDown={stop}>
      <div style={S.tabs}>
        {tabs.map(([k, key, fb]) => (
          <div key={k} style={{ ...S.tab, background: tab === k ? C.accent : "transparent", color: tab === k ? "#0d0f13" : C.ink }} onClick={() => setTab(k)}>{tr(key, fb)}</div>
        ))}
      </div>
      {tab === "s" ? (
        <div style={S.symGrid}>
          {SYMS.map(([id]) => (
            <div key={id} style={S.symCell} title={symName(id)} onClick={() => onAdd({ kind: "s", v: id, slot: "pre" })}>
              <img src={symSrc(id, false)} style={{ height: "24rem", width: (SYM_RATIO[id] || 1) * 24 + "rem" }} />
            </div>
          ))}
        </div>
      ) : null}
      {tab === "n" ? (
        <div>
          <div style={{ display: "flex", marginBottom: "6rem" }}>
            {["O-", "E ", "D-", "K "].map(p => (
              <div key={p} style={{ marginRight: "6rem", cursor: "pointer" }} onClick={() => setNum(p)}>
                <ItemView it={{ kind: "n", v: p.trim() || p, slot: "post" }} h={24} bg={CATS[1].bg} fg={CATS[1].fg} />
              </div>
            ))}
          </div>
          <div style={{ display: "flex" }}>
            <input style={S.input} value={num} placeholder="O-4, E 80, D-100, K 11" {...typingProps} onChange={(e: any) => setNum(clean(e.target.value))} />
            <div style={{ ...S.btn, marginLeft: "6rem" }} onClick={() => num.trim() && onAdd({ kind: "n", v: num.trim(), slot: "post" })}>{tr("Add", "Add")}</div>
          </div>
          <div style={{ ...S.note, marginTop: "6rem" }}>{tr("NumberHint", "O and K: orange, E: green, D: white.")}</div>
        </div>
      ) : null}
      {tab === "ok" ? (
        <div style={S.grid}>
          {ARROW_GRID.map((row, ri) => (
            <div key={ri} style={S.gridRow}>
              {row.map(code => code ? (
                <div key={code} style={S.cell} onClick={() => onAdd({ kind: "ok", v: code, slot: "right" })}><ArrowImg code={code} dark={false} size="20rem" /></div>
              ) : <div key="c" style={{ width: "38rem", height: "38rem", margin: "0 4rem 4rem 0" }} />)}
            </div>
          ))}
        </div>
      ) : null}
    </div>
  );
};

/* ---------------------------------------------------------------- standart trafik işareti düzenleyicisi */
const stdSrc = (id: string) => `coui://ui-mods/SignsPlus-std-${id}.png`;
const STD_GROUPS: [string, string, string][] = [["tehlike", "GroupWarn", "Tehlike uyarı"], ["tanzim", "GroupReg", "Tanzim"], ["bilgi", "GroupInfo", "Bilgi"]];

function parseStd(spec: string) {
  const s = (spec || "").trim();
  const i = s.indexOf(":");
  const id = i >= 0 ? s.substring(0, i).trim() : s;
  const value = i >= 0 ? s.substring(i + 1).trim() : "";
  const sign = STD_SIGNS.find(x => x.id === id) || STD_SIGNS[0];
  return { sign, value };
}

const StdEditor = ({ sel, tr }: EditorProps) => {
  const signFace = sel.faces.find(f => f.kind === 1)!;
  const plateFace = sel.faces.find(f => f.kind === 2);
  const init = parseStd(signFace.spec);
  const [signId, setSignId] = useState(init.sign.id);
  const [value, setValue] = useState(init.value || init.sign.def);
  const [lines, setLines] = useState<string[]>(() => {
    // Ek levha satırları ortalı saklanır: "[orta]200 m"
    const l = (plateFace ? plateFace.spec : "").split("|").map(x => x.replace(/^\[[^\]]*\]/, "").trim());
    return [l[0] || "", l[1] || ""];
  });
  const sign = STD_SIGNS.find(x => x.id === signId) || STD_SIGNS[0];
  const [group, setGroup] = useState(sign.group);

  const timers = useRef<Record<string, any>>({});
  const pending = useRef<Record<string, string>>({});
  const send = (id: string, spec: string) => {
    pending.current[id] = spec;
    if (timers.current[id]) clearTimeout(timers.current[id]);
    timers.current[id] = setTimeout(() => { setFace(id, 2, spec); timers.current[id] = null; delete pending.current[id]; }, 200);
  };
  useEffect(() => () => {
    for (const id of Object.keys(pending.current)) { clearTimeout(timers.current[id]); setFace(id, 2, pending.current[id]); }
    setTyping(false);
  }, []);

  const specOf = (id: string, v: string) => {
    const s = STD_SIGNS.find(x => x.id === id);
    return s && s.param && v.trim() ? `${id}:${clean(v).replace(/:/g, "").trim()}` : id;
  };
  const choose = (id: string) => {
    const s = STD_SIGNS.find(x => x.id === id)!;
    const v = s.param ? (value || s.def) : "";
    setSignId(id); setValue(v);
    send(signFace.id, specOf(id, v));
  };
  const changeValue = (v: string) => { setValue(v); send(signFace.id, specOf(signId, v)); };
  const changeLine = (i: number, v: string) => {
    const l = lines.slice(); l[i] = clean(v); setLines(l);
    if (plateFace) send(plateFace.id, l.map(x => x.trim()).filter(x => x).map(x => "[orta]" + x).join("|"));
  };
  const typingProps = { onFocus: () => setTyping(true), onBlur: () => setTyping(false) };
  const name = (id: string) => tr("Std." + id, (STD_SIGNS.find(x => x.id === id) || { tr: id }).tr);

  return (
    <div>
      <div style={{ display: "flex", alignItems: "center" }}>
        <div style={{ position: "relative", width: "110rem", height: "110rem", display: "flex", alignItems: "center", justifyContent: "center" }}>
          <img src={stdSrc(sign.id)} style={{ width: "104rem", height: "104rem" }} />
          {sign.param ? <div style={{ position: "absolute", left: 0, right: 0, top: "38rem", textAlign: "center", fontSize: "34rem", fontWeight: 800, color: "#1c1c1c", background: "#f2f2ec", margin: "0 26rem", borderRadius: "4rem" }}>{value || sign.def}</div> : null}
        </div>
        <div style={{ marginLeft: "14rem", flex: 1 }}>
          <div style={{ fontSize: "16rem", fontWeight: 700 }}>{name(sign.id)}</div>
          {sign.param ? (
            <div style={{ display: "flex", alignItems: "center", marginTop: "8rem" }}>
              <span style={{ fontSize: "13rem", color: C.muted, marginRight: "8rem" }}>{tr("StdValue", "Value")}</span>
              <input style={{ ...S.input, flex: 0, width: "90rem" }} value={value} placeholder={sign.def} title={tr("StdValueHint", "e.g. 30, 50, 70")} {...typingProps}
                onChange={(e: any) => changeValue((e.target.value || "").substring(0, 6))} />
            </div>
          ) : null}
          {plateFace && (lines[0] || lines[1]) ? (
            <div style={{ display: "inline-block", marginTop: "8rem", padding: "2rem 10rem", background: "#f2f2ec", color: "#1c1c1c", border: "2rem solid #1c1c1c", borderRadius: "3rem", fontWeight: 700, fontSize: "13rem", textAlign: "center" }}>
              {lines.filter(x => x.trim()).map((x, i) => <div key={i}>{x}</div>)}
            </div>
          ) : null}
        </div>
      </div>

      <div style={S.eyebrow}>{tr("StdSign", "Sign")}</div>
      <div style={{ ...S.seg, marginBottom: "8rem" }}>
        {STD_GROUPS.map(([g, key, fb]) => (
          <div key={g} style={{ ...S.segBtn, background: group === g ? C.accent : "transparent", color: group === g ? "#0d0f13" : C.ink }} onClick={() => setGroup(g)}>{tr(key, fb)}</div>
        ))}
      </div>
      <div style={S.symGrid}>
        {STD_SIGNS.filter(x => x.group === group).map(x => (
          <div key={x.id} title={name(x.id)} onClick={() => choose(x.id)}
            style={{ ...S.symCell, width: "54rem", height: "54rem", background: x.id === signId ? C.hover : "transparent", borderColor: x.id === signId ? C.accent : C.rule }}>
            <img src={stdSrc(x.id)} style={{ width: "44rem", height: "44rem" }} />
          </div>
        ))}
      </div>

      {plateFace ? (
        <div>
          <div style={S.eyebrow}>{tr("Plate", "Additional plate")}</div>
          {[0, 1].map(i => (
            <div key={i} style={S.row}>
              <input style={S.input} value={lines[i]} placeholder={i === 0 ? tr("PlateLine1", "Line 1") + " — 200 m" : tr("PlateLine2", "Line 2")} {...typingProps}
                onChange={(e: any) => changeLine(i, e.target.value || "")} />
            </div>
          ))}
          <div style={{ ...S.note, marginTop: "4rem" }}>{tr("PlateHint", "Text under the sign, e.g. 200 m. Empty: no plate.")}</div>
        </div>
      ) : null}
      <div style={S.note}>{tr("Saved", "Changes show on the sign right away and are saved for this city.")}</div>
    </div>
  );
};
