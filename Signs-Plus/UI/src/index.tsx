import { ModRegistrar } from "cs2/modding";
import { SignsWindow } from "./SignsWindow";

// Sol üstte buton yok: pencere levhaya tıklayınca, Shift+L ile ya da UIPlus'tan açılır.
const register: ModRegistrar = (moduleRegistry) => {
  moduleRegistry.append("Game", SignsWindow);
  try { moduleRegistry.append("Editor", SignsWindow); } catch (e) { console.log("[Signs+] Editor UI root yok: " + e); }
};

export default register;
