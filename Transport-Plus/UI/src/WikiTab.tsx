import { useState, CSSProperties, ReactNode } from "react";

/*
 * Wiki sekmesi: Transport+'ın bütün özellikleri ve nasıl kullanıldıkları. Konular açılır kapanır (dar pencereye sığsın).
 * Metinler burada iki dilde durur; dil, oyunun dilinden gelir (Locale.cs'teki "Lang" anahtarı: tr ya da en).
 * Bir konudan ilgili sekmeye geçilebilir ("Sekmeyi aç").
 */

type L = { en: string; tr: string };
type Block =
  | { p: L }                       // paragraf
  | { list: L[] }                  // madde listesi
  | { ex: string; note?: L }       // örnek (hat adı gibi), açıklamasıyla
  | { tip: L };                    // ipucu kutusu
type Topic = { id: string; title: L; tab?: "net" | "routes" | "taxi"; blocks: Block[] };

const T = (en: string, tr: string): L => ({ en, tr });

const TOPICS: Topic[] = [
  {
    id: "start", title: T("Getting started", "Başlarken"), blocks: [
      { p: T("Open Transport+ with the button at the top left of the screen or with Shift + T (you can change the key in Options → Transport+).",
             "Transport+'ı ekranın sol üstündeki düğmeyle ya da Shift + T ile açarsın (tuşu Seçenekler → Transport+'tan değiştirebilirsin).") },
      { list: [
        T("Map: the whole network as a metro map.", "Harita: bütün ağ, metro haritası gibi."),
        T("Lines: every line on the city itself, with its settings, vehicles and stops.", "Hatlar: her hat şehrin üstünde; ayarları, taşıtları ve duraklarıyla."),
        T("Taxi: taxis from outside, fares, taxi models, taxi stands and intercity buses.", "Taksi: şehir dışı taksiler, tarifeler, taksi modelleri, duraklar ve şehirlerarası otobüsler."),
        T("Wiki: this guide.", "Wiki: bu rehber."),
      ] },
      { p: T("Drag the title bar to move the window and the bottom right corner to resize it. Lines, Taxi and Wiki open as a narrow panel on the left so you can see the city; the map keeps its own size.",
             "Pencereyi başlık çubuğundan sürükleyerek taşırsın, sağ alt köşesinden boyutlandırırsın. Hatlar, Taksi ve Wiki solda dar bir panel olarak açılır, şehir görünür kalır; Harita kendi boyutunu hatırlar.") },
    ]
  },
  {
    id: "map", title: T("Network map", "Ağ haritası"), tab: "net", blocks: [
      { p: T("Metro, train, tram and ferry lines are drawn like a real transit map, each in its own colour with its line badge. The map refreshes by itself when you add, remove or recolour lines.",
             "Metro, tren, tramvay ve vapur hatları gerçek bir ulaşım haritası gibi, her biri kendi rengi ve hat rozetiyle çizilir. Hat ekleyince, silince ya da rengini değiştirince harita kendiliğinden yenilenir.") },
      { list: [
        T("Schematic: 45° lines and tidy spacing, lines keep their real order. Geographic: lines where they really are.", "Şematik: 45° çizgiler, düzenli aralıklar, hatlar gerçek sıralarını korur. Coğrafi: hatlar gerçekte oldukları yerde."),
        T("Line list (left): click a line type or a line to show or hide it. Bus lines are off by default.", "Hat listesi (solda): tür ya da hat adına tıklayıp göster / gizle. Otobüs hatları baştan kapalıdır."),
        T("Names: Auto hides names that would overlap and shows more as you zoom in; On shows all; Off hides them.", "Adlar: Otomatik üst üste binecekleri gizler, yakınlaştıkça daha çoğunu gösterir; Açık hepsini, Kapalı hiçbirini göstermez."),
        T("Click a station to see its lines and transfers, then Show in city to fly there.", "Bir istasyona tıkla: hatlarını ve aktarmalarını gör, Şehirde göster ile kamerayı oraya götür."),
        T("Mouse wheel zooms, dragging moves the map.", "Fare tekerleği yakınlaştırır, sürüklemek haritayı kaydırır."),
        T("Save image: PNG or SVG, light or dark, with the line legend. Files go to the Transport+ folder next to the game's Logs folder.", "Resim kaydet: PNG ya da SVG, açık ya da koyu, hat lejantıyla. Dosyalar oyunun Logs klasörünün yanındaki Transport+ klasörüne gider."),
      ] },
      { tip: T("A line is missing? The note under the line list says how many lines were found and why any were left out. Please include it in bug reports.",
               "Bir hat görünmüyor mu? Hat listesinin altındaki not kaç hat bulunduğunu ve hangilerinin neden atlandığını yazar. Hata bildirirken bunu da ekle.") },
    ]
  },
  {
    id: "lines", title: T("Lines on the city", "Hatlar şehirde"), tab: "routes", blocks: [
      { p: T("When you open the Lines tab, the city turns white like the game's traffic view and every route of the chosen type is drawn on its roads in its own colour. Lines that share a road sit side by side.",
             "Hatlar sekmesini açınca şehir, oyunun trafik görünümü gibi beyazlar ve seçili türün bütün güzergahları yolların üstüne kendi renginde çizilir. Aynı yolu kullanan hatlar yan yana durur.") },
      { list: [
        T("Pick a type (bus, tram, metro, train, ferry) with the chips at the top.", "Üstteki çiplerden türü seç (otobüs, tramvay, metro, tren, vapur)."),
        T("Hover over a line in the list to highlight it in the city; click it to open its details.", "Listede bir hattın üstüne gelince şehirde öne çıkar; tıklayınca ayrıntıları açılır."),
        T("Sort the list by number, waiting passengers or vehicles.", "Listeyi numaraya, bekleyen yolcuya ya da araç sayısına göre sırala."),
        T("Back to normal view turns the white city off; leaving the tab does the same.", "Normal görünüme dön beyaz şehri kapatır; sekmeden çıkınca da kapanır."),
      ] },
      { tip: T("If another mod changes the info views, turn off White city when showing routes in the options: the routes are then drawn over the normal city.",
               "Bilgi görünümlerini değiştiren başka bir mod varsa seçeneklerden \"Rotaları gösterirken şehir beyaz olsun\"u kapat: rotalar normal şehrin üstüne çizilir.") },
    ]
  },
  {
    id: "settings", title: T("Line settings", "Hat ayarları"), tab: "routes", blocks: [
      { p: T("Click a line to open its settings. They use the game's own policies, so the game's line panel shows the same values.",
             "Bir hatta tıklayınca ayarları açılır. Oyunun kendi politikalarını kullanır; oyunun hat paneli de aynı değerleri gösterir.") },
      { list: [
        T("Service: in service or out of service; day and night, day only or night only.", "Hizmet: çalışıyor ya da servis dışı; gece ve gündüz, yalnızca gündüz ya da yalnızca gece."),
        T("Ticket price: drag the bar or use − and +. Higher prices bring more money but fewer passengers.", "Bilet ücreti: çubuğu sürükle ya da − / + kullan. Yüksek ücret daha çok gelir, daha az yolcu demek."),
        T("Vehicles: the number of vehicles sets the time between departures. One round trip and the vehicles on the road are shown below, with how full they are.", "Araçlar: araç sayısı sefer aralığını belirler. Altında bir tam tur süresi, yoldaki araçlar ve doluluk görünür."),
        T("Times are in game time (one game day is 24 hours).", "Süreler oyun saatidir (bir oyun günü 24 saat)."),
      ] },
    ]
  },
  {
    id: "models", title: T("Vehicle models", "Araç modelleri"), tab: "routes", blocks: [
      { p: T("Every line type can use chosen vehicle models: buses, trams, metro trains, trains and ferries. Press Change, tick one or more models, then Done. Mixed lets the game pick.",
             "Her hat türünde araç modeli seçilebilir: otobüs, tramvay, metro, tren ve vapur. Değiştir'e bas, bir ya da birkaç model işaretle, sonra Tamam. Karışık seçilirse oyun seçer.") },
      { list: [
        T("Trains with a locomotive have two lists: carriages and locomotive.", "Lokomotifli trenlerde iki liste var: vagonlar ve lokomotif."),
        T("Vehicles from region packs and Paradox Mods assets are listed too, even ones from other regions.", "Bölge paketlerinin ve Paradox Mods assetlerinin araçları da listede, başka bölgelerinkiler dahil."),
        T("Faded models are locked, or need a depot that supports their energy (for example electric buses).", "Soluk modeller kilitli ya da enerjisine uygun garaj gerektiriyor (örneğin elektrikli otobüsler)."),
        T("New vehicles use the choice; vehicles already on the road keep theirs until they return to the depot.", "Yeni çıkan araçlar seçimi kullanır; yoldakiler garaja dönene kadar kendi modelinde kalır."),
      ] },
    ]
  },
  {
    id: "vehicles", title: T("Vehicles and stops", "Taşıtlar ve duraklar"), tab: "routes", blocks: [
      { p: T("Show vehicles marks every vehicle of the line on the map and lists its model, passengers, state, next stop, speed and distance driven.",
             "Taşıtları göster, hattın bütün taşıtlarını haritada işaretler; modelini, yolcusunu, durumunu, sıradaki durağını, hızını ve kilometresini listeler.") },
      { list: [
        T("Click a vehicle in the list to highlight it; Go there moves the camera, Follow opens the game's vehicle panel and follows it.", "Listede bir taşıta tıkla, öne çıksın; Oraya git kamerayı götürür, Takip et oyunun araç panelini açıp takip eder."),
        T("Stops: Wait is the number of people waiting now (hover for the average wait). On and Off count passengers who boarded and got off while the menu was open in this session.", "Duraklar: Bekl. şu an bekleyen kişi (üstüne gelince ortalama bekleme). Binen ve İnen, bu oturumda menü açıkken o durakta binen ve inen yolcudur."),
        T("The busiest stop is highlighted. Reset starts counting again. Click a stop to fly there.", "En yoğun durak vurgulanır. Sıfırla sayımı baştan başlatır. Durağa tıklayınca kamera gider."),
      ] },
    ]
  },
  {
    id: "names", title: T("Line names and LED signs", "Hat adları ve LED tabelalar"), blocks: [
      { p: T("Buses show their line on LED signs: the code and the end of the line they are heading to on the front and right side, the code on the back. Out of service buses (to or from the depot) say NOT IN SERVICE.",
             "Otobüsler hattını LED tabelada gösterir: önde ve sağ yanda hat kodu ve gittiği son durak, arkada hat kodu. Garaja giden ya da hatta henüz girmemiş otobüs SERVİS DIŞI yazar.") },
      { p: T("Rename the line in the game's line panel. The first word with a digit is the line code, the rest are the two ends:",
             "Hattın adını oyunun hat panelinden değiştir. Rakam içeren ilk kelime hat kodu, kalanı iki uçtur:") },
      { ex: "93B Topkapı Hacıosman", note: T("code 93B, between Topkapı and Hacıosman", "kod 93B, Topkapı ile Hacıosman arası") },
      { ex: "15F Kadıköy - Beykoz Kavağı", note: T("use a dash when an end has more than one word", "bir uç birden fazla kelimeyse tire koy") },
      { ex: "M2 Yenikapı / Hacıosman", note: T("/, > and – work too", "/, > ve – da olur") },
      { list: [
        T("The sign switches to the other end when the bus turns around at the stop farthest from the first stop.", "Otobüs ilk duraktan en uzak durakta dönünce tabela öbür uca geçer."),
        T("Without a name, the line number and the end stops are used.", "Ad verilmezse hat numarası ve uç durakların adları kullanılır."),
        T("The code is also used on the line badges in Transport+.", "Kod, Transport+'taki hat rozetlerinde de kullanılır."),
        T("Turn the signs off in the options (LED destination signs on buses).", "Tabelaları seçeneklerden kapatabilirsin (Otobüslerde LED hat tabelası)."),
      ] },
    ]
  },
  {
    id: "taxi", title: T("Taxis", "Taksiler"), tab: "taxi", blocks: [
      { list: [
        T("Taxis from outside the city: Blocked stops outside connections from sending new taxis. Taxis already in the city finish their trip.", "Şehir dışından gelen taksiler: Kapalı seçilince dış bağlantılar yeni taksi göndermez. Şehirdekiler yolculuğunu bitirir."),
        T("Starting fee: the game's own city policy; passengers see it when they choose a taxi.", "Açılış ücreti: oyunun kendi şehir politikası; yolcular taksi seçerken buna bakar."),
        T("Distance fare: Game default is 30 ¢ per km. Transport+ fares lets you set your own per-km price and a night fare (22:00–06:00), applied when the ride ends.", "Mesafe ücreti: oyunun varsayılanı km başı 30 ¢. Transport+ tarifesi ile kendi km ücretini ve gece tarifesini (22.00–06.00) belirlersin; yolculuk bitince uygulanır."),
        T("Taxi models: ticked models are used for new taxis. At least one stays on.", "Taksi modelleri: işaretli modeller yeni taksilerde kullanılır. En az biri açık kalır."),
        T("Taxi stands: the busiest stands; click one to fly there. A stand calls up to 3 taxis plus one for every 4 people waiting (fixed in the game).", "Taksi durakları: en yoğun duraklar; tıklayınca kamera gider. Durak en fazla 3 + her 4 bekleyen için 1 taksi çağırır (oyunda sabit)."),
      ] },
    ]
  },
  {
    id: "coach", title: T("Intercity buses", "Şehirlerarası otobüsler"), tab: "taxi", blocks: [
      { p: T("Buses that come from outside the city and city lines without a chosen model are picked from the same list. With Separate models, outside buses use only the intercity models you tick, and city lines without a model get the city models.",
             "Şehir dışından gelen otobüsler ve modeli seçilmemiş şehir hatları aynı listeden seçilir. Ayrı modeller seçilince dışarıdan gelenler yalnızca işaretlediğin şehirlerarası modelleri, modeli olmayan şehir hatları da şehir modellerini kullanır.") },
      { tip: T("Models you leave out are hidden from the game's own line panel list too. Nothing is saved into the savegame; turning the option off restores everything.",
               "Dışarıda bıraktığın modeller oyunun kendi hat panelindeki listede de görünmez. Kayda bir şey yazılmaz; seçeneği kapatınca her şey eski haline döner.") },
    ]
  },
  {
    id: "options", title: T("Options", "Seçenekler"), blocks: [
      { p: T("Options → Transport+:", "Seçenekler → Transport+:") },
      { list: [
        T("Merge nearby stops: stops closer than this are one station on the map.", "Yakın durakları birleştir: bu mesafeden yakın duraklar haritada tek istasyon olur."),
        T("Transfer links: stations closer than this are joined by a transfer link.", "Aktarma bağlantısı: bu mesafeden yakın istasyonlar aktarma çizgisiyle bağlanır."),
        T("Show ferry lines, show bus lines on the network map.", "Vapur hatlarını göster, otobüs hatlarını ağ haritasında göster."),
        T("White city when showing routes, LED destination signs on buses.", "Rotaları gösterirken şehir beyaz olsun, otobüslerde LED hat tabelası."),
        T("Key binding for opening the menu.", "Menüyü açan tuş."),
      ] },
    ]
  },
  {
    id: "help", title: T("Problems and feedback", "Sorunlar ve geri bildirim"), blocks: [
      { list: [
        T("If a window or tab runs into a problem it shows the reason and a Try again button; the other tabs keep working.", "Bir pencere ya da sekme hata verirse nedenini ve Yeniden dene düğmesini gösterir; diğer sekmeler çalışmaya devam eder."),
        T("Logs: %USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\Logs — TransportPlus.Mod.log and UI.log.", "Günlükler: %USERPROFILE%\\AppData\\LocalLow\\Colossal Order\\Cities Skylines II\\Logs — TransportPlus.Mod.log ve UI.log."),
        T("Please post bug reports with these two files in the forum thread (Forum link on the mod page) or on GitHub: EeryJuge/Cities-Skylines-2-Mods.", "Hata bildirimlerini bu iki dosyayla forum konusuna (mod sayfasındaki Forum bağlantısı) ya da GitHub'a yaz: EeryJuge/Cities-Skylines-2-Mods."),
        T("Transport+ uses no Harmony patches and can be removed from a save at any time.", "Transport+ Harmony yaması kullanmaz, kayıttan istediğin zaman kaldırılabilir."),
      ] },
    ]
  },
];

