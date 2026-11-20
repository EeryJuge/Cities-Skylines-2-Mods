# Signs+ geliştirme notları (Türkçe) — 0.0.4

Cities: Skylines II için Canva tarzı editörlü trafik levhası modu.

- **0.0.1:** Kendi assetlerimiz oyuna aktarıldı, mod panellere yazı çiziyor (editörde denendi, çalıştı).
- **0.0.2:** Her levhanın kendi yazısı var ve bu yazı şehir başına kaydediliyor. Transport+ tarzında, sol üstte ikonu olmayan bir editör penceresi eklendi. Levhalar şehirde alt araç çubuğundaki Signs+ sekmesinden yerleştirilebiliyor.
- **0.0.3:** 23 sembol, yol numarası kutuları (O, E, D, K), satır içi ve sağa yaslı oklar, mesafe gibi sağa yaslı yazılar, satır başına yazı boyu, otoyol yazıları ve numaralar için kalın yazı tipi, 6 hazır şablon ve geri alma. Yazılar daha büyük. Satırlar sola, ortaya ya da sağa hizalanabiliyor.
- **0.0.4:** Standart trafik işaretleri (37 işaret: tehlike uyarı, tanzim, bilgi; hız değeri değiştirilebilir, altına ek levha). Levhalar yolların, kaldırımların ve diğer nesnelerin üstüne hata vermeden konabiliyor (Anarchy gerekmez, onunla çakışmaz).

## Klasörler

