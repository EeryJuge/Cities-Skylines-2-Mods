// @ts-nocheck
/*
 * Çizicinin ürettiği SVG parçasını bir <canvas> üzerine çizer.
 * Oyunun arayüz motoru (Gameface) satır içi SVG'yi ve data: adresli SVG resimlerini güvenilir göstermiyor,
 * canvas 2D ise iyi destekleniyor. Bu yüzden ekranda canvas, dosyaya kaydederken SVG kullanılır.
 * Yalnızca transitMap.ts'nin ürettiği öğeleri bilir: rect, path, line, circle, text.
 */

const unescape = (s: string) => s.replace(/&lt;/g, "<").replace(/&gt;/g, ">").replace(/&quot;/g, '"').replace(/&#39;/g, "'").replace(/&amp;/g, "&");

function attrs(src: string) {
  const a: Record<string, string> = {};
  const re = /([\w:-]+)="([^"]*)"/g;
  let m;
  while ((m = re.exec(src))) a[m[1]] = m[2];
  return a;
}

type El = { tag: string; a: Record<string, string>; text?: string };

/** Düz (iç içe olmayan) öğe listesine çevirir. Sonuç önbelleğe alınır, her karede yeniden ayrıştırılmaz. */
export function parseMarkup(markup: string): El[] {
  const out: El[] = [];
  const re = /<(rect|path|line|circle|text)\b([^>]*?)(\/?)>/g;
  let m;
  while ((m = re.exec(markup))) {
    const el: El = { tag: m[1], a: attrs(m[2]) };
    if (m[1] === "text" && m[3] !== "/") {
      const end = markup.indexOf("</text>", re.lastIndex);
      el.text = unescape(markup.slice(re.lastIndex, end));
      re.lastIndex = end + 7;
    }
    out.push(el);
  }
  return out;
}

function tracePath(ctx: CanvasRenderingContext2D, d: string) {
  const tok = d.match(/[MLQCZHVhvz]|-?\d*\.?\d+(?:e[-+]?\d+)?/g) || [];
  let i = 0, cmd = "", x = 0, y = 0, sx = 0, sy = 0;
  const n = () => parseFloat(tok[i++]);
  ctx.beginPath();
  while (i < tok.length) {
    if (/[A-Za-z]/.test(tok[i])) cmd = tok[i++];
    switch (cmd) {
      case "M": x = n(); y = n(); sx = x; sy = y; ctx.moveTo(x, y); cmd = "L"; break;
      case "L": x = n(); y = n(); ctx.lineTo(x, y); break;
      case "H": x = n(); ctx.lineTo(x, y); break;
      case "V": y = n(); ctx.lineTo(x, y); break;
      case "h": x += n(); ctx.lineTo(x, y); break;
      case "v": y += n(); ctx.lineTo(x, y); break;
      case "Q": { const a = n(), b = n(); x = n(); y = n(); ctx.quadraticCurveTo(a, b, x, y); break; }
      case "C": { const a = n(), b = n(), c = n(), e = n(); x = n(); y = n(); ctx.bezierCurveTo(a, b, c, e, x, y); break; }
      case "Z": case "z": ctx.closePath(); x = sx; y = sy; break;
      default: i++;
    }
  }
}

function applyTransform(ctx: CanvasRenderingContext2D, t?: string) {
  if (!t) return;
  const re = /(translate|rotate|scale)\(([^)]*)\)/g;
  let m;
  while ((m = re.exec(t))) {
    const v = m[2].split(/[\s,]+/).map(parseFloat);
    if (m[1] === "translate") ctx.translate(v[0] || 0, v[1] || 0);
    else if (m[1] === "rotate") ctx.rotate((v[0] || 0) * Math.PI / 180);
    else ctx.scale(v[0] || 1, v.length > 1 ? v[1] : v[0] || 1);
  }
}

