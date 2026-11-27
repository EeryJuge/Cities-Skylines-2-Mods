import { ModRegistrar } from "cs2/modding";
import { TransportButton } from "./TransportButton";
import { MapWindow } from "./MapWindow";
import { ErrorBoundary } from "./ErrorBoundary";

// Buton da hata sınırının içinde: Transport+ bozulursa sol üstteki diğer mod butonları yerinde kalır
const SafeButton = () => <ErrorBoundary><TransportButton /></ErrorBoundary>;

// Her parça ayrı kaydedilir: biri hata verirse diğeri (buton ya da pencere) yine çalışır. Nedeni UI.log'a yazılır.
const register: ModRegistrar = (moduleRegistry) => {
  try { moduleRegistry.append("GameTopLeft", SafeButton); }
  catch (e: any) { console.log("[Transport+] button could not be registered: " + ((e && e.stack) || e)); }
  try { moduleRegistry.append("Game", MapWindow); }
  catch (e: any) { console.log("[Transport+] window could not be registered: " + ((e && e.stack) || e)); }
  console.log("[Transport+] UI module registered");
};

export default register;
