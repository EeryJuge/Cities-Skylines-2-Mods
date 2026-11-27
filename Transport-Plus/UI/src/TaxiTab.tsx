import { useEffect, useMemo, CSSProperties } from "react";
import { useValue } from "cs2/api";
import {
  taxiState$, taxiOpen, setOutsideTaxis, setOpeningFee, setTaxiTariff, setTaxiPerKm, setTaxiNight,
  toggleTaxiModel, setCoachMode, toggleCoachModel, toggleCityModel, taxiJumpTo
} from "./bindings";
import { Stepper, Seg } from "./RoutesTab";
import { ModelPicker, TR } from "./ModelPicker";

/*
 * Taksi sekmesi: şehir dışı taksiler, tarife (açılış, km başı, gece), taksi modelleri, duraklar ve
 * şehirlerarası / şehir otobüsü ayrımı. Durum TaxiCoachSystem'den gelir; ayarlar mod ayarlarına kaydedilir
 * (açılış ücreti şehrin kendi politikası olduğu için şehre kaydedilir).
 */

const C = { rule: "#2d343f", ink: "#e9ecf1", muted: "#9aa3b0", sel: "#313a48", accent: "#5ea6ec", warn: "#e0a43a", good: "#4fb37a", well: "rgba(255,255,255,0.04)" };
const S: Record<string, CSSProperties> = {
  root: { flex: 1, display: "flex", flexDirection: "column", minHeight: 0, minWidth: 0 },
  scroll: { flex: 1, overflowY: "auto", padding: "0 8rem 12rem", minHeight: 0 },
  section: { margin: "10rem 0 0", padding: "10rem 12rem", background: C.well, borderRadius: "8rem" },
  label: { fontSize: "11rem", color: C.muted, fontWeight: 600, textTransform: "uppercase", letterSpacing: "1rem", marginBottom: "6rem", display: "flex", alignItems: "center" },
  hint: { fontSize: "11rem", color: C.muted, marginTop: "6rem", lineHeight: 1.35 },
  stat: { flex: 1, minWidth: 0 },
  statV: { fontSize: "15rem", fontWeight: 700 },
  statL: { fontSize: "11rem", color: C.muted },
  row: { display: "flex", alignItems: "center", padding: "4rem 6rem", borderRadius: "6rem", cursor: "pointer", fontSize: "12rem" },
  sub: { fontSize: "11rem", color: C.muted, margin: "10rem 0 4rem" }
};