const C = { rule: "#2d343f", ink: "#e9ecf1", muted: "#9aa3b0", accent: "#5ea6ec", well: "rgba(255,255,255,0.04)", hover: "#262c36" };
const S: Record<string, CSSProperties> = {
  root: { flex: 1, overflowY: "auto", padding: "6rem 10rem 14rem", minHeight: 0 },
  intro: { fontSize: "12rem", color: C.muted, lineHeight: 1.45, padding: "6rem 4rem 8rem" },
  item: { borderRadius: "8rem", marginTop: "6rem", background: C.well },
  head: { display: "flex", alignItems: "center", padding: "9rem 12rem", cursor: "pointer" },
  num: { width: "22rem", height: "22rem", borderRadius: "11rem", background: "rgba(94,166,236,0.18)", color: C.accent, fontSize: "11rem", fontWeight: 700, display: "flex", alignItems: "center", justifyContent: "center", marginRight: "10rem", flexShrink: 0 },
  title: { flex: 1, fontSize: "14rem", fontWeight: 600 },
  caret: { color: C.muted, fontSize: "14rem", marginLeft: "8rem" },
  body: { padding: "0 14rem 12rem 44rem", fontSize: "13rem", lineHeight: 1.5, color: "#cfd5dd" },
  p: { margin: "0 0 8rem" },
  li: { display: "flex", margin: "0 0 5rem" },
  dot: { width: "5rem", height: "5rem", borderRadius: "3rem", background: C.accent, marginTop: "8rem", marginRight: "9rem", flexShrink: 0 },
  ex: { display: "flex", alignItems: "center", flexWrap: "wrap", margin: "0 0 6rem" },
  exCode: { padding: "3rem 9rem", borderRadius: "5rem", background: "#0b0b0b", color: "#ffaa18", fontWeight: 700, fontSize: "13rem", marginRight: "8rem", letterSpacing: "1rem" },
  tip: { margin: "4rem 0 8rem", padding: "8rem 10rem", borderRadius: "6rem", borderLeft: `3rem solid ${C.accent}`, background: "rgba(94,166,236,0.08)", fontSize: "12rem" },
  go: { display: "inline-block", marginTop: "4rem", padding: "4rem 12rem", border: `1px solid ${C.rule}`, borderRadius: "6rem", fontSize: "12rem", fontWeight: 600, color: C.ink, cursor: "pointer" }
};

