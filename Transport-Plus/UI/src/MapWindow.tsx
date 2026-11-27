import { useEffect, useMemo, useRef, useState, CSSProperties, MouseEvent, WheelEvent } from "react";
import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import { mapData$, refreshMap, jumpTo, lastSaved$, saveImage, openFolder, options$, setOption, refreshRoutes, setWorldView } from "./bindings";
import { useOpen } from "./useOpen";
import { TransitMap, TYPE_ORDER, LineType, LabelLevel, chipText, LIGHT, DARK } from "./transitMap";
import { RoutesTab } from "./RoutesTab";
import { TaxiTab } from "./TaxiTab";
import { ErrorBoundary } from "./ErrorBoundary";
import { ICON } from "./TransportButton";
import { parseMarkup, drawElements } from "./canvasSvg";

type VB = { x: number; y: number; w: number; h: number };
const TYPE_EN: Record<string, string> = { subway: "Metro", train: "Train", tram: "Tram", ship: "Ferry", bus: "Bus" };

export const MapWindow = () => {
  const [open, setOpen] = useOpen();
  // Hata olursa pencere kapanmaz: küçük bir hata kutusu çıkar (neden + yeniden dene / kapat), ayrıntı UI.log'da
  return open ? <ErrorBoundary fallback={(msg, retry) => <WindowError message={msg} retry={retry} close={() => setOpen(false)} />}><MapWindowInner /></ErrorBoundary> : null;
};

const WindowError = ({ message, retry, close }: { message: string; retry: () => void; close: () => void }) => (
  <div style={{ position: "absolute", left: "16rem", top: "120rem", width: "420rem", padding: "14rem 16rem", background: "rgba(24,28,35,0.97)", borderRadius: "10rem", color: "#e9ecf1", fontSize: "13rem", lineHeight: 1.4, pointerEvents: "auto" }}>
    <div style={{ fontWeight: 700, fontSize: "15rem", marginBottom: "6rem" }}>Transport+</div>
    <div>Transport+ ran into a problem and could not draw its window. Please send UI.log and TransportPlus.Mod.log (Logs folder) in the forum thread.</div>
    <div style={{ marginTop: "8rem", color: "#e0a43a", fontSize: "12rem", wordBreak: "break-all" }}>{message}</div>
    <div style={{ display: "flex", marginTop: "10rem" }}>
      <div style={{ padding: "4rem 12rem", border: "1px solid #2d343f", borderRadius: "6rem", cursor: "pointer", marginRight: "8rem" }} onClick={retry}>Try again</div>
      <div style={{ padding: "4rem 12rem", border: "1px solid #2d343f", borderRadius: "6rem", cursor: "pointer" }} onClick={close}>Close</div>
    </div>
  </div>
);
type Tab = "net" | "routes" | "taxi";
let savedTab: Tab = "net";

/* Menünün konumu ve boyutu: kapatıp açınca aynı yerde açılır */
type Rect = { x: number; y: number; w: number; h: number };
let savedRect: Rect | null = null;
let savedRoutesRect: Rect | null = null;
const MIN_W = 640, MIN_H = 400, MIN_RW = 360;
/* Rota sekmesi: harita şehrin kendisinde çizildiği için panel dar ve sola yaslı açılır */
function defaultRoutesRect(): Rect {
  const W = window.innerWidth, H = window.innerHeight, u = Math.max(0.6, H / 1080);
  const w = Math.max(MIN_RW, Math.round(430 * u));
  return { x: Math.round(12 * u), y: Math.round(H * 0.1), w, h: Math.round(H * 0.8) };
}
function defaultRect(): Rect {
  const W = window.innerWidth, H = window.innerHeight;
  const w = Math.max(MIN_W, Math.round(W * 0.74)), h = Math.max(MIN_H, Math.round(H * 0.72));
  return { x: Math.round((W - w) / 2), y: Math.round(H * 0.12), w, h };
}
function clampRect(r: Rect): Rect {
  const W = window.innerWidth, H = window.innerHeight;
  return { ...r, x: Math.min(Math.max(r.x, 120 - r.w), W - 120), y: Math.min(Math.max(r.y, 0), H - 48) };
}

