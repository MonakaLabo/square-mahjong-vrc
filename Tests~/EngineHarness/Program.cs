// ブラウザ版の答え(../golden/out/*.jsonl)を、SqmEngine で同じ操作をして1手ずつ照合する。
//   dotnet run -c Release [-- ファイル...]
// 食い違ったら、C# 側の状態の文字列を表示して止める(JS 側は make-golden.mjs --dump で見られる)。
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using MonakaLabo.SquareMahjong;

static class Program
{
    static readonly string[] PhaseNames = { "deal", "draw", "await_turn", "await_calls", "rinshan", "kyoku_over", "match_over" };
    static readonly string[] PendNames = { "-", "turn", "calls", "next", "end" };
    static readonly string[] TypeNames = { "", "discard", "tsumo", "kan", "abort", "pass", "ron", "pon", "chi", "next" };
    static readonly string[] KanNames = { "-", "ankan", "kakan", "daiminkan" };
    static readonly string[] MeldNames = { "", "K", "T", "F" };
    static readonly string[] ReasonNames = { "", "kyushu", "hachifuu", "hachiriichi" };

    static int Main(string[] args)
    {
        string dir = Path.Combine(AppContext.BaseDirectory, "../../../../golden/out");
        var files = args.Length > 0 ? args : Directory.GetFiles(dir, "*.jsonl").OrderBy(f => f).ToArray();
        int okFiles = 0, totalOps = 0;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        foreach (var f in files)
        {
            var rows = File.ReadAllLines(f).Where(l => l.Length > 0).ToArray();
            if (rows.Length < 2) continue;
            var meta = JsonDocument.Parse(rows[0]).RootElement;
            var e = new SqmEngine();
            e.SetDefaultRules();
            ApplyRules(e, meta.GetProperty("rules"));
            e.NewMatchFromString(meta.GetProperty("seed").GetString());
            bool ok = Check(e, rows[1], 0, f);
            for (int k = 2; ok && k < rows.Length; k++)
            {
                string row = rows[k];
                if (row.StartsWith("A ")) e.Advance();
                else
                {
                    int last = row.LastIndexOf(' ');
                    var act = ToAction(JsonDocument.Parse(row.Substring(2, last - 2)).RootElement);
                    if (!e.ApplyAction(act))
                    {
                        Console.WriteLine($"✖ {Path.GetFileName(f)} 操作{k - 1}: C# で適用できない: {e.lastError}\n  {row}");
                        Console.WriteLine(Canon(e));
                        ok = false;
                        break;
                    }
                }
                ok = Check(e, row, k - 1, f);
                totalOps++;
            }
            if (ok) { okFiles++; Console.WriteLine($"✔ {Path.GetFileName(f)}: {rows.Length - 2} 操作すべて一致"); }
            else return 1;
        }
        Console.WriteLine($"{okFiles}/{files.Length} 試合・{totalOps} 操作が一致 ({sw.ElapsedMilliseconds} ms)");
        return okFiles == files.Length ? 0 : 1;
    }

    static bool Check(SqmEngine e, string row, int opIndex, string file)
    {
        string want = row.Substring(row.LastIndexOf(' ') + 1);
        string c = Canon(e);
        string got = Fnv(c);
        if (got == want) return true;
        Console.WriteLine($"✖ {Path.GetFileName(file)} 操作{opIndex} のあとで食い違い (JS {want} / C# {got})\n  操作: {row}");
        Console.WriteLine("---- C# ----\n" + c);
        Console.WriteLine($"---- JS は: node ../golden/make-golden.mjs --dump {file} {opIndex}");
        return false;
    }

    static void ApplyRules(SqmEngine e, JsonElement r)
    {
        foreach (var p in r.EnumerateObject())
        {
            var v = p.Value;
            switch (p.Name)
            {
                case "doraPerFlip": e.rDoraPerFlip = v.GetInt32(); break;
                case "ronTileCount": e.rRonTileCount = v.GetInt32(); break;
                case "kataRonPenalty": e.rKataRonPenalty = v.GetInt32(); break;
                case "furiten": e.rFuriten = Array.IndexOf(new[] { "off", "loose", "strict" }, v.GetString()); break;
                case "hayashi": e.rHayashi = Array.IndexOf(new[] { "off", "dama", "menzen" }, v.GetString()); break;
                case "kanjunMode": e.rKanjunMode = Array.IndexOf(new[] { "none", "perTwo", "perMeld" }, v.GetString()); break;
                case "renchan": e.rRenchanFirst = v.GetString() == "first"; break;
                case "useUra": e.rUseUra = v.GetBoolean(); break;
                case "agariYame": e.rAgariYame = v.GetBoolean(); break;
                case "tobiEnd": e.rTobiEnd = v.GetBoolean(); break;
                case "startScore": e.rStartScore = v.GetInt32(); break;
                default: throw new Exception("未対応のルール: " + p.Name);
            }
        }
    }

