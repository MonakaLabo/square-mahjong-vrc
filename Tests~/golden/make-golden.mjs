// ブラウザ版(JS)のエンジンで NPC 8人の試合を打ち、答え合わせ用のファイルを作る。
//   node make-golden.mjs [試合数=8] [JS版のフォルダ]
//   node make-golden.mjs --dump <ファイル> <操作の番号>   … その時点の状態の文字列(canon)を表示する(食い違いを調べるとき)
// 出力: out/*.jsonl  1行目は試合の設定、2行目以降は「操作(A=自動進行 / P=アクション)と、その後の状態のハッシュ」。
// 状態の文字列 canon() は、C# の答え合わせ(Tests~/EngineHarness)と同じ形にしてある。
import { readFileSync, writeFileSync, mkdirSync, readdirSync } from 'node:fs';
import { join, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = dirname(fileURLToPath(import.meta.url));
const args = process.argv.slice(2);
const DUMP = args[0] === '--dump';
const JS_ROOT = (DUMP ? args[3] : args[1]) || 'C:/Users/Savanna/Documents/CLAUDE/Game/BakaMahjong';
const R = (p) => pathToFileURL(join(JS_ROOT, p)).href;
const { createMatch, advance, apply, legalActions, liveLeft, canAgariYame } = await import(R('js/engine/game.js'));
const { decide } = await import(R('js/cpu/cpu.js'));
const { hashSeed } = await import(R('js/engine/rng.js'));
const { STYLE_ORDER } = await import(R('js/cpu/styles.js'));

const opt = (v) => (v === undefined || v === null ? '' : String(v));
const list = (a) => (a ? a.join('.') : '');

/** 選択肢・アクション1つの文字列(C# の CanonOption と同じ) */
export function canonOption(o) {
  return [o.type, opt(o.mode), opt(o.type === 'ron' ? o.use : o.tile), opt(o.ronIndex), opt(o.ronTile), opt(o.meldType), opt(o.start),
    list(o.take), list(o.give), opt(o.han), o.yakuman ? 1 : 0, opt(o.points), opt(o.count), o.riichi ? 1 : 0,
    o.fixed ? o.fixed.join('.') : '-', list(o.kuikae), opt(o.reason), o.yame === undefined ? '' : o.yame ? 1 : 0].join(',');
}

/** 状態の文字列(C# の Canon と同じ) */
export function canon(s) {
  const r = s.round;
  const parts = [];
  parts.push(`${s.step}|${s.phase}|${s.turn}|${r.kyoku},${r.honba},${r.kyotaku},${r.dealer}|${s.players.map((p) => p.score).join(',')}`);
  parts.push(`${s.mustDiscard},${s.afterCall ? 1 : 0},${s.isRinshan ? 1 : 0},${s.anyCall ? 1 : 0},${s.kanCount},${s.first ?? -1}`);
  parts.push(`${liveLeft(s)},${s.wall.rinshanPos}|${s.dora.indicators.join('.')}/${s.dora.ura.join('.')}|${s.ronTiles.map((t) => (t === null ? 'x' : t)).join('.')}`);
  for (const p of s.players) {
    const melds = p.melds.map((m) => `${m.type}${m.start}${m.open ? 'o' : 'c'}${m.kan || '-'}${m.from ?? -1}:${list(m.taken)}`).join(';');
    const river = p.river.map((d) => d.tiles.map((t, i) => t + (d.taken[i] ? '*' : '')).join(',') + (d.riichi ? 'r' : '')
      + (d.calledBy !== undefined ? 'c' + d.calledBy : '') + (d.ronBy ? 'R' + d.ronBy.slice().sort((a, b) => a - b).join('.') : '')).join(';');
    const ri = `${p.riichi.on ? 1 : 0}${p.riichi.ippatsu ? 1 : 0}${p.riichi.double ? 1 : 0}${p.riichi.declaring ? 1 : 0}`;
    parts.push(`P${p.seat}:${p.hand.join('.')}/${p.drawn.join('.')}/${melds}/${river}/${ri}/${p.furiten.perm ? 1 : 0}${p.furiten.temp ? 1 : 0}/${p.out || '-'}/${p.turns}`);
  }
  parts.push('W:' + s.wins.map((w) => `${w.seat},${w.role},${w.winType},${w.from ?? -1},${w.han},${w.yakuman ? 1 : 0},${w.total}:${w.yaku.map((y) => y.id + '=' + y.han).join('.')}`).join(';'));
  const res = s.result;
  parts.push('E:' + (res ? res.type + (res.reason ? ':' + res.reason : '') + (res.type === 'exhaust' ? ':' + res.tenpai.join('.') : '') : '-'));
  const pd = s.pending;
  parts.push('Q:' + (pd ? `${pd.kind}:${pd.seats.join('.')}` : '-'));
  // 入力待ちの席の選択肢(局の終わりは親の選択肢)
  const seats = pd ? (pd.kind === 'next' ? [r.dealer] : pd.seats) : [];
  for (const seat of seats) parts.push(`L${seat}:` + legalActions(s, seat).map(canonOption).join(';'));
  if (s.matchResult) parts.push('M:' + s.matchResult.map((m) => `${m.seat},${m.score},${m.rank},${m.uma},${Math.round(m.final * 10)}`).join(';'));
  return parts.join('\n');
}

export function fnv(str) {
  let h = 0x811c9dc5;
  for (let i = 0; i < str.length; i++) { h ^= str.charCodeAt(i); h = Math.imul(h, 0x01000193); }
  return (h >>> 0).toString(16).padStart(8, '0');
}

/** アクションを JSON にするとき、C# が読む項目だけに絞る */
function slim(a) {
  const o = { type: a.type, seat: a.seat };
  for (const k of ['mode', 'use', 'ronIndex', 'ronTile', 'tile', 'meldType', 'start', 'take', 'give', 'reason', 'tiles', 'riichi', 'yame']) if (a[k] !== undefined) o[k] = a[k];
  return o;
}

// 試合の設定: 既定ルール(ふつう・つよい)と、ルールを変えた試合
const CONFIGS = [
  { level: 'normal', rules: {} },
  { level: 'strong', rules: {} },
  { level: 'normal', rules: { doraPerFlip: 2, furiten: 'strict', hayashi: 'menzen', kanjunMode: 'perMeld', renchan: 'any', kataRonPenalty: 1, ronTileCount: 8 } },
  { level: 'normal', rules: { furiten: 'loose', kanjunMode: 'perTwo', useUra: false, agariYame: false, tobiEnd: false } },
];

function playOne(index) {
  const cfg = CONFIGS[index % CONFIGS.length];
  const seedStr = `golden-${index}`;
  const styles = STYLE_ORDER.slice();
  let x = hashSeed(seedStr + ':styles');
  for (let i = styles.length - 1; i > 0; i--) { x = (Math.imul(x, 1664525) + 1013904223) >>> 0; const j = x % (i + 1); [styles[i], styles[j]] = [styles[j], styles[i]]; }
  let s = createMatch({ seed: seedStr, rules: cfg.rules, players: styles.slice(0, 8).map((style, i) => ({ name: `n${i}`, kind: 'cpu', level: cfg.level, style })) });
  const lines = [JSON.stringify({ seed: seedStr, level: cfg.level, rules: cfg.rules })];
  lines.push('I ' + fnv(canon(s)));
  for (let guard = 0; guard < 200000 && s.phase !== 'match_over'; guard++) {
    let r, op;
    if (!s.pending) { r = advance(s); op = 'A'; }
    else if (s.pending.kind === 'end') break;
    else {
      let a;
      if (s.pending.kind === 'next') {
        a = canAgariYame(s) ? { type: 'next', seat: s.round.dealer, yame: s.step % 2 === 0 } : { type: 'next', seat: 0 };
      } else {
        const seat = s.pending.seats[0];
        a = decide(s, legalActions(s, seat), cfg.level, hashSeed(`${s.seed}:${s.step}:${seat}`));
      }
      r = apply(s, a);
      if (r.error) throw new Error(`illegal: ${r.error} ${JSON.stringify(a)}`);
      op = 'P ' + JSON.stringify(slim(a));
    }
    s = r.state;
    lines.push(op + ' ' + fnv(canon(s)));
  }
  return lines;
}

if (DUMP) {
  // 指定したファイルを最初から再生して、n 番目の操作のあとの canon を表示
  const [, file, nStr] = args;
  const rows = readFileSync(file, 'utf8').trim().split('\n');
  const meta = JSON.parse(rows[0]);
  const idx = Number(rows[0] && meta.seed.split('-')[1]);
  const lines = playOne(idx);
  void lines;
  // playOne を途中で止めるより、もう一度打ち直して n 番目まで進める
  const cfg = CONFIGS[idx % CONFIGS.length];
  const styles = STYLE_ORDER.slice();
  let x = hashSeed(meta.seed + ':styles');
  for (let i = styles.length - 1; i > 0; i--) { x = (Math.imul(x, 1664525) + 1013904223) >>> 0; const j = x % (i + 1); [styles[i], styles[j]] = [styles[j], styles[i]]; }
  let s = createMatch({ seed: meta.seed, rules: cfg.rules, players: styles.slice(0, 8).map((style, i) => ({ name: `n${i}`, kind: 'cpu', level: cfg.level, style })) });
  const n = Number(nStr);
  for (let k = 1; k <= n; k++) {
    const row = rows[k + 1];
    if (row.startsWith('A ')) s = advance(s).state;
    else s = apply(s, JSON.parse(row.slice(2, row.lastIndexOf(' ')))).state;
  }
  console.log(canon(s));
} else {
  const N = Number(args[0] || 8);
  const OUT = join(HERE, 'out');
  mkdirSync(OUT, { recursive: true });
  for (const f of readdirSync(OUT)) if (f.endsWith('.jsonl')) writeFileSync(join(OUT, f), ''); // 古いものは空に
  for (let i = 0; i < N; i++) {
    const lines = playOne(i);
    writeFileSync(join(OUT, `golden-${i}.jsonl`), lines.join('\n') + '\n');
    console.log(`golden-${i}.jsonl: ${lines.length - 2} 操作 (${CONFIGS[i % CONFIGS.length].level}, ${JSON.stringify(CONFIGS[i % CONFIGS.length].rules)})`);
  }
}
