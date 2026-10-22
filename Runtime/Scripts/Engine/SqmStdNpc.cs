using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 普通の麻雀(SqmStdEngine)の NPC。強さは「ふつう」1種類(ネット麻雀の初段〜中級者くらい)。
    ///   打牌: 向聴数 → 受け入れ枚数 → 牌の価値(ドラ・役牌の対子は残す)の順で選ぶ。
    ///   押し引き: だれかのリーチに、手が遠ければ安全な牌(現物・筋・字牌)でオリる。聴牌したらリーチ。
    ///   鳴き: 役が残る(役牌・タンヤオ・染め手)うえで、向聴数が進むときだけ。
    /// 性格(席ごと)は押し引きと鳴きの好みだけを変える: 攻め寄り / 守り寄り / 鳴き好き / 門前派。
    /// Decide(seat, seed) で、engine.BuildLegal(seat) の何番目を選ぶかを返す(同じ局面・同じ seed なら同じ答え)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmStdNpc : UdonSharpBehaviour
    {
        public SqmStdEngine engine;

        public const int ST_ATTACK = 0, ST_DEFENSE = 1, ST_CALLER = 2, ST_MENZEN = 3;
        public const int N_STYLES = 4;
        [System.NonSerialized] public int[] seatStyle;

        private const int NK = SqmStdEngine.NK, NC = SqmStdEngine.NC, OPT = SqmStdEngine.OPT;
        private bool ready;
        private int[] c, visible, kSh, kUk;
        private bool[] doraK;

        private void Init()
        {
            if (ready) return;
            ready = true;
            seatStyle = new int[SqmStdEngine.MAXP];
            c = new int[NK]; visible = new int[NK]; kSh = new int[NK]; kUk = new int[NK];
            doraK = new bool[NK];
        }

        public void SetSeat(int seat, int style)
        {
            Init();
            seatStyle[seat] = style;
        }

        public string StyleName(int style)
        {
            if (style == ST_ATTACK) return "攻め寄り";
            if (style == ST_DEFENSE) return "守り寄り";
            if (style == ST_CALLER) return "鳴き好き";
            return "門前派";
        }

        // ================================================================ 乱数(引き分けを崩すだけ)
        private uint rngS;
        private int Rand(int n)
        {
            rngS = rngS + 0x6D2B79F5u;
            uint t = rngS;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            t ^= t >> 14;
            return (int)(((ulong)t * (ulong)n) >> 32);
        }

        // ================================================================ 向聴数
        // 色ごと(萬・筒・索、9枚の並び)に「面子 m 個・雀頭 p 個(0/1)のとき、塔子は最大いくつ作れるか」を求めて覚えておき(同じ形はもう探さない)、
        // 4つ(3色 + 字牌)を組み合わせて 向聴数 = 8 - 2×面子 - 塔子(面子と合わせて4まで) - 雀頭 の最小を出す。
        // 色の表は 2×5 マスを 3 ビットずつ(7 = 作れない)にまとめた int。覚えておく所は自前の小さなハッシュ表
        private const int HASH = 8192;
        private int[] hKey, hVal;
        private int[] st, gt, cur, nxt;          // st: 1色の作業用 / gt: 1つの組の表 / cur・nxt: 組み合わせの途中
        private int[] grp, ex;                   // 受け入れ: 4つの組の表 / その組を除いた3つを組み合わせたもの

        private void EnsureTables()
        {
            if (hKey != null) return;
            hKey = new int[HASH]; hVal = new int[HASH];
            for (int i = 0; i < HASH; i++) hKey[i] = -1;
            st = new int[9]; gt = new int[10]; cur = new int[10]; nxt = new int[10];
            grp = new int[40]; ex = new int[40];
        }

        /// <summary>向聴数(普通の形・七対子・国士の小さい方)。melds = 副露の数。-1 = 和了</summary>
        public int Shanten(int[] h, int melds)
        {
            EnsureTables();
            // cur[p*5 + m] = 塔子の最大(-1 = できない)
            for (int i = 0; i < 10; i++) cur[i] = -1;
            cur[0] = 0;
            for (int g = 0; g < 4; g++) { LoadGroup(h, g); Combine(); }
            int sh = FinishShanten(melds);
            if (melds == 0) { int sp = Special(h); if (sp < sh) sh = sp; }
            return sh;
        }

        /// <summary>組 g(0〜2 = 萬・筒・索、3 = 字牌)の表を gt に入れる</summary>
        private void LoadGroup(int[] h, int g)
        {
            if (g < 3)
            {
                int packed = SuitTable(h, g * 9);
                for (int i = 0; i < 10; i++) { int v = (packed >> (i * 3)) & 7; gt[i] = v == 7 ? -1 : v; }
                return;
            }
            // 字牌: 3枚以上 = 面子、ちょうど2枚 = 塔子か雀頭
            int hm = 0, h2 = 0;
            for (int k = 27; k < NK; k++) { if (h[k] >= 3) hm++; else if (h[k] == 2) h2++; }
            if (hm > 4) hm = 4;
            for (int i = 0; i < 10; i++) gt[i] = -1;
            gt[hm] = h2;
            if (h2 > 0) gt[5 + hm] = h2 - 1;
            if (hm > 0 && gt[5 + hm - 1] < h2) gt[5 + hm - 1] = h2;   // 刻子を雀頭にする
        }

        /// <summary>組み合わせ終わった cur から、普通の形の向聴数</summary>
        private int FinishShanten(int melds)
        {
            int need = 4 - melds;
            int b = 8;
            for (int p = 0; p < 2; p++)
                for (int m = 0; m <= 4; m++)
                {
                    int t = cur[p * 5 + m];
                    if (t < 0) continue;
                    int mm = m > need ? need : m;
                    int tt = t < need - mm ? t : need - mm;
                    int sh = 8 - 2 * (mm + melds) - tt - p;
                    if (sh < b) b = sh;
                }
            return b;
        }

        /// <summary>七対子・国士の向聴数の小さい方(副露が無いときだけ使う)</summary>
        private int Special(int[] h)
        {
            int pairs = 0, kinds = 0, yao = 0, yaoPair = 0;
            for (int k = 0; k < NK; k++)
            {
                if (h[k] >= 2) pairs++;
                if (h[k] >= 1) kinds++;
                bool y = engine.IsYaochuK(k);
                if (y && h[k] >= 1) yao++;
                if (y && h[k] >= 2) yaoPair = 1;
            }
            int ct = 6 - pairs + (kinds < 7 ? 7 - kinds : 0);
            int ks = 13 - yao - yaoPair;
            return ct < ks ? ct : ks;
        }

        /// <summary>cur と gt を組み合わせる(雀頭は合わせて1つまで、面子は4まで)</summary>
        private void Combine()
        {
            for (int i = 0; i < 10; i++) nxt[i] = -1;
            for (int p1 = 0; p1 < 2; p1++)
                for (int m1 = 0; m1 <= 4; m1++)
                {
                    int t1 = cur[p1 * 5 + m1];
                    if (t1 < 0) continue;
                    for (int p2 = 0; p1 + p2 < 2; p2++)
                        for (int m2 = 0; m2 <= 4; m2++)
                        {
                            int t2 = gt[p2 * 5 + m2];
                            if (t2 < 0) continue;
                            int m = m1 + m2; if (m > 4) m = 4;
                            int t = t1 + t2; if (t > 4) t = 4;
                            int at = (p1 + p2) * 5 + m;
                            if (t > nxt[at]) nxt[at] = t;
                        }
                }
            for (int i = 0; i < 10; i++) cur[i] = nxt[i];
        }

        /// <summary>1色の表(覚えていれば、それを返す)</summary>
        private int SuitTable(int[] h, int b)
        {
            int key = 0;
            for (int i = 0; i < 9; i++) key = key * 5 + h[b + i];
            int slot = (key * 31 + (key >> 9)) & (HASH - 1);
            for (int probe = 0; probe < 16; probe++)
            {
                int at = (slot + probe) & (HASH - 1);
                if (hKey[at] == key) return hVal[at];
                if (hKey[at] == -1)
                {
                    int v = ComputeSuit(h, b);
                    hKey[at] = key; hVal[at] = v;
                    return v;
                }
            }
            return ComputeSuit(h, b);   // 表がいっぱい(覚えずに数える)
        }

        private int ComputeSuit(int[] h, int b)
        {
            for (int i = 0; i < 9; i++) st[i] = h[b + i];
            for (int i = 0; i < 10; i++) gt[i] = -1;
            SuitRec(0, 0, 0, 0);
            int packed = 0;
            for (int i = 0; i < 10; i++) packed |= (gt[i] < 0 ? 7 : gt[i]) << (i * 3);
            return packed;
        }

        /// <summary>1色の中で、面子・塔子・雀頭の取り方を全部ためす</summary>
        [RecursiveMethod]
        private void SuitRec(int i, int m, int t, int p)
        {
            while (i < 9 && st[i] == 0) i++;
            if (i >= 9)
            {
                int mm = m > 4 ? 4 : m, tt = t > 4 ? 4 : t;
                int at = p * 5 + mm;
                if (tt > gt[at]) gt[at] = tt;
                return;
            }
            if (st[i] >= 3) { st[i] -= 3; SuitRec(i, m + 1, t, p); st[i] += 3; }
            if (i <= 6 && st[i + 1] > 0 && st[i + 2] > 0) { st[i]--; st[i + 1]--; st[i + 2]--; SuitRec(i, m + 1, t, p); st[i]++; st[i + 1]++; st[i + 2]++; }
            if (st[i] >= 2)
            {
                st[i] -= 2;
                if (p == 0) SuitRec(i, m, t, 1);
                SuitRec(i, m, t + 1, p);
                st[i] += 2;
            }
            if (i <= 7 && st[i + 1] > 0) { st[i]--; st[i + 1]--; SuitRec(i, m, t + 1, p); st[i]++; st[i + 1]++; }
            if (i <= 6 && st[i + 2] > 0) { st[i]--; st[i + 2]--; SuitRec(i, m, t + 1, p); st[i]++; st[i + 2]++; }
            int save = st[i];
            st[i] = 0;
            SuitRec(i + 1, m, t, p);
            st[i] = save;
        }

        // ================================================================ 場の見えている牌
        private void CountVisible(int seat)
        {
            SqmStdEngine e = engine;
            for (int k = 0; k < NK; k++) { visible[k] = 0; doraK[k] = false; }
            for (int t = 0; t < NC; t++) visible[e.KindOf(t)] += e.handC[seat * NC + t];
            for (int s = 0; s < e.NP; s++)
            {
                for (int i = 0; i < e.riverN[s]; i++)
                {
                    int o = s * SqmStdEngine.MAXRV + i;
                    if ((e.rvF[o] & SqmStdEngine.RV_CALLED) == 0) visible[e.KindOf(e.rvC[o])]++;
                }
                for (int i = 0; i < e.meldN[s]; i++)
                {
                    int mi = s * 4 + i;
                    AddVis(e.mT0[mi]); AddVis(e.mT1[mi]); AddVis(e.mT2[mi]); AddVis(e.mT3[mi]);
                }
                visible[SqmStdEngine.NORTH] += e.kitaN[s];
            }
            for (int i = 0; i < e.doraN; i++)
            {
                int ind = e.KindOf(e.DoraIndicator(i));
                visible[ind]++;
                doraK[e.DoraOf(ind)] = true;
            }
        }

        private void AddVis(int code) { if (code >= 0) visible[engine.KindOf(code)]++; }

        private int Remaining(int k)
        {
            int r = 4 - visible[k];
            return r > 0 ? r : 0;
        }

        /// <summary>受け入れ枚数: c(3m+1 枚)に1枚足して向聴数が下がる牌の残り枚数</summary>
        private int Ukeire(int melds, int sh)
        {
            EnsureTables();
            // 1枚足して変わるのは、その牌の組(色か字牌)の表だけ。ほかの3組を組み合わせたもの(ex)を先に作っておく
            for (int g = 0; g < 4; g++) { LoadGroup(c, g); for (int i = 0; i < 10; i++) grp[g * 10 + i] = gt[i]; }
            for (int g = 0; g < 4; g++)
            {
                for (int i = 0; i < 10; i++) cur[i] = -1;
                cur[0] = 0;
                for (int g2 = 0; g2 < 4; g2++)
                {
                    if (g2 == g) continue;
                    for (int i = 0; i < 10; i++) gt[i] = grp[g2 * 10 + i];
                    Combine();
                }
                for (int i = 0; i < 10; i++) ex[g * 10 + i] = cur[i];
            }
            int u = 0;
            for (int k = 0; k < NK; k++)
            {
                if (!engine.KindUsed(k) || c[k] >= 4) continue;
                if (!Near(k)) continue;
                int g = k < 27 ? k / 9 : 3;
                c[k]++;
                LoadGroup(c, g);
                for (int i = 0; i < 10; i++) cur[i] = ex[g * 10 + i];
                Combine();
                int s2 = FinishShanten(melds);
                if (melds == 0) { int sp = Special(c); if (sp < s2) s2 = sp; }
                c[k]--;
                if (s2 < sh) u += Remaining(k);
            }
            return u;
        }

        private bool Near(int k)
        {
            if (c[k] > 0) return true;
            if (k >= 27) return engine.IsYaochuK(k);   // 国士の受け入れ
            int n = k % 9;
            for (int d = -2; d <= 2; d++)
            {
                int q = n + d;
                if (d != 0 && q >= 0 && q <= 8 && c[k + d] > 0) return true;
            }
            return engine.IsYaochuK(k);
        }

        // ================================================================ 危険度
        private bool AnyRiichi(int seat)
        {
            for (int s = 0; s < engine.NP; s++) if (s != seat && engine.riichiOn[s]) return true;
            return false;
        }

        /// <summary>その種類を切ったときの危険度(リーチしている人それぞれの最大)。0 = 現物</summary>
        private int Danger(int seat, int k)
        {
            SqmStdEngine e = engine;
            int worst = 0;
            for (int s = 0; s < e.NP; s++)
            {
                if (s == seat || !e.riichiOn[s]) continue;
                int d = DangerFor(s, k);
                if (d > worst) worst = d;
            }
            if (doraK[k] && worst > 0) worst += 2;
            return worst;
        }

        private bool InRiver(int s, int k)
        {
            SqmStdEngine e = engine;
            for (int i = 0; i < e.riverN[s]; i++) if (e.KindOf(e.rvC[s * SqmStdEngine.MAXRV + i]) == k) return true;
            return false;
        }

        /// <summary>立直のあとにほかの人が切って、その人が和了らなかった牌(見逃しの現物)</summary>
        private bool PassedAfterRiichi(int s, int k)
        {
            SqmStdEngine e = engine;
            int ri = -1;
            for (int i = 0; i < e.riverN[s]; i++) if ((e.rvF[s * SqmStdEngine.MAXRV + i] & SqmStdEngine.RV_RIICHI) != 0) { ri = i; break; }
            if (ri < 0) return false;
            // 立直宣言のあと、自分より先に切られた牌は捨て牌の並びで数えられないので、立直後のその人の河の数で近い分だけ見る
            for (int p = 0; p < e.NP; p++)
            {
                if (p == s) continue;
                int from = ri - 1 < 0 ? 0 : ri - 1;
                for (int i = from; i < e.riverN[p]; i++) if (e.KindOf(e.rvC[p * SqmStdEngine.MAXRV + i]) == k) return true;
            }
            return false;
        }

        private int DangerFor(int s, int k)
        {
            if (InRiver(s, k) || PassedAfterRiichi(s, k)) return 0;
            if (k >= 27)
            {
                if (visible[k] >= 3) return 1;
                if (visible[k] == 2) return 2;
                return 4;
            }
            int n = k % 9;
            bool sujiLo = n >= 3 && InRiver(s, k - 3);
            bool sujiHi = n <= 5 && InRiver(s, k + 3);
            bool suji;
            if (n <= 2) suji = sujiHi;
            else if (n >= 6) suji = sujiLo;
            else suji = sujiLo && sujiHi;
            if (suji) return (n == 0 || n == 8) ? 2 : 3;
            if (n == 0 || n == 8) return 5;
            if (n == 1 || n == 7) return 6;
            return 8;
        }

        // ================================================================ 決める
        /// <summary>Begin / Continue が「まだ考えている」ときに返す値</summary>
        public const int THINKING = -2;

        /// <summary>席 seat の選択肢から1つ選び、その番号を返す(選択肢がなければ -1)。全部をいっぺんに考える</summary>
        public int Decide(int seat, uint seed)
        {
            int r = Begin(seat, seed);
            while (r == THINKING) r = Continue();
            return r;
        }

        // 打牌を考えている途中の状態(Begin で作り、Continue で受け入れを1種類ずつ数える)
        private int dSeat = -1, dStyle, dMelds, dMinSh, dKN, dKPos;
        private bool dFold, dThreat;
        private int[] dKinds;

        /// <summary>
        /// 考え始める。すぐ決まれば選択肢の番号(なければ -1)、打牌の受け入れを数える必要があれば THINKING を返す。
        /// THINKING のときは、盤面を変えずに Continue を呼び続ける(Udon で1フレームに収まるよう、受け入れは1回に1種類ずつ数える)
        /// </summary>
        public int Begin(int seat, uint seed)
        {
            Init();
            dSeat = -1;
            rngS = seed;
            SqmStdEngine e = engine;
            int n = e.BuildLegal(seat);
            if (n == 0) return -1;
            int[] L = e.legal;
            int style = seatStyle[seat];
            int next = -1, win = -1, kyushu = -1, kita = -1, pass = -1;
            for (int i = 0; i < n; i++)
            {
                int ty = L[i * OPT + SqmStdEngine.O_TYPE];
                if (ty == SqmStdEngine.A_NEXT) next = i;
                else if (ty == SqmStdEngine.A_TSUMO || ty == SqmStdEngine.A_RON) win = i;
                else if (ty == SqmStdEngine.A_KYUSHU) kyushu = i;
                else if (ty == SqmStdEngine.A_KITA) kita = i;
                else if (ty == SqmStdEngine.A_PASS) pass = i;
            }
            if (next >= 0) return next;
            if (win >= 0) return win;
            CountVisible(seat);
            if (e.phase == SqmStdEngine.PH_CALLS) return DecideCall(seat, n, pass, style);
            if (kita >= 0) return kita;
            e.KindCounts(seat, c);
            int melds = e.meldN[seat];
            if (kyushu >= 0)
            {
                int yk = 0;
                for (int k = 0; k < NK; k++) if (engine.IsYaochuK(k) && c[k] > 0) yk++;
                if (yk < 11) return kyushu;   // 国士を狙えるほど揃っていなければ流す
            }
            int sh = Shanten(c, melds);
            // 暗槓・加槓
            for (int i = 0; i < n; i++)
            {
                int ty = L[i * OPT + SqmStdEngine.O_TYPE];
                if (ty != SqmStdEngine.A_ANKAN && ty != SqmStdEngine.A_KAKAN) continue;
                if (e.riichiOn[seat]) return i;   // 立直中の暗槓は待ちが変わらないときだけ出てくる
                if (AnyRiichi(seat) && sh > 0) continue;
                int k = ty == SqmStdEngine.A_ANKAN ? L[i * OPT + SqmStdEngine.O_TILE] : e.KindOf(L[i * OPT + SqmStdEngine.O_TILE]);
                int take = ty == SqmStdEngine.A_ANKAN ? 4 : 1;
                c[k] -= take;
                int s2 = Shanten(c, melds + (ty == SqmStdEngine.A_ANKAN ? 1 : 0));
                c[k] += take;
                if (s2 <= sh && (ty == SqmStdEngine.A_ANKAN || !AnyRiichi(seat))) return i;
            }
            return BeginDiscard(seat, n, style);
        }

        /// <summary>打牌: 候補ごとの向聴数を数え、受け入れを数える種類を並べる</summary>
        private int BeginDiscard(int seat, int n, int style)
        {
            SqmStdEngine e = engine;
            int[] L = e.legal;
            int melds = e.meldN[seat];
            dThreat = AnyRiichi(seat);
            int foldAt = style == ST_ATTACK ? 3 : (style == ST_DEFENSE ? 1 : 2);
            int minSh = 99;
            // 向聴数と受け入れは種類ごとに1回だけ数える(赤5 と普通の 5 は同じ)
            for (int k = 0; k < NK; k++) { kSh[k] = -9; kUk[k] = -1; }
            for (int i = 0; i < n; i++)
            {
                if (L[i * OPT + SqmStdEngine.O_TYPE] != SqmStdEngine.A_DISCARD || L[i * OPT + SqmStdEngine.O_RIICHI] != 0) continue;
                int k = e.KindOf(L[i * OPT + SqmStdEngine.O_TILE]);
                if (kSh[k] != -9) continue;
                c[k]--;
                kSh[k] = Shanten(c, melds);
                c[k]++;
                if (kSh[k] < minSh) minSh = kSh[k];
            }
            // 受け入れは、いちばん小さい向聴数になる種類だけ数える(重いので)
            if (dKinds == null) dKinds = new int[NK];
            dKN = 0;
            for (int k = 0; k < NK; k++)
            {
                if (kSh[k] == -9) continue;
                if (kSh[k] == minSh) dKinds[dKN++] = k;
                else kUk[k] = 0;
            }
            dSeat = seat; dStyle = style; dMelds = melds; dMinSh = minSh; dKPos = 0;
            dFold = dThreat && minSh >= foldAt;
            return dKN > 0 ? THINKING : FinishDiscard();
        }

        /// <summary>考えの続き: 受け入れを1種類数える。全部数え終わったら打牌を決めて、その番号を返す</summary>
        public int Continue()
        {
            if (dSeat < 0) return -1;
            if (dKPos < dKN)
            {
                int k = dKinds[dKPos++];
                c[k]--;
                kUk[k] = Ukeire(dMelds, kSh[k]);
                c[k]++;
                if (dKPos < dKN) return THINKING;
            }
            return FinishDiscard();
        }

        private int FinishDiscard()
        {
            SqmStdEngine e = engine;
            int seat = dSeat, style = dStyle, minSh = dMinSh;
            bool threat = dThreat, fold = dFold;
            dSeat = -1;
            int n = e.BuildLegal(seat);   // 考えている間にほかの所で使われたかもしれないので、作り直す(盤面は同じ)
            int[] L = e.legal;
            int bestI = -1, bestScore = int.MinValue, bestSh = 99;
            int riichiI = -1;
            for (int i = 0; i < n; i++)
            {
                int o = i * OPT;
                if (L[o + SqmStdEngine.O_TYPE] != SqmStdEngine.A_DISCARD || L[o + SqmStdEngine.O_RIICHI] != 0) continue;
                int code = L[o + SqmStdEngine.O_TILE];
                int k = e.KindOf(code);
                int s2 = kSh[k];
                int uk = kUk[k] > 0 ? kUk[k] : 0;
                int score;
                if (fold) score = -Danger(seat, k) * 10000 - s2 * 100 + uk;
                else
                {
                    score = -s2 * 10000 + uk * 20 - TileValue(seat, k, code);
                    if (threat) score -= Danger(seat, k) * (minSh == 0 ? 2 : 40);
                }
                score = score * 4 + Rand(4);
                if (score > bestScore) { bestScore = score; bestI = i; bestSh = s2; }
            }
            if (bestI < 0) return 0;
            // 聴牌して門前なら立直(同じ牌の立直の選択肢があれば)
            if (bestSh == 0 && !fold)
            {
                int bestCode = L[bestI * OPT + SqmStdEngine.O_TILE];
                for (int i = 0; i < n; i++)
                {
                    int o = i * OPT;
                    if (L[o + SqmStdEngine.O_TYPE] == SqmStdEngine.A_DISCARD && L[o + SqmStdEngine.O_RIICHI] == 1 && L[o + SqmStdEngine.O_TILE] == bestCode) riichiI = i;
                }
                if (riichiI >= 0 && WaitsLeft(seat, bestCode) > 0 && !(style == ST_DEFENSE && threat)) return riichiI;
            }
            return bestI;
        }

        /// <summary>その牌を切ったあとの待ちの残り枚数</summary>
        private int WaitsLeft(int seat, int code)
        {
            SqmStdEngine e = engine;
            int k = e.KindOf(code);
            c[k]--;
            int w = e.FindWaits(c, 4 - e.meldN[seat]);
            c[k]++;
            int left = 0;
            for (int i = 0; i < w; i++) left += Remaining(e.waitK[i]);
            return left;
        }

        /// <summary>切るのが惜しい度合い(大きいほど残す)</summary>
        private int TileValue(int seat, int k, int code)
        {
            SqmStdEngine e = engine;
            int v = 0;
            if (doraK[k]) v += 60;
            if (e.IsRed(code)) v += 60;
            if (k >= 27)
            {
                bool value = e.IsDragonK(k) || k == e.SeatWind(seat) || k == e.RoundWind();
                if (value && c[k] >= 2) v += 80;
                else if (value && Remaining(k) >= 2) v += 10;
            }
            else
            {
                int n = k % 9;
                if (n >= 2 && n <= 6) v += 6;   // 真ん中の牌は少し残す
            }
            return v;
        }

        // ================================================================ 鳴き
        private int DecideCall(int seat, int n, int pass, int style)
        {
            SqmStdEngine e = engine;
            int[] L = e.legal;
            e.KindCounts(seat, c);
            int melds = e.meldN[seat];
            int sh = Shanten(c, melds);
            bool threat = AnyRiichi(seat);
            if (threat && sh >= 2) return pass;
            int bestI = pass, bestSh = sh;
            for (int i = 0; i < n; i++)
            {
                int o = i * OPT;
                int ty = L[o + SqmStdEngine.O_TYPE];
                if (ty != SqmStdEngine.A_PON && ty != SqmStdEngine.A_CHI && ty != SqmStdEngine.A_MINKAN) continue;
                int k = e.KindOf(L[o + SqmStdEngine.O_TILE]);
                bool valuePon = ty != SqmStdEngine.A_CHI && IsValueKind(seat, k);
                if (style == ST_MENZEN && e.IsMenzen(seat) && !valuePon) continue;
                if (ty == SqmStdEngine.A_MINKAN)
                {
                    // 大明槓は、もう鳴いていて役がある手で、向聴数が下がらないときだけ
                    if (e.IsMenzen(seat) || threat || !HasYakuPath(seat, k, k, k)) continue;
                    c[k] -= 3;
                    int sk = Shanten(c, melds + 1);
                    c[k] += 3;
                    if (sk <= sh && sk <= bestSh) { bestSh = sk; bestI = i; }
                    continue;
                }
                int ka = e.KindOf(L[o + SqmStdEngine.O_A]), kb = e.KindOf(L[o + SqmStdEngine.O_B]);
                if (!valuePon && !HasYakuPath(seat, k, ka, kb)) continue;
                c[ka]--; c[kb]--;
                // 鳴いたあと1枚切る: いちばん良い打牌の向聴数
                int after = 99;
                for (int d = 0; d < NK; d++)
                {
                    if (c[d] == 0 || d == k) continue;
                    c[d]--;
                    int s2 = Shanten(c, melds + 1);
                    c[d]++;
                    if (s2 < after) after = s2;
                }
                c[ka]++; c[kb]++;
                bool good = after < sh || (style == ST_CALLER && after <= sh && sh <= 2) || (valuePon && after <= sh);
                if (good && after < bestSh + (bestI == pass ? 1 : 0)) { bestSh = after; bestI = i; }
            }
            return bestI;
        }

        private bool IsValueKind(int seat, int k)
        {
            return engine.IsDragonK(k) || k == engine.SeatWind(seat) || k == engine.RoundWind();
        }

        /// <summary>鳴いても役が残りそうか(役牌の刻子がある / タンヤオ / 染め手)</summary>
        private bool HasYakuPath(int seat, int called, int ka, int kb)
        {
            SqmStdEngine e = engine;
            for (int i = 0; i < e.meldN[seat]; i++)
            {
                int mi = seat * 4 + i;
                if (e.mType[mi] != SqmStdEngine.M_CHI && IsValueKind(seat, e.mKind[mi])) return true;
            }
            for (int k = 27; k < NK; k++) if (c[k] >= 2 && IsValueKind(seat, k)) return true;   // 役牌の対子がある
            // タンヤオ: 鳴く牌と副露がすべて中張牌で、手の么九牌が2枚まで
            bool simple = !e.IsYaochuK(called) && !e.IsYaochuK(ka) && !e.IsYaochuK(kb);
            for (int i = 0; i < e.meldN[seat] && simple; i++)
            {
                int mi = seat * 4 + i;
                int mk = e.mKind[mi];
                if (e.IsYaochuK(mk) || (e.mType[mi] == SqmStdEngine.M_CHI && e.IsYaochuK(mk + 2))) simple = false;
            }
            if (simple)
            {
                int yao = 0;
                for (int k = 0; k < NK; k++) if (e.IsYaochuK(k)) yao += c[k];
                if (yao <= 2 && e.rKuitan) return true;
            }
            // 染め手: 1色と字牌で、ほかの色が2枚まで
            int[] su = new int[3];
            int total = 0;
            for (int k = 0; k < 27; k++) { su[k / 9] += c[k]; total += c[k]; }
            int main = called < 27 ? called / 9 : -1;
            if (main < 0) { main = 0; if (su[1] > su[main]) main = 1; if (su[2] > su[main]) main = 2; }
            if ((ka < 27 && ka / 9 != main) || (kb < 27 && kb / 9 != main)) return false;
            for (int i = 0; i < e.meldN[seat]; i++)
            {
                int mk = e.mKind[seat * 4 + i];
                if (mk < 27 && mk / 9 != main) return false;
            }
            return total - su[main] <= 2;
        }
    }
}
