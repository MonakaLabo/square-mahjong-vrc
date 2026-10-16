using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// NPC(コンピュータの打ち手)。ブラウザ版 js/cpu(cpu.js・styles.js)の移植。
    /// SqmEngine の状態を読んで、いまの選択肢から1つ選び action(OPT 個の int)に入れる。
    /// 強さ(level)と性格(style)は席ごとに seatLevel / seatStyle で決める。
    /// 同じ局面・同じシードなら、ブラウザ版と同じ手を選ぶ(Tests~ の答え合わせで確かめる)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmNpc : UdonSharpBehaviour
    {
        public SqmEngine engine;

        public const int LV_EASY = 0, LV_NORMAL = 1, LV_STRONG = 2;
        // 性格(ブラウザ版の STYLE_ORDER と同じ順。名前の並び CPU_NAMES とも同じ)
        public const int ST_NONE = -1, ST_DORA = 0, ST_TSUMO = 1, ST_RIICHI = 2, ST_KATA = 3, ST_KAN = 4, ST_YAKUMAN = 5,
            ST_NOTEN = 6, ST_SANYOU = 7, ST_FURIN = 8, ST_SENTE = 9;
        public const int N_STYLES = 10;
        // 性格の中身: リーチ(0=normal 1=always 2=careful 3=never)・鳴き(0=normal 1=never 2=fast)・槓(0=normal 1=never 2=always)
        public const int RI_NORMAL = 0, RI_ALWAYS = 1, RI_CAREFUL = 2, RI_NEVER = 3;
        public const int CALL_NORMAL = 0, CALL_NEVER = 1, CALL_FAST = 2;
        public const int KANP_NORMAL = 0, KANP_NEVER = 1, KANP_ALWAYS = 2;

        [System.NonSerialized] public int[] seatLevel, seatStyle;
        /// <summary>選んだアクション</summary>
        [System.NonSerialized] public int[] action;

        // 性格の表
        private int[] sDora, sHonor, sSanyou, sRiichi, sCall, sKan, sDefense; // sDefense: -1 = 未指定(つよいなら1、ほかは0)
        private bool[] sFlush, sRonWait;

        private const int TOP_MELDS = 12, TOP_PAIRS = 5;
        private const int NT = 47;
        private bool ready;

        private void Init()
        {
            if (ready) return;
            ready = true;
            seatLevel = new int[8]; seatStyle = new int[8];
            for (int i = 0; i < 8; i++) { seatLevel[i] = LV_NORMAL; seatStyle[i] = ST_NONE; }
            action = new int[SqmEngine.OPT];
            sDora = new int[N_STYLES]; sHonor = new int[N_STYLES]; sSanyou = new int[N_STYLES]; sRiichi = new int[N_STYLES];
            sCall = new int[N_STYLES]; sKan = new int[N_STYLES]; sDefense = new int[N_STYLES]; sFlush = new bool[N_STYLES]; sRonWait = new bool[N_STYLES];
            for (int i = 0; i < N_STYLES; i++) sDefense[i] = -1;
            sDora[ST_DORA] = 7;                                                     // ドラ丸: ドラは切らない
            sRiichi[ST_TSUMO] = RI_NEVER; sCall[ST_TSUMO] = CALL_NEVER;             // ツモ子: 門前・黙聴でツモ和了
            sRiichi[ST_RIICHI] = RI_ALWAYS; sDefense[ST_RIICHI] = 0;                // リーチ侍: 聴牌即リーチ、押し切る
            sRonWait[ST_KATA] = true; sCall[ST_KATA] = CALL_FAST;                   // 片ロン姫: ロン牌も使って早く和了る
            sKan[ST_KAN] = KANP_ALWAYS;                                             // カン太郎: 槓できるなら槓
            sFlush[ST_YAKUMAN] = true; sHonor[ST_YAKUMAN] = 1; sDefense[ST_YAKUMAN] = 0; // 役満爺: 染めて大物手、降りない
            sDefense[ST_NOTEN] = 3; sRiichi[ST_NOTEN] = RI_CAREFUL; sCall[ST_NOTEN] = CALL_NEVER; // ノーテン王: 危ない牌は押さない
            sSanyou[ST_SANYOU] = 4;                                                 // 算用博士: 算用数牌を集めて「算数」
            sHonor[ST_FURIN] = 4; sRiichi[ST_FURIN] = RI_NEVER;                     // 風林課長: 字牌を大事に、黙聴で「林の如く」
            sCall[ST_SENTE] = CALL_FAST; sRiichi[ST_SENTE] = RI_ALWAYS;             // 先発隊長: 鳴いて最速

            c = new int[NT]; avail = new int[NT]; spare = new int[NT]; tmp = new int[NT]; visible = new int[NT];
            danger = new double[NT]; passed = new bool[NT];
            mT = new int[128]; mS = new int[128]; mF = new int[128]; mN = 0;
            pT = new int[64]; pS = new int[64]; pF = new int[64]; pN = 0;
            r = new int[NT]; used = new int[NT]; bestUsed = new int[NT];
            chosenI = new int[8]; chosenGot = new int[8]; undoT = new int[128]; undoK = new int[128];
            bestGT = new int[8]; bestGS = new int[8]; bestGGot = new int[8]; bestGSize = new int[8];
            outT = new int[4]; ronIdx = new int[SqmEngine.MAXOPT]; sortTmp = new int[SqmEngine.MAXOPT];
            kinds = new int[NT]; doraSet = new bool[NT];
        }

        public void SetSeat(int seat, int level, int style)
        {
            Init();
            seatLevel[seat] = level; seatStyle[seat] = style;
        }

        // ================================================================ 乱数(ブラウザ版の makeRng / rand と同じ)
        private uint rngS;
        private uint NextU32()
        {
            rngS = rngS + 0x6D2B79F5u;
            uint t = rngS;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return t ^ (t >> 14);
        }

        // ================================================================ 目標の面子(bestTarget)
        private int[] mT, mS, mF; private int mN;   // 面子の候補(種類・開始・埋まり)
        private int[] pT, pS, pF; private int pN;   // 雀頭の候補
        private int[] r, used, bestUsed;
        private int[] chosenI, chosenGot; private int chosenN;
        private int[] undoT, undoK; private int undoN;
        private int[] bestGT, bestGS, bestGGot, bestGSize; private int bestGN;
        private int bestScore, pairMax;
        private const int G_PAIR = 4;

        /// <summary>目標の面子 need 個+雀頭のうち、埋まっている枚数が最大の組合せ(bestScore・bestUsed・bestG*)</summary>
        private int BestTarget(int[] cc, int need)
        {
            mN = 0; pN = 0;
            for (int t = 0; t < NT; t++)
            {
                if (cc[t] >= 2) { mT[mN] = SqmEngine.MT_K; mS[mN] = t; mF[mN] = cc[t] < 6 ? cc[t] : 6; mN++; }
                if (cc[t] >= 1) { pT[pN] = G_PAIR; pS[pN] = t; pF[pN] = cc[t] < 4 ? cc[t] : 4; pN++; }
                if (t < 36 && t % 9 <= 6)
                {
                    int f = Mn(cc[t], 2) + Mn(cc[t + 1], 2) + Mn(cc[t + 2], 2);
                    if (f >= 3) { mT[mN] = SqmEngine.MT_T; mS[mN] = t; mF[mN] = f; mN++; }
                }
                if (t < 36 && t % 9 <= 3)
                {
                    int f = 0;
                    for (int k = 0; k < 6; k++) if (cc[t + k] > 0) f++;
                    if (f >= 3) { mT[mN] = SqmEngine.MT_F; mS[mN] = t; mF[mN] = f; mN++; }
                }
            }
            StableSortDesc(mT, mS, mF, mN);
            StableSortDesc(pT, pS, pF, pN);
            if (mN > TOP_MELDS) mN = TOP_MELDS;
            if (pN > TOP_PAIRS) pN = TOP_PAIRS;
            for (int t = 0; t < NT; t++) { r[t] = cc[t]; used[t] = 0; bestUsed[t] = 0; }
            bestScore = -1; bestGN = 0; chosenN = 0; undoN = 0;
            pairMax = pN > 0 ? pF[0] : 0;
            BtRec(0, need, 0);
            return bestScore;
        }

        private int Mn(int a, int b) { return a < b ? a : b; }

        /// <summary>fill の大きい順に並べる(同じなら元の順。ブラウザ版の安定ソートと同じ結果)</summary>
        private void StableSortDesc(int[] ty, int[] st, int[] fi, int n)
        {
            for (int i = 1; i < n; i++)
            {
                int a = ty[i], b = st[i], f = fi[i];
                int j = i - 1;
                while (j >= 0 && fi[j] < f) { ty[j + 1] = ty[j]; st[j + 1] = st[j]; fi[j + 1] = fi[j]; j--; }
                ty[j + 1] = a; st[j + 1] = b; fi[j + 1] = f;
            }
        }

        private int TakeTile(int t, int n)
        {
            int k = r[t] < n ? r[t] : n;
            r[t] -= k; used[t] += k;
            undoT[undoN] = t; undoK[undoN] = k; undoN++;
            return k;
        }

        private int TakeGroup(int type, int start)
        {
            if (type == SqmEngine.MT_K) return TakeTile(start, 6);
            if (type == G_PAIR) return TakeTile(start, 4);
            if (type == SqmEngine.MT_T) return TakeTile(start, 2) + TakeTile(start + 1, 2) + TakeTile(start + 2, 2);
            int got = 0;
            for (int k = 0; k < 6; k++) got += TakeTile(start + k, 1);
            return got;
        }

        private void Undo(int mark)
        {
            while (undoN > mark)
            {
                undoN--;
                r[undoT[undoN]] += undoK[undoN]; used[undoT[undoN]] -= undoK[undoN];
            }
        }

        private void Record(int score, int pairIdx, int pairGot)
        {
            bestScore = score;
            bestGN = 0;
            for (int j = 0; j < chosenN; j++)
            {
                int i = chosenI[j];
                bestGT[bestGN] = mT[i]; bestGS[bestGN] = mS[i]; bestGGot[bestGN] = chosenGot[j]; bestGSize[bestGN] = 6; bestGN++;
            }
            if (pairIdx >= 0) { bestGT[bestGN] = G_PAIR; bestGS[bestGN] = pS[pairIdx]; bestGGot[bestGN] = pairGot; bestGSize[bestGN] = 4; bestGN++; }
            for (int t = 0; t < NT; t++) bestUsed[t] = used[t];
        }

        [RecursiveMethod]
        private void BtRec(int start, int left, int acc)
        {
            if (left == 0)
            {
                for (int p = 0; p < pN; p++)
                {
                    if (acc + pF[p] <= bestScore) break; // これ以降の雀頭では更新できない
                    int mark = undoN;
                    int got = TakeGroup(G_PAIR, pS[p]);
                    if (acc + got > bestScore) Record(acc + got, p, got);
                    Undo(mark);
                }
                if (pN == 0 && acc > bestScore) Record(acc, -1, 0);
                return;
            }
            for (int i = start; i < mN; i++)
            {
                if (acc + mF[i] * left + pairMax <= bestScore) break; // ここから先の組合せでは更新できない
                int mark = undoN;
                int got = TakeGroup(mT[i], mS[i]);
                chosenI[chosenN] = i; chosenGot[chosenN] = got; chosenN++;
                BtRec(i, left - 1, acc + got);
                chosenN--;
                Undo(mark);
            }
            if (mN == 0 && acc > bestScore) Record(acc, -1, 0);
        }

        // ================================================================ 打牌の評価
        private int[] c, avail, spare, tmp, visible, kinds, outT;
        private double[] danger; private bool[] passed, doraSet;

        private int YakuhaiValue(int t, int seat)
        {
            int v = 0;
            if (engine.IsDragon(t)) v++;
            if (t == engine.SeatWindOf(seat, engine.dealer)) v++;
            if (t == SqmEngine.ROUND_WIND) v++;
            if (t == engine.OppositeWindOf(seat, engine.dealer)) v++;
            return v;
        }

        /// <summary>牌1枚の「残したさ」(小さいほど切る)</summary>
        private int KeepValue(int t, int seat)
        {
            int v = c[t] * 3;
            if (t < 36)
            {
                int b = t - (t % 9);
                for (int d = -2; d <= 2; d++)
                {
                    if (d == 0) continue;
                    int x = t + d;
                    if (x >= b && x < b + 9) v += c[x] * (d == 1 || d == -1 ? 2 : 1);
                }
                if (!engine.IsYaochu(t)) v += 1;
            }
            else v += YakuhaiValue(t, seat) * 2;
            return v;
        }

        private int curStyle, flushSuit;
        private bool hasDanger; private double dWeight;

        private int StyleBonus(int t)
        {
            if (curStyle < 0) return 0;
            int v = 0;
            if (doraSet[t]) v += sDora[curStyle];
            if (sHonor[curStyle] != 0 && t >= 36) v += sHonor[curStyle];
            if (sSanyou[curStyle] != 0 && t >= 27 && t < 36) v += sSanyou[curStyle];
            if (flushSuit >= 0 && t < 36 && t / 9 != flushSuit) v -= 6;
            return v;
        }

        private int Pick(int[] pool, int seat)
        {
            int bt = -1;
            double bv = double.PositiveInfinity;
            for (int t = 0; t < NT; t++)
            {
                if (pool[t] <= 0) continue;
                double v = KeepValue(t, seat) + StyleBonus(t);
                if (hasDanger) v += danger[t] * dWeight;
                if (v < bv) { bv = v; bt = t; }
            }
            return bt;
        }

        /// <summary>count 枚の打牌を選んで outT に入れ、枚数を返す(banned = 喰い替えで切れない牌)</summary>
        private int ChooseDiscard(int seat, int count, int bannedN, int[] bannedArr, int bannedAt, int level)
        {
            for (int t = 0; t < NT; t++) c[t] = engine.handC[seat * NT + t];
            int melds = engine.meldN[seat];
            int need = 4 - melds;
            int score = BestTarget(c, need);
            int defic = 28 - melds * 6 - score;
            curStyle = seatStyle[seat];
            // つよい(と片ロン姫): 聴牌に取れるなら、待ちとロン牌の兼ね合いで一番いい切り方
            if ((level == LV_STRONG || (curStyle >= 0 && sRonWait[curStyle])) && count == 2 && defic <= 4)
            {
                if (BestTenpaiDiscard(seat, bannedN, bannedArr, bannedAt)) return 2;
            }
            // つよい(と守りの性格): リーチ者がいて手が遠いほど、危ない牌を切らない
            int defense = curStyle >= 0 && sDefense[curStyle] >= 0 ? sDefense[curStyle] : (level == LV_STRONG ? 1 : 0);
            hasDanger = defense > 0 && DangerTable(seat);
            dWeight = !hasDanger ? 0 : (defic >= 8 ? 3 : defic >= 5 ? 1.2 : 0.3) * defense;
            // 性格による残したさ
            for (int t = 0; t < NT; t++) doraSet[t] = false;
            if (curStyle >= 0 && sDora[curStyle] != 0)
                for (int i = 0; i < engine.doraIndN; i++) doraSet[engine.DoraFromIndicator(engine.doraInd[i])] = true;
            flushSuit = -1;
            if (curStyle >= 0 && sFlush[curStyle])
            {
                int s0 = 0, s1 = 0, s2 = 0, s3 = 0;
                for (int t = 0; t < 36; t++)
                {
                    int n = c[t];
                    if (t < 9) s0 += n; else if (t < 18) s1 += n; else if (t < 27) s2 += n; else s3 += n;
                }
                int mx = s0; if (s1 > mx) mx = s1; if (s2 > mx) mx = s2; if (s3 > mx) mx = s3;
                if (mx >= 10) flushSuit = s0 == mx ? 0 : s1 == mx ? 1 : s2 == mx ? 2 : 3;
            }
            for (int t = 0; t < NT; t++) avail[t] = c[t];
            for (int i = 0; i < bannedN; i++) avail[bannedArr[bannedAt + i]] = 0;
            for (int t = 0; t < NT; t++)
            {
                int sp = Mn(avail[t], c[t] - bestUsed[t]);
                spare[t] = sp > 0 ? sp : 0;
            }
            int outN = 0;
            while (outN < count)
            {
                int t = Pick(spare, seat);
                if (t < 0)
                {
                    // 目標の中から: いちばん穴の多い面子の牌を切る
                    t = PickFromTargetGroups();
                    if (t < 0) t = Pick(avail, seat);
                }
                if (t < 0) break;
                outT[outN++] = t;
                c[t]--; avail[t]--; spare[t] = spare[t] - 1 > 0 ? spare[t] - 1 : 0;
            }
            if (level == LV_EASY && NextU32() / 4294967296.0 < 0.2)
            {
                // やさしい: 20%でランダムな牌を混ぜる(切れない牌を除いた手牌から)
                int n = 0;
                for (int t = 0; t < NT; t++)
                {
                    bool banned = false;
                    for (int i = 0; i < bannedN; i++) if (bannedArr[bannedAt + i] == t) banned = true;
                    if (!banned) n += engine.handC[seat * NT + t];
                }
                int k = (int)(((ulong)NextU32() * (ulong)n) >> 32);
                for (int t = 0; t < NT && n > 0; t++)
                {
                    bool banned = false;
                    for (int i = 0; i < bannedN; i++) if (bannedArr[bannedAt + i] == t) banned = true;
                    if (banned) continue;
                    int h = engine.handC[seat * NT + t];
                    if (k < h)
                    {
                        // 入れ替えた結果、手牌にある枚数より多く切ることにならないときだけ入れ替える
                        int sameAfter = 1;
                        for (int i = 1; i < outN; i++) if (outT[i] == t) sameAfter++;
                        if (outN > 0 && sameAfter <= engine.handC[seat * NT + t]) outT[0] = t;
                        break;
                    }
                    k -= h;
                }
            }
            return outN;
        }

        /// <summary>目標の面子を「穴の多い順」(同じなら元の順)に見て、切れる牌を探す</summary>
        private int PickFromTargetGroups()
        {
            // 穴(size - got)の大きい順に安定ソートした並びで調べる
            for (int g = 0; g < bestGN; g++) kinds[g] = g;
            for (int i = 1; i < bestGN; i++)
            {
                int x = kinds[i];
                int mx = bestGSize[x] - bestGGot[x];
                int j = i - 1;
                while (j >= 0 && bestGSize[kinds[j]] - bestGGot[kinds[j]] < mx) { kinds[j + 1] = kinds[j]; j--; }
                kinds[j + 1] = x;
            }
            for (int k = 0; k < bestGN; k++)
            {
                int g = kinds[k];
                int ty = bestGT[g], st = bestGS[g];
                int n = ty == SqmEngine.MT_T ? 3 : ty == SqmEngine.MT_F ? 6 : 1;
                for (int i = 0; i < n; i++)
                {
                    int x = st + i;
                    if (avail[x] > 0 && c[x] > 0) return x;
                }
            }
            return -1;
        }

        /// <summary>危険度の表(リーチ者がいなければ false)</summary>
        private bool DangerTable(int seat)
        {
            bool anyThreat = false;
            for (int q = 0; q < 8; q++) if (q != seat && engine.riichiOn[q] && engine.outRole[q] == SqmEngine.ROLE_NONE) anyThreat = true;
            if (!anyThreat) return false;
            for (int t = 0; t < NT; t++) { visible[t] = 0; danger[t] = 0; }
            for (int q = 0; q < 8; q++)
            {
                for (int i = 0; i < engine.riverLen[q]; i++)
                {
                    int ri = q * SqmEngine.MAXRIVER + i;
                    visible[engine.rvT0[ri]]++;
                    if (engine.rvN[ri] == 2) visible[engine.rvT1[ri]]++;
                }
                for (int i = 0; i < engine.meldN[q]; i++)
                {
                    int mi = q * 4 + i;
                    visible[engine.meldStart[mi]] += engine.meldType[mi] == SqmEngine.MT_K ? 6 : 0;
                }
                if (q == seat || engine.outRole[q] != SqmEngine.ROLE_NONE)
                    for (int t = 0; t < NT; t++) visible[t] += engine.handC[q * NT + t];
            }
            for (int i = 0; i < engine.ronTilesLen; i++) if (engine.ronTiles[i] >= 0) visible[engine.ronTiles[i]]++;
            for (int i = 0; i < engine.doraIndN; i++) visible[engine.doraInd[i]]++;
            for (int th = 0; th < 8; th++)
            {
                if (th == seat || !engine.riichiOn[th] || engine.outRole[th] != SqmEngine.ROLE_NONE) continue;
                for (int t = 0; t < NT; t++) passed[t] = false;
                for (int q = 0; q < 8; q++)
                {
                    for (int i = 0; i < engine.riverLen[q]; i++)
                    {
                        int ri = q * SqmEngine.MAXRIVER + i;
                        if (engine.rvSeq[ri] > engine.riichiAt[th] && (engine.rvRonBy[ri] & (1 << th)) == 0)
                        {
                            passed[engine.rvT0[ri]] = true;
                            if (engine.rvN[ri] == 2) passed[engine.rvT1[ri]] = true;
                        }
                    }
                }
                for (int t = 0; t < NT; t++)
                {
                    if (passed[t]) { danger[t] += 0.5; continue; }
                    int u = 28 - visible[t];
                    double unseen = (u > 0 ? u : 0) / 28.0;
                    danger[t] += 2 + 6 * unseen;
                }
            }
            return true;
        }

        /// <summary>ロン牌を見て、和了の組が多く残る切り方(聴牌に取れるときだけ true。outT に2枚)</summary>
        private bool BestTenpaiDiscard(int seat, int bannedN, int[] bannedArr, int bannedAt)
        {
            int kn = 0;
            for (int t = 0; t < NT; t++)
            {
                if (engine.handC[seat * NT + t] == 0) continue;
                bool banned = false;
                for (int i = 0; i < bannedN; i++) if (bannedArr[bannedAt + i] == t) banned = true;
                if (!banned) kinds[kn++] = t;
            }
            if (kn > 30) return false;
            for (int t = 0; t < NT; t++) visible[t] = 0;
            for (int q = 0; q < 8; q++)
            {
                for (int i = 0; i < engine.riverLen[q]; i++)
                {
                    int ri = q * SqmEngine.MAXRIVER + i;
                    if (!engine.rvTaken0[ri]) visible[engine.rvT0[ri]]++;
                    if (engine.rvN[ri] == 2 && !engine.rvTaken1[ri]) visible[engine.rvT1[ri]]++;
                }
                if (q == seat || engine.outRole[q] != SqmEngine.ROLE_NONE)
                    for (int t = 0; t < NT; t++) visible[t] += engine.handC[q * NT + t];
            }
            int need = 4 - engine.meldN[seat];
            int bestA = -1, bestB = -1;
            double bestS = 0;
            for (int i = 0; i < kn; i++)
            {
                for (int j = i; j < kn; j++)
                {
                    int a = kinds[i], b = kinds[j];
                    if (a == b && engine.handC[seat * NT + a] < 2) continue;
                    for (int t = 0; t < NT; t++) tmp[t] = engine.handC[seat * NT + t];
                    tmp[a]--; tmp[b]--;
                    int wn = engine.FindWaits(tmp, need);
                    if (wn == 0) continue;
                    double sc = 0;
                    for (int k = 0; k < wn; k++)
                    {
                        int x = engine.waitA[k], y = engine.waitB[k];
                        int ux = Unseen(x), uy = Unseen(y);
                        // ツモ・二枚ロンの目 + 片ロン(片方がロン牌にあれば、もう片方が切られるだけでよい)
                        sc += ux * uy / 28.0;
                        if (RonHas(x)) sc += uy * 1.5;
                        if (RonHas(y) && x != y) sc += ux * 1.5;
                    }
                    if (sc > bestS) { bestS = sc; bestA = a; bestB = b; }
                }
            }
            if (bestA < 0) return false;
            outT[0] = bestA; outT[1] = bestB;
            return true;
        }

        private int Unseen(int t) { int u = 28 - visible[t]; return u > 0 ? u : 0; }

        private bool RonHas(int t)
        {
            for (int i = 0; i < engine.ronTilesLen; i++) if (engine.ronTiles[i] == t) return true;
            return false;
        }

        /// <summary>和了まで足りない枚数の目安。extra 枚を捨てる前提なら、使っている牌を切るぶんを足す</summary>
        private int Deficiency(int[] cc, int handLen, int meldCount, int extra)
        {
            int fill = BestTarget(cc, 4 - meldCount);
            int unused = handLen - fill;
            int ex = extra - unused;
            return 28 - meldCount * 6 - fill + (ex > 0 ? ex : 0);
        }

        // ================================================================ 判断
        private int[] ronIdx, sortTmp;

        private void Copy(int[] src, int at)
        {
            for (int k = 0; k < SqmEngine.OPT; k++) action[k] = src[at + k];
        }

        /// <summary>
        /// 席 seat の手を選んで action に入れる。seed はブラウザ版と同じく hashSeed(`${試合のシード}:${step}:${席}`)。
        /// 選択肢がなければ false
        /// </summary>
        public bool Decide(int seat, uint seed)
        {
            Init();
            rngS = seed;
            int level = seatLevel[seat];
            int style = seatStyle[seat];
            int n = engine.BuildLegal(seat);
            if (n == 0) return false;
            int[] L = engine.legal;
            const int OPT = SqmEngine.OPT;

            int next = -1, tsumo = -1, disc = -1, abort = -1, firstKan = -1, ankan = -1, pass = -1, ronN = 0;
            for (int i = 0; i < n; i++)
            {
                int ty = L[i * OPT + SqmEngine.O_TYPE];
                if (ty == SqmEngine.A_NEXT && next < 0) next = i;
                else if (ty == SqmEngine.A_TSUMO && tsumo < 0) tsumo = i;
                else if (ty == SqmEngine.A_RON) ronIdx[ronN++] = i;
                else if (ty == SqmEngine.A_DISCARD && disc < 0) disc = i;
                else if (ty == SqmEngine.A_ABORT && abort < 0) abort = i;
                else if (ty == SqmEngine.A_KAN)
                {
                    if (firstKan < 0) firstKan = i;
                    if (ankan < 0 && L[i * OPT + SqmEngine.O_MODE] == SqmEngine.KAN_ANKAN) ankan = i;
                }
                else if (ty == SqmEngine.A_PASS && pass < 0) pass = i;
            }
            if (next >= 0) { Copy(L, next * OPT); return true; }
            // 和了できるときは必ず宣言
            if (tsumo >= 0) { Copy(L, tsumo * OPT); return true; }
            if (ronN > 0)
            {
                V8SortRons(ronN);
                Copy(L, ronIdx[0] * OPT);
                return true;
            }

            // 自分の手番
            if (disc >= 0)
            {
                int db = disc * OPT;
                if (abort >= 0)
                {
                    int yk = 0;
                    for (int t = 0; t < NT; t++) if (engine.handC[seat * NT + t] > 0 && engine.IsYaochu(t)) yk++;
                    if (yk < 17) { Copy(L, abort * OPT); return true; }
                }
                if (firstKan >= 0 && level != LV_EASY && (style < 0 || sKan[style] != KANP_NEVER))
                {
                    Copy(L, (ankan >= 0 ? ankan : firstKan) * OPT);
                    return true;
                }
                ClearAction(seat);
                action[SqmEngine.O_TYPE] = SqmEngine.A_DISCARD;
                if (L[db + SqmEngine.O_FIXEDN] >= 0)
                {
                    int fn = L[db + SqmEngine.O_FIXEDN];
                    action[SqmEngine.O_TAKEN] = fn;
                    for (int k = 0; k < fn; k++) action[SqmEngine.O_TAKE + k] = L[db + SqmEngine.O_FIXED + k];
                    action[SqmEngine.O_MODE] = 0;
                    return true;
                }
                int cnt = ChooseDiscard(seat, L[db + SqmEngine.O_COUNT], L[db + SqmEngine.O_KUIKAEN], L, db + SqmEngine.O_KUIKAE, level);
                action[SqmEngine.O_TAKEN] = cnt;
                for (int k = 0; k < cnt; k++) action[SqmEngine.O_TAKE + k] = outT[k];
                action[SqmEngine.O_MODE] = 0;
                if (L[db + SqmEngine.O_RIICHIOK] == 1 && level != LV_EASY && (style < 0 || sRiichi[style] != RI_NEVER))
                {
                    if (WantRiichi(seat, cnt, level, style)) action[SqmEngine.O_MODE] = 1;
                }
                return true;
            }

            // 打牌への鳴き
            int passBase = pass * OPT;
            if (level == LV_EASY || (style >= 0 && sCall[style] == CALL_NEVER)) { Copy(L, passBase); return true; }
            int callN = 0;
            for (int i = 0; i < n; i++)
            {
                int ty = L[i * OPT + SqmEngine.O_TYPE];
                if (ty == SqmEngine.A_PON || ty == SqmEngine.A_CHI || (ty == SqmEngine.A_KAN && L[i * OPT + SqmEngine.O_MODE] == SqmEngine.KAN_DAIMINKAN)) sortTmp[callN++] = i;
            }
            if (callN == 0) { Copy(L, passBase); return true; }
            // カン太郎: 大明槓できるなら必ず槓する
            if (style >= 0 && sKan[style] == KANP_ALWAYS)
            {
                for (int k = 0; k < callN; k++) if (L[sortTmp[k] * OPT + SqmEngine.O_TYPE] == SqmEngine.A_KAN) { Copy(L, sortTmp[k] * OPT); return true; }
            }
            int melds = engine.meldN[seat];
            bool open = false, hasYakuhaiOpen = false;
            for (int i = 0; i < melds; i++)
            {
                int mi = seat * 4 + i;
                if (engine.meldOpen[mi]) open = true;
                if (engine.meldType[mi] == SqmEngine.MT_K && YakuhaiValue(engine.meldStart[mi], seat) > 0) hasYakuhaiOpen = true;
            }
            int handLen = engine.handLen[seat];
            for (int t = 0; t < NT; t++) tmp[t] = engine.handC[seat * NT + t];
            int before = Deficiency(tmp, handLen, melds, 0);
            int best = -1, bestD = before;
            for (int k = 0; k < callN; k++)
            {
                int ob = sortTmp[k] * OPT;
                bool yh = L[ob + SqmEngine.O_MELDTYPE] == SqmEngine.MT_K && YakuhaiValue(L[ob + SqmEngine.O_START], seat) > 0;
                if (L[ob + SqmEngine.O_TYPE] == SqmEngine.A_KAN && !open) continue;
                // 役が確定するときだけ鳴く。速攻の性格は、手が進むなら鳴いて「侵掠すること火の如く」を目指す
                if (!yh && !hasYakuhaiOpen && !(style >= 0 && sCall[style] == CALL_FAST)) continue;
                for (int t = 0; t < NT; t++) tmp[t] = engine.handC[seat * NT + t];
                int gn = L[ob + SqmEngine.O_GIVEN];
                for (int g = 0; g < gn; g++) tmp[L[ob + SqmEngine.O_GIVE + g]]--;
                int d = Deficiency(tmp, handLen - gn, melds + 1, L[ob + SqmEngine.O_TAKEN]);
                if (d < bestD || (yh && d <= bestD)) { best = sortTmp[k]; bestD = d; }
            }
            Copy(L, best >= 0 ? best * OPT : passBase);
            return true;
        }

        private void ClearAction(int seat)
        {
            for (int k = 0; k < SqmEngine.OPT; k++) action[k] = 0;
            action[SqmEngine.O_SEAT] = seat;
            action[SqmEngine.O_MODE] = -1; action[SqmEngine.O_TILE] = -1; action[SqmEngine.O_RONIDX] = -1; action[SqmEngine.O_RONTILE] = -1;
            action[SqmEngine.O_MELDTYPE] = SqmEngine.MT_NONE; action[SqmEngine.O_START] = -1;
        }

        private bool WantRiichi(int seat, int cnt, int level, int style)
        {
            for (int t = 0; t < NT; t++) tmp[t] = engine.handC[seat * NT + t];
            for (int k = 0; k < cnt; k++) tmp[outT[k]]--;
            int wn = engine.FindWaits(tmp, 4 - engine.meldN[seat]);
            // 自分の河(と今回切る牌)に待ち牌があるか
            for (int t = 0; t < NT; t++) passed[t] = false;
            for (int i = 0; i < engine.riverLen[seat]; i++)
            {
                int ri = seat * SqmEngine.MAXRIVER + i;
                passed[engine.rvT0[ri]] = true;
                if (engine.rvN[ri] == 2) passed[engine.rvT1[ri]] = true;
            }
            for (int k = 0; k < cnt; k++) passed[outT[k]] = true;
            bool furiten = false;
            for (int k = 0; k < engine.waitTileN; k++) if (passed[engine.waitTiles[k]]) furiten = true;
            // 8人目のリーチは八家立直(途中流局)になるだけなので、子は黙聴にする
            bool lastOne = seat != engine.dealer;
            for (int q = 0; q < 8 && lastOne; q++) if (q != seat && !engine.riichiOn[q]) lastOne = false;
            // リーチで増える翻と、失う翻(林の如くが黙聴限定なら1翻)を比べる
            int gain = engine.yakuClosed[SqmEngine.Y_RIICHI];
            int lose = engine.rHayashi == 1 ? engine.yakuClosed[SqmEngine.Y_HAYASHI] : 0;
            int ri2 = style >= 0 ? sRiichi[style] : RI_NORMAL;
            int minWaits = ri2 == RI_ALWAYS ? 1 : ri2 == RI_CAREFUL ? 6 : gain > lose ? 2 : 4;
            return engine.waitTileN >= minWaits && wn > 0 && !lastOne && !(level == LV_STRONG && furiten);
        }

        // ---- ロンの並べ替え: ブラウザ版の rons.sort(...) と同じ結果にする ----
        // 比べ方: 点数の高い順、同点なら二枚ロンが先(片ロン同士の同点は、比べ方が一貫しないので V8 の並べ方をそのまま再現する)
        private int CmpRon(int a, int b)
        {
            int[] L = engine.legal;
            const int OPT = SqmEngine.OPT;
            int d = L[b * OPT + SqmEngine.O_POINTS] - L[a * OPT + SqmEngine.O_POINTS];
            if (d != 0) return d;
            return L[a * OPT + SqmEngine.O_MODE] == SqmEngine.RON_TWO ? -1 : 1;
        }

        /// <summary>V8 の TimSort(要素が少ないときの CountAndMakeRun + BinaryInsertionSort)</summary>
        private void V8SortRons(int n)
        {
            if (n < 2) return;
            // CountAndMakeRun(0, n)
            int runLength = 2;
            if (n > 1)
            {
                bool desc = CmpRon(ronIdx[1], ronIdx[0]) < 0;
                int prev = ronIdx[1];
                for (int i = 2; i < n; i++)
                {
                    int cur = ronIdx[i];
                    int order = CmpRon(cur, prev);
                    if (desc) { if (order >= 0) break; }
                    else { if (order < 0) break; }
                    prev = cur;
                    runLength++;
                }
                if (desc)
                {
                    for (int i = 0, j = runLength - 1; i < j; i++, j--) { int x = ronIdx[i]; ronIdx[i] = ronIdx[j]; ronIdx[j] = x; }
                }
            }
            // BinaryInsertionSort(0, runLength, n)
            for (int start = runLength; start < n; start++)
            {
                int left = 0, right = start;
                int pivot = ronIdx[start];
                while (left < right)
                {
                    int mid = left + ((right - left) >> 1);
                    if (CmpRon(pivot, ronIdx[mid]) < 0) right = mid; else left = mid + 1;
                }
                for (int p = start; p > left; p--) ronIdx[p] = ronIdx[p - 1];
                ronIdx[left] = pivot;
            }
        }
    }
}