/* ---------------------------------------------------------------- stiller (oyun arayüzüne yakın koyu tema) */
const C = { panel: "rgba(24,28,35,0.97)", rule: "#2d343f", ink: "#e9ecf1", muted: "#9aa3b0", hover: "#262c36", accent: "#5ea6ec" };
const S: Record<string, CSSProperties> = {
  win: { position: "absolute", display: "flex", flexDirection: "column", background: C.panel, border: `1px solid ${C.rule}`, borderRadius: "8rem", color: C.ink, pointerEvents: "auto", overflow: "hidden", boxShadow: "0 12rem 40rem rgba(0,0,0,0.45)", userSelect: "none" },
  head: { display: "flex", alignItems: "center", padding: "8rem 8rem 8rem 14rem", background: "rgba(12,14,18,0.92)", borderBottom: `1px solid ${C.rule}` },
  title: { flex: 1, display: "flex", alignItems: "center", fontSize: "18rem", fontWeight: 700, alignSelf: "stretch", cursor: "move" },
  icon: { width: "22rem", height: "22rem", marginRight: "10rem" },
  close: { width: "30rem", height: "30rem", marginLeft: "10rem", borderRadius: "6rem", display: "flex", alignItems: "center", justifyContent: "center", fontSize: "22rem", color: C.muted, cursor: "pointer" },
  pop: { position: "absolute", top: "50rem", right: "48rem", width: "270rem", background: "rgba(16,19,24,0.98)", border: `1px solid ${C.rule}`, borderRadius: "8rem", padding: "12rem 14rem", zIndex: 5, boxShadow: "0 10rem 30rem rgba(0,0,0,0.5)" },
  popLabel: { fontSize: "11rem", color: C.muted, fontWeight: 600, textTransform: "uppercase", letterSpacing: "1rem", margin: "0 0 6rem" },
  notice: { position: "absolute", left: "50%", bottom: "14rem", transform: "translateX(-50%)", display: "flex", alignItems: "center", background: "rgba(12,14,18,0.95)", border: `1px solid ${C.rule}`, borderRadius: "8rem", padding: "8rem 10rem 8rem 14rem", fontSize: "13rem", whiteSpace: "nowrap" },
  primary: { padding: "7rem 14rem", borderRadius: "6rem", fontSize: "13rem", fontWeight: 700, background: C.accent, color: "#0d0f13", textAlign: "center", cursor: "pointer", marginTop: "12rem" },
  grip: { position: "absolute", right: 0, bottom: 0, width: "18rem", height: "18rem", cursor: "nwse-resize", borderRight: `3rem solid ${C.muted}`, borderBottom: `3rem solid ${C.muted}`, opacity: 0.5 },
  seg: { display: "flex", border: `1px solid ${C.rule}`, borderRadius: "16rem", padding: "2rem", marginRight: "10rem" },
  segBtn: { padding: "5rem 14rem", borderRadius: "14rem", fontSize: "13rem", fontWeight: 600, color: C.ink, cursor: "pointer" },
  btn: { padding: "6rem 12rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", fontSize: "13rem", fontWeight: 600, color: C.ink, marginLeft: "8rem", cursor: "pointer" },
  body: { flex: 1, display: "flex", minHeight: 0 },
  side: { width: "260rem", borderRight: `1px solid ${C.rule}`, padding: "10rem 12rem", overflowY: "auto" },
  mapArea: { flex: 1, position: "relative", overflow: "hidden", cursor: "move" },
  grpHead: { display: "flex", alignItems: "center", fontWeight: 700, fontSize: "13rem", padding: "8rem 0 4rem", cursor: "pointer" },
  box: { width: "14rem", height: "14rem", borderRadius: "3rem", border: `2rem solid ${C.muted}`, marginRight: "8rem" },
  row: { display: "flex", alignItems: "center", padding: "4rem 6rem", borderRadius: "6rem", fontSize: "13rem", cursor: "pointer" },
  chip: { minWidth: "26rem", height: "22rem", padding: "0 5rem", borderRadius: "11rem", display: "flex", alignItems: "center", justifyContent: "center", fontWeight: 700, fontSize: "11rem", marginRight: "8rem" },
  card: { position: "absolute", left: "12rem", bottom: "12rem", width: "300rem", background: C.panel, border: `1px solid ${C.rule}`, borderRadius: "8rem", padding: "12rem 14rem" },
  eyebrow: { fontSize: "11rem", color: C.muted, fontWeight: 600, textTransform: "uppercase", letterSpacing: "1rem" },
  cardTitle: { fontSize: "18rem", fontWeight: 700, margin: "2rem 0 8rem" },
  zoom: { position: "absolute", right: "12rem", bottom: "12rem", display: "flex", flexDirection: "column" },
  zBtn: { width: "34rem", height: "34rem", marginTop: "6rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", background: C.panel, display: "flex", alignItems: "center", justifyContent: "center", fontSize: "18rem", fontWeight: 700, cursor: "pointer" },
  empty: { position: "absolute", left: 0, right: 0, top: "40%", textAlign: "center", color: C.muted, fontSize: "15rem", padding: "0 40rem" }
};