    static int[] ToAction(JsonElement a)
    {
        var o = new int[SqmEngine.OPT];
        o[SqmEngine.O_MODE] = -1; o[SqmEngine.O_TILE] = -1; o[SqmEngine.O_RONIDX] = -1; o[SqmEngine.O_RONTILE] = -1;
        o[SqmEngine.O_MELDTYPE] = -1; o[SqmEngine.O_START] = -1;
        string type = a.GetProperty("type").GetString();
        o[SqmEngine.O_TYPE] = Array.IndexOf(TypeNames, type);
        o[SqmEngine.O_SEAT] = a.GetProperty("seat").GetInt32();
        JsonElement v;
        if (a.TryGetProperty("mode", out v))
        {
            string m = v.GetString();
            o[SqmEngine.O_MODE] = m == "two" ? SqmEngine.RON_TWO : m == "one" ? SqmEngine.RON_ONE : Array.IndexOf(KanNames, m);
        }
        if (a.TryGetProperty("use", out v)) o[SqmEngine.O_TILE] = v.GetInt32();
        if (a.TryGetProperty("tile", out v)) o[SqmEngine.O_TILE] = v.GetInt32();
        if (a.TryGetProperty("ronIndex", out v)) o[SqmEngine.O_RONIDX] = v.GetInt32();
        if (a.TryGetProperty("ronTile", out v)) o[SqmEngine.O_RONTILE] = v.GetInt32();
        if (a.TryGetProperty("meldType", out v)) o[SqmEngine.O_MELDTYPE] = Array.IndexOf(MeldNames, v.GetString());
        if (a.TryGetProperty("start", out v)) o[SqmEngine.O_START] = v.GetInt32();
        if (a.TryGetProperty("take", out v)) SetList(o, SqmEngine.O_TAKEN, SqmEngine.O_TAKE, v);
        if (a.TryGetProperty("give", out v)) SetList(o, SqmEngine.O_GIVEN, SqmEngine.O_GIVE, v);
        if (a.TryGetProperty("reason", out v)) o[SqmEngine.O_TILE] = Array.IndexOf(ReasonNames, v.GetString());
        if (a.TryGetProperty("tiles", out v)) SetList(o, SqmEngine.O_TAKEN, SqmEngine.O_TAKE, v);
        if (type == "discard") o[SqmEngine.O_MODE] = a.TryGetProperty("riichi", out v) && v.GetBoolean() ? 1 : 0;
        if (type == "next") o[SqmEngine.O_MODE] = a.TryGetProperty("yame", out v) ? (v.GetBoolean() ? 1 : 0) : -1;
        return o;
    }

    static void SetList(int[] o, int nIdx, int at, JsonElement arr)
    {
        int n = 0;
        foreach (var x in arr.EnumerateArray()) o[at + n++] = x.GetInt32();
        o[nIdx] = n;
    }

    static string Fnv(string s)
    {
        uint h = 0x811c9dc5u;
        foreach (char ch in s) { h ^= ch; h *= 0x01000193u; }
        return h.ToString("x8");
    }

    // ---- 状態の文字列(make-golden.mjs の canon と同じ形) ----
    static string J(System.Collections.Generic.IEnumerable<int> xs) => string.Join(".", xs);

