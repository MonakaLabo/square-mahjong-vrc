// SqmStdEngine(普通の麻雀)の確かめ。 dotnet run -c Release [試合数]
// 1. 決まった手牌で、役・翻・符・点を答え合わせする
// 2. 簡単な打ち手どうしで自動で打たせ、牌の枚数・点数の合計・手牌の枚数が崩れないこと、試合が最後まで終わることを確かめる
// 3. 同じ操作を最初から再生したとき、局の始めから復元して再生したときに、同じ結果になることを確かめる
using System;
using System.Collections.Generic;
using System.Linq;
using MonakaLabo.SquareMahjong;
using E = MonakaLabo.SquareMahjong.SqmStdEngine;

static class Program
{
    static int fails = 0, checks = 0;

    static void Check(string name, bool ok, string detail = "")
    {
        checks++;
        if (ok) return;
        fails++;
        Console.WriteLine($"✖ {name} {detail}");
    }

    static int Main(string[] args)
    {
        int matches = args.Length > 0 ? int.Parse(args[0]) : 200;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        UnitTests();
        Console.WriteLine($"役・点数: {checks - fails}/{checks}");
        foreach (var (p, r) in new[] { (4, 2), (4, 1), (3, 2), (3, 1) })
            SelfPlay(p, r, matches, 0);
        // NPC どうし / 簡単な打ち手 1人と NPC
        SelfPlay(4, 2, matches, 2);
        SelfPlay(3, 2, matches, 2);
        SelfPlay(4, 2, matches, 1);
        Console.WriteLine($"{(fails == 0 ? "OK" : "NG")} 確かめ {checks} 回、失敗 {fails} 回 ({sw.ElapsedMilliseconds} ms)");
        return fails == 0 ? 0 : 1;
    }

    // ================================================================ 手牌の書き方: "123m406p789s1234567z"(0 は赤5、z は 1-7 = 東南西北白發中)
    static List<int> Parse(string s)
    {
        var res = new List<int>();
        var digits = new List<int>();
        foreach (char ch in s)
        {
            if (char.IsDigit(ch)) { digits.Add(ch - '0'); continue; }
            int suit = "mpsz".IndexOf(ch);
            foreach (int d in digits)
            {
                if (suit == 3) res.Add(26 + d);
                else if (d == 0) res.Add(E.RED0 + suit);
                else res.Add(suit * 9 + d - 1);
            }
            digits.Clear();
        }
        return res;
    }

    static int T(string s) => Parse(s)[0];

    /// <summary>単体の確かめ用の卓: 4人・東場・親は 1 番の席(0 番の席は北家)・ドラなし</summary>
    static E Table(int players = 4)
    {
        var e = new E();
        e.SetRules(players, 2);
        e.NewMatch(1);
        AdvanceAll(e);
        e.dealer = 1; e.round = 0; e.doraN = 0;
        for (int s = 0; s < E.MAXP; s++)
        {
            for (int t = 0; t < E.NC; t++) e.handC[s * E.NC + t] = 0;
            e.handN[s] = 0; e.meldN[s] = 0; e.kitaN[s] = 0; e.riichiOn[s] = false; e.ippatsu[s] = false; e.doubleRiichi[s] = false;
        }
        return e;
    }

    static void SetHand(E e, int s, string h)
    {
        foreach (int c in Parse(h)) { e.handC[s * E.NC + c]++; e.handN[s]++; }
    }

    static void AddMeld(E e, int s, int type, string tiles, int from)
    {
        var t = Parse(tiles);
        int mi = s * 4 + e.meldN[s]++;
        e.mType[mi] = type;
        e.mKind[mi] = t.Select(e.KindOf).Min();
        e.mT0[mi] = t[0]; e.mT1[mi] = t[1]; e.mT2[mi] = t[2]; e.mT3[mi] = t.Count > 3 ? t[3] : -1;
        e.mCalled[mi] = type == E.M_ANKAN ? -1 : t[2];
        e.mFrom[mi] = from;
    }

    static string Yaku(E e)
    {
        var parts = new List<string>();
        for (int i = 0; i < e.bN; i++) parts.Add(e.YakuName(e.bId[i]) + e.bHanArr[i]);
        return $"[{string.Join(" ", parts)}] {e.bHan}翻{e.bFu}符 基本点{e.bBase} 役満{e.bYakuman}";
    }