export const WikiTab = ({ tr, openTab }: { tr: (k: string, fb: string) => string; openTab: (t: "net" | "routes" | "taxi") => void }) => {
  const lang = tr("Lang", "en") === "tr" ? "tr" : "en";
  const t = (l: L) => l[lang];
  const [open, setOpen] = useState<string>("start");
  const tabName = (x: string) => x === "net" ? tr("TabNetwork", "Map") : x === "routes" ? tr("TabRoutes", "Lines") : tr("TabTaxi", "Taxi");

  const render = (b: Block, i: number): ReactNode => {
    if ("p" in b) return <div key={i} style={S.p}>{t(b.p)}</div>;
    if ("tip" in b) return <div key={i} style={S.tip}>{t(b.tip)}</div>;
    if ("ex" in b) return (
      <div key={i} style={S.ex}>
        <span style={S.exCode}>{b.ex}</span>
        {b.note ? <span style={{ fontSize: "12rem", color: C.muted }}>{t(b.note)}</span> : null}
      </div>
    );
    return (
      <div key={i} style={{ margin: "0 0 8rem" }}>
        {b.list.map((x, k) => <div key={k} style={S.li}><span style={S.dot} /><span style={{ flex: 1 }}>{t(x)}</span></div>)}
      </div>
    );
  };

  return (
    <div style={S.root}>
      <div style={S.intro}>{tr("WikiIntro", "Everything Transport+ can do and how to use it. Click a topic to open it.")}</div>
      {TOPICS.map((topic, i) => {
        const on = open === topic.id;
        return (
          <div key={topic.id} style={{ ...S.item, background: on ? "rgba(255,255,255,0.06)" : C.well }}>
            <div style={S.head} onClick={() => setOpen(on ? "" : topic.id)}>
              <span style={S.num}>{String(i + 1)}</span>
              <span style={S.title}>{t(topic.title)}</span>
              <span style={S.caret}>{on ? "−" : "+"}</span>
            </div>
            {on ? (
              <div style={S.body}>
                {topic.blocks.map(render)}
                {topic.tab ? <div style={S.go} onClick={() => openTab(topic.tab!)}>{`${tr("OpenTab", "Open")}: ${tabName(topic.tab)}`}</div> : null}
              </div>
            ) : null}
          </div>
        );
      })}
    </div>
  );
};