    static string Canon(SqmEngine e)
    {
        const int NP = SqmEngine.NP, NT = SqmEngine.NT;
        var sb = new StringBuilder();
        sb.Append($"{e.step}|{PhaseNames[e.phase]}|{e.turn}|{e.kyoku},{e.honba},{e.kyotaku},{e.dealer}|{string.Join(",", e.score)}\n");
        sb.Append($"{e.mustDiscard},{(e.afterCall ? 1 : 0)},{(e.isRinshan ? 1 : 0)},{(e.anyCall ? 1 : 0)},{e.kanCount},{e.first}\n");
        sb.Append($"{e.LiveLeft()},{e.wallRinshanPos}|{J(e.doraInd.Take(e.doraIndN))}/{J(e.uraInd.Take(e.uraIndN))}|{string.Join(".", e.ronTiles.Take(e.ronTilesLen).Select(t => t < 0 ? "x" : t.ToString()))}");
        for (int s = 0; s < NP; s++)
        {
            var hand = new System.Collections.Generic.List<int>();
            for (int t = 0; t < NT; t++) for (int k = 0; k < e.handC[s * NT + t]; k++) hand.Add(t);
            var drawn = Enumerable.Range(0, e.drawnLen[s]).Select(i => e.drawn[s * SqmEngine.MAXDRAWN + i]);
            var melds = Enumerable.Range(0, e.meldN[s]).Select(i =>
            {
                int m = s * 4 + i;
                var take = new System.Collections.Generic.List<int>();
                if (e.meldTakeN[m] > 0) take.Add(e.meldTake0[m]);
                if (e.meldTakeN[m] > 1) take.Add(e.meldTake1[m]);
                return $"{MeldNames[e.meldType[m]]}{e.meldStart[m]}{(e.meldOpen[m] ? "o" : "c")}{KanNames[e.meldKan[m]]}{e.meldFrom[m]}:{J(take)}";
            });
            var river = Enumerable.Range(0, e.riverLen[s]).Select(i =>
            {
                int r = s * SqmEngine.MAXRIVER + i;
                string tiles = e.rvT0[r] + (e.rvTaken0[r] ? "*" : "");
                if (e.rvN[r] == 2) tiles += "," + e.rvT1[r] + (e.rvTaken1[r] ? "*" : "");
                string ronBy = e.rvRonBy[r] != 0 ? "R" + J(Enumerable.Range(0, NP).Where(q => (e.rvRonBy[r] & (1 << q)) != 0)) : "";
                return tiles + (e.rvRiichi[r] ? "r" : "") + (e.rvCalledBy[r] >= 0 ? "c" + e.rvCalledBy[r] : "") + ronBy;
            });
            string ri = $"{B(e.riichiOn[s])}{B(e.riichiIppatsu[s])}{B(e.riichiDouble[s])}{B(e.riichiDeclaring[s])}";
            string outR = e.outRole[s] == SqmEngine.ROLE_FIRST ? "first" : e.outRole[s] == SqmEngine.ROLE_SECOND ? "second" : "-";
            sb.Append($"\nP{s}:{J(hand)}/{J(drawn)}/{string.Join(";", melds)}/{string.Join(";", river)}/{ri}/{B(e.furitenPerm[s])}{B(e.furitenTemp[s])}/{outR}/{e.turns[s]}");
        }
        var wins = Enumerable.Range(0, e.winsN).Select(w =>
        {
            string role = e.winRole[w] == SqmEngine.ROLE_FIRST ? "first" : "second";
            string wt = e.winType[w] == SqmEngine.WT_TSUMO ? "tsumo" : e.winType[w] == SqmEngine.WT_RON1 ? "ron1" : "ron2";
            var yaku = Enumerable.Range(0, e.winYakuN[w]).Select(k => e.yakuIds[e.winYakuId[w * SqmEngine.MAXYAKU + k]] + "=" + e.winYakuHan[w * SqmEngine.MAXYAKU + k]);
            return $"{e.winSeat[w]},{role},{wt},{e.winFrom[w]},{e.winHan[w]},{B(e.winYakuman[w])},{e.winTotal[w]}:{string.Join(".", yaku)}";
        });
        sb.Append("\nW:" + string.Join(";", wins));
        string res = "-";
        if (e.resType != SqmEngine.RES_NONE)
        {
            res = e.resType == SqmEngine.RES_WIN ? "win" : e.resType == SqmEngine.RES_EXHAUST ? "exhaust" : "abort";
            if (e.resType == SqmEngine.RES_ABORT) res += ":" + ReasonNames[e.resReason];
            if (e.resType == SqmEngine.RES_EXHAUST) res += ":" + J(e.resTenpai.Take(e.resTenpaiN));
        }
        sb.Append("\nE:" + res);
        sb.Append("\nQ:" + (e.pendKind == SqmEngine.PEND_NONE ? "-" : $"{PendNames[e.pendKind]}:{J(e.pendSeats.Take(e.pendN))}"));
        int[] seats = e.pendKind == SqmEngine.PEND_NEXT ? new[] { e.dealer } : e.pendSeats.Take(e.pendN).ToArray();
        foreach (int seat in seats)
        {
            int n = e.BuildLegal(seat);
            var opts = Enumerable.Range(0, n).Select(i => CanonOption(e, e.legal, i * SqmEngine.OPT));
            sb.Append($"\nL{seat}:" + string.Join(";", opts));
        }
        if (e.matchOver)
            sb.Append("\nM:" + string.Join(";", Enumerable.Range(0, NP).Select(i => $"{e.mrSeat[i]},{e.mrScore[i]},{i + 1},{e.mrUma[i]},{e.mrFinal10[i]}")));
        return sb.ToString();
    }