    static bool Has(E e, int id)
    {
        for (int i = 0; i < e.bN; i++) if (e.bId[i] == id) return true;
        return false;
    }

    /// <summary>tsumo なら手牌に和了牌を足しておく</summary>
    static bool Eval(E e, string hand13, string win, bool tsumo, int ctx = 0)
    {
        SetHand(e, 0, hand13);
        int w = T(win);
        if (tsumo) { e.handC[w]++; e.handN[0]++; }
        return e.EvaluateWin(0, w, tsumo, ctx, tsumo ? -1 : 2);
    }

    static void Expect(string name, E e, bool ok, int han, int fu, int bse, int man, params int[] yaku)
    {
        bool good = ok && e.bHan == han && e.bFu == fu && e.bBase == bse && e.bYakuman == man && yaku.All(y => Has(e, y));
        Check(name, good, ok ? Yaku(e) : "(役なし)");
    }

    static void UnitTests()
    {
        E e;
        e = Table(); Expect("平和ツモ", e, Eval(e, "123m456m78p234s55p", "9p", true), 2, 20, 320, 0, E.Y_PINFU, E.Y_TSUMO);
        e = Table(); Expect("平和ロン", e, Eval(e, "123m456m78p234s55p", "9p", false), 1, 30, 240, 0, E.Y_PINFU);
        e = Table(); Expect("七対子", e, Eval(e, "11m22m33p44p55s66s7z", "7z", false), 2, 25, 400, 0, E.Y_CHIITOI);
        e = Table(); Expect("国士十三面", e, Eval(e, "19m19p19s1234567z", "1m", true), 0, 0, 16000, 2, E.Y_KOKUSHI13);
        e = Table(); Expect("国士", e, Eval(e, "19m19p11s1234567z", "9s", false), 0, 0, 8000, 1, E.Y_KOKUSHI);
        e = Table(); Expect("四暗刻単騎", e, Eval(e, "111m222p333s444z5z", "5z", false), 0, 0, 16000, 2, E.Y_SUUANKOUTANKI);
        e = Table(); Expect("四暗刻(ツモ)", e, Eval(e, "111m222p333s33z55z", "5z", true), 0, 0, 8000, 1, E.Y_SUUANKOU);
        e = Table(); Expect("シャンポンのロンは三暗刻", e, Eval(e, "111m222p33s55s333z", "5s", false), 4, 60, 2000, 0, E.Y_SANANKOU, E.Y_TOITOI);
        e = Table(); Expect("一気通貫・混一色", e, Eval(e, "1234556789m222z", "5m", true), 6, 40, 3000, 0, E.Y_ITTSUU, E.Y_HONITSU, E.Y_TSUMO);
        e = Table(); Expect("純正九蓮宝燈", e, Eval(e, "1112345678999m", "5m", true), 0, 0, 16000, 2, E.Y_JUNSEICHUUREN);
        e = Table(); Expect("九蓮宝燈", e, Eval(e, "1112245678999m", "3m", false), 0, 0, 8000, 1, E.Y_CHUUREN);
        e = Table(); Expect("二盃口(七対子より高い)", e, Eval(e, "223344m55667p55s", "7p", false), 5, 30, 2000, 0, E.Y_RYANPEIKOU, E.Y_PINFU, E.Y_TANYAO);
        Check("二盃口に一盃口・七対子が付かない", !Has(e, E.Y_IIPEIKOU) && !Has(e, E.Y_CHIITOI), Yaku(e));
        e = Table(); Expect("純全帯幺九", e, Eval(e, "123789m789p123s9s", "9s", false), 3, 40, 1280, 0, E.Y_JUNCHAN);
        e = Table(); Expect("混全帯幺九", e, Eval(e, "123m789p123s33z99m", "3z", false), 2, 40, 640, 0, E.Y_CHANTA);
        e = Table(); Check("役なしは和了れない", !Eval(e, "123m456p789s46m99s", "5m", false), Yaku(e));
        e = Table(); e.doraN = 1; e.wall[e.wallN - 6] = T("8m");
        Expect("役牌とドラ", e, Eval(e, "123m456p78s99m555z", "9s", false), 3, 40, 1280, 0, E.Y_HAKU, E.Y_DORA);
        e = Table(); Expect("赤ドラ", e, Eval(e, "123m406p78s99m555z", "9s", false), 2, 40, 640, 0, E.Y_HAKU, E.Y_AKA);
        e = Table(); AddMeld(e, 0, E.M_CHI, "234m", 3);
        Expect("喰いタン", e, Eval(e, "567p345s66s88p", "8p", false), 1, 30, 240, 0, E.Y_TANYAO);
        e = Table(); e.rKuitan = false; AddMeld(e, 0, E.M_CHI, "234m", 3);
        Check("喰いタンなしなら和了れない", !Eval(e, "567p345s66s88p", "8p", false), Yaku(e));
        e = Table(); AddMeld(e, 0, E.M_PON, "555z", 3); AddMeld(e, 0, E.M_PON, "666z", 2);
        Expect("大三元(鳴き)", e, Eval(e, "777z11m23m", "1m", false), 0, 0, 8000, 1, E.Y_DAISANGEN);
        e = Table(); Expect("小三元", e, Eval(e, "555z666z77z123p45m", "6m", false), 4, 50, 2000, 0, E.Y_SHOUSANGEN, E.Y_HAKU, E.Y_HATSU);
        e = Table(); e.riichiOn[0] = true; e.ippatsu[0] = true;
        Expect("立直一発ツモ", e, Eval(e, "123m456m78p234s55p", "9p", true), 4, 20, 1280, 0, E.Y_RIICHI, E.Y_IPPATSU, E.Y_TSUMO, E.Y_PINFU);
        e = Table(); Expect("嶺上開花", e, Eval(e, "123m456m78p234s55p", "9p", true, E.CX_RINSHAN), 3, 20, 640, 0, E.Y_RINSHAN);
        e = Table(); Expect("天和", e, Eval(e, "123m456m78p234s55p", "9p", true, E.CX_TENHOU), 0, 0, 8000, 1, E.Y_TENHOU);
        e = Table(); Expect("字一色・四暗刻単騎", e, Eval(e, "111z222z333z555z6z", "6z", false), 0, 0, 24000, 3, E.Y_TSUUIISOU, E.Y_SUUANKOUTANKI);
        e = Table(); Expect("緑一色", e, Eval(e, "223344s666s888s6z", "6z", false), 0, 0, 8000, 1, E.Y_RYUUIISOU);
        e = Table(); AddMeld(e, 0, E.M_PON, "999p", 3);
        Expect("清一色(鳴き)", e, Eval(e, "123p345p78p55p", "9p", false), 5, 30, 2000, 0, E.Y_CHINITSU);
        e = Table(); Expect("三色同順", e, Eval(e, "123m123p12s456s99m", "3s", false), 2, 40, 640, 0, E.Y_SANSHOKU);
        e = Table(); Expect("切り上げなし(4翻30符)", e, Eval(e, "234m234p234s56s88p", "7s", false), 4, 30, 1920, 0, E.Y_SANSHOKU, E.Y_TANYAO, E.Y_PINFU);
        e = Table(); e.rKiriage = true;
        Expect("切り上げ満貫", e, Eval(e, "234m234p234s56s88p", "7s", false), 4, 30, 2000, 0, E.Y_SANSHOKU);
        e = Table(); e.round = 1; e.dealer = 3;   // 南場、0 番の席は南家
        Expect("連風牌の刻子", e, Eval(e, "222z123m456p78s99m", "9s", false), 2, 40, 640, 0, E.Y_SEATWIND, E.Y_ROUNDWIND);

        // 待ち
        e = Table();
        int[] c = new int[E.NK];
        foreach (int t in Parse("1112345678999m")) c[e.KindOf(t)]++;
        Check("九蓮の待ちは9種", e.FindWaits(c, 4) == 9, e.waitN.ToString());
        c = new int[E.NK];
        foreach (int t in Parse("19m19p19s1234567z")) c[e.KindOf(t)]++;
        Check("国士十三面の待ちは13種", e.FindWaits(c, 4) == 13, e.waitN.ToString());

        // 三麻: 萬子は 1 と 9 だけ、ドラ表示 1萬 → ドラ 9萬
        e = new E(); e.SetRules(3, 2); e.NewMatch(1);
        Check("三麻のドラ 1萬→9萬", e.DoraOf(0) == 8 && e.DoraOf(8) == 0);
        AdvanceAll(e);
        Check("三麻は108枚", e.wallN == 108, e.wallN.ToString());
        bool noMan = true;
        for (int i = 0; i < e.wallN; i++) { int k = e.KindOf(e.wall[i]); if (k >= 1 && k <= 7) noMan = false; }
        Check("三麻に 2萬〜8萬 が無い", noMan);
        e = new E(); e.SetRules(4, 2); e.NewMatch(1); AdvanceAll(e);
        Check("四麻は136枚・赤2枚(5筒・5索)", e.wallN == 136 && e.wall.Take(136).Count(t => t >= E.RED0) == 2 && !e.wall.Take(136).Contains(E.RED0));
    }

