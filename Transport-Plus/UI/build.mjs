// Transport+ UI modülünü derler. Resmi şablonun webpack çıktısıyla aynı biçim:
// tek bir ES modülü (TransportPlus.mjs), oyunun hazır kütüphaneleri (React, cs2/*) window üzerinden alınır.
import * as esbuild from "esbuild";
import fs from "node:fs";
import path from "node:path";

const MOD_ID = "TransportPlus";
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
fs.copyFileSync("icon.svg", path.join(outDir, MOD_ID + "-icon.svg"));
console.log("UI modulu yazildi: " + path.join(outDir, MOD_ID + ".mjs"));
