import { bindValue, trigger } from "cs2/api";

// C# tarafındaki TransitMapUISystem ile aynı grup adı
export const GROUP = "transportPlus";

// Menünün açık/kapalı durumu oyun tarafında tutulur; buton, X ve kısayol tuşu aynı durumu değiştirir.
export const isOpen$ = bindValue<boolean>(GROUP, "isOpen", false);
export const setOpen = (open: boolean) => trigger(GROUP, "setOpen", open);
export const toggleOpen = () => trigger(GROUP, "toggle");

export const mapData$ = bindValue<string>(GROUP, "mapData", "{}");
export const refreshMap = () => trigger(GROUP, "refresh");
export const jumpTo = (x: number, y: number, z: number) => trigger(GROUP, "jumpTo", x, y, z);

// Resmi kaydet: oyun arayüzü dosya indiremediği için dosyayı C# tarafı yazar
export const lastSaved$ = bindValue<string>(GROUP, "lastSaved", "");
// "svg2png": SVG gönderilir, C# tarafı PNG'ye çevirir (oyunun arayüz motoru canvas'tan PNG üretemiyor)
export const saveImage = (format: "svg2png" | "svg", data: string) => trigger(GROUP, "saveImage", format, data);
export const openFolder = () => trigger(GROUP, "openFolder");

// Rota sekmesi: hatların durak sırası, güzergâhı ve arkasındaki yol ağı. Sekme açılınca istenir.
export const routesData$ = bindValue<string>(GROUP, "routesData", "{}");
export const refreshRoutes = () => trigger(GROUP, "refreshRoutes");

// Pencereden değişen ve oyun ayarlarına kaydedilen seçenekler: { buses: boolean, names: 0 | 1 | 2 }
export const options$ = bindValue<string>(GROUP, "options", "{}");
export const setOption = (key: "names" | "buses", value: number) => trigger(GROUP, "setOption", key, value);

// Şehirdeki rota görünümü: oyunun bilgi görünümü gibi beyaz dünya, rotalar yolların üstünde kendi renkleriyle
export const worldView$ = bindValue<boolean>(GROUP, "worldView", false);
export const setWorldView = (on: boolean) => trigger(GROUP, "setWorldView", on);
export const setWorldSelection = (type: string, lineId: string) => trigger(GROUP, "setWorldSelection", type, lineId);

// Hat yönetimi (LineManagerSystem): seçili hattın ayarları ve durak istatistikleri. Ayarlar oyunun kendi politikalarıyla
// uygulanır, böylece oyunun hat paneli de aynı değerleri gösterir.
export const LINES = "transportPlusLines";
export const lineDetail$ = bindValue<string>(LINES, "detail", "{}");
export const selectLine = (id: string) => trigger(LINES, "select", id);
export const setTicket = (v: number) => trigger(LINES, "setTicket", Math.round(v));
export const setVehicles = (v: number) => trigger(LINES, "setVehicles", Math.round(v));
export const setSchedule = (v: number) => trigger(LINES, "setSchedule", v);
export const setLineActive = (on: boolean) => trigger(LINES, "setActive", on);
export const setModel = (key: string, slot: number) => trigger(LINES, "setModel", key, slot);
// Taşıtlar: şehirde işaretle, panelde seçileni öne çıkar, oyunun araç panelini açıp takip et
export const showVehicles = (on: boolean) => trigger(LINES, "showVehicles", on);
export const focusVehicle = (key: string) => trigger(LINES, "focusVehicle", key);
export const followVehicle = (key: string) => trigger(LINES, "followVehicle", key);
export const resetStats = () => trigger(LINES, "resetStats");

// Taksi ve şehirlerarası otobüs (TaxiCoachSystem)
export const TAXI = "transportPlusTaxi";
export const taxiState$ = bindValue<string>(TAXI, "state", "{}");
export const taxiOpen = (on: boolean) => trigger(TAXI, "setOpen", on);
export const setOutsideTaxis = (on: boolean) => trigger(TAXI, "setOutside", on);
export const setOpeningFee = (v: number) => trigger(TAXI, "setOpening", Math.round(v));
export const setTaxiTariff = (on: boolean) => trigger(TAXI, "setTariff", on);
export const setTaxiPerKm = (v: number) => trigger(TAXI, "setPerKm", Math.round(v));
export const setTaxiNight = (v: number) => trigger(TAXI, "setNight", Math.round(v));
export const toggleTaxiModel = (key: string) => trigger(TAXI, "toggleTaxi", key);
export const setCoachMode = (on: boolean) => trigger(TAXI, "setCoachMode", on);
export const toggleCoachModel = (key: string) => trigger(TAXI, "toggleCoach", key);
export const toggleCityModel = (key: string) => trigger(TAXI, "toggleCity", key);
export const taxiJumpTo = (x: number, y: number, z: number) => trigger(TAXI, "jumpTo", x, y, z);
