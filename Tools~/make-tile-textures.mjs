// ブラウザ版の牌の絵(js/ui/tile-art.js の SVG)から、Unity 用の牌の表面の画像を作る。
//   node make-tile-textures.mjs [JS版のフォルダ]
// 1枚のページに全部の牌を並べて Edge(ヘッドレス)で撮り、Python(Pillow)で1枚ずつに切り分けて
// Runtime/Textures/Tiles/tile_00.png 〜 tile_46.png に書き出す。
// 普通の麻雀の赤ドラ(赤5筒・赤5索)は、5筒・5索の緑と青を赤に塗り替えて tile_47・tile_48 にする。
import { writeFileSync, mkdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { tmpdir } from 'node:os';

const HERE = dirname(fileURLToPath(import.meta.url));
const JS_ROOT = process.argv[2] || process.env.SQM_JS_ROOT;
if (!JS_ROOT) { console.error('ブラウザ版のフォルダを引数か環境変数 SQM_JS_ROOT で指定してください'); process.exit(1); }
const { tileSvg } = await import(pathToFileURL(join(JS_ROOT, 'js/ui/tile-art.js')).href);

const W = 240, H = 320;          // 1枚の大きさ(牌の表面 26×35mm の比)
const COLS = 8, N = 49;
// 赤ドラ: 5筒(13)・5索(22)の緑・青を赤に
const RED_OF = { 47: 13, 48: 22 };
function redden(svg) {
  return svg.replace(/#0b7a3e/gi, '#c8102e').replace(/#1c3f94/gi, '#c8102e').replace(/#05522a/gi, '#8d0b20').replace(/#12296a/gi, '#8d0b20');
}
function artOf(t) { return t in RED_OF ? redden(tileSvg(RED_OF[t])) : tileSvg(t); }
const ROWS = Math.ceil(N / COLS);
const cells = [];
for (let t = 0; t < N; t++) {
  const x = (t % COLS) * W, y = Math.floor(t / COLS) * H;
  const art = 'data:image/svg+xml,' + encodeURIComponent(artOf(t));
  cells.push(`<div style="position:absolute;left:${x}px;top:${y}px;width:${W}px;height:${H}px;background:linear-gradient(160deg,#fffdf6 30%,#efe6cf)">`
    + `<img src="${art}" style="position:absolute;left:${W * 0.06}px;top:${H * 0.04}px;width:${W * 0.88}px;height:${H * 0.92}px"></div>`);
}
const html = `<!doctype html><meta charset="utf-8"><body style="margin:0;width:${COLS * W}px;height:${ROWS * H}px;position:relative;overflow:hidden">${cells.join('')}</body>`;
const work = join(tmpdir(), 'sqm-tiles');
mkdirSync(work, { recursive: true });
const page = join(work, 'atlas.html');
const shot = join(work, 'atlas.png');
writeFileSync(page, html);

const edge = 'C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe';
execFileSync(edge, ['--headless=new', '--disable-gpu', `--user-data-dir=${join(work, 'profile')}`, '--no-first-run', '--hide-scrollbars', '--force-device-scale-factor=1',
  `--window-size=${COLS * W},${ROWS * H}`, `--screenshot=${shot}`, pathToFileURL(page).href], { stdio: 'ignore' });

const out = join(HERE, '..', 'Runtime', 'Textures', 'Tiles');
mkdirSync(out, { recursive: true });
const py = `
from PIL import Image
import sys
im = Image.open(sys.argv[1]).convert('RGB')
W, H, COLS, N = ${W}, ${H}, ${COLS}, ${N}
for t in range(N):
    x, y = (t % COLS) * W, (t // COLS) * H
    im.crop((x, y, x + W, y + H)).save(f"{sys.argv[2]}/tile_{t:02d}.png", optimize=True)
print('ok', im.size)
`;
const pyFile = join(work, 'split.py');
writeFileSync(pyFile, py);
console.log(execFileSync('python', [pyFile, shot, out], { encoding: 'utf8' }).trim(), '→', out);
