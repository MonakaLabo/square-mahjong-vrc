using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 普通の麻雀(リーチ麻雀)のルールエンジン。四麻・三麻、東風戦・東南戦。平方麻雀の SqmEngine とは別。
    /// UdonSharp で動くように、状態はすべて数値の配列で持つ(独自のクラス・構造体・ジェネリクス・ラムダ・例外・再帰は使わない)。
    /// 同じシードと同じ操作なら、だれの手元でも同じ結果になる。
    ///
    /// 使い方: SetRules(人数, 1=東風/2=東南) → NewMatch(seed) → 以降 pendKind を見て
    ///   PEND_NONE なら Advance()、それ以外は BuildLegal(seat) で選択肢(legal に OPT 個ずつ)を得て ApplyIndex(seat, i) か ApplyAction(act)。
    ///
    /// 牌の番号(コード): 0-8 萬子 / 9-17 筒子 / 18-26 索子 / 27-30 東南西北 / 31-33 白發中 / 34-36 赤5萬・赤5筒・赤5索。
    /// 種類(kind)は 0-33 で、赤5 は普通の 5 と同じ種類。三麻は 2萬〜8萬 を使わない。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmStdEngine : UdonSharpBehaviour
    {
        // ================================================================ 定数
        public const int MAXP = 4;        // 人数の上限
        public const int NK = 34;         // 牌の種類
        public const int NC = 37;         // 牌のコード(赤5 を含む)
        public const int RED0 = 34;       // 赤5萬のコード(+1 で筒子、+2 で索子)
        public const int EAST = 27, SOUTH = 28, WEST = 29, NORTH = 30, HAKU = 31, HATSU = 32, CHUN = 33;

        // 局面
        public const int PH_DEAL = 0, PH_DRAW = 1, PH_TURN = 2, PH_CALLS = 3, PH_KYOKU_OVER = 4, PH_MATCH_OVER = 5;
        // 入力待ち
        public const int PEND_NONE = 0, PEND_TURN = 1, PEND_CALLS = 2, PEND_NEXT = 3, PEND_END = 4;
        // アクション
        public const int A_DISCARD = 1, A_TSUMO = 2, A_ANKAN = 3, A_KAKAN = 4, A_KITA = 5, A_KYUSHU = 6,
            A_PASS = 7, A_RON = 8, A_PON = 9, A_CHI = 10, A_MINKAN = 11, A_NEXT = 12;
        // アクション/選択肢のレコード(int の並び)。OPT 個ずつ
        public const int OPT = 8;
        public const int O_TYPE = 0, O_SEAT = 1, O_TILE = 2, O_A = 3, O_B = 4, O_RIICHI = 5;
        public const int MAXOPT = 64, MAXCO = 16;
        // 副露の種類
        public const int M_CHI = 1, M_PON = 2, M_MINKAN = 3, M_KAKAN = 4, M_ANKAN = 5;
        // 局の終わり方
        public const int RES_NONE = 0, RES_WIN = 1, RES_EXHAUST = 2, RES_ABORT = 3;
        public const int AB_KYUSHU = 1, AB_SUUFUU = 2, AB_SUUCHA = 3, AB_SUUKAN = 4, AB_SANCHA = 5;
        public const int EX_NORMAL = 0, EX_NAGASHI = 1;
        // 宣言の窓(だれかの打牌・加槓・暗槓・北抜きに、ほかの人が応える)
        public const int DC_DISCARD = 1, DC_KAKAN = 2, DC_ANKAN = 3, DC_KITA = 4;
        // 河の印
        public const int RV_RIICHI = 1, RV_CALLED = 2, RV_TSUMOGIRI = 4;
        // 点数の区切り
        public const int LIM_NONE = 0, LIM_MANGAN = 1, LIM_HANEMAN = 2, LIM_BAIMAN = 3, LIM_SANBAIMAN = 4, LIM_KAZOE = 5, LIM_YAKUMAN = 6;
        // 和了の状況(EvaluateWin の ctx)
        public const int CX_RINSHAN = 1, CX_CHANKAN = 2, CX_HAITEI = 4, CX_HOUTEI = 8, CX_TENHOU = 16, CX_CHIIHOU = 32;
        // 和了の形・待ち・面子
        public const int F_NORMAL = 0, F_CHIITOI = 1, F_KOKUSHI = 2;
        public const int W_RYANMEN = 0, W_KANCHAN = 1, W_PENCHAN = 2, W_SHANPON = 3, W_TANKI = 4;
        private const int G_SEQ = 1, G_TRIP = 2, G_KAN = 3;

        // 役の ID。役満は 40 から(翻の欄には倍数が入る)
        public const int Y_RIICHI = 0, Y_IPPATSU = 1, Y_TSUMO = 2, Y_PINFU = 3, Y_IIPEIKOU = 4, Y_TANYAO = 5,
            Y_HAKU = 6, Y_HATSU = 7, Y_CHUN = 8, Y_SEATWIND = 9, Y_ROUNDWIND = 10,
            Y_HAITEI = 11, Y_HOUTEI = 12, Y_RINSHAN = 13, Y_CHANKAN = 14, Y_DOUBLERIICHI = 15,
            Y_CHIITOI = 16, Y_SANSHOKU = 17, Y_ITTSUU = 18, Y_CHANTA = 19, Y_TOITOI = 20, Y_SANANKOU = 21,
            Y_SANSHOKUDOUKOU = 22, Y_SANKANTSU = 23, Y_SHOUSANGEN = 24, Y_HONROUTOU = 25,
            Y_RYANPEIKOU = 26, Y_HONITSU = 27, Y_JUNCHAN = 28, Y_CHINITSU = 29, Y_NAGASHI = 30,
            Y_DORA = 31, Y_AKA = 32, Y_URA = 33, Y_KITA = 34,
            Y_KOKUSHI = 40, Y_KOKUSHI13 = 41, Y_SUUANKOU = 42, Y_SUUANKOUTANKI = 43, Y_DAISANGEN = 44, Y_SHOUSUUSHII = 45,
            Y_DAISUUSHII = 46, Y_TSUUIISOU = 47, Y_RYUUIISOU = 48, Y_CHINROUTOU = 49, Y_CHUUREN = 50, Y_JUNSEICHUUREN = 51,
            Y_SUUKANTSU = 52, Y_TENHOU = 53, Y_CHIIHOU = 54;
        public const int N_YAKU = 55, MAXYAKU = 20, MAXWINS = 4;
        [System.NonSerialized] public string[] yakuNames;

        // ================================================================ ルール(既定値は SetRules)
        [System.NonSerialized] public int rPlayers, rRounds, rStartScore, rReturnScore, rRiichiStick, rHonbaRon, rHonbaTsumo, rNotenTotal;
        /// <summary>順位ごとのウマ(千点)</summary>
        [System.NonSerialized] public int[] rUma;
        [System.NonSerialized] public bool rAka, rKuitan, rDoubleYakuman, rKiriage, rTobi, rAgariYame, rNagashi, rTsumoLoss, rKita, rSanchaho, rPao;

        // ================================================================ 状態
        [System.NonSerialized] public int NP;
        [System.NonSerialized] public uint seed, kyokuSeed;
        [System.NonSerialized] public int dice0, dice1;
        /// <summary>round: 0=東場 1=南場 / kyokuNo: その場の何局目(1〜NP)</summary>
        [System.NonSerialized] public int round, kyokuNo, honba, kyotaku, dealer, initialDealer, kyokuSeq;
        [System.NonSerialized] public int phase, turn;
        [System.NonSerialized] public bool afterCall, afterRinshan, nextRinshan, noCalls, matchOver;

        // 山: wall[0..wallN)。前から引く。王牌は後ろの14枚(嶺上は末尾から、ドラ表示・裏ドラ表示はその手前)
        [System.NonSerialized] public int[] wall;
        [System.NonSerialized] public int wallN, wallPos, liveEnd, replN, doraN, pendingDora;

        // 席ごと
        [System.NonSerialized] public int[] score, kyokuStartScore;
        /// <summary>手牌は枚数で持つ: handC[seat*NC + コード]</summary>
        [System.NonSerialized] public int[] handC, handN, drawnCode;
        // 副露(最大4): [seat*4 + i]。牌は mT0〜mT3(使わない所は -1)、鳴いた牌 mCalled、鳴いた相手 mFrom(暗槓は -1)
        [System.NonSerialized] public int[] meldN, mType, mKind, mT0, mT1, mT2, mT3, mCalled, mFrom;
        // 河: [seat*MAXRV + i]
        public const int MAXRV = 40;
        [System.NonSerialized] public int[] rvC, rvF, riverN;
        [System.NonSerialized] public int[] kitaN;
        [System.NonSerialized] public bool[] riichiOn, ippatsu, doubleRiichi, furitenTemp, furitenRiichi, firstTurn;
        [System.NonSerialized] public int riichiDeclaring;
        private bool riichiDeclDouble;
        [System.NonSerialized] public int[] kanBy; [System.NonSerialized] public int kanTotal;
        /// <summary>責任払い: paoSeat[和了者] が払う(paoYaku の役満のとき)</summary>
        [System.NonSerialized] public int[] paoSeat, paoYaku;
        [System.NonSerialized] public bool[] kuikaeNg;
        private int fdN, fdKind; private bool fdSame;   // 最初の巡の打牌(四風連打)

        // 宣言の窓
        [System.NonSerialized] public int dcType, dcSeat, dcCode;
        [System.NonSerialized] public int[] callOpt, callN, resp;
        [System.NonSerialized] public bool[] responded, missed;

        // 局の結果
        [System.NonSerialized] public int resType, resReason;
        [System.NonSerialized] public bool resRenchan, uraShown;
        [System.NonSerialized] public bool[] resTenpai;
        [System.NonSerialized] public int[] resDelta;
        // その局の和了(ロンは最大3。流し満貫もここに入れる)
        [System.NonSerialized] public int winsN;
        [System.NonSerialized] public int[] winSeat, winFrom, winTile, winHan, winFu, winYakuman, winLimit, winBase, winPoints, winPao, winYakuN, winYakuId, winYakuHan, winDelta;

        // 終局の順位: [順位-1]
        [System.NonSerialized] public int[] mrSeat, mrScore, mrFinal10;

        // 入力待ち
        [System.NonSerialized] public int pendKind, pendN;
        [System.NonSerialized] public int[] pendSeats;

        // 選択肢の出力先
        [System.NonSerialized] public int[] legal; [System.NonSerialized] public int legalN;
        [System.NonSerialized] public string lastError;

        // 待ちの出力先
        [System.NonSerialized] public int[] waitK; [System.NonSerialized] public int waitN;

        // 評価の結果(EvaluateWin)
        [System.NonSerialized] public int bN, bHan, bFu, bYakuman, bBase, bLimit, bForm, bWait;
        [System.NonSerialized] public int[] bId, bHanArr;

        // ================================================================ 初期化
        private bool allocated;

        private void Alloc()
        {
            if (allocated) return;
            allocated = true;
            rUma = new int[MAXP];
            yakuNames = new string[N_YAKU];
            string[] nm = new string[] {
                "立直", "一発", "門前清自摸和", "平和", "一盃口", "断幺九", "役牌 白", "役牌 發", "役牌 中", "自風牌", "場風牌",
                "海底摸月", "河底撈魚", "嶺上開花", "槍槓", "ダブル立直", "七対子", "三色同順", "一気通貫", "混全帯幺九", "対々和", "三暗刻",
                "三色同刻", "三槓子", "小三元", "混老頭", "二盃口", "混一色", "純全帯幺九", "清一色", "流し満貫", "ドラ", "赤ドラ", "裏ドラ", "抜きドラ" };
            for (int i = 0; i < nm.Length; i++) yakuNames[i] = nm[i];
            string[] ym = new string[] {
                "国士無双", "国士無双十三面待ち", "四暗刻", "四暗刻単騎", "大三元", "小四喜", "大四喜", "字一色", "緑一色", "清老頭",
                "九蓮宝燈", "純正九蓮宝燈", "四槓子", "天和", "地和" };
            for (int i = 0; i < ym.Length; i++) yakuNames[40 + i] = ym[i];

            wall = new int[136];
            score = new int[MAXP]; kyokuStartScore = new int[MAXP];
            handC = new int[MAXP * NC]; handN = new int[MAXP]; drawnCode = new int[MAXP];
            meldN = new int[MAXP]; mType = new int[MAXP * 4]; mKind = new int[MAXP * 4];
            mT0 = new int[MAXP * 4]; mT1 = new int[MAXP * 4]; mT2 = new int[MAXP * 4]; mT3 = new int[MAXP * 4];
            mCalled = new int[MAXP * 4]; mFrom = new int[MAXP * 4];
            rvC = new int[MAXP * MAXRV]; rvF = new int[MAXP * MAXRV]; riverN = new int[MAXP];
            kitaN = new int[MAXP];
            riichiOn = new bool[MAXP]; ippatsu = new bool[MAXP]; doubleRiichi = new bool[MAXP];
            furitenTemp = new bool[MAXP]; furitenRiichi = new bool[MAXP]; firstTurn = new bool[MAXP];
            kanBy = new int[MAXP]; paoSeat = new int[MAXP]; paoYaku = new int[MAXP];
            kuikaeNg = new bool[NK];
            callOpt = new int[MAXP * MAXCO * OPT]; callN = new int[MAXP]; resp = new int[MAXP * OPT];
            responded = new bool[MAXP]; missed = new bool[MAXP];
            resTenpai = new bool[MAXP]; resDelta = new int[MAXP];
            winSeat = new int[MAXWINS]; winFrom = new int[MAXWINS]; winTile = new int[MAXWINS]; winHan = new int[MAXWINS];
            winFu = new int[MAXWINS]; winYakuman = new int[MAXWINS]; winLimit = new int[MAXWINS]; winBase = new int[MAXWINS];
            winPoints = new int[MAXWINS]; winPao = new int[MAXWINS]; winYakuN = new int[MAXWINS];
            winYakuId = new int[MAXWINS * MAXYAKU]; winYakuHan = new int[MAXWINS * MAXYAKU]; winDelta = new int[MAXWINS * MAXP];
            mrSeat = new int[MAXP]; mrScore = new int[MAXP]; mrFinal10 = new int[MAXP];
            pendSeats = new int[MAXP];
            legal = new int[MAXOPT * OPT];
            waitK = new int[NK];
            bId = new int[MAXYAKU]; bHanArr = new int[MAXYAKU];
            ksBuf = new int[KS_LEN];
            // 作業領域
            wkC = new int[NK]; wkC2 = new int[NK]; dWk = new int[NK]; dWk2 = new int[NK]; dTc = new int[4];
            decPair = new int[MAXDEC]; decTripN = new int[MAXDEC]; decTrip = new int[MAXDEC * 4]; decSeqN = new int[MAXDEC]; decSeq = new int[MAXDEC * 4];
            evC = new int[NK]; allC = new int[NK];
            gType = new int[4]; gKind = new int[4]; gOpen = new bool[4];
            hgType = new int[4]; hgKind = new int[4];
            cyId = new int[MAXYAKU]; cyHan = new int[MAXYAKU];
            optTmp = new int[OPT]; actTmp = new int[OPT]; ronSeats = new int[MAXP];
            rigKey = new int[RIG_MAX]; rigSeat = new int[RIG_MAX]; rigTpl = new int[RIG_MAX];
            rigFixed = new bool[136];
        }

        /// <summary>
        /// 既定のルール(よくあるネット麻雀・プロリーグ並み)。players = 4 か 3、rounds = 1(東風戦)か 2(東南戦)。
        /// 喰いタン・後付けあり、赤ドラ(5筒・5索 の1枚ずつ。四麻・三麻とも)、飛びあり、オーラスの和了やめ・聴牌やめあり。
        /// </summary>
        public void SetRules(int players, int rounds)
        {
            Alloc();
            rPlayers = players == 3 ? 3 : 4;
            rRounds = rounds == 1 ? 1 : 2;
            rAka = true; rKuitan = true; rDoubleYakuman = true; rKiriage = false; rTobi = true; rAgariYame = true;
            rNagashi = true; rSanchaho = true; rPao = true; rRiichiStick = 1000;
            for (int i = 0; i < MAXP; i++) rUma[i] = 0;
            if (rPlayers == 4)
            {
                rStartScore = 25000; rReturnScore = 30000;
                rUma[0] = 30; rUma[1] = 10; rUma[2] = -10; rUma[3] = -30;
                rHonbaRon = 300; rHonbaTsumo = 100; rNotenTotal = 3000;
                rKita = false; rTsumoLoss = false;
            }
            else
            {
                rStartScore = 35000; rReturnScore = 40000;
                rUma[0] = 15; rUma[1] = 0; rUma[2] = -15;
                rHonbaRon = 200; rHonbaTsumo = 100; rNotenTotal = 2000;
                rKita = true; rTsumoLoss = true;
            }
        }

        public void SetDefaultRules() { SetRules(4, 2); }

        // ================================================================ 乱数(mulberry32)・シード(FNV-1a)
        private uint rngS;

        private uint NextU32()
        {
            rngS = rngS + 0x6D2B79F5u;
            uint t = rngS;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return t ^ (t >> 14);
        }

        private int RandInt(int n)
        {
            ulong v = (ulong)NextU32() * (ulong)n;
            return (int)(v >> 32);
        }

        public uint HashSeed(string s)
        {
            uint h = 0x811c9dc5u;
            for (int i = 0; i < s.Length; i++)
            {
                h ^= (uint)s[i];
                h *= 0x01000193u;
            }
            return h;
        }

        // ================================================================ 牌の性質
        public int KindOf(int c) { return c < NK ? c : (c - RED0) * 9 + 4; }
        public bool IsRed(int c) { return c >= RED0; }
        public bool IsYaochuK(int k) { return k >= 27 || k % 9 == 0 || k % 9 == 8; }
        public bool IsTerminalK(int k) { return k < 27 && (k % 9 == 0 || k % 9 == 8); }
        public bool IsDragonK(int k) { return k >= HAKU; }
        public bool IsWindK(int k) { return k >= EAST && k <= NORTH; }
        /// <summary>この人数で使う種類か(三麻は 2萬〜8萬 を使わない)</summary>
        public bool KindUsed(int k) { return rPlayers == 4 || k < 1 || k > 7; }

        public int DoraOf(int indKind)
        {
            int k = indKind;
            if (k < 27)
            {
                if (rPlayers == 3 && k < 9) return k == 0 ? 8 : 0;   // 三麻の萬子は 1 と 9 だけ
                return k - (k % 9) + ((k % 9) + 1) % 9;
            }
            if (k <= NORTH) return EAST + ((k - EAST + 1) % 4);
            return HAKU + ((k - HAKU + 1) % 3);
        }

        public int SeatWind(int s) { return EAST + (s - dealer + NP) % NP; }
        public int RoundWind() { return EAST + round; }
        public int LiveLeft() { return liveEnd - wallPos; }
        public int DoraIndicator(int i) { return wall[wallN - 6 - 2 * i]; }
        public int UraIndicator(int i) { return wall[wallN - 5 - 2 * i]; }
        public string YakuName(int id) { return id >= 0 && id < N_YAKU && yakuNames[id] != null ? yakuNames[id] : "?"; }

        public bool IsMenzen(int s)
        {
            for (int i = 0; i < meldN[s]; i++) if (mType[s * 4 + i] != M_ANKAN) return false;
            return true;
        }

        private int MinI(int a, int b) { return a < b ? a : b; }
        private int MaxI(int a, int b) { return a > b ? a : b; }
        private int Ceil100(int x) { return (x + 99) / 100 * 100; }

        // ================================================================ 手牌
        private void AddHand(int s, int c) { handC[s * NC + c]++; handN[s]++; }
        private void RemoveHand(int s, int c) { handC[s * NC + c]--; handN[s]--; }
        public int CountKind(int s, int k)
        {
            int n = handC[s * NC + k];
            if (k < 27 && k % 9 == 4) n += handC[s * NC + RED0 + k / 9];
            return n;
        }

        /// <summary>手牌を種類ごとの枚数にする(赤5 は 5 に数える)</summary>
        public void KindCounts(int s, int[] c)
        {
            for (int k = 0; k < NK; k++) c[k] = 0;
            int b = s * NC;
            for (int t = 0; t < NC; t++) { int n = handC[b + t]; if (n > 0) c[KindOf(t)] += n; }
        }

        /// <summary>その種類の牌を1枚、手から取る(普通の牌を先に。無ければ赤)。取ったコードを返す</summary>
        private int TakeKind(int s, int k)
        {
            if (handC[s * NC + k] > 0) { RemoveHand(s, k); return k; }
            if (k < 27 && k % 9 == 4 && handC[s * NC + RED0 + k / 9] > 0) { RemoveHand(s, RED0 + k / 9); return RED0 + k / 9; }
            return -1;
        }

        // ================================================================ 形(和了・待ち)
        private int[] wkC, wkC2, dWk, dWk2, dTc;
        private const int MAXDEC = 32;
        private int decN;
        private int[] decPair, decTripN, decTrip, decSeqN, decSeq;

        /// <summary>
        /// 雀頭1つと面子 m 個に分ける方法をすべて数える(firstOnly なら1つ見つけたら終わる)。
        /// 刻子になりうる牌(3枚以上)を刻子にするかどうかを全部ためし、残りは小さい牌から順子で取っていく(順子だけの分け方は1通り)
        /// </summary>
        public int Decompose(int[] c, int m, bool firstOnly)
        {
            decN = 0;
            for (int p = 0; p < NK; p++)
            {
                if (c[p] < 2) continue;
                for (int k = 0; k < NK; k++) dWk[k] = c[k];
                dWk[p] -= 2;
                int tcN = 0;
                for (int k = 0; k < NK && tcN < 4; k++) if (dWk[k] >= 3) dTc[tcN++] = k;
                for (int mask = 0; mask < (1 << tcN); mask++)
                {
                    for (int k = 0; k < NK; k++) dWk2[k] = dWk[k];
                    int nt = 0;
                    for (int i = 0; i < tcN; i++) if ((mask & (1 << i)) != 0) { dWk2[dTc[i]] -= 3; nt++; }
                    if (nt > m) continue;
                    int ns = 0;
                    bool ok = true;
                    int d = decN < MAXDEC ? decN : MAXDEC - 1;
                    for (int k = 0; k < NK; k++)
                    {
                        int n = dWk2[k];
                        if (n == 0) continue;
                        if (k >= 27 || k % 9 > 6 || dWk2[k + 1] < n || dWk2[k + 2] < n || ns + n + nt > m) { ok = false; break; }
                        dWk2[k + 1] -= n; dWk2[k + 2] -= n; dWk2[k] = 0;
                        for (int j = 0; j < n; j++) decSeq[d * 4 + ns + j] = k;
                        ns += n;
                    }
                    if (!ok || ns + nt != m) continue;
                    decPair[d] = p;
                    decSeqN[d] = ns;
                    decTripN[d] = 0;
                    for (int i = 0; i < tcN; i++) if ((mask & (1 << i)) != 0) decTrip[d * 4 + decTripN[d]++] = dTc[i];
                    if (decN < MAXDEC) decN++;
                    if (firstOnly) return decN;
                }
            }
            return decN;
        }

        public bool IsChiitoi(int[] c)
        {
            int pairs = 0;
            for (int k = 0; k < NK; k++)
            {
                if (c[k] == 2) pairs++;
                else if (c[k] != 0) return false;
            }
            return pairs == 7;
        }

        public bool IsKokushi(int[] c)
        {
            int pairs = 0;
            for (int k = 0; k < NK; k++)
            {
                if (IsYaochuK(k))
                {
                    if (c[k] == 0) return false;
                    if (c[k] == 2) pairs++;
                    else if (c[k] != 1) return false;
                }
                else if (c[k] != 0) return false;
            }
            return pairs == 1;
        }

        /// <summary>
        /// 和了の形か(c は 3m+2 枚。m = 手の中で作る面子の数 = 4 - 副露の数)。
        /// 速い方法: 色ごとに、刻子(3枚ずつ)を除いた残りは小さい牌から順子で取るしかない(同じ順子3つ = 刻子3つなので、これで漏れない)。
        /// 雀頭はその色の中の候補をためす。分け方を全部数える Decompose は、役を数えるとき(EvaluateWin)だけ使う
        /// </summary>
        public bool IsAgari(int[] c, int m)
        {
            if (m == 4 && (IsChiitoi(c) || IsKokushi(c))) return true;
            int pairs = 0;
            for (int k = 27; k < NK; k++)
            {
                int n = c[k];
                if (n == 2) pairs++;
                else if (n != 0 && n != 3) return false;
            }
            int pairSuit = -1;
            for (int su = 0; su < 3; su++)
            {
                int tot = 0;
                for (int i = 0; i < 9; i++) tot += c[su * 9 + i];
                int r = tot % 3;
                if (r == 1) return false;
                if (r == 2) { if (pairSuit >= 0) return false; pairSuit = su; }
            }
            if (pairs + (pairSuit >= 0 ? 1 : 0) != 1) return false;
            for (int su = 0; su < 3; su++)
            {
                int b = su * 9;
                if (su != pairSuit) { if (!SuitMelds(c, b, -1)) return false; continue; }
                bool ok = false;
                for (int i = 0; i < 9 && !ok; i++) if (c[b + i] >= 2 && SuitMelds(c, b, i)) ok = true;
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>1色(c[b..b+8])が面子だけに分けられるか(pairAt の牌から雀頭の2枚を除いて)</summary>
        private bool SuitMelds(int[] c, int b, int pairAt)
        {
            for (int i = 0; i < 9; i++) dWk[i] = c[b + i];
            if (pairAt >= 0) dWk[pairAt] -= 2;
            for (int i = 0; i < 9; i++)
            {
                int n = dWk[i];
                if (n < 0) return false;
                n %= 3;
                if (n == 0) continue;
                if (i > 6) return false;
                dWk[i + 1] -= n; dWk[i + 2] -= n;
            }
            return true;
        }

        /// <summary>待ちを waitK に書き出す(c は 3m+1 枚)。待ちの数を返す</summary>
        public int FindWaits(int[] c, int m)
        {
            waitN = 0;
            for (int k = 0; k < NK; k++)
            {
                if (!KindUsed(k) || c[k] >= 4) continue;
                if (!NearHeld(c, k)) continue;
                c[k]++;
                bool ok = IsAgari(c, m);
                c[k]--;
                if (ok) waitK[waitN++] = k;
            }
            return waitN;
        }

        /// <summary>待ちになりうるか(持っている牌か、同じ色で2つ以内に牌があるか、么九牌(国士の待ち))</summary>
        private bool NearHeld(int[] c, int k)
        {
            if (c[k] > 0 || IsYaochuK(k)) return true;
            if (k >= 27) return false;
            int n = k % 9;
            for (int d = -2; d <= 2; d++)
            {
                int q = n + d;
                if (d == 0 || q < 0 || q > 8) continue;
                if (c[k + d] > 0) return true;
            }
            return false;
        }

        /// <summary>その席の待ち(手牌が 3m+1 枚のとき)</summary>
        public int WaitsOf(int s)
        {
            KindCounts(s, wkC);
            return FindWaits(wkC, 4 - meldN[s]);
        }

        public bool IsTenpai(int s) { return WaitsOf(s) > 0; }

        private bool InWaits(int k)
        {
            for (int i = 0; i < waitN; i++) if (waitK[i] == k) return true;
            return false;
        }

        /// <summary>自分の河に待ちの牌があるか(捨て牌のフリテン)</summary>
        private bool RiverFuriten(int s)
        {
            if (WaitsOf(s) == 0) return false;
            for (int i = 0; i < riverN[s]; i++) if (InWaits(KindOf(rvC[s * MAXRV + i]))) return true;
            return false;
        }

        public bool IsFuriten(int s) { return furitenTemp[s] || furitenRiichi[s] || RiverFuriten(s); }

        // ================================================================ 試合の開始
        public void NewMatchFromString(string seedStr) { NewMatch(HashSeed(seedStr)); }

        public void NewMatch(uint matchSeed)
        {
            Alloc();
            NP = rPlayers;
            seed = matchSeed;
            rngS = matchSeed;
            dice0 = RandInt(6) + 1;
            dice1 = RandInt(6) + 1;
            dealer = (dice0 + dice1 - 1) % NP;
            initialDealer = dealer;
            round = 0; kyokuNo = 1; honba = 0; kyotaku = 0; kyokuSeq = 0;
            for (int s = 0; s < MAXP; s++) { score[s] = s < NP ? rStartScore : 0; kyokuStartScore[s] = score[s]; }
            ClearKyoku();
            phase = PH_DEAL;
            matchOver = false;
            resType = RES_NONE;
            UpdatePending();
        }

        private void ClearKyoku()
        {
            for (int s = 0; s < MAXP; s++)
            {
                for (int t = 0; t < NC; t++) handC[s * NC + t] = 0;
                handN[s] = 0; drawnCode[s] = -1; meldN[s] = 0; riverN[s] = 0; kitaN[s] = 0;
                riichiOn[s] = false; ippatsu[s] = false; doubleRiichi[s] = false;
                furitenTemp[s] = false; furitenRiichi[s] = false; firstTurn[s] = true;
                kanBy[s] = 0; paoSeat[s] = -1; paoYaku[s] = -1;
                callN[s] = 0; responded[s] = false; missed[s] = false; resTenpai[s] = false; resDelta[s] = 0;
            }
            for (int k = 0; k < NK; k++) kuikaeNg[k] = false;
            wallN = 0; wallPos = 0; liveEnd = 0; replN = 0; doraN = 0; pendingDora = 0;
            kanTotal = 0; noCalls = true; afterCall = false; afterRinshan = false; nextRinshan = false;
            riichiDeclaring = -1; riichiDeclDouble = false;
            fdN = 0; fdKind = -1; fdSame = true;
            dcType = 0; dcSeat = -1; dcCode = -1;
            winsN = 0; resType = RES_NONE; resReason = 0; resRenchan = false; uraShown = false;
            turn = dealer;
        }

        // ================================================================ 局の始めの状態(途中参加の追いつき用)
        public const int KS_LEN = 8 + MAXP;
        public const int KS_ROUND = 0, KS_KYOKU = 1, KS_HONBA = 2, KS_KYOTAKU = 3, KS_DEALER = 4, KS_SEQ = 5, KS_INIT_DEALER = 6, KS_SCORE = 8;
        [System.NonSerialized] public int[] ksBuf;

        private void SaveKyokuStart()
        {
            ksBuf[KS_ROUND] = round; ksBuf[KS_KYOKU] = kyokuNo; ksBuf[KS_HONBA] = honba; ksBuf[KS_KYOTAKU] = kyotaku;
            ksBuf[KS_DEALER] = dealer; ksBuf[KS_SEQ] = kyokuSeq; ksBuf[KS_INIT_DEALER] = initialDealer; ksBuf[7] = 0;
            for (int s = 0; s < MAXP; s++) ksBuf[KS_SCORE + s] = score[s];
        }

        /// <summary>局の始めの状態(ksBuf と同じ並び)から復元する。ルールは先に設定しておく。このあと Advance で同じ局が始まる</summary>
        public bool RestoreKyokuStart(uint matchSeed, int[] ks)
        {
            if (ks == null || ks.Length < KS_LEN) return false;
            NewMatch(matchSeed);
            round = ks[KS_ROUND]; kyokuNo = ks[KS_KYOKU]; honba = ks[KS_HONBA]; kyotaku = ks[KS_KYOTAKU];
            dealer = ks[KS_DEALER]; kyokuSeq = ks[KS_SEQ]; initialDealer = ks[KS_INIT_DEALER];
            for (int s = 0; s < MAXP; s++) score[s] = ks[KS_SCORE + s];
            turn = dealer;
            phase = PH_DEAL;
            UpdatePending();
            return true;
        }

        // ================================================================ 局の始め
        private void StartKyoku()
        {
            ClearKyoku();
            for (int s = 0; s < MAXP; s++) kyokuStartScore[s] = score[s];
            kyokuSeed = HashSeed(seed.ToString() + ":" + kyokuSeq.ToString());
            rngS = kyokuSeed;
            int n = 0;
            for (int k = 0; k < NK; k++)
            {
                if (!KindUsed(k)) continue;
                for (int cp = 0; cp < 4; cp++)
                {
                    int code = k;
                    if (rAka && cp == 0 && k >= 9 && k < 27 && k % 9 == 4) code = RED0 + k / 9;   // 赤ドラは 5筒・5索 の1枚ずつ
                    wall[n++] = code;
                }
            }
            wallN = n;
            for (int i = n - 1; i > 0; i--)
            {
                int j = RandInt(i + 1);
                int x = wall[i]; wall[i] = wall[j]; wall[j] = x;
            }
            if (rigCount > 0) ApplyRig();   // デバッグの仕込み(ふだんは無い)
            liveEnd = wallN - 14;
            doraN = 1;
            // 配牌: 親から 13 枚ずつ
            for (int r = 0; r < 13; r++)
                for (int i = 0; i < NP; i++) AddHand((dealer + i) % NP, wall[wallPos++]);
            turn = dealer;
        }

        // ================================================================ デバッグの仕込み
        // 局の番号(kyokuSeq)と席を決めて、その席の配牌にひな形の13枚を配り、2巡目のツモで和了牌が来るように山を並べ替える
        // (1巡目にすると天和・地和になって、狙った役が見えなくなる。九種九牌は最初のツモで)。
        // 三人打ちでも使えるように、萬子の 2〜8 は使わない。牌の種類の番号: 萬 0-8 / 筒 9-17 / 索 18-26 / 東南西北白發中 27-33
        public const int RIG_MAX = 8, RIG_N = 9;
        [System.NonSerialized] public int rigCount;
        [System.NonSerialized] public int[] rigKey, rigSeat, rigTpl;
        private bool[] rigFixed;
        private int[] rigKinds;

        public string RigName(int i)
        {
            if (i == 0) return "国士無双(十三面)";
            if (i == 1) return "九蓮宝燈";
            if (i == 2) return "大三元";
            if (i == 3) return "四暗刻単騎";
            if (i == 4) return "字一色";
            if (i == 5) return "緑一色";
            if (i == 6) return "清一色";
            if (i == 7) return "七対子";
            return "九種九牌";
        }

        /// <summary>ひな形 i の 14 枚(最後の1枚が和了牌)</summary>
        private void InitRigKinds()
        {
            if (rigKinds != null) return;
            rigKinds = new int[] {
                0, 8, 9, 17, 18, 26, 27, 28, 29, 30, 31, 32, 33, 0,           // 国士無双(十三面): 么九牌13種 + 1萬
                9, 9, 9, 10, 11, 12, 13, 14, 15, 16, 17, 17, 17, 13,          // 九蓮宝燈(筒子): 1112345678999 + 5筒
                31, 31, 31, 32, 32, 32, 33, 33, 33, 10, 11, 12, 13, 13,       // 大三元: 白發中 + 234筒 5筒 + 5筒
                9, 9, 9, 11, 11, 11, 22, 22, 22, 24, 24, 24, 26, 26,          // 四暗刻単騎: 111筒 333筒 555索 777索 9索 + 9索
                27, 27, 27, 28, 28, 28, 29, 29, 29, 31, 31, 31, 32, 32,       // 字一色: 東南西白 + 發 + 發
                19, 20, 21, 19, 20, 21, 23, 23, 23, 25, 25, 25, 32, 32,       // 緑一色: 234索 234索 666索 888索 發 + 發
                9, 9, 9, 9, 10, 11, 12, 13, 14, 15, 16, 17, 17, 17,           // 清一色(一気通貫): 1111筒 2〜8筒 99筒 + 9筒
                9, 9, 11, 11, 19, 19, 21, 21, 31, 31, 32, 32, 33, 33,         // 七対子: 11筒 33筒 22索 44索 白白 發發 中 + 中
                0, 8, 9, 17, 18, 26, 27, 28, 29, 12, 13, 14, 22, 23 };        // 九種九牌: 么九牌9種 + 456筒 5索 + 6索
        }

        private void ApplyRig()
        {
            InitRigKinds();
            for (int i = 0; i < wallN; i++) rigFixed[i] = false;
            for (int r = 0; r < rigCount; r++)
            {
                if (rigKey[r] != kyokuSeq) continue;
                int s = rigSeat[r], tpl = rigTpl[r];
                if (s < 0 || s >= NP || tpl < 0 || tpl >= RIG_N) continue;
                int rel = (s - dealer + NP) % NP;
                for (int j = 0; j < 14; j++)
                {
                    // 配牌は親から1枚ずつ巡るので、席 s の j 枚目は j*NP + rel。和了牌は2巡目のツモ(九種九牌は最初のツモ)
                    int pos = j < 13 ? j * NP + rel : 13 * NP + (tpl == 8 ? 0 : NP) + rel;
                    RigPut(pos, rigKinds[tpl * 14 + j]);
                }
            }
        }

        /// <summary>山の pos に種類 k の牌を持ってくる(まだ決めていない所の牌と入れ替える)</summary>
        private void RigPut(int pos, int k)
        {
            if (pos >= wallN || !KindUsed(k)) return;
            if (KindOf(wall[pos]) != k)
                for (int q = 0; q < wallN; q++)
                {
                    if (q == pos || rigFixed[q] || KindOf(wall[q]) != k) continue;
                    int x = wall[q]; wall[q] = wall[pos]; wall[pos] = x;
                    break;
                }
            rigFixed[pos] = true;
        }

        /// <summary>嶺上牌を引く。王牌が減った分、海底が1つ前にずれる(三麻で嶺上が足りなくなったら、山の最後から取る)</summary>
        private int DrawReplacement()
        {
            int k = replN++;
            liveEnd--;
            return k < 4 ? wall[wallN - 1 - k] : wall[liveEnd];
        }

        private void FlipPendingDora()
        {
            doraN = MinI(5, doraN + pendingDora);
            pendingDora = 0;
        }

        // ================================================================ 進行
        public void Advance()
        {
            if (pendKind != PEND_NONE) return;
            for (int guard = 0; guard < 8; guard++)
            {
                if (phase == PH_DEAL)
                {
                    SaveKyokuStart();
                    StartKyoku();
                    phase = PH_DRAW;
                    continue;
                }
                if (phase == PH_DRAW) { DoDraw(); break; }
                break;
            }
            UpdatePending();
        }

        private void DoDraw()
        {
            if (!nextRinshan && LiveLeft() <= 0) { Exhaust(); return; }
            int t = nextRinshan ? DrawReplacement() : wall[wallPos++];
            afterRinshan = nextRinshan;
            nextRinshan = false;
            AddHand(turn, t);
            drawnCode[turn] = t;
            afterCall = false;
            for (int k = 0; k < NK; k++) kuikaeNg[k] = false;
            phase = PH_TURN;
        }

        public void UpdatePending()
        {
            pendN = 0;
            pendKind = PEND_NONE;
            if (phase == PH_TURN) { pendKind = PEND_TURN; pendSeats[pendN++] = turn; }
            else if (phase == PH_CALLS)
            {
                pendKind = PEND_CALLS;
                for (int i = 1; i < NP; i++)
                {
                    int p = (dcSeat + i) % NP;
                    if (callN[p] > 0 && !responded[p]) pendSeats[pendN++] = p;
                }
            }
            else if (phase == PH_KYOKU_OVER) pendKind = PEND_NEXT;
            else if (phase == PH_MATCH_OVER) pendKind = PEND_END;
        }

        // ================================================================ 選択肢
        private int[] optTmp, actTmp, ronSeats;

        private void ClearOpt()
        {
            for (int f = 0; f < OPT; f++) optTmp[f] = -1;
        }

        private void PushLegal(int seat)
        {
            if (legalN >= MAXOPT) return;
            optTmp[O_SEAT] = seat;
            for (int f = 0; f < OPT; f++) legal[legalN * OPT + f] = optTmp[f];
            legalN++;
        }

        /// <summary>その席の今の選択肢を legal に書き出し、数を返す</summary>
        public int BuildLegal(int seat)
        {
            legalN = 0;
            if (seat < 0 || seat >= NP) return 0;
            if (phase == PH_TURN && seat == turn) BuildTurnLegal(seat);
            else if (phase == PH_CALLS && callN[seat] > 0 && !responded[seat])
            {
                for (int i = 0; i < callN[seat]; i++)
                {
                    for (int f = 0; f < OPT; f++) legal[legalN * OPT + f] = callOpt[(seat * MAXCO + i) * OPT + f];
                    legalN++;
                }
            }
            else if (phase == PH_KYOKU_OVER) { ClearOpt(); optTmp[O_TYPE] = A_NEXT; PushLegal(seat); }
            return legalN;
        }

        private int TsumoCtx(int s)
        {
            int ctx = 0;
            if (afterRinshan) ctx |= CX_RINSHAN;
            else if (LiveLeft() <= 0) ctx |= CX_HAITEI;
            if (firstTurn[s] && noCalls && !afterRinshan) ctx |= s == dealer ? CX_TENHOU : CX_CHIIHOU;
            return ctx;
        }

        private bool CanKanMore() { return kanTotal < 4 && LiveLeft() > 0; }

        private void BuildTurnLegal(int s)
        {
            int b = s * NC;
            if (!afterCall)
            {
                // ツモ
                KindCounts(s, wkC);
                if (IsAgari(wkC, 4 - meldN[s]) && drawnCode[s] >= 0 && EvaluateWin(s, drawnCode[s], true, TsumoCtx(s), -1))
                {
                    ClearOpt(); optTmp[O_TYPE] = A_TSUMO; optTmp[O_TILE] = drawnCode[s]; PushLegal(s);
                }
                // 九種九牌
                if (firstTurn[s] && noCalls)
                {
                    KindCounts(s, wkC);
                    int kinds = 0;
                    for (int k = 0; k < NK; k++) if (IsYaochuK(k) && wkC[k] > 0) kinds++;
                    if (kinds >= 9) { ClearOpt(); optTmp[O_TYPE] = A_KYUSHU; PushLegal(s); }
                }
                // 暗槓・加槓
                if (CanKanMore())
                {
                    KindCounts(s, wkC);
                    for (int k = 0; k < NK; k++)
                    {
                        if (wkC[k] < 4) continue;
                        if (riichiOn[s] && !RiichiKanOk(s, k)) continue;
                        ClearOpt(); optTmp[O_TYPE] = A_ANKAN; optTmp[O_TILE] = k; PushLegal(s);
                    }
                    if (!riichiOn[s])
                    {
                        for (int i = 0; i < meldN[s]; i++)
                        {
                            if (mType[s * 4 + i] != M_PON) continue;
                            int k = mKind[s * 4 + i];
                            if (wkC[k] == 0) continue;
                            int code = handC[b + k] > 0 ? k : RED0 + k / 9;
                            ClearOpt(); optTmp[O_TYPE] = A_KAKAN; optTmp[O_TILE] = code; PushLegal(s);
                        }
                    }
                }
                // 北抜き(三麻)
                if (rKita && NP == 3 && handC[b + NORTH] > 0 && LiveLeft() > 0 && (!riichiOn[s] || KindOf(drawnCode[s]) == NORTH))
                {
                    ClearOpt(); optTmp[O_TYPE] = A_KITA; optTmp[O_TILE] = NORTH; PushLegal(s);
                }
            }
            // 打牌
            if (riichiOn[s])
            {
                ClearOpt(); optTmp[O_TYPE] = A_DISCARD; optTmp[O_TILE] = drawnCode[s]; optTmp[O_RIICHI] = 0; PushLegal(s);
                return;
            }
            for (int t = 0; t < NC; t++)
            {
                if (handC[b + t] == 0 || kuikaeNg[KindOf(t)]) continue;
                ClearOpt(); optTmp[O_TYPE] = A_DISCARD; optTmp[O_TILE] = t; optTmp[O_RIICHI] = 0; PushLegal(s);
            }
            // 立直
            if (!afterCall && IsMenzen(s) && score[s] >= rRiichiStick && LiveLeft() >= NP)
            {
                for (int t = 0; t < NC; t++)
                {
                    if (handC[b + t] == 0) continue;
                    RemoveHand(s, t);
                    bool ok = WaitsOf(s) > 0;
                    AddHand(s, t);
                    if (!ok) continue;
                    ClearOpt(); optTmp[O_TYPE] = A_DISCARD; optTmp[O_TILE] = t; optTmp[O_RIICHI] = 1; PushLegal(s);
                }
            }
        }

        /// <summary>立直中の暗槓: ツモった牌で、待ちが変わらないときだけ</summary>
        private bool RiichiKanOk(int s, int k)
        {
            if (drawnCode[s] < 0 || KindOf(drawnCode[s]) != k) return false;
            KindCounts(s, wkC2);
            wkC2[k]--;
            int n1 = FindWaits(wkC2, 4 - meldN[s]);
            int[] keep = new int[NK];
            for (int i = 0; i < n1; i++) keep[i] = waitK[i];
            wkC2[k] -= 3;
            int n2 = FindWaits(wkC2, 3 - meldN[s]);
            if (n1 == 0 || n1 != n2) return false;
            for (int i = 0; i < n1; i++) if (keep[i] != waitK[i]) return false;
            return true;
        }

        // ================================================================ 操作
        public bool ApplyAction(int[] act)
        {
            lastError = "";
            int seat = act[O_SEAT];
            int n = BuildLegal(seat);
            for (int i = 0; i < n; i++)
            {
                int o = i * OPT;
                if (legal[o + O_TYPE] == act[O_TYPE] && legal[o + O_TILE] == act[O_TILE] && legal[o + O_A] == act[O_A]
                    && legal[o + O_B] == act[O_B] && legal[o + O_RIICHI] == act[O_RIICHI])
                    return ApplyIndex(seat, i);
            }
            lastError = "その操作はできません";
            return false;
        }

        /// <summary>BuildLegal(seat) の i 番目を行う</summary>
        public bool ApplyIndex(int seat, int idx)
        {
            lastError = "";
            int n = BuildLegal(seat);
            if (idx < 0 || idx >= n) { lastError = "選択肢の番号が範囲外です"; return false; }
            for (int f = 0; f < OPT; f++) actTmp[f] = legal[idx * OPT + f];
            actTmp[O_SEAT] = seat;
            Execute(seat, actTmp);
            UpdatePending();
            return true;
        }

        /// <summary>選択肢の中から、その種類の最初のものの番号(無ければ -1)</summary>
        public int FindLegalType(int seat, int type)
        {
            int n = BuildLegal(seat);
            for (int i = 0; i < n; i++) if (legal[i * OPT + O_TYPE] == type) return i;
            return -1;
        }

        private void Execute(int s, int[] a)
        {
            int type = a[O_TYPE];
            if (phase == PH_CALLS)
            {
                for (int f = 0; f < OPT; f++) resp[s * OPT + f] = a[f];
                responded[s] = true;
                for (int p = 0; p < NP; p++) if (callN[p] > 0 && !responded[p]) return;
                ResolveCalls();
                return;
            }
            if (type == A_NEXT) { NextKyoku(); return; }
            if (type == A_DISCARD) { DoDiscard(s, a[O_TILE], a[O_RIICHI] == 1); return; }
            if (type == A_TSUMO)
            {
                AddWin(s, -1, drawnCode[s], TsumoCtx(s), true);
                FinishWins();
                return;
            }
            if (type == A_KYUSHU) { Abort(AB_KYUSHU); return; }
            if (type == A_ANKAN) { DoAnkan(s, a[O_TILE]); return; }
            if (type == A_KAKAN) { DoKakan(s, a[O_TILE]); return; }
            if (type == A_KITA) { DoKita(s); return; }
        }

        private void DoDiscard(int s, int code, bool riichi)
        {
            RemoveHand(s, code);
            int i = riverN[s]++;
            if (i >= MAXRV) { riverN[s] = MAXRV; i = MAXRV - 1; }
            rvC[s * MAXRV + i] = code;
            rvF[s * MAXRV + i] = (riichi ? RV_RIICHI : 0) | (code == drawnCode[s] && !afterCall ? RV_TSUMOGIRI : 0);
            FlipPendingDora();
            if (riichi) { riichiDeclaring = s; riichiDeclDouble = firstTurn[s] && noCalls; }
            ippatsu[s] = false;
            furitenTemp[s] = false;
            if (noCalls && fdN < NP)
            {
                int k = KindOf(code);
                if (fdN == 0) fdKind = k;
                else if (k != fdKind) fdSame = false;
                if (!IsWindK(k)) fdSame = false;
                fdN++;
            }
            firstTurn[s] = false;
            drawnCode[s] = -1;
            afterCall = false;
            afterRinshan = false;
            for (int k = 0; k < NK; k++) kuikaeNg[k] = false;
            if (!OpenWindow(DC_DISCARD, s, code)) AfterDiscardPassed();
        }

        private void DoAnkan(int s, int k)
        {
            FlipPendingDora();
            int mi = s * 4 + meldN[s]++;
            mType[mi] = M_ANKAN; mKind[mi] = k; mCalled[mi] = -1; mFrom[mi] = -1;
            mT0[mi] = TakeKind(s, k); mT1[mi] = TakeKind(s, k); mT2[mi] = TakeKind(s, k); mT3[mi] = TakeKind(s, k);
            kanTotal++; kanBy[s]++;
            doraN = MinI(5, doraN + 1);   // 暗槓は即めくり
            noCalls = false;
            for (int p = 0; p < NP; p++) ippatsu[p] = false;
            drawnCode[s] = -1;
            if (!OpenWindow(DC_ANKAN, s, k)) { nextRinshan = true; phase = PH_DRAW; }
        }

        private void DoKakan(int s, int code)
        {
            FlipPendingDora();
            int k = KindOf(code);
            for (int i = 0; i < meldN[s]; i++)
            {
                int mi = s * 4 + i;
                if (mType[mi] != M_PON || mKind[mi] != k) continue;
                RemoveHand(s, code);
                mType[mi] = M_KAKAN; mT3[mi] = code;
                break;
            }
            kanTotal++; kanBy[s]++;
            pendingDora++;   // 明槓・加槓は打牌のあとにめくる
            noCalls = false;
            for (int p = 0; p < NP; p++) ippatsu[p] = false;
            drawnCode[s] = -1;
            if (!OpenWindow(DC_KAKAN, s, code)) { nextRinshan = true; phase = PH_DRAW; }
        }

        private void DoKita(int s)
        {
            RemoveHand(s, NORTH);
            kitaN[s]++;
            drawnCode[s] = -1;
            if (!OpenWindow(DC_KITA, s, NORTH)) { nextRinshan = true; phase = PH_DRAW; }
        }

        // ================================================================ 宣言の窓
        private void PushCall(int p)
        {
            int i = callN[p];
            if (i >= MAXCO) return;
            optTmp[O_SEAT] = p;
            for (int f = 0; f < OPT; f++) callOpt[(p * MAXCO + i) * OPT + f] = optTmp[f];
            callN[p] = i + 1;
        }

        /// <summary>打牌などに、ほかの人が応えられるか調べる。応えられる人がいれば PH_CALLS にして true</summary>
        private bool OpenWindow(int type, int s, int code)
        {
            dcType = type; dcSeat = s; dcCode = code;
            int k = KindOf(code);
            bool any = false;
            for (int p = 0; p < NP; p++)
            {
                callN[p] = 0; responded[p] = false; missed[p] = false;
                if (p == s) continue;
                // ロン
                KindCounts(p, wkC);
                int m = 4 - meldN[p];
                FindWaits(wkC, m);
                bool waiting = InWaits(k);
                if (waiting)
                {
                    int ctx = 0;
                    if (type == DC_KAKAN) ctx |= CX_CHANKAN;
                    if (type == DC_DISCARD && LiveLeft() <= 0) ctx |= CX_HOUTEI;
                    bool canRon = !IsFuriten(p) && EvaluateWin(p, code, false, ctx, s);
                    if (canRon && type == DC_ANKAN && bForm != F_KOKUSHI) canRon = false;   // 暗槓は国士だけ
                    if (type != DC_ANKAN || canRon) missed[p] = true;
                    if (canRon) { ClearOpt(); optTmp[O_TYPE] = A_RON; optTmp[O_TILE] = code; PushCall(p); }
                }
                // 鳴き
                if (type == DC_DISCARD && !riichiOn[p] && LiveLeft() > 0)
                {
                    int nNorm = handC[p * NC + k];
                    int nRed = (k < 27 && k % 9 == 4) ? handC[p * NC + RED0 + k / 9] : 0;
                    if (nNorm >= 2 && CallKeepsDiscard(p, k, k, k, -1)) { ClearOpt(); optTmp[O_TYPE] = A_PON; optTmp[O_TILE] = code; optTmp[O_A] = k; optTmp[O_B] = k; PushCall(p); }
                    if (nNorm >= 1 && nRed >= 1 && CallKeepsDiscard(p, k, k, RED0 + k / 9, -1)) { ClearOpt(); optTmp[O_TYPE] = A_PON; optTmp[O_TILE] = code; optTmp[O_A] = k; optTmp[O_B] = RED0 + k / 9; PushCall(p); }
                    if (nNorm + nRed >= 3 && kanTotal < 4) { ClearOpt(); optTmp[O_TYPE] = A_MINKAN; optTmp[O_TILE] = code; PushCall(p); }
                    if (NP == 4 && p == (s + 1) % NP && k < 27)
                    {
                        int n = k % 9;
                        if (n >= 2) AddChi(p, code, k - 2, k - 1, k);
                        if (n >= 1 && n <= 7) AddChi(p, code, k - 1, k + 1, k);
                        if (n <= 6) AddChi(p, code, k + 1, k + 2, k);
                    }
                }
                if (callN[p] > 0) { ClearOpt(); optTmp[O_TYPE] = A_PASS; PushCall(p); any = true; }
            }
            if (any) phase = PH_CALLS;
            return any;
        }

        private void AddChi(int p, int code, int x, int y, int k)
        {
            int b = p * NC;
            for (int vx = 0; vx < 2; vx++)
            {
                int cx = vx == 0 ? x : (x % 9 == 4 ? RED0 + x / 9 : -1);
                if (cx < 0 || handC[b + cx] == 0) continue;
                for (int vy = 0; vy < 2; vy++)
                {
                    int cy = vy == 0 ? y : (y % 9 == 4 ? RED0 + y / 9 : -1);
                    if (cy < 0 || handC[b + cy] == 0) continue;
                    if (!CallKeepsDiscard(p, k, cx, cy, ChiSuji(k, x, y))) continue;
                    ClearOpt(); optTmp[O_TYPE] = A_CHI; optTmp[O_TILE] = code; optTmp[O_A] = cx; optTmp[O_B] = cy; PushCall(p);
                }
            }
        }

        /// <summary>チーの喰い替えで、さらに切れない筋の牌(無ければ -1)</summary>
        private int ChiSuji(int k, int x, int y)
        {
            if (x == k + 1 && k % 9 <= 5) return k + 3;
            if (y == k - 1 && k % 9 >= 3) return k - 3;
            return -1;
        }

        /// <summary>鳴いたあと、喰い替えにならない打牌が残るか</summary>
        private bool CallKeepsDiscard(int p, int k, int ca, int cb, int suji)
        {
            int b = p * NC;
            handC[b + ca]--; handC[b + cb]--;
            bool ok = false;
            for (int t = 0; t < NC && !ok; t++)
            {
                if (handC[b + t] <= 0) continue;
                int kt = KindOf(t);
                if (kt != k && kt != suji) ok = true;
            }
            handC[b + ca]++; handC[b + cb]++;
            return ok;
        }

        private void ResolveCalls()
        {
            // ロン(放銃者から順に)
            int rn = 0;
            for (int i = 1; i < NP; i++)
            {
                int p = (dcSeat + i) % NP;
                if (callN[p] > 0 && resp[p * OPT + O_TYPE] == A_RON) ronSeats[rn++] = p;
            }
            if (rn > 0)
            {
                if (dcType == DC_DISCARD) riichiDeclaring = -1;   // 立直宣言牌での放銃は、立直が成り立たない
                if (rSanchaho && NP == 4 && rn >= 3) { Abort(AB_SANCHA); return; }
                int ctx = 0;
                if (dcType == DC_KAKAN) ctx |= CX_CHANKAN;
                if (dcType == DC_DISCARD && LiveLeft() <= 0) ctx |= CX_HOUTEI;
                for (int i = 0; i < rn; i++) AddWin(ronSeats[i], dcSeat, dcCode, ctx, i == 0);
                FinishWins();
                return;
            }
            // 見逃し: 同巡内のフリテン(立直中はずっと)
            for (int p = 0; p < NP; p++)
            {
                if (!missed[p]) continue;
                if (riichiOn[p]) furitenRiichi[p] = true;
                else furitenTemp[p] = true;
            }
            if (dcType != DC_DISCARD) { nextRinshan = true; turn = dcSeat; phase = PH_DRAW; return; }
            EstablishRiichi();
            if (CheckSuucha()) return;
            int caller = -1;
            for (int p = 0; p < NP && caller < 0; p++)
            {
                if (callN[p] == 0) continue;
                int t = resp[p * OPT + O_TYPE];
                if (t == A_PON || t == A_MINKAN) caller = p;
            }
            for (int p = 0; p < NP && caller < 0; p++)
                if (callN[p] > 0 && resp[p * OPT + O_TYPE] == A_CHI) caller = p;
            if (caller >= 0) DoCall(caller);
            else AfterDiscardPassed();
        }

        private void EstablishRiichi()
        {
            int s = riichiDeclaring;
            if (s < 0) return;
            riichiDeclaring = -1;
            riichiOn[s] = true;
            ippatsu[s] = true;
            doubleRiichi[s] = riichiDeclDouble;
            score[s] -= rRiichiStick;
            kyotaku++;
        }

        private bool CheckSuucha()
        {
            if (NP != 4) return false;
            for (int p = 0; p < NP; p++) if (!riichiOn[p]) return false;
            Abort(AB_SUUCHA);
            return true;
        }

        private int KanPlayers()
        {
            int n = 0;
            for (int p = 0; p < NP; p++) if (kanBy[p] > 0) n++;
            return n;
        }

        private void AfterDiscardPassed()
        {
            if (riichiDeclaring >= 0) { EstablishRiichi(); if (CheckSuucha()) return; }
            if (NP == 4 && fdN == 4 && fdSame && noCalls) { Abort(AB_SUUFUU); return; }
            if (kanTotal >= 4 && KanPlayers() > 1) { Abort(AB_SUUKAN); return; }
            turn = (dcSeat + 1) % NP;
            phase = PH_DRAW;
        }

        private void DoCall(int p)
        {
            int ro = p * OPT;
            int type = resp[ro + O_TYPE];
            int k = KindOf(dcCode);
            int ri = riverN[dcSeat] - 1;
            if (ri >= 0) rvF[dcSeat * MAXRV + ri] |= RV_CALLED;
            noCalls = false;
            for (int q = 0; q < NP; q++) ippatsu[q] = false;
            int mi = p * 4 + meldN[p]++;
            mFrom[mi] = dcSeat; mCalled[mi] = dcCode; mT3[mi] = -1;
            for (int t = 0; t < NK; t++) kuikaeNg[t] = false;
            turn = p;
            if (type == A_MINKAN)
            {
                FlipPendingDora();
                mType[mi] = M_MINKAN; mKind[mi] = k;
                mT0[mi] = TakeKind(p, k); mT1[mi] = TakeKind(p, k); mT2[mi] = TakeKind(p, k); mT3[mi] = dcCode;
                kanTotal++; kanBy[p]++;
                pendingDora++;
                nextRinshan = true;
                afterCall = false;
                phase = PH_DRAW;
            }
            else
            {
                int a = resp[ro + O_A], b = resp[ro + O_B];
                RemoveHand(p, a); RemoveHand(p, b);
                mT0[mi] = a; mT1[mi] = b; mT2[mi] = dcCode;
                if (type == A_PON) { mType[mi] = M_PON; mKind[mi] = k; }
                else
                {
                    mType[mi] = M_CHI;
                    int ka = KindOf(a), kb = KindOf(b);
                    mKind[mi] = MinI(k, MinI(ka, kb));
                    int sj = ChiSuji(k, MinI(ka, kb), MaxI(ka, kb));
                    if (sj >= 0) kuikaeNg[sj] = true;
                }
                kuikaeNg[k] = true;
                afterCall = true;
                drawnCode[p] = -1;
                phase = PH_TURN;
            }
            // 責任払い: 三元牌の3つ目・風牌の4つ目を鳴かせた人
            if (rPao && (type == A_PON || type == A_MINKAN))
            {
                int dr = 0, wi = 0;
                for (int i = 0; i < meldN[p]; i++)
                {
                    int t = mType[p * 4 + i];
                    if (t == M_CHI) continue;
                    int mk = mKind[p * 4 + i];
                    if (IsDragonK(mk)) dr++;
                    if (IsWindK(mk)) wi++;
                }
                if (IsDragonK(k) && dr == 3) { paoSeat[p] = dcSeat; paoYaku[p] = Y_DAISANGEN; }
                if (IsWindK(k) && wi == 4) { paoSeat[p] = dcSeat; paoYaku[p] = Y_DAISUUSHII; }
            }
        }

        // ================================================================ 和了の評価
        private int[] evC, allC, gType, gKind, hgType, hgKind, cyId, cyHan;
        private bool[] gOpen;
        private int evSeat, evWinK, evWinCode, evCtx, evPair, evWait, evForm, evDoraAll, evDora, evAka, evUra, evKita, cyN, cyMan;
        private bool evTsumo, evMenzen;

        /// <summary>
        /// 和了を評価する(ロンなら winCode を手に足して考える)。役があれば true で、いちばん高い取り方を b* に入れる。
        /// ctx は CX_* の組み合わせ(嶺上・槍槓・海底・河底・天和・地和)
        /// </summary>
        public bool EvaluateWin(int s, int winCode, bool tsumo, int ctx, int from)
        {
            evSeat = s; evWinCode = winCode; evWinK = KindOf(winCode); evTsumo = tsumo; evCtx = ctx;
            KindCounts(s, evC);
            if (!tsumo) evC[evWinK]++;
            int m = 4 - meldN[s];
            evMenzen = IsMenzen(s);
            for (int k = 0; k < NK; k++) allC[k] = evC[k];
            evAka = 0;
            for (int t = RED0; t < NC; t++) evAka += handC[s * NC + t];
            if (!tsumo && IsRed(winCode)) evAka++;
            for (int i = 0; i < meldN[s]; i++)
            {
                int mi = s * 4 + i;
                int mk = mKind[mi];
                if (mType[mi] == M_CHI) { allC[mk]++; allC[mk + 1]++; allC[mk + 2]++; }
                else allC[mk] += (mType[mi] == M_PON) ? 3 : 4;
                if (mT0[mi] >= RED0) evAka++;
                if (mT1[mi] >= RED0) evAka++;
                if (mT2[mi] >= RED0) evAka++;
                if (mT3[mi] >= RED0) evAka++;
            }
            evKita = kitaN[s];
            evDora = 0;
            for (int i = 0; i < doraN; i++)
            {
                int dk = DoraOf(KindOf(DoraIndicator(i)));
                evDora += allC[dk] + (dk == NORTH ? evKita : 0);
            }
            evUra = 0;
            if (riichiOn[s])
                for (int i = 0; i < doraN; i++)
                {
                    int dk = DoraOf(KindOf(UraIndicator(i)));
                    evUra += allC[dk] + (dk == NORTH ? evKita : 0);
                }
            if (!rAka) evAka = 0;
            evDoraAll = evDora + evAka + evUra + evKita;

            bN = 0; bHan = 0; bFu = 0; bYakuman = 0; bBase = 0; bLimit = LIM_NONE; bForm = F_NORMAL; bWait = W_RYANMEN;
            if (m == 4 && IsKokushi(evC)) { evForm = F_KOKUSHI; evPair = -1; evWait = W_TANKI; ClearGroups(); Candidate(); }
            if (m == 4 && IsChiitoi(evC)) { evForm = F_CHIITOI; evPair = -1; evWait = W_TANKI; ClearGroups(); Candidate(); }
            int n = Decompose(evC, m, false);
            for (int d = 0; d < n; d++)
            {
                int hg = 0;
                for (int i = 0; i < decTripN[d]; i++) { hgType[hg] = G_TRIP; hgKind[hg] = decTrip[d * 4 + i]; hg++; }
                for (int i = 0; i < decSeqN[d]; i++) { hgType[hg] = G_SEQ; hgKind[hg] = decSeq[d * 4 + i]; hg++; }
                evForm = F_NORMAL;
                evPair = decPair[d];
                if (evPair == evWinK) { SetGroups(hg, -1); evWait = W_TANKI; Candidate(); }
                for (int g = 0; g < hg; g++)
                {
                    int gk = hgKind[g];
                    if (hgType[g] == G_TRIP)
                    {
                        if (gk != evWinK) continue;
                        SetGroups(hg, tsumo ? -1 : g);   // ロンで完成した刻子は明刻
                        evWait = W_SHANPON;
                        Candidate();
                    }
                    else if (evWinK >= gk && evWinK <= gk + 2)
                    {
                        int pos = evWinK - gk, num = gk % 9;
                        if (pos == 1) evWait = W_KANCHAN;
                        else if (pos == 0) evWait = num == 6 ? W_PENCHAN : W_RYANMEN;
                        else evWait = num == 0 ? W_PENCHAN : W_RYANMEN;
                        SetGroups(hg, -1);
                        Candidate();
                    }
                }
            }
            return bBase > 0;
        }

        private void ClearGroups()
        {
            for (int i = 0; i < 4; i++) { gType[i] = 0; gKind[i] = -1; gOpen[i] = false; }
        }

        /// <summary>手の中の面子(hg 個)と副露を、4つの面子に並べる。openIdx の刻子はロンで完成したので明刻にする</summary>
        private void SetGroups(int hg, int openIdx)
        {
            int n = 0;
            for (int i = 0; i < hg && n < 4; i++) { gType[n] = hgType[i]; gKind[n] = hgKind[i]; gOpen[n] = i == openIdx; n++; }
            for (int i = 0; i < meldN[evSeat] && n < 4; i++)
            {
                int mi = evSeat * 4 + i;
                int t = mType[mi];
                gType[n] = t == M_CHI ? G_SEQ : (t == M_PON ? G_TRIP : G_KAN);
                gKind[n] = mKind[mi];
                gOpen[n] = t != M_ANKAN;
                n++;
            }
        }

        private void AddY(int id, int han) { if (cyN < MAXYAKU) { cyId[cyN] = id; cyHan[cyN] = han; cyN++; } }
        private void AddMan(int id, int mult)
        {
            if (!rDoubleYakuman) mult = 1;
            AddY(id, mult);
            cyMan += mult;
        }

        private bool IsValuePair(int k) { return IsDragonK(k) || k == SeatWind(evSeat) || k == RoundWind(); }

        /// <summary>1つの取り方(evForm・groups・evPair・evWait)の役と点を出し、今までより高ければ b* に写す</summary>
        private void Candidate()
        {
            cyN = 0; cyMan = 0;
            int sw = SeatWind(evSeat), rw = RoundWind();
            bool allHonor = true, allTerm = true, allGreen = true, allYaochu = true, anyHonor = false;
            int suitMask = 0;
            for (int k = 0; k < NK; k++)
            {
                if (allC[k] == 0) continue;
                if (k >= 27) anyHonor = true; else { allHonor = false; suitMask |= 1 << (k / 9); }
                if (!IsTerminalK(k)) allTerm = false;
                if (!IsYaochuK(k)) allYaochu = false;
                if (k != 19 && k != 20 && k != 21 && k != 23 && k != 25 && k != HATSU) allGreen = false;
            }
            int suits = ((suitMask & 1) != 0 ? 1 : 0) + ((suitMask & 2) != 0 ? 1 : 0) + ((suitMask & 4) != 0 ? 1 : 0);

            // ---- 役満
            if (evForm == F_KOKUSHI)
            {
                evC[evWinK]--;
                bool thirteen = true;
                for (int k = 0; k < NK; k++) if (IsYaochuK(k) && evC[k] != 1) thirteen = false;
                evC[evWinK]++;
                if (thirteen) AddMan(Y_KOKUSHI13, 2); else AddMan(Y_KOKUSHI, 1);
            }
            if ((evCtx & CX_TENHOU) != 0) AddMan(Y_TENHOU, 1);
            if ((evCtx & CX_CHIIHOU) != 0) AddMan(Y_CHIIHOU, 1);
            if (allHonor) AddMan(Y_TSUUIISOU, 1);
            if (allGreen) AddMan(Y_RYUUIISOU, 1);
            if (allTerm) AddMan(Y_CHINROUTOU, 1);
            int anko = 0, kans = 0, dragonT = 0, windT = 0, seqN = 0;
            if (evForm == F_NORMAL)
            {
                for (int g = 0; g < 4; g++)
                {
                    if (gType[g] == G_SEQ) { seqN++; continue; }
                    if (!gOpen[g]) anko++;
                    if (gType[g] == G_KAN) kans++;
                    if (IsDragonK(gKind[g])) dragonT++;
                    if (IsWindK(gKind[g])) windT++;
                }
                if (anko == 4) { if (evWait == W_TANKI) AddMan(Y_SUUANKOUTANKI, 2); else AddMan(Y_SUUANKOU, 1); }
                if (dragonT == 3) AddMan(Y_DAISANGEN, 1);
                if (windT == 4) AddMan(Y_DAISUUSHII, 2);
                else if (windT == 3 && IsWindK(evPair)) AddMan(Y_SHOUSUUSHII, 1);
                if (kans == 4) AddMan(Y_SUUKANTSU, 1);
                if (meldN[evSeat] == 0 && suits == 1 && !anyHonor)
                {
                    int sb = 0;
                    for (int k = 0; k < 27; k++) if (evC[k] > 0) { sb = k - k % 9; break; }
                    bool ch = evC[sb] >= 3 && evC[sb + 8] >= 3;
                    for (int i = 1; i < 8; i++) if (evC[sb + i] < 1) ch = false;
                    if (ch)
                    {
                        evC[evWinK]--;
                        bool pure = evC[sb] == 3 && evC[sb + 8] == 3;
                        for (int i = 1; i < 8; i++) if (evC[sb + i] != 1) pure = false;
                        evC[evWinK]++;
                        if (pure) AddMan(Y_JUNSEICHUUREN, 2); else AddMan(Y_CHUUREN, 1);
                    }
                }
            }
            if (cyMan > 0)
            {
                int ybase = 8000 * cyMan;
                if (ybase > bBase) TakeCandidate(0, 0, ybase, LIM_YAKUMAN, cyMan);
                return;
            }

            // ---- 一般役
            bool menzen = evMenzen;
            int fu;
            if (riichiOn[evSeat])
            {
                if (doubleRiichi[evSeat]) AddY(Y_DOUBLERIICHI, 2); else AddY(Y_RIICHI, 1);
                if (ippatsu[evSeat]) AddY(Y_IPPATSU, 1);
            }
            if (menzen && evTsumo) AddY(Y_TSUMO, 1);
            if ((evCtx & CX_HAITEI) != 0) AddY(Y_HAITEI, 1);
            if ((evCtx & CX_HOUTEI) != 0) AddY(Y_HOUTEI, 1);
            if ((evCtx & CX_RINSHAN) != 0) AddY(Y_RINSHAN, 1);
            if ((evCtx & CX_CHANKAN) != 0) AddY(Y_CHANKAN, 1);
            bool simplesOnly = true;
            for (int k = 0; k < NK; k++) if (allC[k] > 0 && IsYaochuK(k)) simplesOnly = false;
            if (simplesOnly && (menzen || rKuitan)) AddY(Y_TANYAO, 1);
            if (allYaochu && !allHonor && !allTerm) AddY(Y_HONROUTOU, 2);
            if (suits == 1) { if (anyHonor) AddY(Y_HONITSU, menzen ? 3 : 2); else AddY(Y_CHINITSU, menzen ? 6 : 5); }
            if (evForm == F_CHIITOI)
            {
                AddY(Y_CHIITOI, 2);
                fu = 25;
            }
            else
            {
                for (int g = 0; g < 4; g++)
                {
                    if (gType[g] == G_SEQ) continue;
                    int gk = gKind[g];
                    if (gk == HAKU) AddY(Y_HAKU, 1);
                    if (gk == HATSU) AddY(Y_HATSU, 1);
                    if (gk == CHUN) AddY(Y_CHUN, 1);
                    if (gk == sw) AddY(Y_SEATWIND, 1);
                    if (gk == rw) AddY(Y_ROUNDWIND, 1);
                }
                bool pinfu = menzen && seqN == 4 && !IsValuePair(evPair) && evWait == W_RYANMEN;
                if (pinfu) AddY(Y_PINFU, 1);
                if (menzen)
                {
                    int peiko = 0;
                    for (int a = 0; a < 4; a++)
                    {
                        if (gType[a] != G_SEQ) continue;
                        for (int b = a + 1; b < 4; b++)
                            if (gType[b] == G_SEQ && gKind[b] == gKind[a] && gKind[a] >= 0) { peiko++; gKind[b] = -100 - gKind[b]; break; }
                    }
                    for (int g = 0; g < 4; g++) if (gKind[g] <= -100) gKind[g] = -100 - gKind[g];
                    if (peiko >= 2) AddY(Y_RYANPEIKOU, 3);
                    else if (peiko == 1) AddY(Y_IIPEIKOU, 1);
                }
                for (int n = 0; n < 7; n++)
                    if (HasGroup(G_SEQ, n) && HasGroup(G_SEQ, n + 9) && HasGroup(G_SEQ, n + 18)) { AddY(Y_SANSHOKU, menzen ? 2 : 1); break; }
                for (int n = 0; n < 9; n++)
                    if (HasTrip(n) && HasTrip(n + 9) && HasTrip(n + 18)) { AddY(Y_SANSHOKUDOUKOU, 2); break; }
                for (int su = 0; su < 3; su++)
                    if (HasGroup(G_SEQ, su * 9) && HasGroup(G_SEQ, su * 9 + 3) && HasGroup(G_SEQ, su * 9 + 6)) { AddY(Y_ITTSUU, menzen ? 2 : 1); break; }
                if (seqN == 0) AddY(Y_TOITOI, 2);
                if (anko == 3) AddY(Y_SANANKOU, 2);
                if (kans == 3) AddY(Y_SANKANTSU, 2);
                if (dragonT == 2 && IsDragonK(evPair)) AddY(Y_SHOUSANGEN, 2);
                if (seqN > 0)
                {
                    bool outside = IsYaochuK(evPair);
                    for (int g = 0; g < 4 && outside; g++)
                    {
                        int gk = gKind[g];
                        if (gType[g] == G_SEQ) { if (gk % 9 != 0 && gk % 9 != 6) outside = false; }
                        else if (!IsYaochuK(gk)) outside = false;
                    }
                    if (outside) { if (anyHonor) AddY(Y_CHANTA, menzen ? 2 : 1); else AddY(Y_JUNCHAN, menzen ? 3 : 2); }
                }
                // 符
                if (pinfu) fu = evTsumo ? 20 : 30;
                else
                {
                    fu = 20;
                    if (menzen && !evTsumo) fu += 10;
                    if (evTsumo) fu += 2;
                    for (int g = 0; g < 4; g++)
                    {
                        if (gType[g] == G_SEQ) continue;
                        int f = gType[g] == G_KAN ? 8 : 2;
                        if (IsYaochuK(gKind[g])) f *= 2;
                        if (!gOpen[g]) f *= 2;
                        fu += f;
                    }
                    if (IsDragonK(evPair)) fu += 2;
                    if (evPair == sw) fu += 2;
                    if (evPair == rw) fu += 2;
                    if (evWait == W_KANCHAN || evWait == W_PENCHAN || evWait == W_TANKI) fu += 2;
                    if (fu == 20) fu = 30;   // 鳴いた平和形のロン
                    fu = (fu + 9) / 10 * 10;
                }
            }
            if (cyN == 0) return;   // 役なし
            int han = 0;
            for (int i = 0; i < cyN; i++) han += cyHan[i];
            int total = han + evDoraAll;
            int lim = LIM_NONE, bse;
            if (total >= 13) { bse = 8000; lim = LIM_KAZOE; }
            else if (total >= 11) { bse = 6000; lim = LIM_SANBAIMAN; }
            else if (total >= 8) { bse = 4000; lim = LIM_BAIMAN; }
            else if (total >= 6) { bse = 3000; lim = LIM_HANEMAN; }
            else if (total >= 5) { bse = 2000; lim = LIM_MANGAN; }
            else
            {
                bse = fu << (2 + total);
                if (bse >= 2000 || (rKiriage && bse >= 1920)) { bse = 2000; lim = LIM_MANGAN; }
            }
            if (bse > bBase || (bse == bBase && total > bHan)) TakeCandidate(total, fu, bse, lim, 0);
        }

        private bool HasGroup(int type, int k)
        {
            for (int g = 0; g < 4; g++) if (gType[g] == type && gKind[g] == k) return true;
            return false;
        }

        private bool HasTrip(int k)
        {
            for (int g = 0; g < 4; g++) if (gType[g] != G_SEQ && gType[g] != 0 && gKind[g] == k) return true;
            return false;
        }

        private void TakeCandidate(int han, int fu, int bse, int lim, int man)
        {
            bHan = han; bFu = fu; bBase = bse; bLimit = lim; bYakuman = man; bForm = evForm; bWait = evWait;
            bN = 0;
            for (int i = 0; i < cyN && bN < MAXYAKU; i++) { bId[bN] = cyId[i]; bHanArr[bN] = cyHan[i]; bN++; }
            if (man == 0)
            {
                if (evDora > 0 && bN < MAXYAKU) { bId[bN] = Y_DORA; bHanArr[bN] = evDora; bN++; }
                if (evAka > 0 && bN < MAXYAKU) { bId[bN] = Y_AKA; bHanArr[bN] = evAka; bN++; }
                if (evUra > 0 && bN < MAXYAKU) { bId[bN] = Y_URA; bHanArr[bN] = evUra; bN++; }
                if (evKita > 0 && bN < MAXYAKU) { bId[bN] = Y_KITA; bHanArr[bN] = evKita; bN++; }
            }
        }

        // ================================================================ 和了・流局・精算
        private void Delta(int w, int p, int d) { winDelta[w * MAXP + p] += d; }

        private bool HasYaku(int id)
        {
            for (int i = 0; i < bN; i++) if (bId[i] == id) return true;
            return false;
        }

        /// <summary>和了を1つ記録して、点のやり取りを決める(sticks = 本場と供託を受け取る人)</summary>
        private void AddWin(int s, int from, int winCode, int ctx, bool sticks)
        {
            if (winsN >= MAXWINS) return;
            EvaluateWin(s, winCode, from < 0, ctx, from);
            int w = winsN++;
            winSeat[w] = s; winFrom[w] = from; winTile[w] = winCode; winHan[w] = bHan; winFu[w] = bFu;
            winYakuman[w] = bYakuman; winLimit[w] = bLimit; winBase[w] = bBase;
            winYakuN[w] = bN;
            for (int i = 0; i < bN; i++) { winYakuId[w * MAXYAKU + i] = bId[i]; winYakuHan[w * MAXYAKU + i] = bHanArr[i]; }
            for (int p = 0; p < MAXP; p++) winDelta[w * MAXP + p] = 0;
            bool isDealer = s == dealer;
            int pao = -1;
            if (paoSeat[s] >= 0 && HasYaku(paoYaku[s])) pao = paoSeat[s];
            winPao[w] = pao;
            int got = 0;
            if (from >= 0)
            {
                int pay = Ceil100(bBase * (isDealer ? 6 : 4));
                int hb = sticks ? honba * rHonbaRon : 0;
                if (pao >= 0 && pao != from) { int half = pay / 2; Delta(w, pao, -half); Delta(w, from, -(pay - half) - hb); }
                else Delta(w, from, -pay - hb);
                got = pay + hb;
            }
            else
            {
                int missing = isDealer ? Ceil100(bBase * 2) : Ceil100(bBase);   // 三麻で、いない北家の分
                for (int p = 0; p < NP; p++)
                {
                    if (p == s) continue;
                    int d = (isDealer || p == dealer) ? Ceil100(bBase * 2) : Ceil100(bBase);
                    if (NP == 3 && !rTsumoLoss) d += Ceil100(missing / 2);
                    if (sticks) d += honba * rHonbaTsumo;
                    if (pao < 0) Delta(w, p, -d);
                    got += d;
                }
                if (pao >= 0) Delta(w, pao, -got);
            }
            if (sticks) { got += kyotaku * rRiichiStick; kyotaku = 0; }
            Delta(w, s, got);
            winPoints[w] = got;
            if (riichiOn[s]) uraShown = true;
        }

        private void FinishWins()
        {
            resType = RES_WIN;
            resRenchan = false;
            for (int p = 0; p < MAXP; p++) resDelta[p] = 0;
            for (int w = 0; w < winsN; w++)
            {
                if (winSeat[w] == dealer) resRenchan = true;
                for (int p = 0; p < NP; p++) resDelta[p] += winDelta[w * MAXP + p];
            }
            for (int p = 0; p < NP; p++) score[p] += resDelta[p];
            phase = PH_KYOKU_OVER;
        }

        private bool NagashiOk(int s)
        {
            if (riverN[s] == 0) return false;
            for (int i = 0; i < riverN[s]; i++)
            {
                int o = s * MAXRV + i;
                if ((rvF[o] & RV_CALLED) != 0 || !IsYaochuK(KindOf(rvC[o]))) return false;
            }
            return true;
        }

        private void Exhaust()
        {
            resType = RES_EXHAUST;
            resReason = EX_NORMAL;
            winsN = 0;
            int tn = 0;
            for (int p = 0; p < MAXP; p++) { resDelta[p] = 0; resTenpai[p] = false; }
            for (int p = 0; p < NP; p++) { resTenpai[p] = IsTenpai(p); if (resTenpai[p]) tn++; }
            if (rNagashi)
            {
                for (int i = 0; i < NP; i++)
                {
                    int s = (dealer + i) % NP;
                    if (!NagashiOk(s) || winsN >= MAXWINS) continue;
                    int w = winsN++;
                    winSeat[w] = s; winFrom[w] = -1; winTile[w] = -1; winHan[w] = 5; winFu[w] = 0; winYakuman[w] = 0;
                    winLimit[w] = LIM_MANGAN; winBase[w] = 2000; winPao[w] = -1;
                    winYakuN[w] = 1; winYakuId[w * MAXYAKU] = Y_NAGASHI; winYakuHan[w * MAXYAKU] = 5;
                    for (int p = 0; p < MAXP; p++) winDelta[w * MAXP + p] = 0;
                    int got = 0;
                    for (int p = 0; p < NP; p++)
                    {
                        if (p == s) continue;
                        int d = (s == dealer || p == dealer) ? 4000 : 2000;
                        Delta(w, p, -d);
                        got += d;
                    }
                    Delta(w, s, got);
                    winPoints[w] = got;
                    for (int p = 0; p < NP; p++) resDelta[p] += winDelta[w * MAXP + p];
                }
                if (winsN > 0) resReason = EX_NAGASHI;
            }
            if (winsN == 0 && tn > 0 && tn < NP)
            {
                for (int p = 0; p < NP; p++) resDelta[p] = resTenpai[p] ? rNotenTotal / tn : -rNotenTotal / (NP - tn);
            }
            for (int p = 0; p < NP; p++) score[p] += resDelta[p];
            resRenchan = resTenpai[dealer];
            phase = PH_KYOKU_OVER;
        }

        private void Abort(int reason)
        {
            resType = RES_ABORT;
            resReason = reason;
            resRenchan = true;
            winsN = 0;
            for (int p = 0; p < MAXP; p++) resDelta[p] = 0;
            phase = PH_KYOKU_OVER;
        }

        /// <summary>順位(同点は起家に近い方が上)。0 が1位</summary>
        public int RankOf(int s)
        {
            int r = 0;
            for (int p = 0; p < NP; p++)
            {
                if (p == s) continue;
                if (score[p] > score[s]) r++;
                else if (score[p] == score[s] && (p - initialDealer + NP) % NP < (s - initialDealer + NP) % NP) r++;
            }
            return r;
        }

        private void NextKyoku()
        {
            if (phase != PH_KYOKU_OVER) return;
            if (resType == RES_WIN) honba = resRenchan ? honba + 1 : 0;
            else honba++;
            bool end = false;
            if (rTobi) for (int p = 0; p < NP; p++) if (score[p] < 0) end = true;
            bool allLast = round == rRounds - 1 && kyokuNo == NP;
            if (!end)
            {
                if (resRenchan)
                {
                    if (allLast && rAgariYame && resType != RES_ABORT && RankOf(dealer) == 0) end = true;
                }
                else
                {
                    dealer = (dealer + 1) % NP;
                    kyokuNo++;
                    if (kyokuNo > NP) { kyokuNo = 1; round++; }
                    if (round >= rRounds) end = true;
                }
            }
            if (end) { MatchEnd(); return; }
            kyokuSeq++;
            phase = PH_DEAL;
        }

        private void MatchEnd()
        {
            // 残った供託はトップへ
            for (int p = 0; p < NP; p++) if (RankOf(p) == 0) { score[p] += kyotaku * rRiichiStick; kyotaku = 0; }
            int oka = (rReturnScore - rStartScore) * NP;
            for (int p = 0; p < NP; p++)
            {
                int r = RankOf(p);
                mrSeat[r] = p;
                mrScore[r] = score[p];
                mrFinal10[r] = (score[p] - rReturnScore) / 100 + rUma[r] * 10 + (r == 0 ? oka / 100 : 0);
            }
            matchOver = true;
            phase = PH_MATCH_OVER;
        }
    }
}