    static string B(bool b) => b ? "1" : "0";

    static string CanonOption(SqmEngine e, int[] a, int o)
    {
        int type = a[o + SqmEngine.O_TYPE];
        string tn = TypeNames[type];
        string mode = "", tile = "", ronIdx = "", ronTile = "", meldType = "", start = "", take = "", give = "", han = "", points = "", count = "", fixedS = "-", kuikae = "", reason = "", yame = "";
        int yakuman = 0, riichi = 0;
        if (type == SqmEngine.A_KAN)
        {
            mode = KanNames[a[o + SqmEngine.O_MODE]]; tile = a[o + SqmEngine.O_TILE].ToString();
            if (a[o + SqmEngine.O_MODE] == SqmEngine.KAN_DAIMINKAN) { meldType = "K"; start = a[o + SqmEngine.O_START].ToString(); take = Lst(a, o, SqmEngine.O_TAKEN, SqmEngine.O_TAKE); give = Lst(a, o, SqmEngine.O_GIVEN, SqmEngine.O_GIVE); }
        }
        else if (type == SqmEngine.A_RON)
        {
            bool one = a[o + SqmEngine.O_MODE] == SqmEngine.RON_ONE;
            mode = one ? "one" : "two";
            if (one) { tile = a[o + SqmEngine.O_TILE].ToString(); ronIdx = a[o + SqmEngine.O_RONIDX].ToString(); ronTile = a[o + SqmEngine.O_RONTILE].ToString(); }
            han = a[o + SqmEngine.O_HAN].ToString(); yakuman = a[o + SqmEngine.O_YAKUMAN]; points = a[o + SqmEngine.O_POINTS].ToString();
        }
        else if (type == SqmEngine.A_TSUMO)
        {
            han = a[o + SqmEngine.O_HAN].ToString(); yakuman = a[o + SqmEngine.O_YAKUMAN]; points = a[o + SqmEngine.O_POINTS].ToString();
        }
        else if (type == SqmEngine.A_PON || type == SqmEngine.A_CHI)
        {
            meldType = MeldNames[a[o + SqmEngine.O_MELDTYPE]]; start = a[o + SqmEngine.O_START].ToString();
            take = Lst(a, o, SqmEngine.O_TAKEN, SqmEngine.O_TAKE); give = Lst(a, o, SqmEngine.O_GIVEN, SqmEngine.O_GIVE);
        }
        else if (type == SqmEngine.A_DISCARD)
        {
            count = a[o + SqmEngine.O_COUNT].ToString();
            if (a[o + SqmEngine.O_FIXEDN] >= 0) fixedS = Lst(a, o, SqmEngine.O_FIXEDN, SqmEngine.O_FIXED);
            else { riichi = a[o + SqmEngine.O_RIICHIOK]; kuikae = Lst(a, o, SqmEngine.O_KUIKAEN, SqmEngine.O_KUIKAE); }
        }
        else if (type == SqmEngine.A_ABORT) reason = ReasonNames[a[o + SqmEngine.O_TILE]];
        else if (type == SqmEngine.A_NEXT && a[o + SqmEngine.O_MODE] >= 0) yame = a[o + SqmEngine.O_MODE].ToString();
        return string.Join(",", tn, mode, tile, ronIdx, ronTile, meldType, start, take, give, han, yakuman, points, count, riichi, fixedS, kuikae, reason, yame);
    }

    static string Lst(int[] a, int o, int nIdx, int at)
    {
        int n = a[o + nIdx];
        return string.Join(".", Enumerable.Range(0, Math.Max(0, n)).Select(i => a[o + at + i]));
    }
}
