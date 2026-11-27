import { useValue } from "cs2/api";
import { isOpen$, setOpen } from "./bindings";

export function useOpen(): [boolean, (v: boolean) => void] {
  const open = useValue(isOpen$);
  return [open, setOpen];
}
