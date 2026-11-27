import { useEffect, useMemo, useRef, useState, CSSProperties, MouseEvent } from "react";
import { useValue } from "cs2/api";
import { useLocalization } from "cs2/l10n";
import {
  routesData$, refreshRoutes, jumpTo, worldView$, setWorldView, setWorldSelection,
  lineDetail$, selectLine, setTicket, setVehicles, setSchedule, setLineActive, setModel, resetStats,
  showVehicles, focusVehicle, followVehicle
} from "./bindings";
import { ModelPicker } from "./ModelPicker";
import { TYPE_ORDER, chipText } from "./transitMap";
import { parseRoutes } from "./cartoMap";

/*
 * Rota sekmesi (Better Transit Overview tarzı). Harita şehrin kendisinde çizilir (RouteOverlaySystem), bu yüzden panel
 * dar ve solda durur: üstte tür seçimi, altında rota listesi; bir rotaya tıklanınca aynı yerde ayrıntı ve ayarlar açılır.
 */

const C = { rule: "#2d343f", ink: "#e9ecf1", muted: "#9aa3b0", hover: "#262c36", sel: "#313a48", accent: "#5ea6ec", warn: "#e0a43a", bad: "#d64545", good: "#4fb37a", well: "rgba(255,255,255,0.04)" };
const S: Record<string, CSSProperties> = {
  root: { flex: 1, display: "flex", flexDirection: "column", minHeight: 0, minWidth: 0 },
  top: { padding: "8rem 10rem 4rem", borderBottom: `1px solid ${C.rule}` },
  chips: { display: "flex", flexWrap: "wrap", marginTop: "6rem" },
  typeChip: { padding: "4rem 10rem", borderRadius: "13rem", fontSize: "12rem", fontWeight: 600, marginRight: "6rem", marginBottom: "4rem", cursor: "pointer", border: `1px solid ${C.rule}` },
  scroll: { flex: 1, overflowY: "auto", padding: "4rem 8rem 10rem", minHeight: 0 },
  row: { display: "flex", alignItems: "center", padding: "6rem 6rem", borderRadius: "6rem", cursor: "pointer" },
  chip: { minWidth: "28rem", height: "22rem", padding: "0 6rem", borderRadius: "4rem", display: "flex", alignItems: "center", justifyContent: "center", fontWeight: 700, fontSize: "12rem", marginRight: "8rem", flexShrink: 0 },
  name: { flex: 1, fontSize: "13rem", overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis", minWidth: 0 },
  meta: { fontSize: "11rem", color: C.muted, marginLeft: "8rem", whiteSpace: "nowrap", flexShrink: 0 },
  btn: { padding: "4rem 10rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", fontSize: "12rem", fontWeight: 600, color: C.ink, cursor: "pointer", whiteSpace: "nowrap", flexShrink: 0 },
  hint: { padding: "24rem 16rem", color: C.muted, fontSize: "13rem", textAlign: "center", lineHeight: 1.4 },
  head: { display: "flex", alignItems: "center", padding: "8rem 10rem", borderBottom: `1px solid ${C.rule}` },
  back: { width: "28rem", height: "28rem", borderRadius: "6rem", display: "flex", alignItems: "center", justifyContent: "center", fontSize: "18rem", marginRight: "6rem", cursor: "pointer", color: C.muted, flexShrink: 0 },
  title: { flex: 1, fontSize: "15rem", fontWeight: 700, overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis", minWidth: 0 },
  section: { margin: "10rem 0 0", padding: "10rem 12rem", background: C.well, borderRadius: "8rem" },
  label: { fontSize: "11rem", color: C.muted, fontWeight: 600, textTransform: "uppercase", letterSpacing: "1rem", marginBottom: "6rem", display: "flex", alignItems: "center" },
  seg: { display: "flex", border: `1px solid ${C.rule}`, borderRadius: "14rem", padding: "2rem" },
  segBtn: { flex: 1, textAlign: "center", padding: "4rem 6rem", borderRadius: "12rem", fontSize: "12rem", fontWeight: 600, cursor: "pointer" },
  stepBtn: { width: "30rem", height: "28rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", display: "flex", alignItems: "center", justifyContent: "center", fontSize: "17rem", fontWeight: 700, cursor: "pointer", flexShrink: 0 },
  bar: { flex: 1, height: "8rem", margin: "0 10rem", borderRadius: "4rem", background: "rgba(255,255,255,0.10)", position: "relative", cursor: "pointer" },
  big: { fontSize: "18rem", fontWeight: 700, minWidth: "70rem", textAlign: "right" },
  stat: { flex: 1, minWidth: 0 },
  statV: { fontSize: "15rem", fontWeight: 700 },
  statL: { fontSize: "11rem", color: C.muted },
  grid: { display: "flex", flexWrap: "wrap", margin: "0 -3rem" },
  card: { width: "30%", boxSizing: "border-box", margin: "3rem", borderRadius: "6rem", border: `1px solid ${C.rule}`, padding: "4rem", cursor: "pointer", position: "relative" },
  thumb: { width: "100%", height: "52rem", backgroundSize: "contain", backgroundRepeat: "no-repeat", backgroundPosition: "center" },
  cardName: { fontSize: "10rem", overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis", marginTop: "2rem" },
  th: { fontSize: "10rem", color: C.muted, fontWeight: 600, textAlign: "right", width: "44rem", flexShrink: 0 },
  td: { fontSize: "12rem", textAlign: "right", width: "44rem", flexShrink: 0 },
  num: { width: "22rem", height: "18rem", borderRadius: "4rem", display: "flex", alignItems: "center", justifyContent: "center", fontWeight: 700, fontSize: "10rem", marginRight: "8rem", flexShrink: 0 }
};

// Oyun saati: bir oyun günü 262144 karedir, simülasyon saniyede 60 kare işler → 1 simülasyon saniyesi ≈ 0,33 oyun dakikası
const GAME_MIN_PER_SEC = 1440 / (262144 / 60);
const gm = (sec: number) => {
  const m = sec * GAME_MIN_PER_SEC;
  return m >= 90 ? `${(m / 60).toFixed(1)} sa` : `${Math.max(1, Math.round(m))} dk`;
};
const km = (m: number) => m >= 1000 ? (m / 1000).toFixed(1) + " km" : Math.round(m) + " m";

type TR = (k: string, fb: string) => string;

/* Sayı ayarlayıcı: − / + ve tıklanabilir/sürüklenebilir çubuk. Oyundan yeni değer gelene kadar seçilen değer gösterilir. */
export const Stepper = ({ value, min, max, step, fmt, onSet }: { value: number; min: number; max: number; step: number; fmt: (v: number) => string; onSet: (v: number) => void }) => {
  const [pending, setPending] = useState<number | null>(null);
  useEffect(() => { setPending(null); }, [value]);
  const v = pending !== null ? pending : value;
  const barRef = useRef<HTMLDivElement>(null);
  const clamp = (x: number) => Math.min(max, Math.max(min, Math.round(x / step) * step));
  const commit = (x: number) => { const c = clamp(x); if (c !== v) { setPending(c); onSet(c); } };
  const fromMouse = (clientX: number) => {
    const r = barRef.current!.getBoundingClientRect();
    return clamp(min + (max - min) * Math.min(1, Math.max(0, (clientX - r.left) / Math.max(1, r.width))));
  };
  const down = (e: MouseEvent) => {
    e.stopPropagation();
    let last = fromMouse(e.clientX); setPending(last);
    const mm = (ev: any) => { last = fromMouse(ev.clientX); setPending(last); };
    const up = () => { document.removeEventListener("mousemove", mm); document.removeEventListener("mouseup", up); onSet(last); };
    document.addEventListener("mousemove", mm); document.addEventListener("mouseup", up);
  };
  const f = max > min ? (v - min) / (max - min) : 0;
  return (
    <div style={{ display: "flex", alignItems: "center" }}>
      <div style={{ ...S.stepBtn, opacity: v <= min ? 0.35 : 1 }} onClick={() => commit(v - step)}>−</div>
      <div ref={barRef} style={S.bar} onMouseDown={down}>
        <div style={{ position: "absolute", left: 0, top: 0, bottom: 0, width: `${f * 100}%`, borderRadius: "4rem", background: C.accent }} />
        <div style={{ position: "absolute", top: "-5rem", left: `${f * 100}%`, width: "18rem", height: "18rem", marginLeft: "-9rem", borderRadius: "9rem", background: C.ink }} />
      </div>
      <div style={{ ...S.stepBtn, opacity: v >= max ? 0.35 : 1 }} onClick={() => commit(v + step)}>+</div>
      <div style={S.big}>{fmt(v)}</div>
    </div>
  );
};

export const Seg = ({ items, value, onPick }: { items: [number, string][]; value: number; onPick: (v: number) => void }) => (
  <div style={S.seg}>
    {items.map(([v, label]) => (
      <div key={v} style={{ ...S.segBtn, background: v === value ? C.ink : "transparent", color: v === value ? "#16191f" : C.ink }} onClick={() => v !== value && onPick(v)}>{label}</div>
    ))}
  </div>
);

export const RoutesTab = ({ tr, typeName }: { tr: TR; typeName: (t: string) => string }) => {
  const { translate } = useLocalization();
  const raw = useValue(routesData$);
  const model = useMemo(() => {
    let d: any = null;
    try { d = JSON.parse(raw || "{}"); } catch (e) { d = null; }
    return parseRoutes(d);
  }, [raw]);
  const failed = useMemo(() => (raw || "").indexOf('"error"') >= 0, [raw]);
  useEffect(() => { if (!raw || raw === "{}") refreshRoutes(); }, []);

  const types = useMemo(() => TYPE_ORDER.filter(t => model && model.lines.some((l: any) => l.type === t)) as string[], [model]);
  const [type, setType] = useState<string>("bus");
  const curType = types.indexOf(type) >= 0 ? type : (types.indexOf("bus") >= 0 ? "bus" : types[0] || "bus");
  const lines: any[] = useMemo(() => model ? model.lines.filter((l: any) => l.type === curType) : [], [model, curType]);

  const [selId, setSelId] = useState<string | null>(null);
  const [hover, setHover] = useState<string | null>(null);
  const sel = useMemo(() => lines.find(l => l.id === selId) || null, [lines, selId]);
  const world = useValue(worldView$);
  // Şehirdeki çizim aynı türü gösterir; üstüne gelinen ya da seçilen rota öne çıkar
  const focus = sel ? sel.id : hover || "";
  useEffect(() => { setWorldSelection(curType, focus); }, [curType, focus]);
  useEffect(() => { selectLine(sel ? sel.id : ""); }, [sel ? sel.id : ""]);
  useEffect(() => () => selectLine(""), []);

  const detailRaw = useValue(lineDetail$);
  const d = useMemo(() => { try { const x = JSON.parse(detailRaw || "{}"); return x && sel && x.id === sel.id ? x : null; } catch (e) { return null; } }, [detailRaw, sel ? sel.id : ""]);

  // Liste: kısa özet (araç, durak, bekleyen yolcu); sıralama
  // Taşıtlar: şehirde göster / panelde seçili taşıt
  const [showVeh, setShowVeh] = useState(false);
  const [focusKey, setFocusKey] = useState("");
  const toggleShowVeh = () => { const n = !showVeh; setShowVeh(n); showVehicles(n); if (!n) { setFocusKey(""); focusVehicle(""); } };
  const pickVehicle = (k: string) => { setFocusKey(k); focusVehicle(k); };
  useEffect(() => { setFocusKey(""); }, [selId]);
  useEffect(() => () => { showVehicles(false); focusVehicle(""); }, []);
  const [sort, setSort] = useState<"num" | "wait" | "veh">("num");
  const sorted = useMemo(() => {
    const w = (l: any) => l.stops.reduce((a: number, s: any) => a + (+s.w || 0), 0);
    const arr = lines.map(l => ({ l, wait: w(l) }));
    if (sort === "wait") arr.sort((a, b) => b.wait - a.wait);
    else if (sort === "veh") arr.sort((a, b) => b.l.veh - a.l.veh);
    return arr;
  }, [lines, sort]);

  const assetName = (id: string) => { const n = translate("Assets.NAME[" + id + "]", id); return n && n !== "Assets.NAME[" + id + "]" ? n : id; };

  /* ------------------------------------------------ liste görünümü */
  if (!sel) return (
    <div style={S.root}>
      <div style={S.top}>
        <div style={{ display: "flex", alignItems: "center" }}>
          <div style={{ ...S.btn, background: world ? C.accent : "transparent", color: world ? "#0d0f13" : C.ink, borderColor: world ? C.accent : C.rule }}
            onClick={() => setWorldView(!world)}>{world ? tr("WorldOff", "Back to normal view") : tr("WorldOn", "Show in city")}</div>
          <span style={{ flex: 1 }} />
          <Seg items={[[0, tr("SortNum", "No.")], [1, tr("SortWait", "Waiting")], [2, tr("SortVeh", "Vehicles")]]}
            value={sort === "num" ? 0 : sort === "wait" ? 1 : 2} onPick={v => setSort(v === 0 ? "num" : v === 1 ? "wait" : "veh")} />
        </div>
        <div style={S.chips}>
          {types.map(t => {
            const on = t === curType, n = model.lines.filter((l: any) => l.type === t).length;
            return <div key={t} style={{ ...S.typeChip, background: on ? C.ink : "transparent", color: on ? "#16191f" : C.ink }}
              onClick={() => { setType(t); setSelId(null); }}>{`${typeName(t)} ${n}`}</div>;
          })}
        </div>
      </div>
      <div style={S.scroll}>
        {!model ? <div style={S.hint}>{failed ? tr("Error", "Transport+ ran into a problem.") : tr("Loading", "Reading the city…")}</div> : null}
        {model && !lines.length ? <div style={S.hint}>{tr("NoRoutes", "No routes of this type yet. Build a line and press Refresh.")}</div> : null}
        {sorted.map(({ l, wait }) => (
          <div key={l.id} style={{ ...S.row, opacity: l.active ? 1 : 0.5, background: hover === l.id ? C.hover : "transparent" }}
            onMouseEnter={() => setHover(l.id)} onMouseLeave={() => setHover(null)} onClick={() => { setHover(null); setSelId(l.id); }}>
            <div style={{ ...S.chip, background: l.color, color: chipText(l.color) }}>{l.code || "•"}</div>
            <div style={{ flex: 1, minWidth: 0 }}>
              <div style={{ ...S.name, display: "block" }}>{l.name}</div>
              <div style={{ fontSize: "11rem", color: C.muted }}>
                {`${l.veh} ${tr("Vehicles", "vehicles")} · ${l.stops.length} ${tr("Stops", "stops")} · ${km(l.len)}`
                  + (!l.active ? ` · ${tr("Inactive", "inactive")}` : l.sched === 1 ? ` · ${tr("DayOnly", "day only")}` : l.sched === 2 ? ` · ${tr("NightOnly", "night only")}` : "")}
              </div>
            </div>
            <span style={{ ...S.meta, color: wait > 40 ? C.warn : C.muted }}>{`${wait} ${tr("Waiting", "waiting")}`}</span>
          </div>
        ))}
      </div>
    </div>
  );

  /* ------------------------------------------------ ayrıntı görünümü */
  const stops: any[] = d && d.stops ? d.stops : sel.stops.map((s: any) => ({ n: s.n, w: s.w, wt: 0, on: 0, off: 0 }));
  const maxFlow = Math.max(1, ...stops.map((s: any) => Math.max(s.on, s.off, s.w)));
  const busiest = stops.reduce((b: number, s: any, i: number) => (s.on + s.off > (b >= 0 ? stops[b].on + stops[b].off : 0) ? i : b), -1);
  const v = d ? d.veh : null;
  const load = v && v.capacity > 0 ? v.onBoard / v.capacity : 0;
  const unit = d && d.ticket ? "¢" : "";
  const vehs: any[] = d && d.vehicles ? d.vehicles : [];
  const stateText = (st: string) => {
    const base = st.split("+")[0];
    const t = base === "boarding" ? tr("StBoarding", "at a stop") : base === "depot" ? tr("StDepot", "returning to depot") : base === "maint" ? tr("StMaint", "needs maintenance")
      : base === "fuel" ? tr("StFuel", "refuelling") : base === "toLine" ? tr("StToLine", "heading to the line") : tr("StRoad", "on the way");
    return st.indexOf("+full") >= 0 ? `${t} · ${tr("StFull", "full")}` : t;
  };

  return (
    <div style={S.root}>
      <div style={S.head}>
        <div style={S.back} onClick={() => setSelId(null)}>‹</div>
        <div style={{ ...S.chip, background: sel.color, color: chipText(sel.color) }}>{sel.code || "•"}</div>
        <div style={S.title}>{sel.name}</div>
        {sel.stops.length ? <div style={S.btn} onClick={() => { const s = sel.stops[0]; jumpTo(s.x, s.y, s.z); }}>{tr("ShowInCity", "Show in city")}</div> : null}
      </div>
      <div style={S.scroll}>
        {!d ? <div style={S.hint}>{tr("Loading", "Reading the city…")}</div> : null}

        {d ? (
          <div style={S.section}>
            <div style={S.label}>{tr("Service", "Service")}</div>
            {d.hasActive ? <Seg items={[[1, tr("InService", "In service")], [0, tr("OutOfService", "Out of service")]]} value={d.active ? 1 : 0} onPick={x => setLineActive(x === 1)} /> : null}
            {d.hasSched ? (
              <div style={{ marginTop: "8rem" }}>
                <Seg items={[[0, tr("DayNight", "Day & night")], [1, tr("Day", "Day")], [2, tr("Night", "Night")]]} value={d.sched} onPick={setSchedule} />
              </div>
            ) : null}
          </div>
        ) : null}

        {d && d.ticket ? (
          <div style={S.section}>
            <div style={S.label}>{tr("Ticket", "Ticket price")}</div>
            <Stepper value={Math.round(d.ticket.v)} min={Math.round(d.ticket.min)} max={Math.round(d.ticket.max)} step={Math.max(1, Math.round(d.ticket.step))}
              fmt={x => x <= 0 ? tr("Free", "Free") : `${x} ${unit}`} onSet={setTicket} />
            <div style={{ fontSize: "11rem", color: C.muted, marginTop: "6rem" }}>{tr("TicketHint", "Higher prices bring in more money but fewer passengers.")}</div>
          </div>
        ) : null}

        {v ? (
          <div style={S.section}>
            <div style={S.label}>{tr("VehiclesTitle", "Vehicles and frequency")}</div>
            {v.max ? <Stepper value={v.count} min={v.min || 1} max={v.max} step={1} fmt={x => String(x)} onSet={setVehicles} /> : <div style={S.statV}>{String(v.count)}</div>}
            <div style={{ display: "flex", marginTop: "10rem" }}>
              <div style={S.stat}><div style={S.statV}>{gm(v.interval)}</div><div style={S.statL}>{tr("Headway", "between departures")}</div></div>
              <div style={S.stat}><div style={S.statV}>{gm(v.duration)}</div><div style={S.statL}>{tr("RoundTrip", "one round trip")}</div></div>
              <div style={S.stat}><div style={S.statV}>{`${v.running} / ${v.count}`}</div><div style={S.statL}>{tr("Running", "on the road")}</div></div>
            </div>
            <div style={{ marginTop: "10rem" }}>
              <div style={{ display: "flex", fontSize: "11rem", color: C.muted, marginBottom: "4rem" }}>
                <span style={{ flex: 1 }}>{tr("OnBoard", "Passengers on board")}</span>
                <span>{`${v.onBoard} / ${v.capacity} · ${Math.round(load * 100)}%`}</span>
              </div>
              <div style={{ height: "6rem", borderRadius: "3rem", background: "rgba(255,255,255,0.10)" }}>
                <div style={{ height: "6rem", borderRadius: "3rem", width: `${Math.min(100, load * 100)}%`, background: load > 0.9 ? C.bad : load > 0.6 ? C.warn : C.good }} />
              </div>
            </div>
          </div>
        ) : null}

        {d ? (
          <div style={S.section}>
            <div style={S.label}>
              <span style={{ flex: 1 }}>{`${tr("VehList", "Vehicles on this line")} · ${vehs.length}`}</span>
              <div style={{ ...S.btn, padding: "2rem 8rem", fontSize: "11rem", textTransform: "none", letterSpacing: 0, background: showVeh ? C.accent : "transparent", color: showVeh ? "#0d0f13" : C.ink, borderColor: showVeh ? C.accent : C.rule }}
                onClick={() => toggleShowVeh()}>{showVeh ? tr("HideVehicles", "Hide vehicles") : tr("ShowVehicles", "Show vehicles")}</div>
            </div>
            {showVeh ? (
              vehs.length ? vehs.map((x: any, i: number) => {
                const on = x.e === focusKey, ld = x.cap > 0 ? x.pax / x.cap : 0;
                return (
                  <div key={x.e} style={{ ...S.row, display: "block", padding: "5rem 6rem", background: on ? C.sel : "transparent" }} onClick={() => pickVehicle(on ? "" : x.e)}>
                    <div style={{ display: "flex", alignItems: "center" }}>
                      <div style={{ ...S.num, background: sel.color, color: chipText(sel.color) }}>{String(i + 1)}</div>
                      <span style={{ ...S.name, fontSize: "12rem", fontWeight: 600 }}>{assetName(x.id) + (x.cars > 1 ? ` ×${x.cars}` : "")}</span>
                      <span style={{ ...S.meta, color: ld > 0.9 ? C.bad : ld > 0.6 ? C.warn : C.muted }}>{`${x.pax}/${x.cap}`}</span>
                    </div>
                    <div style={{ fontSize: "11rem", color: C.muted, marginLeft: "30rem", marginTop: "2rem" }}>
                      {stateText(x.st) + (x.next ? ` · ${tr("NextStop", "next")}: ${x.next}` : "") + ` · ${Math.round(x.kmh)} km/h · ${x.km.toFixed(1)} km`}
                    </div>
                    {on ? (
                      <div style={{ display: "flex", marginLeft: "30rem", marginTop: "5rem" }}>
                        <div style={{ ...S.btn, padding: "2rem 8rem", fontSize: "11rem" }} onClick={(e: any) => { e.stopPropagation(); jumpTo(x.x, x.y, x.z); }}>{tr("GoThere", "Go there")}</div>
                        <div style={{ ...S.btn, padding: "2rem 8rem", fontSize: "11rem", marginLeft: "6rem" }} onClick={(e: any) => { e.stopPropagation(); followVehicle(x.e); }}>{tr("Follow", "Follow + details")}</div>
                      </div>
                    ) : null}
                  </div>
                );
              }) : <div style={{ fontSize: "12rem", color: C.muted }}>{tr("NoVehicles", "No vehicles on this line right now.")}</div>
            ) : <div style={{ fontSize: "11rem", color: C.muted, lineHeight: 1.35 }}>{tr("VehHint", "Shows each vehicle on the city map and lists its model, passengers, next stop and speed.")}</div>}
          </div>
        ) : null}

        {d && d.model ? (
          <ModelPicker tr={tr} title={d.model.loco ? tr("ModelCars", "Carriages / train sets") : tr("Model", "Vehicle model")}
            list={d.model.list || []} isOn={(m: any) => (d.model.sel || []).indexOf(m.e) >= 0}
            onToggle={(m: any) => setModel(m.e, 0)} onAny={() => setModel("", 0)}
            unit={(m: any) => `${m.cap * Math.max(1, m.units)} ${tr("Seats", "pax")}`}
            hint={d.model.loco ? undefined : tr("ModelHint", "Includes vehicles from region packs and Paradox Mods assets. New vehicles use the choice; buses already on the road keep theirs until they return to the depot.")} />
        ) : null}
        {d && d.model && d.model.loco ? (
          <ModelPicker tr={tr} title={tr("ModelLoco", "Locomotive")} list={d.model.list2 || []}
            isOn={(m: any) => (d.model.sel2 || []).indexOf(m.e) >= 0} onToggle={(m: any) => setModel(m.e, 1)} onAny={() => setModel("", 1)}
            unit={() => tr("LocoUnit", "engine")}
            hint={tr("ModelHint", "Includes vehicles from region packs and Paradox Mods assets. New vehicles use the choice; buses already on the road keep theirs until they return to the depot.")} />
        ) : null}

        <div style={S.section}>
          <div style={S.label}>
            <span style={{ flex: 1 }}>{tr("StopStats", "Stops")}</span>
            {d ? <span style={{ textTransform: "none", letterSpacing: 0, fontWeight: 400, marginRight: "8rem" }}>{`${tr("Since", "counted for")} ${Math.round(d.since)} ${tr("Minutes", "min")}`}</span> : null}
            {d ? <div style={{ ...S.btn, padding: "2rem 8rem", fontSize: "11rem", textTransform: "none", letterSpacing: 0 }} onClick={() => resetStats()}>{tr("Reset", "Reset")}</div> : null}
          </div>
          <div style={{ display: "flex", alignItems: "center", padding: "0 6rem 4rem" }}>
            <span style={{ flex: 1 }} />
            <span style={S.th}>{tr("ColWait", "Wait")}</span>
            <span style={S.th}>{tr("ColOn", "On")}</span>
            <span style={S.th}>{tr("ColOff", "Off")}</span>
          </div>
          {stops.map((s: any, i: number) => (
            <div key={i} style={{ ...S.row, padding: "4rem 6rem", background: i === busiest ? "rgba(94,166,236,0.12)" : "transparent" }}
              onClick={() => { const p = sel.stops[i]; if (p) jumpTo(p.x, p.y, p.z); }}>
              <div style={{ ...S.num, background: sel.color, color: chipText(sel.color) }}>{String(i + 1)}</div>
              <div style={{ flex: 1, minWidth: 0 }}>
                <div style={{ ...S.name, display: "block", fontSize: "12rem" }}>{s.n}</div>
                <div style={{ display: "flex", height: "3rem", marginTop: "3rem" }}>
                  <div style={{ width: `${(s.on / maxFlow) * 50}%`, background: C.good }} />
                  <div style={{ width: `${(s.off / maxFlow) * 50}%`, background: C.warn, marginLeft: "2rem" }} />
                </div>
              </div>
              <span style={{ ...S.td, color: s.w > 30 ? C.warn : C.ink }} title={s.wt ? gm(s.wt) : ""}>{String(s.w)}</span>
              <span style={S.td}>{String(s.on)}</span>
              <span style={S.td}>{String(s.off)}</span>
            </div>
          ))}
          <div style={{ fontSize: "11rem", color: C.muted, marginTop: "6rem", lineHeight: 1.35 }}>
            {tr("StopHint", "Wait = people waiting now. On / Off = passengers who boarded / got off here while Transport+ was counting (this session). The busiest stop is highlighted.")}
          </div>
        </div>
      </div>
    </div>
  );
};