- `SignsPlus\` — mod projesi (bu klasör)
  - `Systems\SignRenderSystem.cs` — levhaları bulur, yüzlerine yazıyı çizer
  - `Systems\SignsPlusUISystem.cs` — editör penceresinin köprüsü (`signsPlus` grubu)
  - `Systems\SignToolbarSystem.cs` — araç çubuğuna Signs+ sekmesi
  - `Data\SignStore.cs` — levha yazılarının deposu
  - `UI\` — editör penceresi (React, `build.mjs` ile esbuild), simgeler ve küçük resimler (`thumbs\`)
- `SignsPlus\Assets\` — assetler (FBX + dokular). Asset Importer'da **Project Root** olarak bu klasör seçilir.
  - `SignsPlusYerLevhasi`, `SignsPlusLBayrak`, `SignsPlusTDirek`, `SignsPlusGantry`, `SignsPlusIsaretDirek` (standart işaret direği; işaretin kendisini mod çizer)
  - Asset adlarında **alt çizgi yok**: Asset Importer adı alt çizgilerden bölerek okuyor (`ad_LOD1`, `ad_BaseColor`).

## Derleme

Oyun kapalıyken `build.bat` dosyasını çalıştır. Önce arayüz paketleri kurulur (ilk seferde `npm install`), sonra C# derlenir ve arayüz modülü (`SignsPlus.mjs`), simgeler ve küçük resimler mod klasörüne yazılır. Sonuç `build.log` dosyasına yazılır.

## Kullanım

- **Yerleştirme (şehirde):** alt araç çubuğunda **Signs+** sekmesi (`Roads` menüsünde; yoksa `Landscaping`). Hangi menüye eklendiği günlükte yazar.
- **Yerleştirme (editörde):** Asset Browser → Custom Assets; adında "Mesh" geçmeyenler.
- **Düzenleme:** levhaya tıkla → editör açılır. Ya da imleci levhanın üstüne getir ve **Shift+L**'ye bas. Pencerede "Ekranın ortasındaki levhayı seç" düğmesi de var.
- **Editör:** panel seçimi (gantry ve T direkte sol/sağ), şablonlar, levha rengi, satırlar ve büyük ok (8 yön), diğer panele kopyalama, temizleme ve geri alma. Değişiklikler levhada hemen görünür.
  - Her satırda: blok rengi, yazı boyu (%100 / %85 / %70 / %125), hiza (sol / orta / sağ; "Hepsini hizala" ile tümü), yer adı, sağa yaslı yazı (mesafe) ve **+ Sembol / numara / ok**.
  - Bir öğeye tıklayınca yerini seçersin: yazının önünde, arkasında ya da sağ kenarda. Semboller kutu içine de alınabilir.
  - Numara kutusunun rengi ilk harften gelir: O ve K turuncu, E yeşil, D beyaz.
- **Standart işaret:** "Trafik işareti" direğini yerleştir, üstüne tıkla. Pencerede grup (Tehlike uyarı / Tanzim / Bilgi) ve işaret seçilir; hız sınırında değer yazılır; "Ek levha" satırları doluysa işaretin altına beyaz levha çıkar.
- **Serbest yerleştirme** (ayarlardan kapatılabilir): Signs+ levhaları yol, kaldırım ve başka nesnelerin üstüne hata vermeden konur; yol yapılırken levhalar engel olmaz, yol onları silmez. Nasıl: yalnızca Signs+ prefablarının çarpışma bayrakları değişir — nesne, yol ve alan araçları açıkken "Marker" bayrağı eklenir (oyun bu nesneleri çakışma denetiminde saymaz), seçim ve buldozerde kaldırılır (tıklanabilsinler diye); "Overridable" bayrağı kaldırılır (yol altında gizlenmesinler diye). Anarchy'nin genel hata ayarlarına dokunulmaz.
- **UIPlus için:** pencere `trigger("signsPlus", "toggle")` ya da `trigger("signsPlus", "setOpen", true)` ile açılır.

## Kayıt

Yazılar şehir adına göre şu dosyada tutulur: `%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\ModsData\SignsPlus\<şehir adı>.txt`

- Oyunun kayıt dosyasına hiçbir şey eklenmez; mod kaldırılınca şehir bozulmaz.
- Bir levha, asset adı ve konumuyla (5 cm hassasiyetle) tanınır. Levhayı taşırsan yazısı kaybolur.
- Aynı şehir adıyla iki farklı kayıt dosyası varsa aynı yazı dosyasını paylaşırlar.

## Günlük

`...\Cities Skylines II\Logs\SignsPlus.Mod.log` dosyasında şunlara bakılır:
- `Araç çubuğu menüleri: ...` ve `Signs+ sekmesi "..." menüsüne eklendi`
- `Signs+ sekmesine eklendi: SignsPlusGantry_...` (4 levha)
- `Signs+ levha yazıları: "<şehir>" için N levha okundu`

Arayüz hataları `UI.log` dosyasında `[Signs+]` satırlarıyla görünür.

## Levha yazı biçimi

Editör, levha yazısını tek satırlık bir metin olarak saklar (`Rendering/SignComposer.cs`):

- Satırlar `|` ile ayrılır. Satır başında `[otoyol]`, `[kgm,x0.7,orta]` gibi seçenekler: blok rengi, yazı boyu ve hiza (`orta`, `sag`).
- `{s:ucak}` sembol, `{s:hastane:k}` kutulu sembol, `{n:O-4}` numara kutusu, `{ok:sag}` satır içi ok.
- `{>}` sonrası sağ kenara yaslanır: `Bergama{>}25 km`.
- Tek başına `{sagust}` gibi bir satır büyük oktur. 0.0.2'de yazılan levhalar aynen açılır.

## Araçlar (`tools\`)

- `assets_spec.py` — asset ölçüleri, tek kaynak
- `build_assets.py` — Blender 5 Python modülüyle (bpy) FBX + doku üretir
- `gen_faces_cs.py` — `Data/Faces.cs` dosyasını üretir (elle düzenlenmez)
- `render_thumbs.py` — araç çubuğu küçük resimleri; `render_preview.py` — genel önizleme
- `make_font_atlas.py` — yazı atlasları (Overpass 600 ve 700, SIL OFL 1.1; lisansı `Resources/OFL-Overpass.txt`)
- `make_std_signs.py` — standart işaret kütüphanesi: `Resources/std_signs.txt`, `UI/stdsigns/*.png`, `UI/src/stdsigns.ts`, `Data/StdSignNames.cs` (make_symbols.py'deki bazı sembolleri kullanır)
- `make_symbols.py` — sembol kütüphanesi: `Resources/symbols.txt` (mod) ve `UI/symbols/*.svg` (editör); shapely gerekir
- `test\` — kodu oyunsuz derleyip denemek için Unity/Game taklitleri (mod derlemesine dahil değil)

## Oyun kodunu inceleme

`build.bat` oyunun `Game.dll` ve birkaç yardımcı DLL'ini `tools\ref\` klasörüne kopyalar (yalnızca inceleme için; moda girmez, `.gitignore`'da). Serbest yerleştirme bu incelemeyle tasarlandı.

## Bilinen sınırlar (0.0.4)

- Standart işaret direği arazinin yüksekliğine konur; köprü ve yükseltilmiş yol üstüne konamaz.

- Levha taşınırsa yazısı kaybolur (anahtar konuma bağlı).
- Yazı tipi Overpass (KGM'nin E ve O serilerine yakın, aynısı değil); renkler RAL'e yakın tahmini değerler.
- Henüz bölme (sütunlu levha), ok şekilli panel ve kullanıcı görseli yok.
- Editördeki önizleme kabadır; doğru görüntü levhanın kendisidir.
- Yazı kutusundayken oyunun kendi kısayolları (kamera tuşları gibi) çalışabilir; Signs+'ın kendi kısayolu kapalıdır.
