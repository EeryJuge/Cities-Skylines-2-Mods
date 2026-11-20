import { bindValue, trigger } from "cs2/api";

// C# tarafındaki SignsPlusUISystem ile aynı grup adı. UIPlus gibi başka bir mod da pencereyi
// trigger("signsPlus", "toggle") ya da trigger("signsPlus", "setOpen", true) ile açabilir.
export const GROUP = "signsPlus";

export const isOpen$ = bindValue<boolean>(GROUP, "isOpen", false);
export const setOpen = (open: boolean) => trigger(GROUP, "setOpen", open);

// { city, count, sel: { asset, key, faces: [{ id, w, h, cat, spec }] } | null }
export const state$ = bindValue<string>(GROUP, "state", "{}");
export const setFace = (id: string, cat: number, spec: string) => trigger(GROUP, "setFace", id, cat, spec);
export const pickCenter = () => trigger(GROUP, "pickCenter");
/** Yazı kutusundayken Shift+L kısayolu pencereyi kapatmasın */
export const setTyping = (typing: boolean) => trigger(GROUP, "setTyping", typing);
