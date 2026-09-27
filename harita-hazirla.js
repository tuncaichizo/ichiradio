// Natural Earth 50m ülke sınırlarını sadeleştirip exe'ye gömülecek küçük ikili dosyaya (harita.bin) yazar.
// Kullanım: node harita-hazirla.js ne_50m_admin_0_countries.geojson harita.bin
// Biçim: u16 ülke sayısı; her ülke: string iso, string ad, u16 halka sayısı; her halka: u16 n, n × (i16 boylam*100, i16 enlem*100).
// string: .NET BinaryWriter biçimi (7-bit uzunluk + UTF-8).
const fs = require('fs');
const [src, out] = process.argv.slice(2);
const gj = JSON.parse(fs.readFileSync(src, 'utf8'));

function perpDist(p, a, b) {
  const dx = b[0] - a[0], dy = b[1] - a[1];
  const len = dx * dx + dy * dy;
  if (len === 0) return Math.hypot(p[0] - a[0], p[1] - a[1]);
  const t = Math.max(0, Math.min(1, ((p[0] - a[0]) * dx + (p[1] - a[1]) * dy) / len));
  return Math.hypot(p[0] - (a[0] + t * dx), p[1] - (a[1] + t * dy));
}

// Douglas-Peucker, özyinelemesiz.
function simplify(pts, tol) {
  if (pts.length < 4) return pts.slice();
  const keep = new Uint8Array(pts.length);
  keep[0] = keep[pts.length - 1] = 1;
  const stack = [[0, pts.length - 1]];
  while (stack.length) {
    const [s, e] = stack.pop();
    let max = 0, idx = -1;
    for (let i = s + 1; i < e; i++) {
      const d = perpDist(pts[i], pts[s], pts[e]);
      if (d > max) { max = d; idx = i; }
    }
    if (max > tol && idx > 0) { keep[idx] = 1; stack.push([s, idx], [idx, e]); }
  }
  return pts.filter((_, i) => keep[i]);
}

function quantize(ring) {
  const res = [];
  for (const [x, y] of ring) {
    const q = [Math.round(x * 100), Math.round(y * 100)];
    const last = res[res.length - 1];
    if (!last || last[0] !== q[0] || last[1] !== q[1]) res.push(q);
  }
  if (res.length > 1 && res[0][0] === res[res.length - 1][0] && res[0][1] === res[res.length - 1][1]) res.pop();
  return res;
}

function str(s) {
  const u = Buffer.from(s, 'utf8');
  const len = [];
  let n = u.length;
  while (n >= 0x80) { len.push((n & 0x7f) | 0x80); n >>= 7; }
  len.push(n);
  return Buffer.concat([Buffer.from(len), u]);
}
function u16(n) { const b = Buffer.alloc(2); b.writeUInt16LE(n); return b; }
function i16(n) { const b = Buffer.alloc(2); b.writeInt16LE(n); return b; }

const parts = [];
let count = 0, points = 0;
for (const f of gj.features) {
  const p = f.properties;
  let iso = p.ISO_A2_EH && p.ISO_A2_EH !== '-99' ? p.ISO_A2_EH : p.ISO_A2;
  if (iso === 'AQ') continue; // Antarktika haritada yer kaplamasın
  if (!iso || iso === '-99') iso = '--';
  const name = p.NAME_TR || p.NAME;
  const g = f.geometry;
  const polys = g.type === 'Polygon' ? [g.coordinates] : g.coordinates;
  let rings = [];
  for (const poly of polys) {
    const r = quantize(simplify(poly[0], 0.05));
    if (r.length >= 3) rings.push(r);
  }
  // Çok küçük ada ülkeleri sadeleştirmede kaybolmasın.
  if (!rings.length)
    for (const poly of polys) {
      const r = quantize(simplify(poly[0], 0.005));
      if (r.length >= 3) rings.push(r);
    }
  if (!rings.length) continue;
  const buf = [str(iso), str(name), u16(rings.length)];
  for (const r of rings) {
    buf.push(u16(r.length));
    for (const [x, y] of r) buf.push(i16(x), i16(y));
    points += r.length;
  }
  parts.push(Buffer.concat(buf));
  count++;
}
const data = Buffer.concat([u16(count), ...parts]);
fs.writeFileSync(out, data);
console.log(`${count} ülke, ${points} nokta, ${(data.length / 1024).toFixed(0)} KB`);
