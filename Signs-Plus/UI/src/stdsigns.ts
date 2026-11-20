// tools/make_std_signs.py üretir. Elle düzenlemeyin.
export type StdSign = { id: string; group: string; param: boolean; def: string; tr: string };
export const STD_SIGNS: StdSign[] = [
 {
  "id": "dikkat",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Dikkat"
 },
 {
  "id": "viraj-sag",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Sağa tehlikeli viraj"
 },
 {
  "id": "viraj-sol",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Sola tehlikeli viraj"
 },
 {
  "id": "kavsak",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Kontrolsüz kavşak"
 },
 {
  "id": "isik",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Işıklı işaret cihazı"
 },
 {
  "id": "yaya",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Yaya geçidi (uyarı)"
 },
 {
  "id": "daralma",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Daralan kaplama"
 },
 {
  "id": "kasis",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Kasisli yol"
 },
 {
  "id": "donel",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "Dönel kavşak (uyarı)"
 },
 {
  "id": "iki-yon",
  "group": "tehlike",
  "param": false,
  "def": "",
  "tr": "İki yönlü trafik"
 },
 {
  "id": "dur",
  "group": "tanzim",
  "param": false,
  "def": "DUR",
  "tr": "Dur"
 },
 {
  "id": "yolver",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Yol ver"
 },
 {
  "id": "anayol",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Ana yol"
 },
 {
  "id": "hiz",
  "group": "tanzim",
  "param": true,
  "def": "50",
  "tr": "Azami hız sınırı"
 },
 {
  "id": "girilmez",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Girilmez"
 },
 {
  "id": "kapali",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Taşıt trafiğine kapalı"
 },
 {
  "id": "sola-donulmez",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Sola dönülmez"
 },
 {
  "id": "saga-donulmez",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Sağa dönülmez"
 },
 {
  "id": "u-donulmez",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "U dönüşü yapılmaz"
 },
 {
  "id": "gecmek-yasak",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Öndeki taşıtı geçmek yasak"
 },
 {
  "id": "park-yasak",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Park etmek yasak"
 },
 {
  "id": "duraklama-yasak",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Duraklamak ve park etmek yasak"
 },
 {
  "id": "mecburi-ileri",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "İleri mecburi yön"
 },
 {
  "id": "mecburi-sag",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Sağa mecburi yön"
 },
 {
  "id": "mecburi-sol",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Sola mecburi yön"
 },
 {
  "id": "mecburi-ileri-sag",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "İleri ve sağa mecburi yön"
 },
 {
  "id": "mecburi-ileri-sol",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "İleri ve sola mecburi yön"
 },
 {
  "id": "sagdan-git",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Sağdan gidiniz"
 },
 {
  "id": "soldan-git",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Soldan gidiniz"
 },
 {
  "id": "donel-mecburi",
  "group": "tanzim",
  "param": false,
  "def": "",
  "tr": "Ada etrafında dönünüz"
 },
 {
  "id": "park",
  "group": "bilgi",
  "param": false,
  "def": "",
  "tr": "Park yeri"
 },
 {
  "id": "yaya-gecidi",
  "group": "bilgi",
  "param": false,
  "def": "",
  "tr": "Yaya geçidi"
 },
 {
  "id": "tek-yon",
  "group": "bilgi",
  "param": false,
  "def": "",
  "tr": "Tek yönlü yol"
 },
 {
  "id": "cikmaz",
  "group": "bilgi",
  "param": false,
  "def": "",
  "tr": "Çıkmaz yol"
 },
 {
  "id": "durak",
  "group": "bilgi",
  "param": false,
  "def": "",
  "tr": "Otobüs durağı"
 },
 {
  "id": "hastane",
  "group": "bilgi",
  "param": false,
  "def": "",
  "tr": "Hastane"
 },
 {
  "id": "ilk-yardim",
  "group": "bilgi",
  "param": false,
  "def": "",
  "tr": "İlk yardım"
 }
];