    // ================================================================ 自動対局
    static void AdvanceAll(E e)
    {
        for (int g = 0; g < 64 && e.pendKind == E.PEND_NONE; g++) e.Advance();
    }

    /// <summary>向聴数(普通の形・七対子・国士の小さい方)。melds = 副露の数</summary>
    static int Shanten(int[] c0, int melds)
    {
        var c = (int[])c0.Clone();
        int best = 8;
        void Dfs(int i, int m, int t, int pair)
        {
            while (i < 34 && c[i] == 0) i++;
            if (i >= 34) { best = Math.Min(best, 8 - 2 * m - Math.Min(t, 4 - m) - pair); return; }
            if (c[i] >= 3) { c[i] -= 3; Dfs(i, m + 1, t, pair); c[i] += 3; }
            if (i < 27 && i % 9 <= 6 && c[i + 1] > 0 && c[i + 2] > 0) { c[i]--; c[i + 1]--; c[i + 2]--; Dfs(i, m + 1, t, pair); c[i]++; c[i + 1]++; c[i + 2]++; }
            if (c[i] >= 2) { c[i] -= 2; Dfs(i, m, t + 1, pair); c[i] += 2; }
            if (i < 27 && i % 9 <= 7 && c[i + 1] > 0) { c[i]--; c[i + 1]--; Dfs(i, m, t + 1, pair); c[i]++; c[i + 1]++; }
            if (i < 27 && i % 9 <= 6 && c[i + 2] > 0) { c[i]--; c[i + 2]--; Dfs(i, m, t + 1, pair); c[i]++; c[i + 2]++; }
            int save = c[i]; c[i] = 0; Dfs(i + 1, m, t, pair); c[i] = save;
        }
        Dfs(0, melds, 0, 0);
        for (int p = 0; p < 34; p++)
            if (c[p] >= 2) { c[p] -= 2; Dfs(0, melds, 0, 1); c[p] += 2; }
        if (melds == 0)
        {
            int pairs = 0, kinds = 0, yao = 0, yaoPair = 0;
            for (int k = 0; k < 34; k++)
            {
                if (c[k] >= 2) pairs++;
                if (c[k] >= 1) kinds++;
                bool y = k >= 27 || k % 9 == 0 || k % 9 == 8;
                if (y && c[k] >= 1) yao++;
                if (y && c[k] >= 2) yaoPair = 1;
            }
            best = Math.Min(best, 6 - pairs + Math.Max(0, 7 - kinds));
            best = Math.Min(best, 13 - yao - yaoPair);
        }
        return best;
    }

