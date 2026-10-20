// Servidor estático mínimo para probar site/ localmente (espejo EN de la landing).
// Uso: node scripts\serve-site.mjs [puerto]  (por defecto 8123)
//
// Task 022: los artefactos de release (latest.json/.sig/.pub y notas HTML)
// ya NO viven en site\ sino en la carpeta autocontenida releases\<versión>\.
// Este servidor mapea las URLs DESPLEGADAS a esa carpeta para que el espejo
// local siga funcionando igual:
//   /pcsuite/latest.json(.sig|.pub) → releases\<versión máxima>\latest.json(.sig|.pub)
//   /notes/<vers>.html              → releases\<vers>\<vers>.html
// El resto (index.html, assets/) se sirve de site\ como siempre.
import http from 'node:http';
import { readFile, readdir } from 'node:fs/promises';
import { extname, join, normalize } from 'node:path';

const root = join(process.cwd(), 'site');
const releasesRoot = join(process.cwd(), 'releases');
const port = Number(process.argv[2] ?? 8123);

const types = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.svg': 'image/svg+xml',
  '.ico': 'image/x-icon',
  '.sig': 'application/pgp-signature',
  '.pub': 'application/pgp-keys',
  '.zip': 'application/zip',
  '.exe': 'application/octet-stream',
};

const versionPattern = /^\d+\.\d+\.\d+$/;

// Comparación numérica por componentes ("0.10.0" > "0.9.0").
function compareVersions(a, b) {
  const pa = a.split('.').map(Number);
  const pb = b.split('.').map(Number);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const diff = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (diff) return diff;
  }
  return 0;
}

async function latestReleaseVersion() {
  const entries = await readdir(releasesRoot, { withFileTypes: true });
  const versions = entries
    .filter((e) => e.isDirectory() && versionPattern.test(e.name))
    .map((e) => e.name);
  versions.sort(compareVersions);
  return versions.at(-1) ?? null;
}

http.createServer(async (req, res) => {
  try {
    let path = decodeURIComponent(new URL(req.url, 'http://x').pathname);
    if (path === '/') path = '/index.html';

    let file;
    // Task 022: mapear las URLs desplegadas a releases\<versión>\.
    // Los grupos de captura están anclados a patrones fijos (versión
    // numérica, sufijos .sig/.pub): no hay recorrido de rutas posible.
    const liveManifest = path.match(/^\/pcsuite\/latest\.json(\.sig|\.pub)?$/);
    const notes = path.match(/^\/notes\/(\d+\.\d+\.\d+)\.html$/);
    if (liveManifest) {
      const latest = await latestReleaseVersion();
      if (!latest) throw new Error('no hay releases');
      file = join(releasesRoot, latest, `latest.json${liveManifest[1] ?? ''}`);
    } else if (notes) {
      file = join(releasesRoot, notes[1], `${notes[1]}.html`);
    } else {
      file = normalize(join(root, path));
      if (!file.startsWith(root)) { res.writeHead(403).end(); return; }
    }

    const data = await readFile(file);
    res.writeHead(200, { 'Content-Type': types[extname(file)] ?? 'application/octet-stream' });
    res.end(data);
  } catch {
    res.writeHead(404, { 'Content-Type': 'text/plain' });
    res.end('404');
  }
}).listen(port, async () => {
  const latest = await latestReleaseVersion().catch(() => null);
  console.log(`site/ sirviendo en http://localhost:${port}`);
  console.log(`manifiesto live: releases/${latest ?? '(ninguno)'} → /pcsuite/latest.json`);
});
