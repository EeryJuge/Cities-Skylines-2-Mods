import { Button } from "cs2/ui";
import { useOpen } from "./useOpen";
import { toggleOpen } from "./bindings";

// Sol üstteki yüzen buton. Simge, build.mjs'nin mod klasörüne kopyaladığı icon.svg.
// (data: adresli SVG oyunun arayüz motorunda görünmüyor.)
export const ICON = "coui://ui-mods/TransportPlus-icon.svg";

export const TransportButton = () => {
  const [open] = useOpen();
  return (
    <Button
      variant="floating"
      selected={open}
      src={ICON}
      tooltipLabel="Transport+"
      onSelect={toggleOpen}
    />
  );
};