    static int Choose(E e, int seat, int n, Random rng)
    {
        int Find(int type) { for (int i = 0; i < n; i++) if (e.legal[i * E.OPT + E.O_TYPE] == type) return i; return -1; }
        int win = Find(E.A_TSUMO); if (win < 0) win = Find(E.A_RON);
        if (win >= 0) return win;
        if (e.pendKind == E.PEND_NEXT) return 0;
        if (e.pendKind == E.PEND_CALLS)
        {
            var calls = new List<int>();
            for (int i = 0; i < n; i++) { int t = e.legal[i * E.OPT + E.O_TYPE]; if (t == E.A_PON || t == E.A_CHI || t == E.A_MINKAN) calls.Add(i); }
            if (calls.Count > 0 && rng.NextDouble() < 0.2) return calls[rng.Next(calls.Count)];
            return Find(E.A_PASS);
        }
        int ky = Find(E.A_KYUSHU); if (ky >= 0 && rng.NextDouble() < 0.5) return ky;
        int kita = Find(E.A_KITA); if (kita >= 0) return kita;
        int ankan = Find(E.A_ANKAN); if (ankan >= 0 && rng.NextDouble() < 0.6) return ankan;
        int kakan = Find(E.A_KAKAN); if (kakan >= 0 && rng.NextDouble() < 0.6) return kakan;
        var riichi = new List<int>();
        for (int i = 0; i < n; i++) if (e.legal[i * E.OPT + E.O_TYPE] == E.A_DISCARD && e.legal[i * E.OPT + E.O_RIICHI] == 1) riichi.Add(i);
        if (riichi.Count > 0 && rng.NextDouble() < 0.7) return riichi[rng.Next(riichi.Count)];
        int[] c = new int[E.NK];
        e.KindCounts(seat, c);
        int bestI = -1, bestScore = int.MaxValue;
        for (int i = 0; i < n; i++)
        {
            if (e.legal[i * E.OPT + E.O_TYPE] != E.A_DISCARD || e.legal[i * E.OPT + E.O_RIICHI] != 0) continue;
            int k = e.KindOf(e.legal[i * E.OPT + E.O_TILE]);
            c[k]--;
            int sc = Shanten(c, e.meldN[seat]) * 100 + (k >= 27 ? 0 : (k % 9 == 0 || k % 9 == 8) ? 10 : 20) + rng.Next(10);
            c[k]++;
            if (sc < bestScore) { bestScore = sc; bestI = i; }
        }
        return bestI >= 0 ? bestI : 0;
    }