function setDash(ctx: CanvasRenderingContext2D, dash?: string) {
  try { if (ctx.setLineDash) ctx.setLineDash(dash ? dash.split(/[\s,]+/).map(parseFloat) : []); } catch (e) { /* desteklenmiyorsa düz çizgi */ }
}

function paint(ctx: CanvasRenderingContext2D, a: Record<string, string>) {
  const fill = a.fill, stroke = a.stroke;
  if (fill && fill !== "none") { ctx.fillStyle = fill; ctx.fill(); }
  if (stroke && stroke !== "none") {
    ctx.strokeStyle = stroke;
    ctx.lineWidth = parseFloat(a["stroke-width"] || "1");
    ctx.lineCap = (a["stroke-linecap"] as any) || "butt";
    ctx.lineJoin = (a["stroke-linejoin"] as any) || "miter";
    setDash(ctx, a["stroke-dasharray"]);
    ctx.stroke();
    setDash(ctx, "");
  }
}

/** Öğeleri çizer. view: görünen SVG alanı; W×H: canvas piksel boyutu. */
export function drawElements(ctx: CanvasRenderingContext2D, els: El[], view: { x: number; y: number; w: number; h: number }, W: number, H: number, bg: string) {
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  ctx.globalAlpha = 1;
  ctx.fillStyle = bg;
  ctx.fillRect(0, 0, W, H);
  const s = Math.min(W / view.w, H / view.h);
  const ox = (W - view.w * s) / 2 - view.x * s, oy = (H - view.h * s) / 2 - view.y * s;
  for (const el of els) {
    const a = el.a;
    ctx.save();
    ctx.setTransform(s, 0, 0, s, ox, oy);
    ctx.globalAlpha = a.opacity ? parseFloat(a.opacity) : 1;
    applyTransform(ctx, a.transform);
    switch (el.tag) {
      case "rect": {
        const x = +a.x || 0, y = +a.y || 0, w = +a.width || 0, h = +a.height || 0;
        const r = Math.min(+a.rx || 0, w / 2, h / 2);
        ctx.beginPath();
        if (r > 0) {
          ctx.moveTo(x + r, y);
          ctx.arcTo(x + w, y, x + w, y + h, r); ctx.arcTo(x + w, y + h, x, y + h, r);
          ctx.arcTo(x, y + h, x, y, r); ctx.arcTo(x, y, x + w, y, r);
          ctx.closePath();
        } else ctx.rect(x, y, w, h);
        paint(ctx, a);
        break;
      }
      case "path": tracePath(ctx, a.d || ""); paint(ctx, a); break;
      case "line":
        ctx.beginPath(); ctx.moveTo(+a.x1, +a.y1); ctx.lineTo(+a.x2, +a.y2);
        paint(ctx, { ...a, fill: "none" });
        break;
      case "circle":
        ctx.beginPath(); ctx.arc(+a.cx, +a.cy, +a.r, 0, Math.PI * 2);
        paint(ctx, a);
        break;
      case "text": {
        const fs = parseFloat(a["font-size"] || "12");
        ctx.font = `${a["font-style"] === "italic" ? "italic " : ""}${a["font-weight"] || "400"} ${fs}px ${a["font-family"] || "sans-serif"}`;
        ctx.textAlign = a["text-anchor"] === "middle" ? "center" : a["text-anchor"] === "end" ? "right" : "left";
        ctx.textBaseline = "alphabetic";
        const x = +a.x || 0, y = +a.y || 0, t = el.text || "";
        if (a.stroke && a.stroke !== "none") {
          ctx.strokeStyle = a.stroke; ctx.lineWidth = parseFloat(a["stroke-width"] || "1"); ctx.lineJoin = "round";
          ctx.strokeText(t, x, y);
        }
        if (a.fill && a.fill !== "none") { ctx.fillStyle = a.fill; ctx.fillText(t, x, y); }
        break;
      }
    }
    ctx.restore();
  }
}
