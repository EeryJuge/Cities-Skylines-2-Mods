// Signs+ UI modülünü derler. Resmi şablonun webpack çıktısıyla aynı biçim:
// tek bir ES modülü (SignsPlus.mjs), oyunun hazır kütüphaneleri (React, cs2/*) window üzerinden alınır.
import * as esbuild from "esbuild";
import fs from "node:fs";
import path from "node:path";

const MOD_ID = "SignsPlus";
const modsPath = process.env.CSII_LOCALMODSPATH
  || (process.env.CSII_USERDATAPATH && path.join(process.env.CSII_USERDATAPATH, "Mods"));
if (!modsPath) { console.error("CSII_LOCALMODSPATH bulunamadi (oyunun mod araclari kurulu mu?)"); process.exit(1); }
const outDir = path.join(modsPath, MOD_ID);
fs.mkdirSync(outDir, { recursive: true });

// Oyunun sağladığı modüller: import edilen ad -> window üzerindeki ad
const GLOBALS = {
  "react": "React",
  "react-dom": "ReactDOM",
  "cs2/modding": "cs2/modding",
  "cs2/api": "cs2/api",
  "cs2/bindings": "cs2/bindings",
  "cs2/l10n": "cs2/l10n",
  "cs2/ui": "cs2/ui",
  "cs2/input": "cs2/input",
  "cs2/utils": "cs2/utils",
  "cohtml/cohtml": "cohtml/cohtml"
};

const gamePlugin = {
  name: "cs2-globals",
  setup(b) {
    b.onResolve({ filter: /^(react|react-dom|react\/jsx-runtime|cs2\/.*|cohtml\/cohtml)$/ }, a => ({ path: a.path, namespace: "cs2-global" }));
    b.onLoad({ filter: /.*/, namespace: "cs2-global" }, a => {
      if (a.path === "react/jsx-runtime") {
        // Oyun yalnızca React'i verir; JSX çalışma zamanını React.createElement üzerinden kur
        return { loader: "js", contents: `
          const R = window["React"];
          function jsx(type, props, key) { const p = key === undefined ? props : Object.assign({}, props, { key }); return R.createElement(type, p); }
          module.exports = { jsx, jsxs: jsx, Fragment: R.Fragment };` };
      }
      return { loader: "js", contents: `module.exports = window[${JSON.stringify(GLOBALS[a.path])}];` };
    });
  }
};

await esbuild.build({
  entryPoints: { [MOD_ID]: "src/index.tsx" },
  bundle: true,
  format: "esm",
  target: "es2020",
  jsx: "automatic",
  minify: true,
  outExtension: { ".js": ".mjs" },
  outdir: outDir,
  plugins: [gamePlugin],
  logLevel: "info"
});
// Simge ve küçük resimler mod klasörüne düz adla kopyalanır; oyunda coui://ui-mods/<ad> olarak açılır
fs.copyFileSync("icon.svg", path.join(outDir, MOD_ID + "-icon.svg"));
fs.copyFileSync("arrow.svg", path.join(outDir, MOD_ID + "-arrow.svg"));
fs.copyFileSync("arrow-dark.svg", path.join(outDir, MOD_ID + "-arrow-dark.svg"));
for (const f of fs.readdirSync("thumbs")) {
  if (f.endsWith(".png")) fs.copyFileSync(path.join("thumbs", f), path.join(outDir, MOD_ID + "-thumb-" + f));
}
for (const f of fs.readdirSync("symbols")) {
  if (f.endsWith(".svg")) fs.copyFileSync(path.join("symbols", f), path.join(outDir, MOD_ID + "-sym-" + f));
}
for (const f of fs.readdirSync("stdsigns")) {
  if (f.endsWith(".png")) fs.copyFileSync(path.join("stdsigns", f), path.join(outDir, MOD_ID + "-std-" + f));
}
console.log("UI modulu yazildi: " + path.join(outDir, MOD_ID + ".mjs"));