export const TaxiTab = ({ tr }: { tr: TR }) => {
  const raw = useValue(taxiState$);
  const d = useMemo(() => { try { return JSON.parse(raw || "{}") || {}; } catch (e) { return {}; } }, [raw]);
  useEffect(() => { taxiOpen(true); return () => taxiOpen(false); }, []);
  const ready = d.taxis !== undefined;
  const taxiOff: string[] = d.taxiOff || [], coach: string[] = d.coach || [], city: string[] = d.city || [];
  const t = d.taxis || {};
  const perKm = d.perKm || 0, opening = d.opening ? d.opening.v : 0, night = (d.nightPct || 100) / 100;
  const example = (km: number, n: boolean) => Math.round((opening + km * (d.tariff ? perKm : 30)) * (n && d.tariff ? night : 1));
  const buses: any[] = d.busList || [];
  const cityList = buses.filter((m: any) => coach.indexOf(m.id) < 0);

  if (!ready) return <div style={S.root}><div style={{ ...S.hint, padding: "24rem", textAlign: "center" }}>{tr("Loading", "Reading the city…")}</div></div>;

  return (
    <div style={S.root}>
      <div style={S.scroll}>
        {/* Genel durum */}
        <div style={S.section}>
          <div style={S.label}>{tr("TaxisNow", "Taxis in the city")}</div>
          <div style={{ display: "flex" }}>
            <div style={S.stat}><div style={S.statV}>{String(t.total)}</div><div style={S.statL}>{tr("TxTotal", "driving")}</div></div>
            <div style={S.stat}><div style={S.statV}>{String(t.busy)}</div><div style={S.statL}>{tr("TxBusy", "with passengers")}</div></div>
            <div style={S.stat}><div style={S.statV}>{String((t.free || 0) + (t.dispatched || 0))}</div><div style={S.statL}>{tr("TxFree", "free / on call")}</div></div>
            <div style={S.stat}><div style={S.statV}>{String(t.outside)}</div><div style={S.statL}>{tr("TxOutside", "from outside")}</div></div>
          </div>
        </div>

        {/* Şehir dışı taksiler */}
        <div style={S.section}>
          <div style={S.label}>{tr("OutsideTaxis", "Taxis from outside the city")}</div>
          <Seg items={[[1, tr("Allowed", "Allowed")], [0, tr("Blocked", "Blocked")]]} value={d.outside ? 1 : 0} onPick={v => setOutsideTaxis(v === 1)} />
          <div style={S.hint}>{tr("OutsideHint", "When blocked, outside connections stop sending new taxis. Taxis already in the city finish their trip.")}</div>
        </div>

        {/* Tarife */}
        <div style={S.section}>
          <div style={S.label}>{tr("Tariff", "Fares")}</div>
          {d.opening ? (
            <div>
              <div style={S.sub}>{tr("Opening", "Starting fee (city policy)")}</div>
              <Stepper value={Math.round(d.opening.v)} min={Math.round(d.opening.min)} max={Math.round(d.opening.max)} step={Math.max(1, Math.round(d.opening.step))}
                fmt={x => `${x} ¢`} onSet={setOpeningFee} />
            </div>
          ) : null}
          <div style={S.sub}>{tr("TariffMode", "Distance fare")}</div>
          <Seg items={[[0, tr("TariffGame", "Game default (30 ¢/km)")], [1, tr("TariffOwn", "Transport+ fares")]]} value={d.tariff ? 1 : 0} onPick={v => setTaxiTariff(v === 1)} />
          {d.tariff ? (
            <div>
              <div style={S.sub}>{tr("PerKm", "Per km")}</div>
              <Stepper value={perKm} min={0} max={200} step={1} fmt={x => `${x} ¢/km`} onSet={setTaxiPerKm} />
              <div style={S.sub}>{tr("NightTariff", "Night fare, 22:00–06:00")}</div>
              <Stepper value={d.nightPct} min={50} max={300} step={5} fmt={x => `×${(x / 100).toFixed(2).replace(/\.?0+$/, "")}`} onSet={setTaxiNight} />
            </div>
          ) : null}
          <div style={{ display: "flex", marginTop: "10rem" }}>
            <div style={S.stat}><div style={S.statV}>{`${example(5, false)} ¢`}</div><div style={S.statL}>{tr("Example5Day", "5 km by day")}</div></div>
            <div style={S.stat}><div style={S.statV}>{`${example(5, true)} ¢`}</div><div style={S.statL}>{tr("Example5Night", "5 km at night")}</div></div>
            <div style={S.stat}><div style={{ ...S.statV, color: d.night ? C.warn : C.ink }}>{d.night ? tr("NowNight", "Night") : tr("NowDay", "Day")}</div><div style={S.statL}>{tr("NowLabel", "now")}</div></div>
          </div>
          <div style={S.hint}>{tr("TariffHint", "The starting fee is the game's own city policy; passengers see it when choosing a taxi. The per-km and night fare are applied when the ride ends.")}</div>
        </div>

        {/* Taksi modelleri */}
        <ModelPicker tr={tr} title={tr("TaxiModels", "Taxi models")} list={d.taxiList || []}
          isOn={(m: any) => taxiOff.indexOf(m.id) < 0} onToggle={(m: any) => toggleTaxiModel(m.e)} allowLockedPick
          anyLabel={tr("AllTaxis", "All taxi models")}
          hint={tr("TaxiModelHint", "Ticked models are used for new taxis, including ones from region packs and Paradox Mods. At least one stays on.")} />

        {/* Duraklar */}
        <div style={S.section}>
          <div style={S.label}>{`${tr("Stands", "Taxi stands")} · ${d.stands ? d.stands.count : 0}`}</div>
          <div style={{ fontSize: "12rem", marginBottom: "6rem" }}>{`${d.stands ? d.stands.waiting : 0} ${tr("WaitingPeople", "people waiting at stands")}`}</div>
          {(d.stands ? d.stands.top : []).filter((s: any) => s.w > 0).map((s: any, i: number) => (
            <div key={i} style={S.row} onClick={() => taxiJumpTo(s.x, s.y, s.z)}>
              <span style={{ flex: 1, overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis" }}>{s.n || tr("Stand", "Taxi stand")}</span>
              <span style={{ color: s.w > 10 ? C.warn : C.muted, fontSize: "11rem" }}>{`${s.w} ${tr("Waiting", "waiting")}`}</span>
            </div>
          ))}
          <div style={S.hint}>{tr("StandHint", "A stand calls taxis by itself: up to 3 plus one for every 4 people waiting. Click a stand to fly there.")}</div>
        </div>

        {/* Şehirlerarası otobüsler */}
        <div style={S.section}>
          <div style={S.label}>{tr("Coaches", "Intercity buses")}</div>
          <div style={{ fontSize: "12rem", marginBottom: "6rem" }}>{`${d.coaches || 0} ${tr("CoachesNow", "buses from outside in the city now")}`}</div>
          <Seg items={[[0, tr("CoachOff", "Same pool as city buses")], [1, tr("CoachOn", "Separate models")]]} value={d.coachMode ? 1 : 0} onPick={v => setCoachMode(v === 1)} />
          <div style={S.hint}>{tr("CoachHint", "Buses coming from outside and city lines without a chosen model are picked from the same list. With separate models, outside buses use only the intercity models below, and city lines without a model get the city models.")}</div>
        </div>
        {d.coachMode ? (
          <div>
            <ModelPicker tr={tr} title={tr("CoachModels", "Intercity (outside) bus models")} list={buses}
              isOn={(m: any) => coach.indexOf(m.id) >= 0} onToggle={(m: any) => toggleCoachModel(m.e)} allowLockedPick
              anyLabel={tr("PickCoach", "Pick at least one model")} />
            <ModelPicker tr={tr} title={tr("CityModels", "City bus models")} list={cityList}
              isOn={(m: any) => city.indexOf(m.id) >= 0} onToggle={(m: any) => toggleCityModel(m.e)}
              anyLabel={tr("AllCity", "All non-intercity models")}
              hint={tr("CityModelHint", "Written to city bus lines that have no model of their own. You can still pick models per line in the Lines tab.")} />
          </div>
        ) : null}
      </div>
    </div>
  );
};
