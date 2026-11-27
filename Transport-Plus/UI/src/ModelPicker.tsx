import { useState, CSSProperties } from "react";
import { useLocalization } from "cs2/l10n";

/*
 * Araç modeli seçici: kapalıyken seçili modeller (küçük resim + ad), "Değiştir" ile bütün liste ızgara olarak açılır.
 * Hat panelinde (birincil ve lokomotif) ve Taksi sekmesinde (taksi, şehirlerarası, şehir otobüsü) aynı bileşen kullanılır.
 */

const C = { rule: "#2d343f", ink: "#e9ecf1", muted: "#9aa3b0", sel: "#313a48", accent: "#5ea6ec", well: "rgba(255,255,255,0.04)" };
const S: Record<string, CSSProperties> = {
  section: { margin: "10rem 0 0", padding: "10rem 12rem", background: C.well, borderRadius: "8rem" },
  label: { fontSize: "11rem", color: C.muted, fontWeight: 600, textTransform: "uppercase", letterSpacing: "1rem", marginBottom: "6rem", display: "flex", alignItems: "center" },
  btn: { padding: "2rem 8rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", fontSize: "11rem", fontWeight: 600, color: C.ink, cursor: "pointer", whiteSpace: "nowrap", flexShrink: 0, textTransform: "none", letterSpacing: 0 },
  grid: { display: "flex", flexWrap: "wrap", margin: "0 -3rem" },
  card: { width: "30%", boxSizing: "border-box", margin: "3rem", borderRadius: "6rem", border: `1px solid ${C.rule}`, padding: "4rem", cursor: "pointer", position: "relative" },
  thumb: { width: "100%", height: "52rem", backgroundSize: "contain", backgroundRepeat: "no-repeat", backgroundPosition: "center" },
  name: { fontSize: "10rem", overflow: "hidden", whiteSpace: "nowrap", textOverflow: "ellipsis", marginTop: "2rem" },
  tick: { position: "absolute", right: "4rem", top: "4rem", width: "16rem", height: "16rem", borderRadius: "8rem", background: C.accent, color: "#0d0f13", fontSize: "11rem", fontWeight: 700, display: "flex", alignItems: "center", justifyContent: "center" },
  hint: { fontSize: "11rem", color: C.muted, marginTop: "6rem", lineHeight: 1.35 }
};

export type TR = (k: string, fb: string) => string;

export function useAssetName() {
  const { translate } = useLocalization();
  return (id: string) => { const k = "Assets.NAME[" + id + "]"; const n = translate(k, id); return n && n !== k ? n : id; };
}

export const ModelPicker = ({ tr, title, list, isOn, onToggle, anyLabel, onAny, hint, unit, allowLockedPick }: {
  tr: TR; title: string; list: any[]; isOn: (m: any) => boolean; onToggle: (m: any) => void;
  anyLabel?: string; onAny?: () => void; hint?: string; unit?: (m: any) => string; allowLockedPick?: boolean;
}) => {
  const [open, setOpen] = useState(false);
  const name = useAssetName();
  const chosen = list.filter(isOn);
  const meta = (m: any) => (unit ? unit(m) : `${m.cap} ${tr("Seats", "pax")}`) + (m.elec ? " · ⚡" : "");
  return (
    <div style={S.section}>
      <div style={S.label}>
        <span style={{ flex: 1 }}>{title}</span>
        <div style={S.btn} onClick={() => setOpen(!open)}>{open ? tr("Done", "Done") : `${tr("Change", "Change")} (${list.length})`}</div>
      </div>
      {!open ? (
        chosen.length ? (
          <div style={S.grid}>
            {chosen.map((m: any) => (
              <div key={m.e} style={{ display: "flex", alignItems: "center", width: "48%", margin: "3rem 1%" }}>
                <div style={{ ...S.thumb, width: "64rem", height: "36rem", flexShrink: 0, backgroundImage: m.thumb ? `url(${m.thumb})` : "none" }} />
                <div style={{ marginLeft: "6rem", minWidth: 0 }}>
                  <div style={{ ...S.name, fontSize: "12rem", fontWeight: 600 }}>{name(m.id)}</div>
                  <div style={{ fontSize: "10rem", color: C.muted }}>{meta(m)}</div>
                </div>
              </div>
            ))}
          </div>
        ) : <div style={{ fontSize: "13rem", fontWeight: 600 }}>{anyLabel || tr("AnyModel", "Mixed (game picks)")}</div>
      ) : (
        <div style={S.grid}>
          {onAny ? (
            <div style={{ ...S.card, borderColor: !chosen.length ? C.accent : C.rule, background: !chosen.length ? C.sel : "transparent" }} onClick={() => chosen.length && onAny()}>
              <div style={{ ...S.thumb, display: "flex", alignItems: "center", justifyContent: "center", fontSize: "12rem", color: C.muted, textAlign: "center" }}>{anyLabel || tr("AnyModel", "Mixed (game picks)")}</div>
              <div style={S.name}>{tr("AnyModelShort", "Any")}</div>
            </div>
          ) : null}
          {list.map((m: any) => {
            const on = isOn(m), off = m.locked || !m.usable;
            return (
              <div key={m.e} title={name(m.id)} style={{ ...S.card, borderColor: on ? C.accent : C.rule, background: on ? C.sel : "transparent", opacity: off ? 0.4 : 1 }}
                onClick={() => (allowLockedPick || !m.locked) && onToggle(m)}>
                <div style={{ ...S.thumb, backgroundImage: m.thumb ? `url(${m.thumb})` : "none" }} />
                {on ? <div style={S.tick}>✓</div> : null}
                <div style={S.name}>{name(m.id)}</div>
                <div style={{ fontSize: "10rem", color: C.muted }}>{meta(m) + (m.locked ? " · " + tr("Locked", "locked") : !m.usable ? " · " + tr("NoDepot", "no depot") : "")}</div>
              </div>
            );
          })}
        </div>
      )}
      {hint ? <div style={S.hint}>{hint}</div> : null}
    </div>
  );
};