    static string Canon(E e)
    {
        return $"ph{e.phase} r{e.round} k{e.kyokuNo} h{e.honba} kt{e.kyotaku} d{e.dealer} seq{e.kyokuSeq} sc{string.Join(",", e.score)} " +
               $"fin{string.Join(",", e.mrFinal10)} wall{e.wallPos}/{e.liveEnd}/{e.replN} hand{string.Join(",", e.handN)}";
    }

    static void Invariants(E e, string where)
    {
        int inPlay = 0;
        for (int s = 0; s < e.NP; s++)
        {
            inPlay += e.handN[s] + e.kitaN[s];
            for (int i = 0; i < e.meldN[s]; i++) inPlay += (e.mType[s * 4 + i] == E.M_CHI || e.mType[s * 4 + i] == E.M_PON) ? 3 : 4;
            for (int i = 0; i < e.riverN[s]; i++) if ((e.rvF[s * E.MAXRV + i] & E.RV_CALLED) == 0) inPlay++;
        }
        if (e.phase != E.PH_DEAL && e.phase != E.PH_MATCH_OVER)
            Check("牌の枚数", inPlay == e.wallPos + e.replN, $"{where}: 場 {inPlay} / 山から {e.wallPos + e.replN}");
        int sum = e.kyotaku * e.rRiichiStick;
        for (int s = 0; s < e.NP; s++) sum += e.score[s];
        Check("点数の合計", sum == e.rStartScore * e.NP, $"{where}: {sum}");
        if (e.phase == E.PH_TURN || e.phase == E.PH_CALLS)
            for (int s = 0; s < e.NP; s++)
            {
                int want = (e.phase == E.PH_TURN && s == e.turn) ? 14 : 13;
                Check("手牌の枚数", e.handN[s] + 3 * e.meldN[s] == want, $"{where}: 席{s} {e.handN[s]}+{e.meldN[s]}副露");
            }
    }