function attrUp(el: any, name: string, stop: any): string | null {
  for (let e = el; e && e !== stop; e = e.parentNode) {
    const v = e.getAttribute && e.getAttribute(name);
    if (v) return v;
  }
  return null;
}

const MapWindowInner = () => {
  const [, setOpen] = useOpen();
  const raw = useValue(mapData$);
  const { translate } = useLocalization();
  const tr = (k: string, fb: string) => translate("TransportPlus.UI." + k, fb) || fb;

  const mapRef = useRef<TransitMap>(new TransitMap());
  const map = mapRef.current;
  const areaRef = useRef<HTMLDivElement>(null);
  const [version, setVersion] = useState(0);
  const [mode, setMode] = useState<"schem" | "geo">("schem");
  const [tab, setTabState] = useState<Tab>(savedTab);
  const setTab = (t: Tab) => { savedTab = t; setTabState(t); };
  const compact = tab !== "net";
  // Otobüs hatları ağ haritasında baştan gizlidir (çok sayıda ve kısa olurlar); lejanttan ya da seçeneklerden açılır
  const [hidden, setHidden] = useState<Set<string>>(new Set(["bus"]));
  const optRaw = useValue(options$);
  const opt = useMemo(() => { try { return JSON.parse(optRaw || "{}") || {}; } catch (e) { return {}; } }, [optRaw]);
  const namesMode: number = opt.names === 1 || opt.names === 2 ? opt.names : 0;
  const [diag, setDiag] = useState<any>(null);
  const [failed, setFailed] = useState(false);
  const [hover, setHover] = useState<string | null>(null);
  const [lock, setLock] = useState<string | null>(null);
  const [sel, setSel] = useState<string | null>(null);
  const [vb, setVb] = useState<VB | null>(null);
  const vbRef = useRef<VB | null>(null);
  vbRef.current = vb;
  const prevCity = useRef<string | null>(null);
  const [netRect, setNetRect] = useState<Rect>(() => clampRect(savedRect || defaultRect()));
  const [routesRect, setRoutesRect] = useState<Rect>(() => clampRect(savedRoutesRect || defaultRoutesRect()));
  useEffect(() => { savedRect = netRect; }, [netRect]);
  useEffect(() => { savedRoutesRect = routesRect; }, [routesRect]);
  const rect = compact ? routesRect : netRect;
  const setRect = compact ? setRoutesRect : setNetRect;
  // Rota sekmesine geçince şehirdeki rota görünümü kendiliğinden açılır; sekmeden çıkınca ya da menü kapanınca kapanır
  useEffect(() => {
    if (tab !== "routes") return;
    setWorldView(true);
    return () => setWorldView(false);
  }, [tab]);
  const [saveOpen, setSaveOpen] = useState(false);
  const [theme, setTheme] = useState<"light" | "dark">("light");
  const [format, setFormat] = useState<"png" | "svg">("png");
  const [saving, setSaving] = useState(false);
  const [notice, setNotice] = useState<{ ok: boolean; text: string } | null>(null);
  const lastSaved = useValue(lastSaved$);
  const seenSave = useRef(lastSaved);
  const tRef = useRef(1);
  const drag = useRef<{ x: number; y: number; moved: boolean } | null>(null);

  // Oyundan yeni veri gelince yeniden yerleştir
  useEffect(() => {
    let d: any = null;
    try { d = JSON.parse(raw || "{}"); } catch (e) { d = null; }
    console.log(`[Transport+] map data received: ${(raw || "").length} chars`);
    setDiag(d && d.diag ? d.diag : null);
    setFailed(!!(d && d.error));
    try {
      map.hidden = hidden;
      map.load(d);
      map.geometry(tRef.current, true);
    } catch (e) {
      // Beklenmeyen bir hat verisi çizimi bozarsa boş harita göster, oyunun arayüzüne dokunma
      console.log("[Transport+] layout failed: " + ((e && (e as any).stack) || e));
      map.M = null; map.G = null; map.FULL = null;
      setFailed(true);
    }
    console.log(`[Transport+] layout done: ${map.M ? map.M.S.size : 0} stations, ${map.M ? map.M.lines.length : 0} lines`);
    // Aynı şehrin yenilenmiş verisiyse (otomatik yenileme, Yenile) yakınlaştırma ve seçim korunur
    const same = !!(map.M && prevCity.current === map.M.city && vbRef.current);
    prevCity.current = map.M ? map.M.city : null;
    if (!same) setVb(map.FULL ? { ...map.FULL } : null);
    if (!same || !(map.M && sel && map.M.S.has(sel))) { setSel(null); setLock(null); }
    setVersion(v => v + 1);
  }, [raw]);

  // Seçeneklerde "otobüsleri ağ haritasında göster" açıksa otobüsler baştan görünür
  const busInit = useRef(false);
  useEffect(() => {
    if (busInit.current || opt.buses === undefined) return;
    busInit.current = true;
    if (opt.buses && hidden.has("bus")) applyHidden(new Set(Array.from(hidden).filter(t => t !== "bus")));
  }, [opt.buses]);

  /* Ad düzeyi: Açık = hepsi, Kapalı = hiçbiri, Otomatik = yazı ekranda okunamayacak kadar küçülünce yalnızca aktarma istasyonları */
  const uiScale = Math.max(0.6, window.innerHeight / 1080);
  const zoomPx = vb ? 12 * Math.min((rect.w - 262 * uiScale) / vb.w, (rect.h - 48 * uiScale) / vb.h) : 12;
  const labelLevel: LabelLevel = namesMode === 1 ? 0 : namesMode === 2 ? 2 : zoomPx >= 8.5 * uiScale ? 0 : zoomPx >= 3.5 * uiScale ? 1 : 2;


  const focus = lock || hover;
  const auto = namesMode === 0, busNames = zoomPx >= 13 * uiScale;
  const inner = useMemo(() => map.markup(focus, sel, labelLevel, auto, busNames), [version, focus, sel, labelLevel, auto, busNames]);
  const lines = map.M ? map.M.lines : [];

  /*
   * Harita bir <canvas> üzerine çizilir. Oyunun arayüz motoru (Gameface) satır içi SVG'yi ve
   * data: adresli SVG resimlerini göstermiyor; canvas 2D destekleniyor.
   */
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const els = useMemo(() => parseMarkup(inner), [inner]);
  useEffect(() => {
    const cv = canvasRef.current, el = areaRef.current;
    if (!cv || !el || !vb) return;
    const id = requestAnimationFrame(() => {
      try {
        const r = el.getBoundingClientRect();
        const W = Math.max(1, Math.round(r.width)), H = Math.max(1, Math.round(r.height));
        if (cv.width !== W) cv.width = W;
        if (cv.height !== H) cv.height = H;
        const ctx = cv.getContext("2d");
        if (!ctx) { console.log("[Transport+] no 2d context"); return; }
        drawElements(ctx, els, vb, W, H, map.palette.land);
      } catch (e) { console.log("[Transport+] draw failed: " + e); }
    });
    return () => cancelAnimationFrame(id);
  }, [els, vb, rect.w, rect.h, tab]);
  const drawnOnce = useRef(false);
  useEffect(() => { if (els.length && !drawnOnce.current) { drawnOnce.current = true; console.log(`[Transport+] drawing ${els.length} map elements`); } }, [els]);

  /* ------------------------------------------------ görünüm değişimi */
  function switchMode(m: "schem" | "geo") {
    if (m === mode || !map.M) return;
    setMode(m);
    tRef.current = m === "schem" ? 1 : 0;
    map.geometry(tRef.current, true);
    setVersion(v => v + 1);
  }
  function applyHidden(next: Set<string>) {
    setHidden(next);
    map.hidden = next;
    try {
      map.layout(); // yerleşim yalnızca görünen türlerle yapılır
      map.geometry(tRef.current, true);
    } catch (e) { console.log("[Transport+] layout failed: " + e); }
    setVb(map.FULL ? { ...map.FULL } : null);
    setSel(null); setLock(null);
    setVersion(v => v + 1);
  }
  function toggleType(type: string) {
    const next = new Set(hidden);
    next.has(type) ? next.delete(type) : next.add(type);
    if (type === "bus") setOption("buses", next.has("bus") ? 0 : 1); // seçim hatırlansın
    applyHidden(next);
  }

  /* ------------------------------------------------ kaydır / yakınlaştır */
  function toSvg(cx: number, cy: number, v: VB) {
    const r = areaRef.current!.getBoundingClientRect();
    const s = Math.min(r.width / v.w, r.height / v.h);
    const ox = (r.width - v.w * s) / 2, oy = (r.height - v.h * s) / 2;
    return { x: v.x + (cx - r.left - ox) / s, y: v.y + (cy - r.top - oy) / s, s };
  }
  function zoomAt(cx: number, cy: number, f: number) {
    if (!vb || !map.FULL) return;
    const nw = Math.max(map.FULL.w * 0.07, Math.min(map.FULL.w * 1.6, vb.w * f)); f = nw / vb.w;
    const p = toSvg(cx, cy, vb);
    setVb({ x: p.x - (p.x - vb.x) * f, y: p.y - (p.y - vb.y) * f, w: vb.w * f, h: vb.h * f });
  }
  function zoomCenter(f: number) {
    const r = areaRef.current!.getBoundingClientRect();
    zoomAt(r.left + r.width / 2, r.top + r.height / 2, f);
  }
  const onDown = (e: MouseEvent) => { drag.current = { x: e.clientX, y: e.clientY, moved: false }; };
  const onMove = (e: MouseEvent) => {
    const d = drag.current;
    if (d && vb) {
      const dx = e.clientX - d.x, dy = e.clientY - d.y;
      if (!d.moved && Math.abs(dx) + Math.abs(dy) > 4) d.moved = true;
      if (d.moved) {
        const s = toSvg(e.clientX, e.clientY, vb).s;
        setVb({ ...vb, x: vb.x - dx / s, y: vb.y - dy / s });
        d.x = e.clientX; d.y = e.clientY;
      }
      return;
    }
  };
  const onUp = (e: MouseEvent) => {
    const d = drag.current; drag.current = null;
    if (d && d.moved) return;
    // Önce geometriden bul: oyun motoru SVG içindeki tek tek öğelere tıklamayı her zaman iletmeyebilir.
    if (vb && map.G) {
      const p = toSvg(e.clientX, e.clientY, vb);
      const hit = map.G.ST.find((s: any) => Math.hypot(s.cx - p.x, s.cy - p.y) < s.r + 6
        || (s.lab && p.x >= s.lab.x && p.x <= s.lab.x + s.lab.w && p.y >= s.lab.y && p.y <= s.lab.y + s.lab.h));
      if (hit) { setSel(hit.id); setLock(null); return; }
      const b = map.G.badges.find((b: any) => Math.hypot(b.x - p.x, b.y - p.y) < 12);
      if (b) { setLock(b.l.id === lock ? null : b.l.id); setSel(null); return; }
    }
    const st = attrUp(e.target, "data-st", areaRef.current);
    if (st) { setSel(st); setLock(null); return; }
    const ln = attrUp(e.target, "data-line", areaRef.current);
    if (ln) { setLock(ln === lock ? null : ln); setSel(null); return; }
    setSel(null); setLock(null);
  };
  const onWheel = (e: WheelEvent) => zoomAt(e.clientX, e.clientY, Math.exp(e.deltaY * 0.0015));

  /* ------------------------------------------------ resmi kaydet */
  useEffect(() => {
    if (!lastSaved || lastSaved === seenSave.current) return;
    seenSave.current = lastSaved;
    setSaving(false);
    let r: any = null;
    try { r = JSON.parse(lastSaved); } catch (e) { r = null; }
    if (r && r.ok) setNotice({ ok: true, text: `${tr("Saved", "Saved")}: ${r.file}` + (r.fallback ? ` · ${tr("PngFallback", "PNG could not be created, saved as SVG instead")}` : "") });
    else setNotice({ ok: false, text: `${tr("SaveFailed", "Could not save the image")}${r && r.error ? ": " + r.error : ""}` });
  }, [lastSaved]);
  useEffect(() => {
    if (!notice) return;
    const id = setTimeout(() => setNotice(null), 9000);
    return () => clearTimeout(id);
  }, [notice]);

  function doSave() {
    if (!map.M || saving) return;
    const types: Record<string, string> = {};
    TYPE_ORDER.forEach(t => { types[t] = tr("Type." + t, TYPE_EN[t]); });
    // Adlar: Kapalı → yok, Açık → hepsi, Otomatik → hepsi ama çakışanlar yazılmaz
    const ex = map.exportSVG(theme === "light" ? LIGHT : DARK,
      { lines: tr("Legend", "Lines"), types, footer: "Transport+ · Cities: Skylines II" }, 2,
      namesMode === 2 ? 2 : 0, namesMode === 0);
    if (!ex) return;
    setSaveOpen(false); setSaving(true); setNotice(null);
    saveImage(format === "png" ? "svg2png" : "svg", ex.svg);
  }

  /* ------------------------------------------------ menüyü taşı / boyutlandır */
  function startFrame(e: MouseEvent, kind: "move" | "size") {
    e.stopPropagation();
    const sx = e.clientX, sy = e.clientY, r0 = { ...rect };
    const mm = (ev: any) => {
      const dx = ev.clientX - sx, dy = ev.clientY - sy;
      if (kind === "move") setRect(clampRect({ ...r0, x: r0.x + dx, y: r0.y + dy }));
      else setRect({ ...r0, w: Math.max(compact ? MIN_RW : MIN_W, r0.w + dx), h: Math.max(MIN_H, r0.h + dy) });
    };
    const up = () => { document.removeEventListener("mousemove", mm); document.removeEventListener("mouseup", up); };
    document.addEventListener("mousemove", mm);
    document.addEventListener("mouseup", up);
  }

  /* ------------------------------------------------ kart içerikleri */
  const chip = (l: any) => <div style={{ ...S.chip, background: l.color, color: chipText(l.color), borderRadius: l.type === "ship" || l.type === "bus" ? "4rem" : "11rem" }}>{l.code}</div>;
  const typeName = (t: string) => tr("Type." + t, TYPE_EN[t] || t);
  // "Bulunan: 3 Tren, 5 Tramvay · gösterilmeyen: 2 yük hattı" — bir hat haritada yoksa nedeni burada yazar
  let diagText = "";
  if (diag) {
    const shown = TYPE_ORDER.filter(t => diag.shown && diag.shown[t]).map(t => `${diag.shown[t]} ${typeName(t)}`);
    const skipped = [
      diag.cargo ? `${diag.cargo} ${tr("SkipCargo", "cargo")}` : "",
      diag.few ? `${diag.few} ${tr("SkipFew", "fewer than 2 stops")}` : "",
      diag.ferryOff ? `${diag.ferryOff} ${tr("SkipFerry", "ferries turned off in options")}` : "",
      diag.other ? `${diag.other} ${tr("SkipOther", "other vehicle types")}` : ""
    ].filter(Boolean);
    diagText = `${tr("Found", "Found")}: ${shown.length ? shown.join(", ") : "0"}` + (skipped.length ? ` · ${tr("Skipped", "not shown")}: ${skipped.join(", ")}` : "");
  }
  const stop = (e: MouseEvent) => e.stopPropagation();
  let card = null;
  const sInfo = sel && map.G ? map.G.STm.get(sel) : null;
  if (sInfo) {
    const kind = sInfo.kind === "hub" || sInfo.xfer ? tr("Interchange", "Interchange") : sInfo.kind === "pier" ? tr("Pier", "Pier") : tr("Stop", "Stop");
    const s0 = sInfo.s;
    card = (
      <div style={S.card} onMouseDown={stop} onMouseUp={stop}>
        <div style={S.eyebrow}>{`${kind} · ${map.gridRef(sInfo.cx, sInfo.cy)}`}</div>
        <div style={S.cardTitle}>{s0.name}</div>
        {sInfo.lines.concat(sInfo.ships).map((l: any) => <div key={l.id} style={{ display: "flex", alignItems: "center", marginBottom: "5rem", fontSize: "13rem" }}>{chip(l)}<span>{l.name}</span></div>)}
        {(sInfo.xfer || []).map((x: any) => (
          <div key={"x" + x.other.id} style={{ marginTop: "8rem" }}>
            <div style={S.eyebrow}>{`${tr("TransferTo", "Transfer")} · ${Math.round(x.m)} m ${tr("Walk", "walk")}`}</div>
            <div style={{ display: "flex", alignItems: "center", marginTop: "4rem", fontSize: "13rem" }}>
              {x.other.lines.concat(x.other.ships).map((l: any) => <div key={l.id}>{chip(l)}</div>)}
              <span>{x.other.s.name}</span>
            </div>
          </div>
        ))}
        <div style={{ ...S.btn, marginLeft: 0, marginTop: "8rem", display: "inline-block" }} onClick={() => jumpTo(s0.wx, s0.wy, s0.wz)}>{tr("ShowInCity", "Show in city")}</div>
      </div>
    );
  } else if (lock) {
    const l: any = lines.find((x: any) => x.id === lock);
    if (l) card = (
      <div style={S.card} onMouseDown={stop} onMouseUp={stop}>
        <div style={S.eyebrow}>{`${tr("Type." + l.type, TYPE_EN[l.type])} · ${l.stops.length} ${tr("Stops", "stops")}`}</div>
        <div style={{ ...S.cardTitle, display: "flex", alignItems: "center" }}>{chip(l)}<span>{l.name}</span></div>
        <div style={{ fontSize: "13rem", color: C.muted, lineHeight: 1.5 }}>{l.stops.map((id: string) => map.M.S.get(id).name).join(" · ")}</div>
      </div>
    );
  }

  return (
    <div style={{ ...S.win, left: rect.x + "px", top: rect.y + "px", width: rect.w + "px", height: rect.h + "px" }}>
      <div style={S.head}>
        <div style={S.title} onMouseDown={e => startFrame(e, "move")}>
          <img src={ICON} style={S.icon} />
          {tab === "net" ? <span>{tr("Title", "Transport+")}</span> : null}
          {tab === "net" && map.M && map.M.city ? <span style={{ color: C.muted, fontWeight: 400, marginLeft: "8rem" }}>{map.M.city}</span> : null}
        </div>
        <div style={S.seg}>
          <div style={{ ...S.segBtn, background: tab === "net" ? C.accent : "transparent", color: tab === "net" ? "#0d0f13" : C.ink }} onClick={() => setTab("net")}>{tr("TabNetwork", "Network map")}</div>
          <div style={{ ...S.segBtn, background: tab === "routes" ? C.accent : "transparent", color: tab === "routes" ? "#0d0f13" : C.ink }} onClick={() => { setSaveOpen(false); setTab("routes"); }}>{tr("TabRoutes", "Lines")}</div>
          <div style={{ ...S.segBtn, background: tab === "taxi" ? C.accent : "transparent", color: tab === "taxi" ? "#0d0f13" : C.ink }} onClick={() => { setSaveOpen(false); setTab("taxi"); }}>{tr("TabTaxi", "Taxi")}</div>
        </div>
        {tab === "net" ? (
          <div style={S.seg}>
            <div style={{ ...S.segBtn, background: mode === "schem" ? C.ink : "transparent", color: mode === "schem" ? "#16191f" : C.ink }} onClick={() => switchMode("schem")}>{tr("Schematic", "Schematic")}</div>
            <div style={{ ...S.segBtn, background: mode === "geo" ? C.ink : "transparent", color: mode === "geo" ? "#16191f" : C.ink }} onClick={() => switchMode("geo")}>{tr("Geographic", "Geographic")}</div>
          </div>
        ) : null}
        {tab !== "taxi" ? <div style={S.btn} onClick={() => { if (tab === "net") refreshMap(); else refreshRoutes(); }}>{tr("Refresh", "Refresh")}</div> : null}
        {tab === "net" ? (
          <div style={{ ...S.btn, background: saveOpen ? C.hover : "transparent", opacity: map.M ? 1 : 0.4 }}
            onClick={() => map.M && setSaveOpen(!saveOpen)}>{saving ? tr("Saving", "Saving…") : tr("SaveImage", "Save image")}</div>
        ) : null}
        <div style={S.close} title={tr("Close", "Close")} onClick={() => setOpen(false)}>×</div>
      </div>
      {saveOpen ? (
        <div style={S.pop} onMouseDown={stop} onMouseUp={stop}>
          <div style={S.popLabel}>{tr("Theme", "Theme")}</div>
          <div style={{ ...S.seg, marginRight: 0 }}>
            {(["light", "dark"] as const).map(v => (
              <div key={v} style={{ ...S.segBtn, flex: 1, textAlign: "center", background: theme === v ? C.ink : "transparent", color: theme === v ? "#16191f" : C.ink }}
                onClick={() => setTheme(v)}>{v === "light" ? tr("Light", "Light") : tr("Dark", "Dark")}</div>
            ))}
          </div>
          <div style={{ ...S.popLabel, marginTop: "12rem" }}>{tr("Format", "Format")}</div>
          <div style={{ ...S.seg, marginRight: 0 }}>
            {(["png", "svg"] as const).map(v => (
              <div key={v} style={{ ...S.segBtn, flex: 1, textAlign: "center", background: format === v ? C.ink : "transparent", color: format === v ? "#16191f" : C.ink }}
                onClick={() => setFormat(v)}>{v.toUpperCase()}</div>
            ))}
          </div>
          <div style={{ fontSize: "12rem", color: C.muted, marginTop: "10rem", lineHeight: 1.4 }}>{tr("SaveHint", "Saves the whole map with its line legend, whatever the zoom.")}</div>
          <div style={S.primary} onClick={doSave}>{tr("Save", "Save")}</div>
        </div>
      ) : null}
      {tab === "routes" ? (
        <div style={S.body}><ErrorBoundary fallback={(m, retry) => <TabError message={m} retry={retry} />}><RoutesTab tr={tr} typeName={typeName} /></ErrorBoundary></div>
      ) : tab === "taxi" ? (
        <div style={S.body}><ErrorBoundary fallback={(m, retry) => <TabError message={m} retry={retry} />}><TaxiTab tr={tr} /></ErrorBoundary></div>
      ) : (
      <div style={S.body}>
        <div style={S.side}>
          <div style={{ ...S.eyebrow, marginBottom: "4rem" }}>{tr("Names", "Names")}</div>
          <div style={{ ...S.seg, marginRight: 0, marginBottom: "10rem" }}>
            {([[0, "NamesAuto", "Auto"], [1, "NamesOn", "On"], [2, "NamesOff", "Off"]] as const).map(([v, k, fb]) => (
              <div key={k} style={{ ...S.segBtn, flex: 1, textAlign: "center", padding: "4rem 6rem", fontSize: "12rem", background: namesMode === v ? C.ink : "transparent", color: namesMode === v ? "#16191f" : C.ink }}
                onClick={() => setOption("names", v)}>{tr(k, fb)}</div>
            ))}
          </div>
          <div style={{ ...S.eyebrow, marginBottom: "4rem" }}>{tr("Legend", "Lines")}</div>
          {TYPE_ORDER.map((type: LineType) => {
            const group = lines.filter((l: any) => l.type === type);
            if (!group.length) return null;
            const off = hidden.has(type);
            return (
              <div key={type}>
                <div style={S.grpHead} onClick={() => toggleType(type)}>
                  <div style={{ ...S.box, background: off ? "transparent" : C.accent, borderColor: off ? C.muted : C.accent }} />
                  <span style={{ flex: 1 }}>{tr("Type." + type, TYPE_EN[type])}</span>
                  <span style={{ color: C.muted, fontWeight: 400 }}>{String(group.length)}</span>
                </div>
                {group.map((l: any) => (
                  <div key={l.id}
                    style={{ ...S.row, opacity: off ? 0.4 : 1, background: focus === l.id ? C.hover : "transparent" }}
                    onMouseEnter={() => setHover(l.id)} onMouseLeave={() => setHover(null)}
                    onClick={() => { setLock(lock === l.id ? null : l.id); setSel(null); }}>
                    {chip(l)}<span>{l.name}</span>
                  </div>
                ))}
              </div>
            );
          })}
          {diagText ? <div style={{ fontSize: "11rem", color: C.muted, lineHeight: 1.45, marginTop: "12rem", paddingTop: "8rem", borderTop: `1px solid ${C.rule}` }}>{diagText}</div> : null}
        </div>
        <div ref={areaRef} style={S.mapArea} onMouseDown={onDown} onMouseMove={onMove} onMouseUp={onUp}
          onMouseLeave={() => { drag.current = null; setHover(null); }} onWheel={onWheel}>
          {vb && map.M ? (
            <canvas ref={canvasRef} style={{ position: "absolute", left: 0, top: 0, width: "100%", height: "100%", pointerEvents: "none" }} />
          ) : (
            <div style={S.empty}>{failed ? tr("Error", "Transport+ ran into a problem. Details are in the game's UI log.") : tr("Empty", "No public transport lines found. Build a line and press Refresh.")}</div>
          )}
          {card}
          {vb && map.M ? (
            <div style={S.zoom} onMouseDown={stop} onMouseUp={stop}>
              <div style={S.zBtn} onClick={() => zoomCenter(1 / 1.4)}>+</div>
              <div style={S.zBtn} onClick={() => zoomCenter(1.4)}>−</div>
              <div style={S.zBtn} onClick={() => map.FULL && setVb({ ...map.FULL })}><div style={{ width: "14rem", height: "10rem", border: `2rem solid ${C.ink}`, borderRadius: "2rem" }} /></div>
            </div>
          ) : null}
          {notice ? (
            <div style={{ ...S.notice, borderColor: notice.ok ? C.rule : "#d64545" }} onMouseDown={stop} onMouseUp={stop}>
              <span>{notice.text}</span>
              {notice.ok ? <div style={{ ...S.btn, marginLeft: "12rem" }} onClick={() => openFolder()}>{tr("OpenFolder", "Open folder")}</div> : null}
              <div style={{ ...S.close, width: "24rem", height: "24rem", fontSize: "18rem", marginLeft: "6rem" }} onClick={() => setNotice(null)}>×</div>
            </div>
          ) : null}
        </div>
      </div>
      )}
      <div style={S.grip} onMouseDown={e => startFrame(e, "size")} />
    </div>
  );
};

const TabError = ({ message, retry }: { message: string; retry: () => void }) => (
  <div style={{ padding: "20rem 16rem", color: "#9aa3b0", fontSize: "13rem", lineHeight: 1.4 }}>
    <div style={{ color: "#e9ecf1", fontWeight: 600 }}>This tab ran into a problem. The other tabs still work.</div>
    <div style={{ marginTop: "6rem", color: "#e0a43a", fontSize: "12rem", wordBreak: "break-all" }}>{message}</div>
    <div style={{ marginTop: "10rem", display: "inline-block", padding: "4rem 12rem", border: "1px solid #2d343f", borderRadius: "6rem", cursor: "pointer", color: "#e9ecf1" }} onClick={retry}>Try again</div>
  </div>
);