    static void SelfPlay(int players, int rounds, int matches, int npcMode)
    {
        // npcMode: 0 = 全員簡単な打ち手 / 1 = 0 番の席だけ簡単な打ち手、ほかは NPC / 2 = 全員 NPC
        var rng = new Random(players * 100 + rounds + npcMode * 7);
        var styleFinal = new long[SqmStdNpc.N_STYLES + 1]; var styleN = new int[SqmStdNpc.N_STYLES + 1];
        var styleWin = new int[SqmStdNpc.N_STYLES + 1]; var styleDeal = new int[SqmStdNpc.N_STYLES + 1]; var styleRank = new long[SqmStdNpc.N_STYLES + 1];
        long npcTicks = 0; int npcCalls = 0; double npcMax = 0;
        var yakuCount = new int[E.N_YAKU];
        int kyoku = 0, wins = 0, tsumo = 0, exhaust = 0, nagashi = 0, aborts = 0, tobi = 0, yakuman = 0, restores = 0, replays = 0;
        var abortKinds = new int[8];
        long leftSum = 0; var leftHist = new int[8];
        int failsBefore = fails;
        for (int mt = 0; mt < matches && fails - failsBefore < 5; mt++)
        {
            uint seed = (uint)rng.Next();
            var e = new E();
            e.SetRules(players, rounds);
            e.NewMatch(seed);
            var npc = new SqmStdNpc(); npc.engine = e;
            var seatStyle = new int[4];
            for (int s = 0; s < 4; s++)
            {
                seatStyle[s] = rng.Next(SqmStdNpc.N_STYLES);
                if (npcMode == 0 || (npcMode == 1 && s == 0)) seatStyle[s] = SqmStdNpc.N_STYLES;   // 簡単な打ち手
                else npc.SetSeat(s, seatStyle[s]);
            }
            var log = new List<(int seat, int idx)>();
            var starts = new List<(int at, int[] ks)>();
            int seenSeq = -1, countedSeq = -1, guard = 0;
            bool stuck = false;
            while (true)
            {
                AdvanceAll(e);
                if (e.kyokuSeq != seenSeq && e.phase != E.PH_DEAL) { seenSeq = e.kyokuSeq; starts.Add((log.Count, (int[])e.ksBuf.Clone())); }
                if (e.phase == E.PH_KYOKU_OVER && countedSeq != e.kyokuSeq)
                {
                    countedSeq = e.kyokuSeq;
                    kyoku++;
                    leftSum += e.LiveLeft(); leftHist[Math.Min(7, e.LiveLeft() / 10)]++;
                    if (e.resType == E.RES_WIN)
                    {
                        wins += e.winsN;
                        for (int w = 0; w < e.winsN; w++)
                        {
                            if (e.winFrom[w] < 0) tsumo++;
                            styleWin[seatStyle[e.winSeat[w]]]++;
                            if (e.winFrom[w] >= 0) styleDeal[seatStyle[e.winFrom[w]]]++;
                            if (e.winYakuman[w] > 0) yakuman++;
                            for (int i = 0; i < e.winYakuN[w]; i++) yakuCount[e.winYakuId[w * E.MAXYAKU + i]]++;
                            Check("和了に役がある", e.winYakuN[w] > 0 && e.winBase[w] > 0);
                        }
                    }
                    else if (e.resType == E.RES_EXHAUST)
                    {
                        exhaust++;
                        if (e.resReason == E.EX_NAGASHI) { nagashi++; for (int w = 0; w < e.winsN; w++) yakuCount[E.Y_NAGASHI]++; }
                    }
                    else { aborts++; abortKinds[e.resReason]++; }
                }
                if (e.pendKind == E.PEND_END) break;
                if (++guard > 50000 || e.pendKind == E.PEND_NONE) { stuck = true; break; }
                int seat = e.pendKind == E.PEND_NEXT ? 0 : e.pendSeats[0];
                int n = e.BuildLegal(seat);
                if (n == 0) { Check("選択肢がある", false, Canon(e)); stuck = true; break; }
                int idx;
                if (seatStyle[seat] < SqmStdNpc.N_STYLES && e.pendKind != E.PEND_NEXT)
                {
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    idx = npc.Decide(seat, e.HashSeed(seed + ":" + log.Count));
                    long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                    npcTicks += dt; npcCalls++;
                    npcMax = Math.Max(npcMax, dt * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                    if (idx < 0 || idx >= n) { Check("NPC の選択", false, idx.ToString()); idx = 0; }
                }
                else idx = Choose(e, seat, n, rng);
                log.Add((seat, idx));
                if (!e.ApplyIndex(seat, idx)) { Check("操作できる", false, e.lastError); stuck = true; break; }
                Invariants(e, $"試合{mt} 手{log.Count}");
            }
            Check("試合が終わる", !stuck, Canon(e));
            if (stuck) continue;
            for (int s = 0; s < players; s++) if (e.score[s] < 0) { tobi++; break; }
            for (int rk = 0; rk < players; rk++)
            {
                int st = seatStyle[e.mrSeat[rk]];
                styleFinal[st] += e.mrFinal10[rk]; styleN[st]++; styleRank[st] += rk + 1;
            }
            int finSum = e.mrFinal10.Take(players).Sum();
            Check("最終の合計は 0", finSum == 0, string.Join(",", e.mrFinal10));
            string want = Canon(e);

            // 最初から再生
            var r = new E(); r.SetRules(players, rounds); r.NewMatch(seed);
            foreach (var (seat, idx) in log) { AdvanceAll(r); r.ApplyIndex(seat, idx); }
            AdvanceAll(r);
            Check("最初から再生すると同じ", Canon(r) == want, $"\n  {Canon(r)}\n  {want}");
            replays++;
            // 局の始めから復元して再生(最初・真ん中・最後の局)
            foreach (int k in new[] { 0, starts.Count / 2, starts.Count - 1 }.Distinct())
            {
                var (at, ks) = starts[k];
                var c = new E(); c.SetRules(players, rounds);
                c.RestoreKyokuStart(seed, ks);
                for (int i = at; i < log.Count; i++) { AdvanceAll(c); c.ApplyIndex(log[i].seat, log[i].idx); }
                AdvanceAll(c);
                Check("局の始めから復元すると同じ", Canon(c) == want, $"局{k}\n  {Canon(c)}\n  {want}");
                restores++;
            }
        }
        string name = $"{(players == 4 ? "四麻" : "三麻")}{(rounds == 1 ? "東風" : "東南")}{(npcMode == 0 ? "" : npcMode == 1 ? "(簡単な打ち手1人 vs NPC)" : "(NPC どうし)")}";
        Console.WriteLine($"{name}: {matches} 試合・{kyoku} 局 / 和了 {wins}(ツモ {tsumo}・役満 {yakuman}) 流局 {exhaust}(流し満貫 {nagashi}) 途中流局 {aborts}" +
                          $"(九種{abortKinds[E.AB_KYUSHU]} 四風{abortKinds[E.AB_SUUFUU]} 四家立直{abortKinds[E.AB_SUUCHA]} 四槓{abortKinds[E.AB_SUUKAN]} 三家和{abortKinds[E.AB_SANCHA]}) 飛び {tobi} / 再生 {replays}・復元 {restores}");
        if (npcMode > 0)
        {
            string[] sn = { "攻め寄り", "守り寄り", "鳴き好き", "門前派", "簡単な打ち手" };
            var parts = new List<string>();
            for (int st = 0; st <= SqmStdNpc.N_STYLES; st++)
                if (styleN[st] > 0) parts.Add($"{sn[st]}: 平均順位 {(double)styleRank[st] / styleN[st]:F2}・平均 {styleFinal[st] / 10.0 / styleN[st]:+0.0;-0.0}・和了 {styleWin[st] * 100.0 / (kyoku * styleN[st] / (double)matches):F1}%・放銃 {styleDeal[st] * 100.0 / (kyoku * styleN[st] / (double)matches):F1}%");
            foreach (var line in parts) Console.WriteLine("  " + line);
            Console.WriteLine($"  NPC の判断 {npcCalls} 回: 平均 {npcTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency / Math.Max(1, npcCalls):F3} ms・最大 {npcMax:F1} ms");
        }
        Console.WriteLine($"  局の終わりの残り枚数: 平均 {(double)leftSum / Math.Max(1, kyoku):F1} 分布(10枚ごと) {string.Join(",", leftHist)}");
        var top = Enumerable.Range(0, E.N_YAKU).Where(i => yakuCount[i] > 0).OrderByDescending(i => yakuCount[i]).Select(i => $"{new E().YakuNameStatic(i)}{yakuCount[i]}");
        Console.WriteLine("  役: " + string.Join(" ", top));
    }

    static string YakuNameStatic(this E e, int id)
    {
        e.SetRules(4, 2);
        return e.YakuName(id);
    }
}
