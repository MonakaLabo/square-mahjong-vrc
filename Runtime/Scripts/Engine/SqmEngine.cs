using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 平方麻雀のルールエンジン(ブラウザ版 js/engine の移植。ルールブック v0.8)。
    /// UdonSharp で動くように、状態はすべて数値の配列で持つ(独自のクラス・構造体・ジェネリクス・ラムダ・例外は使えない)。
    /// 同じシードと同じ操作なら、ブラウザ版と1手単位で同じ結果になる(Tests~ の答え合わせで確かめる)。
    ///
    /// 使い方: SetDefaultRules() → NewMatch(seed) → 以降 pendKind を見て
    ///   PEND_NONE なら Advance()、それ以外は BuildLegal(seat) で選択肢を得て ApplyAction(act) を呼ぶ。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmEngine : UdonSharpBehaviour
    {
        // ================================================================ 定数
        public const int NP = 8;          // 人数
        public const int NT = 47;         // 牌の種類
        public const int ROUND_WIND = 36; // 場風(東)

        // 局面
        public const int PH_DEAL = 0, PH_DRAW = 1, PH_TURN = 2, PH_CALLS = 3, PH_RINSHAN = 4, PH_KYOKU_OVER = 5, PH_MATCH_OVER = 6;
        // 入力待ち
        public const int PEND_NONE = 0, PEND_TURN = 1, PEND_CALLS = 2, PEND_NEXT = 3, PEND_END = 4;
        // アクションの種類
        public const int A_DISCARD = 1, A_TSUMO = 2, A_KAN = 3, A_ABORT = 4, A_PASS = 5, A_RON = 6, A_PON = 7, A_CHI = 8, A_NEXT = 9;
        // 槓 / 面子の種類
        public const int KAN_NONE = 0, KAN_ANKAN = 1, KAN_KAKAN = 2, KAN_DAIMINKAN = 3;
        public const int MT_NONE = -1, MT_K = 1, MT_T = 2, MT_F = 3;
        public const int RON_TWO = 1, RON_ONE = 2;
        // 和了の種類・役割
        public const int WT_TSUMO = 1, WT_RON1 = 2, WT_RON2 = 3;
        public const int ROLE_NONE = 0, ROLE_FIRST = 1, ROLE_SECOND = 2;
        // 局の終わり方
        public const int RES_NONE = 0, RES_WIN = 1, RES_EXHAUST = 2, RES_ABORT = 3;
        public const int REASON_KYUSHU = 1, REASON_HACHIFUU = 2, REASON_HACHIRIICHI = 3;
        // 和了形
        public const int FORM_NORMAL = 0, FORM_KOKUSHI = 1, FORM_CHUUREN = 2, FORM_CHIITOI = 3;

        // アクション/選択肢のレコード(int の並び)。OPT 個ずつ
        public const int OPT = 32;
        public const int O_TYPE = 0, O_SEAT = 1, O_MODE = 2, O_TILE = 3, O_RONIDX = 4, O_RONTILE = 5, O_MELDTYPE = 6, O_START = 7,
            O_TAKEN = 8, O_TAKE = 9, O_GIVEN = 11, O_GIVE = 12, O_HAN = 18, O_YAKUMAN = 19, O_POINTS = 20, O_COUNT = 21,
            O_FIXEDN = 22, O_FIXED = 23, O_KUIKAEN = 26, O_KUIKAE = 27, O_RIICHIOK = 29;
        public const int MAXOPT = 96;

        // 役の ID(ブラウザ版の役 ID と同じ名前)
        public const int Y_RIICHI = 0, Y_DOUBLERIICHI = 1, Y_IPPATSU = 2, Y_MENZENTSUMO = 3, Y_TANYAO = 4, Y_YAKUHAI = 5, Y_JUNKO = 6,
            Y_HAYASHI = 7, Y_HAYAKI = 8, Y_RINSHAN = 9, Y_CHANKAN = 10, Y_HAITEI = 11, Y_HOUTEI = 12, Y_NIMAIRON = 13, Y_SHINRYAKU = 14,
            Y_TOITOI = 15, Y_SANANKOU = 16, Y_SANSHOKU = 17, Y_ITTSUU = 18, Y_SHOUSANGEN = 19, Y_HONROUTOU = 20, Y_CHANTA = 21,
            Y_SANSUU2 = 22, Y_SANSUU3 = 23, Y_HONITSU = 24, Y_JUNCHAN = 25, Y_YONSHOKU = 26, Y_CHIITOI = 27, Y_CHINITSU = 28,
            Y_KANJUN = 29, Y_KANZEN = 30, Y_KATARON_PENALTY = 31, Y_DORA = 32, Y_URA = 33,
            Y_KOKUSHI = 40, Y_CHUUREN = 41, Y_TENHOU = 42, Y_CHIIHOU = 43, Y_TSUUIISOU = 44, Y_FURINKAZAN = 45, Y_DAISANGEN = 46,
            Y_DAISUUSHII = 47, Y_SUUANKOU = 48, Y_CHINROUTOU = 49, Y_SUUKANTSU = 50, Y_SANYOU = 51;
        public const int N_YAKU = 52;
        [System.NonSerialized] public string[] yakuIds;

        // ================================================================ ルール(既定値は SetDefaultRules)
        [System.NonSerialized] public int rCopies, rHandSize, rDwRinshan, rDwDora, rDwUra, rDwRon, rDwSpare, rRonTileCount, rDoraPerFlip, rMaxKan;
        [System.NonSerialized] public int rStartScore, rKyokuCount, rBasePoint, rCapHan, rYakumanPoint;
        [System.NonSerialized] public int rMultChildSecond, rMultChildFirst, rMultDealerSecond, rMultDealerFirst, rDealerTsumoWeight10;
        [System.NonSerialized] public int rRiichiStick, rRiichiMinScore, rHonbaRon, rHonbaTsumoEach, rNotenTotal;
        [System.NonSerialized] public int[] rUma;
        [System.NonSerialized] public bool rUseUma, rAgariYame, rTobiEnd, rUseUra, rRenchanFirst;
        /// <summary>フリテン: 0=off 1=loose 2=strict / 林の如く: 0=off 1=dama 2=menzen / 完順子ボーナス: 0=none 1=perTwo 2=perMeld</summary>
        [System.NonSerialized] public int rFuriten, rHayashi, rKanjunMode, rKataRonPenalty, rHayakiTurns;
        /// <summary>役の翻数 [門前] [副露]</summary>
        [System.NonSerialized] public int[] yakuClosed, yakuOpen;

        // ================================================================ 状態
        [System.NonSerialized] public uint seed;
        [System.NonSerialized] public int dice0, dice1;
        [System.NonSerialized] public int kyoku, honba, kyotaku, dealer, initialDealer, kyokuSeq, step;
        [System.NonSerialized] public int phase, turn, mustDiscard, kanCount, pendingDora, doraFlips, first, discardSeq, furitenBlocked;
        [System.NonSerialized] public bool afterCall, isRinshan, anyCall;
        [System.NonSerialized] public uint kyokuSeed;

        // 山・王牌
        [System.NonSerialized] public int[] wallLive; [System.NonSerialized] public int wallLiveLen, wallPos;
        [System.NonSerialized] public int[] wallRinshan; [System.NonSerialized] public int wallRinshanLen, wallRinshanPos;
        [System.NonSerialized] public int[] wallDoraInd; [System.NonSerialized] public int wallDoraIndLen;
        [System.NonSerialized] public int[] wallUraInd; [System.NonSerialized] public int wallUraIndLen;
        [System.NonSerialized] public int[] wallSpare; [System.NonSerialized] public int wallSpareLen;
        /// <summary>公開ロン牌(-1 = 取られて空)</summary>
        [System.NonSerialized] public int[] ronTiles; [System.NonSerialized] public int ronTilesLen;
        [System.NonSerialized] public int[] doraInd; [System.NonSerialized] public int doraIndN;
        [System.NonSerialized] public int[] uraInd; [System.NonSerialized] public int uraIndN;

        // 席ごと
        [System.NonSerialized] public int[] score, kyokuStartScore, turns, outRole;
        /// <summary>手牌は枚数で持つ: handC[seat*NT + tile]</summary>
        [System.NonSerialized] public int[] handC, handLen;
        /// <summary>直前にツモった牌(順番つき): drawn[seat*MAXDRAWN + i]</summary>
        public const int MAXDRAWN = 8;
        [System.NonSerialized] public int[] drawn, drawnLen;
        // 副露(最大4): [seat*4 + i]
        [System.NonSerialized] public int[] meldType, meldStart, meldKan, meldFrom, meldTakeN, meldTake0, meldTake1, meldN;
        [System.NonSerialized] public bool[] meldOpen;
        // 河: [seat*MAXRIVER + i]
        public const int MAXRIVER = 200;
        [System.NonSerialized] public int[] rvN, rvT0, rvT1, rvSeq, rvCalledBy, rvRonBy, riverLen;
        [System.NonSerialized] public bool[] rvTaken0, rvTaken1, rvRiichi;
        // リーチ・フリテン
        [System.NonSerialized] public bool[] riichiOn, riichiIppatsu, riichiDouble, riichiDeclaring, furitenPerm, furitenTemp;
        [System.NonSerialized] public int[] riichiTurn, riichiAt;

        [System.NonSerialized] public int[] kuikae; [System.NonSerialized] public int kuikaeN;
        [System.NonSerialized] public int[] firstRound; [System.NonSerialized] public int firstRoundN;
        [System.NonSerialized] public int lastDiscardSeat, lastDiscardIdx;
        // 直前の打牌(または加槓)の文脈
        [System.NonSerialized] public int dcSeat, dcN, dcRiverIdx;
        [System.NonSerialized] public int[] dcT;
        [System.NonSerialized] public bool dcRiichi, dcHoutei, dcChankan, dcActive;

        // 宣言の選択肢と返事: coBuf[(seat*MAXOPT + i)*OPT + field]
        [System.NonSerialized] public int[] coBuf, coN, respBuf;
        [System.NonSerialized] public bool[] hasCall, responded;

        // 局の結果
        [System.NonSerialized] public int resType, resReason, resSeat, resTenpaiN;
        [System.NonSerialized] public int[] resTenpai, resDeltas;
        [System.NonSerialized] public bool resHadWin;

        // その局の和了(最大8)
        public const int MAXWINS = 8, MAXYAKU = 24;
        [System.NonSerialized] public int winsN;
        [System.NonSerialized] public int[] winSeat, winRole, winType, winFrom, winHan, winTotal, winBase, winM10, winHonba, winStick, winYakuN, winYakuId, winYakuHan, winDeltas, winRonTile, winForm;
        [System.NonSerialized] public int[] winTileA, winTileB;   // 和了牌(表示用。ツモはツモ牌、ロンは取った牌。無ければ -1)
        [System.NonSerialized] public bool[] winYakuman, winRiichi;

        // 終局の順位: [順位-1]
        [System.NonSerialized] public int[] mrSeat, mrScore, mrUma, mrFinal10;
        [System.NonSerialized] public bool matchOver;

        // 入力待ち
        [System.NonSerialized] public int pendKind, pendN;
        [System.NonSerialized] public int[] pendSeats;

        // 選択肢の出力先
        [System.NonSerialized] public int[] legal; [System.NonSerialized] public int legalN;
        [System.NonSerialized] public string lastError;

        // ================================================================ 初期化
        private bool allocated;

        private void Alloc()
        {
            if (allocated) return;
            allocated = true;
            rUma = new int[NP];
            yakuClosed = new int[N_YAKU]; yakuOpen = new int[N_YAKU];
            yakuIds = new string[N_YAKU];
            string[] ids = new string[] {
                "riichi", "doubleRiichi", "ippatsu", "menzenTsumo", "tanyao", "yakuhai", "junko", "hayashi", "hayaki", "rinshan",
                "chankan", "haitei", "houtei", "nimaiRon", "shinryaku", "toitoi", "sanankou", "sanshoku", "ittsuu", "shousangen",
                "honroutou", "chanta", "sansuu2", "sansuu3", "honitsu", "junchan", "yonshoku", "chiitoi", "chinitsu", "kanjun",
                "kanzen", "kataRonPenalty", "dora", "ura" };
            for (int i = 0; i < ids.Length; i++) yakuIds[i] = ids[i];
            string[] ym = new string[] { "kokushi", "chuuren", "tenhou", "chiihou", "tsuuiisou", "furinkazan", "daisangen", "daisuushii", "suuankou", "chinroutou", "suukantsu", "sanyou" };
            for (int i = 0; i < ym.Length; i++) yakuIds[40 + i] = ym[i];

            wallLive = new int[NT * 40]; wallRinshan = new int[64]; wallDoraInd = new int[64]; wallUraInd = new int[64]; wallSpare = new int[64];
            ronTiles = new int[64]; doraInd = new int[64]; uraInd = new int[64];
            score = new int[NP]; kyokuStartScore = new int[NP]; turns = new int[NP]; outRole = new int[NP];
            handC = new int[NP * NT]; handLen = new int[NP];
            drawn = new int[NP * MAXDRAWN]; drawnLen = new int[NP];
            meldType = new int[NP * 4]; meldStart = new int[NP * 4]; meldKan = new int[NP * 4]; meldFrom = new int[NP * 4];
            meldTakeN = new int[NP * 4]; meldTake0 = new int[NP * 4]; meldTake1 = new int[NP * 4]; meldN = new int[NP]; meldOpen = new bool[NP * 4];
            rvN = new int[NP * MAXRIVER]; rvT0 = new int[NP * MAXRIVER]; rvT1 = new int[NP * MAXRIVER]; rvSeq = new int[NP * MAXRIVER];
            rvCalledBy = new int[NP * MAXRIVER]; rvRonBy = new int[NP * MAXRIVER]; riverLen = new int[NP];
            rvTaken0 = new bool[NP * MAXRIVER]; rvTaken1 = new bool[NP * MAXRIVER]; rvRiichi = new bool[NP * MAXRIVER];
            riichiOn = new bool[NP]; riichiIppatsu = new bool[NP]; riichiDouble = new bool[NP]; riichiDeclaring = new bool[NP];
            furitenPerm = new bool[NP]; furitenTemp = new bool[NP]; riichiTurn = new int[NP]; riichiAt = new int[NP];
            kuikae = new int[4]; firstRound = new int[64]; dcT = new int[2];
            coBuf = new int[NP * MAXOPT * OPT]; coN = new int[NP]; respBuf = new int[NP * OPT]; hasCall = new bool[NP]; responded = new bool[NP];
            resTenpai = new int[NP]; resDeltas = new int[NP];
            winSeat = new int[MAXWINS]; winRole = new int[MAXWINS]; winType = new int[MAXWINS]; winFrom = new int[MAXWINS];
            winHan = new int[MAXWINS]; winTotal = new int[MAXWINS]; winBase = new int[MAXWINS]; winM10 = new int[MAXWINS];
            winHonba = new int[MAXWINS]; winStick = new int[MAXWINS]; winYakuN = new int[MAXWINS]; winRonTile = new int[MAXWINS]; winForm = new int[MAXWINS];
            winTileA = new int[MAXWINS]; winTileB = new int[MAXWINS];
            winYakuId = new int[MAXWINS * MAXYAKU]; winYakuHan = new int[MAXWINS * MAXYAKU]; winDeltas = new int[MAXWINS * NP];
            winYakuman = new bool[MAXWINS]; winRiichi = new bool[MAXWINS];
            mrSeat = new int[NP]; mrScore = new int[NP]; mrUma = new int[NP]; mrFinal10 = new int[NP];
            pendSeats = new int[NP];
            ksBuf = new int[KS_LEN];
            legal = new int[MAXOPT * OPT];
            // 形の計算の作業領域
            sw = new int[NT]; sw2 = new int[NT]; ankTmp = new int[NT]; tmpC = new int[NT]; tmpC2 = new int[NT]; allC = new int[NT];
            decPair = new int[MAXDEC]; decType = new int[MAXDEC * 4]; decStart = new int[MAXDEC * 4];
            curType = new int[4]; curStart = new int[4];
            waitFound = new bool[NT * 64]; waitA = new int[1200]; waitB = new int[1200]; waitTiles = new int[NT]; jok = new int[4];
            wKeepA = new int[1200]; wKeepB = new int[1200];
            gType = new int[8]; gStart = new int[8]; gKan = new int[8]; gOpen = new bool[8]; gConc = new bool[8];
            cYakuId = new int[MAXYAKU]; cYakuHan = new int[MAXYAKU];
            evYakuId = new int[MAXYAKU]; evYakuHanArr = new int[MAXYAKU];
            evGType = new int[8]; evGStart = new int[8];
            wcRonTaken = new int[2];
            optTmp = new int[OPT];
            uniqBuf = new int[4];
            patMask = new int[2 * 10];
        }

        /// <summary>既定のルール(ルールブック v0.8)</summary>
        public void SetDefaultRules()
        {
            Alloc();
            rCopies = 28; rHandSize = 26;
            rDwRinshan = 8; rDwDora = 18; rDwUra = 18; rDwRon = 4; rDwSpare = 8;
            rRonTileCount = 4; rDoraPerFlip = 1; rMaxKan = 8; rStartScore = 150000; rKyokuCount = 8;
            rBasePoint = 500; rCapHan = 8; rYakumanPoint = 256000;
            rMultChildSecond = 10; rMultChildFirst = 13; rMultDealerSecond = 15; rMultDealerFirst = 18; rDealerTsumoWeight10 = 15;
            rRiichiStick = 5000; rRiichiMinScore = 5000; rHonbaRon = 500; rHonbaTsumoEach = 100; rNotenTotal = 10000;
            rUma[0] = 40; rUma[1] = 20; rUma[2] = 10; rUma[3] = 5; rUma[4] = -5; rUma[5] = -10; rUma[6] = -20; rUma[7] = -40;
            rUseUma = true; rAgariYame = true; rTobiEnd = true; rUseUra = true; rRenchanFirst = true;
            rFuriten = 0; rHayashi = 1; rKanjunMode = 0; rKataRonPenalty = 0; rHayakiTurns = 5;
            SetYaku(Y_RIICHI, 1, 0); SetYaku(Y_DOUBLERIICHI, 2, 0); SetYaku(Y_IPPATSU, 1, 0); SetYaku(Y_MENZENTSUMO, 1, 0);
            SetYaku(Y_TANYAO, 1, 1); SetYaku(Y_YAKUHAI, 1, 1); SetYaku(Y_JUNKO, 1, 0); SetYaku(Y_HAYASHI, 1, 0); SetYaku(Y_HAYAKI, 1, 1);
            SetYaku(Y_RINSHAN, 1, 1); SetYaku(Y_CHANKAN, 1, 1); SetYaku(Y_HAITEI, 1, 1); SetYaku(Y_HOUTEI, 1, 1);
            SetYaku(Y_NIMAIRON, 2, 2); SetYaku(Y_SHINRYAKU, 2, 2); SetYaku(Y_TOITOI, 2, 2); SetYaku(Y_SANANKOU, 2, 2);
            SetYaku(Y_SANSHOKU, 2, 1); SetYaku(Y_ITTSUU, 2, 1); SetYaku(Y_SHOUSANGEN, 2, 2); SetYaku(Y_HONROUTOU, 2, 2);
            SetYaku(Y_CHANTA, 2, 1); SetYaku(Y_SANSUU2, 2, 2); SetYaku(Y_SANSUU3, 3, 3);
            SetYaku(Y_HONITSU, 3, 2); SetYaku(Y_JUNCHAN, 3, 2); SetYaku(Y_YONSHOKU, 3, 2); SetYaku(Y_CHIITOI, 3, 0); SetYaku(Y_CHINITSU, 6, 5);
            SetYaku(Y_KANJUN, 1, 0); SetYaku(Y_KANZEN, 3, 2);
        }

        public void SetYaku(int id, int closedHan, int openHan) { yakuClosed[id] = closedHan; yakuOpen[id] = openHan; }

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

        /// <summary>floor(u32 / 2^32 * n)。ブラウザ版(double の計算)と同じ値を、整数だけで求める</summary>
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
        public bool IsYaochu(int t) { return t >= 36 || t % 9 == 0 || t % 9 == 8; }
        public bool IsTerminal(int t) { return t < 36 && (t % 9 == 0 || t % 9 == 8); }
        public bool IsDragon(int t) { return t >= 40 && t <= 42; }
        public int SuitOf(int t) { return t < 36 ? t / 9 : -1; }
        public int NumOf(int t) { return t < 36 ? (t % 9) + 1 : 0; }

        public int DoraFromIndicator(int t)
        {
            if (t < 36) return t - (t % 9) + ((t % 9) + 1) % 9;
            if (t < 40) return 36 + ((t - 36 + 1) % 4);
            if (t < 43) return 40 + ((t - 40 + 1) % 3);
            return 43 + ((t - 43 + 1) % 4);
        }

        /// <summary>席風の並び: 東・風・南・林・西・火・北・山</summary>
        public int SeatWindOf(int seat, int dlr)
        {
            int k = (seat - dlr + NP) % NP;
            if (k == 0) return 36; if (k == 1) return 43; if (k == 2) return 37; if (k == 3) return 44;
            if (k == 4) return 38; if (k == 5) return 45; if (k == 6) return 39; return 46;
        }

        public int OppositeWindOf(int seat, int dlr) { return SeatWindOf((seat + NP / 2) % NP, dlr); }

        private int MinI(int a, int b) { return a < b ? a : b; }
        private int MaxI(int a, int b) { return a > b ? a : b; }

        // ================================================================ 試合の開始
        public void NewMatchFromString(string seedStr) { NewMatch(HashSeed(seedStr)); }

        public void NewMatch(uint matchSeed)
        {
            Alloc();
            seed = matchSeed;
            rngS = matchSeed;
            dice0 = RandInt(6) + 1;
            dice1 = RandInt(6) + 1;
            dealer = (dice0 + dice1 - 1) % NP;
            initialDealer = dealer;
            kyoku = 1; honba = 0; kyotaku = 0;
            for (int s = 0; s < NP; s++)
            {
                score[s] = rStartScore; handLen[s] = 0; meldN[s] = 0; riverLen[s] = 0; turns[s] = 0; drawnLen[s] = 0;
                riichiOn[s] = false; riichiIppatsu[s] = false; riichiDouble[s] = false; riichiDeclaring[s] = false; riichiTurn[s] = -1; riichiAt[s] = 0;
                furitenPerm[s] = false; furitenTemp[s] = false; outRole[s] = ROLE_NONE;
                for (int t = 0; t < NT; t++) handC[s * NT + t] = 0;
            }
            wallLiveLen = 0; wallPos = 0; wallRinshanLen = 0; wallRinshanPos = 0; ronTilesLen = 0; doraIndN = 0; uraIndN = 0;
            pendingDora = 0; kanCount = 0; anyCall = false; turn = dealer; phase = PH_DEAL; mustDiscard = 2;
            afterCall = false; kuikaeN = 0; isRinshan = false; dcActive = false; ClearCalls();
            first = -1; winsN = 0; resType = RES_NONE; matchOver = false; kyokuSeq = 0; step = 0;
            UpdatePending();
        }

        // ================================================================ 局の始めの状態(途中参加の追いつき用)
        // 局をまたいで残るのは、局の番号・本場・供託・親・点数などだけ(山は種と kyokuSeq から作り直せる)。
        // 局を始める直前(配牌の前)にこれを ksBuf に写しておき、途中参加の人はここから復元して、その局の手だけを再生する。
        public const int KS_LEN = 8 + NP;
        public const int KS_KYOKU = 0, KS_HONBA = 1, KS_KYOTAKU = 2, KS_DEALER = 3, KS_SEQ = 4, KS_STEP = 5, KS_INIT_DEALER = 6, KS_SCORE = 8;
        [System.NonSerialized] public int[] ksBuf;

        private void SaveKyokuStart()
        {
            ksBuf[KS_KYOKU] = kyoku; ksBuf[KS_HONBA] = honba; ksBuf[KS_KYOTAKU] = kyotaku; ksBuf[KS_DEALER] = dealer;
            ksBuf[KS_SEQ] = kyokuSeq; ksBuf[KS_STEP] = step; ksBuf[KS_INIT_DEALER] = initialDealer; ksBuf[7] = 0;
            for (int s = 0; s < NP; s++) ksBuf[KS_SCORE + s] = score[s];
        }

        /// <summary>
        /// 局の始めの状態(ksBuf と同じ並び)から復元する。ルールと仕込みは先に設定しておく。
        /// このあと Advance で、同じ山・同じ配牌の局が始まる
        /// </summary>
        public bool RestoreKyokuStart(uint matchSeed, int[] ks)
        {
            if (ks == null || ks.Length < KS_LEN) return false;
            NewMatch(matchSeed);
            kyoku = ks[KS_KYOKU]; honba = ks[KS_HONBA]; kyotaku = ks[KS_KYOTAKU]; dealer = ks[KS_DEALER];
            kyokuSeq = ks[KS_SEQ]; step = ks[KS_STEP]; initialDealer = ks[KS_INIT_DEALER];
            for (int s = 0; s < NP; s++) score[s] = ks[KS_SCORE + s];
            turn = dealer;
            phase = PH_DEAL;
            UpdatePending();
            return true;
        }

        private void ClearCalls()
        {
            for (int s = 0; s < NP; s++) { hasCall[s] = false; responded[s] = false; coN[s] = 0; }
        }

        /// <summary>局を始める: 山・王牌・ロン牌・配牌・ドラ表示</summary>
        private void StartKyoku()
        {
            kyokuSeed = HashSeed(seed.ToString() + ":" + kyokuSeq.ToString());
            rngS = kyokuSeed;
            int total = NT * rCopies;
            int[] all = new int[total];
            int q = 0;
            for (int t = 0; t < NT; t++) for (int k = 0; k < rCopies; k++) all[q++] = t;
            for (int i = total - 1; i > 0; i--)
            {
                int j = RandInt(i + 1);
                int x = all[i]; all[i] = all[j]; all[j] = x;
            }
            int deadSize = rDwRinshan + rDwDora + rDwUra + rDwRon + rDwSpare;
            int deadStart = total - deadSize;
            if (rigCount > 0) ApplyRig(all, total, deadStart);   // デバッグの仕込み(ふだんは無い)
            int p = deadStart;
            wallRinshanLen = rDwRinshan; for (int i = 0; i < rDwRinshan; i++) wallRinshan[i] = all[p++];
            wallRinshanPos = 0;
            wallDoraIndLen = rDwDora; for (int i = 0; i < rDwDora; i++) wallDoraInd[i] = all[p++];
            wallUraIndLen = rDwUra; for (int i = 0; i < rDwUra; i++) wallUraInd[i] = all[p++];
            int ronCount = MaxI(0, MinI(rRonTileCount, rDwRon + rDwSpare));
            ronTilesLen = ronCount; for (int i = 0; i < ronCount; i++) ronTiles[i] = all[p++];
            wallSpareLen = rDwRon + rDwSpare - ronCount; for (int i = 0; i < wallSpareLen; i++) wallSpare[i] = all[p++];
            // 配牌: 親から2枚×13回
            for (int s = 0; s < NP; s++) { for (int t = 0; t < NT; t++) handC[s * NT + t] = 0; handLen[s] = 0; }
            q = 0;
            for (int r = 0; r < rHandSize / 2; r++)
            {
                for (int i = 0; i < NP; i++)
                {
                    int seat = (dealer + i) % NP;
                    handC[seat * NT + all[q]]++; handC[seat * NT + all[q + 1]]++; handLen[seat] += 2;
                    q += 2;
                }
            }
            wallLiveLen = deadStart - q;
            for (int i = 0; i < wallLiveLen; i++) wallLive[i] = all[q + i];
            wallPos = 0;
            for (int s = 0; s < NP; s++)
            {
                meldN[s] = 0; riverLen[s] = 0; turns[s] = 0; drawnLen[s] = 0;
                riichiOn[s] = false; riichiIppatsu[s] = false; riichiDouble[s] = false; riichiDeclaring[s] = false; riichiTurn[s] = -1; riichiAt[s] = 0;
                furitenPerm[s] = false; furitenTemp[s] = false; outRole[s] = ROLE_NONE;
            }
            doraIndN = rDoraPerFlip; for (int i = 0; i < rDoraPerFlip; i++) doraInd[i] = wallDoraInd[i];
            uraIndN = rDoraPerFlip; for (int i = 0; i < rDoraPerFlip; i++) uraInd[i] = wallUraInd[i];
            doraFlips = 1; pendingDora = 0; kanCount = 0; anyCall = false;
            turn = dealer; mustDiscard = 2; afterCall = false; kuikaeN = 0; isRinshan = false; dcActive = false;
            ClearCalls();
            first = -1; winsN = 0; resType = RES_NONE; resTenpaiN = 0; resHadWin = false;
            firstRoundN = 0; lastDiscardSeat = -1; lastDiscardIdx = -1; furitenBlocked = 0; discardSeq = 0;
            kyokuSeq++;
        }

        // ================================================================ デバッグ: 配牌の仕込み
        // 「次の局で、指定した役が必ず出る」ようにする。席 rigSeat の配牌を、役のひな形(28枚)から最後の2枚を抜いた 26 枚にし、
        // 2巡目のツモで残りの2枚を引くように山を並べ替える(1巡目にすると天和・地和になって、狙った役が見えなくなる)。
        // 仕込みは rigKey(局の番号 kyokuSeq)ごとに持ち、同期して全員のエンジンで同じように並べ替える(途中参加の再生でも同じ)。
        public const int RIG_N = 16, RIG_MAX = 16;
        [System.NonSerialized] public int rigCount;
        [System.NonSerialized] public int[] rigKey, rigSeat, rigTpl;
        private int[] rigTiles, rigYakuId;

        public void InitRig()
        {
            if (rigTiles != null) return;
            rigKey = new int[RIG_MAX]; rigSeat = new int[RIG_MAX]; rigTpl = new int[RIG_MAX];
            rigYakuId = new int[] { 4, 5, 24, 28, 27, 30, 19, 16, 40, 41, 44, 46, 45, 48, 47, 49 };
            rigTiles = new int[] {
                4, 4, 1, 1, 2, 2, 3, 3, 11, 12, 13, 14, 15, 16, 23, 23, 23, 23, 23, 23, 30, 30, 31, 31, 32, 32, 4, 4,
                1, 1, 11, 11, 12, 12, 13, 13, 18, 19, 20, 21, 22, 23, 32, 32, 33, 33, 34, 34, 41, 41, 41, 41, 41, 41, 1, 1,
                36, 36, 0, 0, 1, 1, 2, 2, 2, 3, 4, 5, 6, 7, 8, 8, 8, 8, 8, 8, 40, 40, 40, 40, 40, 40, 36, 36,
                4, 4, 0, 0, 1, 1, 2, 2, 2, 3, 4, 5, 6, 7, 8, 8, 8, 8, 8, 8, 5, 5, 6, 6, 7, 7, 4, 4,
                0, 0, 2, 2, 4, 4, 6, 6, 8, 8, 10, 10, 12, 12, 14, 14, 16, 16, 18, 18, 20, 20, 36, 36, 40, 40, 43, 43,
                0, 1, 2, 3, 4, 5, 12, 13, 14, 15, 16, 17, 19, 20, 21, 22, 23, 24, 29, 30, 31, 32, 33, 34, 36, 36, 36, 36,
                40, 40, 40, 40, 40, 40, 41, 41, 41, 41, 41, 41, 42, 42, 0, 0, 1, 1, 2, 2, 10, 11, 12, 13, 14, 15, 42, 42,
                1, 1, 1, 1, 1, 1, 13, 13, 13, 13, 13, 13, 25, 25, 25, 25, 25, 25, 29, 29, 30, 30, 31, 31, 40, 40, 40, 40,
                0, 8, 9, 17, 18, 26, 27, 35, 36, 37, 38, 39, 40, 41, 43, 44, 45, 46, 0, 8, 9, 17, 18, 26, 27, 42, 42, 42,
                0, 0, 0, 8, 8, 8, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 1, 2, 3, 4, 5, 6, 0, 8,
                36, 36, 37, 37, 37, 37, 37, 37, 38, 38, 38, 38, 38, 38, 40, 40, 40, 40, 40, 40, 42, 42, 42, 42, 42, 42, 36, 36,
                40, 40, 40, 40, 40, 40, 41, 41, 41, 41, 41, 41, 42, 42, 42, 42, 42, 42, 0, 0, 10, 10, 11, 11, 12, 12, 0, 0,
                43, 43, 43, 43, 43, 43, 44, 44, 44, 44, 44, 44, 45, 45, 45, 45, 45, 45, 46, 46, 46, 46, 46, 46, 0, 0, 0, 0,
                1, 1, 1, 1, 1, 1, 13, 13, 13, 13, 13, 13, 25, 25, 25, 25, 25, 25, 29, 29, 29, 29, 29, 29, 40, 40, 40, 40,
                36, 36, 36, 36, 36, 36, 37, 37, 37, 37, 37, 37, 38, 38, 38, 38, 38, 38, 39, 39, 39, 39, 39, 39, 4, 4, 4, 4,
                0, 0, 0, 0, 0, 0, 8, 8, 8, 8, 8, 8, 9, 9, 9, 9, 9, 9, 17, 17, 17, 17, 17, 17, 18, 18, 18, 18 };
        }

        /// <summary>ひな形の名前(パッドの設定で選ぶ)</summary>
        public string RigName(int i)
        {
            string[] n = new string[] { "断幺九", "役牌(發)", "混一色", "清一色", "十四対子", "完全完順", "小三元", "三暗刻", "国士無双", "九蓮宝燈", "字一色", "大三元", "風林火山", "四暗刻", "大四喜", "清老頭" };
            return i >= 0 && i < n.Length ? n[i] : "";
        }

        /// <summary>ひな形 i で出るはずの役の ID</summary>
        public int RigYaku(int i) { InitRig(); return rigYakuId[i]; }

        /// <summary>配牌の前に、この局の仕込みがあれば山(all)を並べ替える</summary>
        private void ApplyRig(int[] all, int total, int deadStart)
        {
            InitRig();
            for (int r = 0; r < rigCount; r++)
            {
                if (rigKey[r] != kyokuSeq) continue;
                int s = rigSeat[r], ti = rigTpl[r];
                if (s < 0 || s >= NP || ti < 0 || ti >= RIG_N) continue;
                int k = (s - dealer + NP) % NP;
                int dealEnd = rHandSize / 2 * NP * 2;
                bool[] fixedPos = new bool[total];
                for (int j = 0; j < 28; j++)
                {
                    // 配牌の 26 枚(親から2枚ずつ)と、2巡目のツモの2枚の場所
                    int p = j < 26 ? ((j / 2) * NP + k) * 2 + (j % 2) : dealEnd + NP * 2 + k * 2 + (j - 26);
                    if (p >= deadStart) continue;
                    int want = rigTiles[ti * 28 + j];
                    if (all[p] != want)
                    {
                        int from = -1;
                        for (int q = 0; q < total && from < 0; q++) if (!fixedPos[q] && q != p && all[q] == want) from = q;
                        if (from < 0) continue;
                        int x = all[p]; all[p] = all[from]; all[from] = x;
                    }
                    fixedPos[p] = true;
                }
            }
        }

        // ================================================================ 小さな道具
        public int LiveLeft() { return wallLiveLen - wallPos; }
        private int EffCount(int s) { return handLen[s] + 6 * meldN[s]; }
        private int NeedMelds(int s) { return 4 - meldN[s]; }

        public bool IsMenzen(int s)
        {
            for (int i = 0; i < meldN[s]; i++) if (meldOpen[s * 4 + i]) return false;
            return true;
        }

        public int NextActive(int seat)
        {
            for (int k = 1; k <= NP; k++)
            {
                int s = (seat + k) % NP;
                if (outRole[s] == ROLE_NONE) return s;
            }
            return seat;
        }

        private void AddHand(int s, int t) { handC[s * NT + t]++; handLen[s]++; }
        private void RemoveHand(int s, int t, int n) { handC[s * NT + t] -= n; handLen[s] -= n; }

        private void CopyHand(int s, int[] dst)
        {
            for (int t = 0; t < NT; t++) dst[t] = handC[s * NT + t];
        }

        // ================================================================ 和了形の分解(通常形)
        public const int MAXDEC = 256;
        [System.NonSerialized] public int decN, decM;
        [System.NonSerialized] public int[] decPair, decType, decStart;
        private int[] sw, sw2, tmpC, tmpC2, allC, ankTmp;
        private int[] curType, curStart;
        private int curN, curPair;
        private bool decFirstOnly;

        private int SumC(int[] c) { int s = 0; for (int i = 0; i < NT; i++) s += c[i]; return s; }

        /// <summary>通常形の分解をすべて decPair/decType/decStart に入れる(m = 必要な面子数)</summary>
        public int Decompose(int[] c, int m, bool firstOnly)
        {
            decN = 0; decM = m;
            if (SumC(c) != 6 * m + 4) return 0;
            for (int i = 0; i < NT; i++) sw[i] = c[i];
            curN = 0; curPair = -1; decFirstOnly = firstOnly;
            DecRec(0, m, false);
            return decN;
        }

        [RecursiveMethod]
        private void DecRec(int i, int left, bool hasPair)
        {
            if (decFirstOnly && decN > 0) return;
            while (i < NT && sw[i] == 0) i++;
            if (i == NT)
            {
                if (left == 0 && hasPair) RecordDec();
                return;
            }
            if (!hasPair && sw[i] >= 4)
            {
                sw[i] -= 4; curPair = i;
                DecRec(i, left, true);
                sw[i] += 4; curPair = -1;
            }
            if (left > 0)
            {
                if (sw[i] >= 6)
                {
                    sw[i] -= 6; curType[curN] = MT_K; curStart[curN] = i; curN++;
                    DecRec(i, left - 1, hasPair);
                    curN--; sw[i] += 6;
                }
                if (i < 36 && i % 9 <= 6 && sw[i] >= 2 && sw[i + 1] >= 2 && sw[i + 2] >= 2)
                {
                    sw[i] -= 2; sw[i + 1] -= 2; sw[i + 2] -= 2; curType[curN] = MT_T; curStart[curN] = i; curN++;
                    DecRec(i, left - 1, hasPair);
                    curN--; sw[i] += 2; sw[i + 1] += 2; sw[i + 2] += 2;
                }
                if (i < 36 && i % 9 <= 3 && sw[i + 1] > 0 && sw[i + 2] > 0 && sw[i + 3] > 0 && sw[i + 4] > 0 && sw[i + 5] > 0)
                {
                    for (int k = 0; k < 6; k++) sw[i + k]--;
                    curType[curN] = MT_F; curStart[curN] = i; curN++;
                    DecRec(i, left - 1, hasPair);
                    curN--;
                    for (int k = 0; k < 6; k++) sw[i + k]++;
                }
            }
        }

        private void RecordDec()
        {
            if (decN >= MAXDEC) return;
            decPair[decN] = curPair;
            for (int g = 0; g < curN; g++) { decType[decN * 4 + g] = curType[g]; decStart[decN * 4 + g] = curStart[g]; }
            decN++;
        }

        public bool IsChiitoi(int[] c)
        {
            int pairs = 0;
            for (int t = 0; t < NT; t++)
            {
                if (c[t] == 0) continue;
                if (c[t] != 2) return false;
                pairs++;
            }
            return pairs == 14;
        }

        public bool IsKokushi(int[] c)
        {
            int kinds = 0, total = 0;
            for (int t = 0; t < NT; t++)
            {
                if (c[t] == 0) continue;
                if (!IsYaochu(t)) return false;
                kinds++; total += c[t];
            }
            return kinds == 19 && total == 28;
        }

        public bool IsChuuren(int[] c)
        {
            int suit = -1, total = 0;
            for (int t = 0; t < NT; t++)
            {
                if (c[t] == 0) continue;
                if (t >= 36) return false;
                int s = t / 9;
                if (suit == -1) suit = s; else if (suit != s) return false;
                total += c[t];
            }
            if (suit < 0 || total != 28) return false;
            int b = suit * 9;
            if (c[b] < 4 || c[b + 8] < 4) return false;
            for (int k = 1; k <= 7; k++) if (c[b + k] < 2) return false;
            return true;
        }

        public bool IsAgari(int[] c, int m)
        {
            if (SumC(c) != 6 * m + 4) return false;
            if (m == 4 && (IsKokushi(c) || IsChuuren(c) || IsChiitoi(c))) return true;
            return Decompose(c, m, true) > 0;
        }

        // ================================================================ 聴牌・待ち(2枚足して和了になる組)
        [System.NonSerialized] public int waitN, waitTileN;
        [System.NonSerialized] public int[] waitA, waitB, waitTiles;
        private bool[] waitFound;
        private int[] jok; private int jokN;
        private int[] wKeepA, wKeepB; private int wKeepN;

        /// <summary>26枚相当の手 c(必要面子 m)に2枚足して和了になる組を waitA/waitB(ソート済み)と waitTiles に入れる</summary>
        public int FindWaits(int[] c, int m)
        {
            for (int k = 0; k < NT * 64; k++) waitFound[k] = false;
            if (SumC(c) == 6 * m + 2)
            {
                for (int i = 0; i < NT; i++) sw2[i] = c[i];
                jokN = 0;
                WaitRec(0, m, false);
                if (m == 4) SpecialWaits(c);
            }
            waitN = 0;
            for (int a = 0; a < NT; a++)
                for (int b = a; b < NT; b++)
                    if (waitFound[a * 64 + b]) { waitA[waitN] = a; waitB[waitN] = b; waitN++; }
            waitTileN = 0;
            for (int t = 0; t < NT; t++)
            {
                bool used = false;
                for (int k = 0; k < waitN && !used; k++) if (waitA[k] == t || waitB[k] == t) used = true;
                if (used) waitTiles[waitTileN++] = t;
            }
            return waitN;
        }

        [RecursiveMethod]
        private void WaitRec(int i, int left, bool hasPair)
        {
            while (i < NT && sw2[i] == 0) i++;
            if (i == NT)
            {
                if (left == 0 && hasPair && jokN == 2)
                {
                    int a = MinI(jok[0], jok[1]), b = MaxI(jok[0], jok[1]);
                    waitFound[a * 64 + b] = true;
                }
                return;
            }
            int budget = 2 - jokN;
            // 雀頭
            if (!hasPair)
            {
                int take = MinI(sw2[i], 4), miss = 4 - take;
                if (miss <= budget)
                {
                    sw2[i] -= take; for (int k = 0; k < miss; k++) jok[jokN++] = i;
                    WaitRec(i, left, true);
                    jokN -= miss; sw2[i] += take;
                }
            }
            if (left <= 0) return;
            // 刻子
            {
                int take = MinI(sw2[i], 6), miss = 6 - take;
                if (miss <= budget)
                {
                    sw2[i] -= take; for (int k = 0; k < miss; k++) jok[jokN++] = i;
                    WaitRec(i, left - 1, hasPair);
                    jokN -= miss; sw2[i] += take;
                }
            }
            if (i >= 36) return;
            int bse = i - (i % 9);
            // 対順子(i を含む開始位置)
            for (int s = MaxI(bse, i - 2); s <= i && s % 9 <= 6; s++)
            {
                int t0 = MinI(sw2[s], 2), t1 = MinI(sw2[s + 1], 2), t2 = MinI(sw2[s + 2], 2);
                int miss = 6 - t0 - t1 - t2;
                if (miss > budget) continue;
                sw2[s] -= t0; sw2[s + 1] -= t1; sw2[s + 2] -= t2;
                for (int k = 0; k < 2 - t0; k++) jok[jokN++] = s;
                for (int k = 0; k < 2 - t1; k++) jok[jokN++] = s + 1;
                for (int k = 0; k < 2 - t2; k++) jok[jokN++] = s + 2;
                WaitRec(i, left - 1, hasPair);
                jokN -= miss;
                sw2[s] += t0; sw2[s + 1] += t1; sw2[s + 2] += t2;
            }
            // 完順子
            for (int s = MaxI(bse, i - 5); s <= i && s % 9 <= 3; s++)
            {
                int miss = 0;
                for (int k = 0; k < 6; k++) if (sw2[s + k] == 0) miss++;
                if (miss > budget) continue;
                // took: 手牌から取った位置のビット(取れなかった位置はジョーカーで埋める)
                int took = 0;
                for (int k = 0; k < 6; k++)
                {
                    if (sw2[s + k] > 0) { sw2[s + k]--; took |= 1 << k; }
                    else jok[jokN++] = s + k;
                }
                WaitRec(i, left - 1, hasPair);
                jokN -= miss;
                for (int k = 0; k < 6; k++) if ((took & (1 << k)) != 0) sw2[s + k]++;
            }
        }

        private void SpecialWaits(int[] c)
        {
            // 十四対子
            bool ok = true; int pairs = 0, s0 = -1, s1 = -1, singles = 0;
            for (int t = 0; t < NT; t++)
            {
                if (c[t] > 2) { ok = false; break; }
                if (c[t] == 1) { if (singles == 0) s0 = t; else if (singles == 1) s1 = t; singles++; }
                if (c[t] == 2) pairs++;
            }
            if (ok)
            {
                if (pairs == 12 && singles == 2) waitFound[s0 * 64 + s1] = true;
                if (pairs == 13 && singles == 0) for (int t = 0; t < NT; t++) if (c[t] == 0) waitFound[t * 64 + t] = true;
            }
            // 国士無双・九蓮宝燈(候補が少ないので総当たり)
            bool allYao = true, mixed = false; int oneSuit = -1;
            for (int t = 0; t < NT; t++)
            {
                if (c[t] == 0) continue;
                if (!IsYaochu(t)) allYao = false;
                if (t >= 36) mixed = true;
                else { int s = t / 9; if (oneSuit == -1) oneSuit = s; else if (oneSuit != s) mixed = true; }
            }
            for (int i = 0; i < NT; i++) tmpC2[i] = c[i];
            if (allYao)
            {
                for (int a = 0; a < NT; a++)
                {
                    if (!IsYaochu(a)) continue;
                    for (int b = a; b < NT; b++)
                    {
                        if (!IsYaochu(b)) continue;
                        tmpC2[a]++; tmpC2[b]++;
                        if (IsKokushi(tmpC2)) waitFound[a * 64 + b] = true;
                        tmpC2[a]--; tmpC2[b]--;
                    }
                }
            }
            if (!mixed && oneSuit >= 0)
            {
                for (int x = 0; x < 9; x++)
                {
                    for (int y = x; y < 9; y++)
                    {
                        int a = oneSuit * 9 + x, b = oneSuit * 9 + y;
                        tmpC2[a]++; tmpC2[b]++;
                        if (IsChuuren(tmpC2)) waitFound[a * 64 + b] = true;
                        tmpC2[a]--; tmpC2[b]--;
                    }
                }
            }
        }

        /// <summary>席 s の手牌の待ち(結果は waitA/waitB/waitTiles)</summary>
        public int WaitsOf(int s)
        {
            CopyHand(s, tmpC);
            return FindWaits(tmpC, NeedMelds(s));
        }

        private bool RiverFuriten(int s, int mode)
        {
            if (mode == 0) return false;
            if (WaitsOf(s) == 0) return false;
            // 自分の河にある牌
            for (int t = 0; t < NT; t++) tmpC2[t] = 0;
            for (int i = 0; i < riverLen[s]; i++)
            {
                int r = s * MAXRIVER + i;
                tmpC2[rvT0[r]] = 1;
                if (rvN[r] == 2) tmpC2[rvT1[r]] = 1;
            }
            if (mode == 1)
            {
                for (int k = 0; k < waitN; k++) if (tmpC2[waitA[k]] == 1 && tmpC2[waitB[k]] == 1) return true;
                return false;
            }
            for (int k = 0; k < waitTileN; k++) if (tmpC2[waitTiles[k]] == 1) return true;
            return false;
        }

        public bool IsFuriten(int s, int mode) { return furitenPerm[s] || furitenTemp[s] || RiverFuriten(s, mode); }

        // ================================================================ 役の判定
        // 和了の文脈
        private int wcSeat, wcWinType, wcRonTakenN;
        private int[] wcRonTaken;
        private bool wcIsDealer, wcRiichi, wcDouble, wcIppatsu, wcAnyCall, wcFirstDraw, wcHaitei, wcHoutei, wcRinshan, wcChankan;
        private int wcSeatWind, wcOppWind, wcTurns;

        // 評価結果(いちばん良い形)
        [System.NonSerialized] public bool evOk, evYakuman;
        [System.NonSerialized] public int evHan, evYakuHan, evDora, evUra, evForm, evPair, evYakuN, evGN;
        [System.NonSerialized] public int[] evYakuId, evYakuHanArr, evGType, evGStart;

        // 候補ごとの作業領域
        private int[] gType, gStart, gKan; private bool[] gOpen, gConc; private int gN;
        private int[] cYakuId, cYakuHan; private int cYakuN;
        private bool cYakuman; private int cHan, cYakuHanSum, cDora, cUra;
        private int[] patMask;
        private bool cMenzen;
        private int[] uniqBuf;

        private void SetWinCtx(int s, int winTypeArg, bool tsumo)
        {
            wcSeat = s; wcWinType = winTypeArg;
            wcIsDealer = s == dealer;
            wcSeatWind = SeatWindOf(s, dealer); wcOppWind = OppositeWindOf(s, dealer);
            wcRiichi = riichiOn[s]; wcDouble = riichiDouble[s]; wcIppatsu = riichiIppatsu[s];
            wcTurns = turns[s]; wcAnyCall = anyCall;
            wcFirstDraw = tsumo && turns[s] == 0 && !anyCall && !isRinshan;
            wcHaitei = tsumo && LiveLeft() == 0 && !isRinshan;
            wcHoutei = !tsumo && dcActive && dcHoutei;
            wcRinshan = tsumo && isRinshan;
            wcChankan = !tsumo && dcActive && dcChankan;
        }

        private int YakuhaiCount(int t)
        {
            int n = 0;
            if (IsDragon(t)) n++;
            if (t == wcSeatWind) n++;
            if (t == ROUND_WIND) n++;
            if (t == wcOppWind) n++;
            return n;
        }

        private int SansuuSum(int type, int start)
        {
            int k = NumOf(start);
            if (type == MT_K) return 6 * k;
            if (type == MT_T) return 6 * (k + 1);
            return 6 * k + 15;
        }

        private bool GroupHasYaochu(int type, int start)
        {
            if (type == MT_K) return IsYaochu(start);
            int k = NumOf(start);
            if (type == MT_T) return k == 1 || k == 7;
            return k == 1 || k == 4;
        }

        private bool GroupHasTerminal(int type, int start)
        {
            if (type == MT_K) return IsTerminal(start);
            return GroupHasYaochu(type, start);
        }

        /// <summary>
        /// 和了の評価。hc = 手牌(副露以外)の枚数、席 wcSeat の副露を使う。和了形でなければ evOk=false。
        /// 役がなければ evYakuHan=0(呼ぶ側で不可にする)
        /// </summary>
        private void EvaluateWin(int[] hc)
        {
            int s = wcSeat;
            int m = 4 - meldN[s];
            cMenzen = IsMenzen(s);
            for (int t = 0; t < NT; t++) allC[t] = hc[t];
            for (int i = 0; i < meldN[s]; i++)
            {
                int mi = s * 4 + i;
                AddNeeds(allC, meldType[mi], meldStart[mi]);
            }
            for (int i = 0; i < meldN[s]; i++) if (meldKan[s * 4 + i] != KAN_NONE) allC[meldStart[s * 4 + i]] += 1;

            evOk = false;
            int nd = Decompose(hc, m, false);
            for (int d = 0; d < nd; d++)
            {
                gN = 0;
                for (int i = 0; i < meldN[s]; i++)
                {
                    int mi = s * 4 + i;
                    gType[gN] = meldType[mi]; gStart[gN] = meldStart[mi]; gOpen[gN] = meldOpen[mi]; gKan[gN] = meldKan[mi]; gConc[gN] = !meldOpen[mi]; gN++;
                }
                for (int g = 0; g < m; g++)
                {
                    int ty = decType[d * 4 + g], st = decStart[d * 4 + g];
                    bool conc = ty != MT_K || wcWinType == WT_TSUMO || !RonTakenHas(st);
                    gType[gN] = ty; gStart[gN] = st; gOpen[gN] = false; gKan[gN] = KAN_NONE; gConc[gN] = conc; gN++;
                }
                ScoreCandidate(FORM_NORMAL, decPair[d]);
            }
            if (m == 4)
            {
                gN = 0;
                if (IsKokushi(hc)) ScoreCandidate(FORM_KOKUSHI, -1);
                if (IsChuuren(hc)) ScoreCandidate(FORM_CHUUREN, -1);
                if (IsChiitoi(hc)) ScoreCandidate(FORM_CHIITOI, -1);
            }
        }

        private bool RonTakenHas(int t)
        {
            for (int i = 0; i < wcRonTakenN; i++) if (wcRonTaken[i] == t) return true;
            return false;
        }

        private void AddNeeds(int[] c, int type, int start)
        {
            if (type == MT_K) c[start] += 6;
            else if (type == MT_T) { c[start] += 2; c[start + 1] += 2; c[start + 2] += 2; }
            else for (int k = 0; k < 6; k++) c[start + k] += 1;
        }

        private void AddY(int id, int times)
        {
            int h = cMenzen ? yakuClosed[id] : yakuOpen[id];
            if (h == 0 || times == 0) return;
            cYakuId[cYakuN] = id; cYakuHan[cYakuN] = h * times; cYakuN++;
        }

        private void PushY(int id, int han) { cYakuId[cYakuN] = id; cYakuHan[cYakuN] = han; cYakuN++; }

        private void ScoreCandidate(int form, int pair)
        {
            cYakuN = 0; cYakuman = false; cDora = 0; cUra = 0;
            bool allHonor = true, allYao = true, hasHonor = false, allTerm = true, noYao = true;
            int suitMask = 0;
            for (int t = 0; t < NT; t++)
            {
                if (allC[t] == 0) continue;
                if (t < 36) { allHonor = false; suitMask |= 1 << (t / 9); }
                else hasHonor = true;
                if (!IsYaochu(t)) allYao = false; else noYao = false;
                if (!IsTerminal(t)) allTerm = false;
            }
            int nSuits = 0;
            for (int k = 0; k < 4; k++) if ((suitMask & (1 << k)) != 0) nSuits++;
            bool isTsumo = wcWinType == WT_TSUMO;

            // ---- 役満 ----
            if (form == FORM_KOKUSHI) PushY(Y_KOKUSHI, 0);
            if (form == FORM_CHUUREN && cMenzen) PushY(Y_CHUUREN, 0);
            if (wcFirstDraw && isTsumo && wcIsDealer) PushY(Y_TENHOU, 0);
            if (wcFirstDraw && isTsumo && !wcIsDealer && !wcAnyCall) PushY(Y_CHIIHOU, 0);
            if (allHonor) PushY(Y_TSUUIISOU, 0);
            if (form == FORM_NORMAL)
            {
                if (HasK(43) && HasK(44) && HasK(45) && HasK(46)) PushY(Y_FURINKAZAN, 0);
                if (HasK(40) && HasK(41) && HasK(42)) PushY(Y_DAISANGEN, 0);
                if (HasK(36) && HasK(37) && HasK(38) && HasK(39)) PushY(Y_DAISUUSHII, 0);
                int concK = 0, kans = 0;
                for (int g = 0; g < gN; g++) { if (gType[g] == MT_K && gConc[g]) concK++; if (gKan[g] != KAN_NONE) kans++; }
                if (concK == 4) PushY(Y_SUUANKOU, 0);
                if (allTerm) PushY(Y_CHINROUTOU, 0);
                if (kans == 4) PushY(Y_SUUKANTSU, 0);
                if (SanyouSame(4)) PushY(Y_SANYOU, 0);
            }
            if (cYakuN > 0)
            {
                cYakuman = true; cHan = 0; cYakuHanSum = 1;
                Consider(form, pair);
                return;
            }

            // ---- 通常役 ----
            if (wcRiichi) AddY(wcDouble ? Y_DOUBLERIICHI : Y_RIICHI, 1);
            if (wcRiichi && wcIppatsu) AddY(Y_IPPATSU, 1);
            if (isTsumo && cMenzen) AddY(Y_MENZENTSUMO, 1);
            if (noYao) AddY(Y_TANYAO, 1);
            if (cMenzen && (rHayashi == 2 || (rHayashi == 1 && !wcRiichi))) AddY(Y_HAYASHI, 1);
            if (wcTurns < rHayakiTurns && !wcAnyCall) AddY(Y_HAYAKI, 1);
            if (wcRinshan) AddY(Y_RINSHAN, 1);
            if (wcChankan) AddY(Y_CHANKAN, 1);
            if (wcHaitei && isTsumo) AddY(Y_HAITEI, 1);
            if (wcHoutei && !isTsumo) AddY(Y_HOUTEI, 1);
            if (wcWinType == WT_RON2) AddY(Y_NIMAIRON, 1);
            if (allYao && !allHonor) AddY(Y_HONROUTOU, 1);
            if (nSuits == 1 && hasHonor) AddY(Y_HONITSU, 1);
            if (nSuits == 1 && !hasHonor) AddY(Y_CHINITSU, 1);

            if (form == FORM_CHIITOI)
            {
                AddY(Y_CHIITOI, 1);
            }
            else
            {
                int nK = 0, nSeq = 0, yh = 0, openCount = 0, ankou = 0, dragonK = 0, closedF = 0, nF = 0;
                for (int g = 0; g < gN; g++)
                {
                    if (gType[g] == MT_K)
                    {
                        nK++; yh += YakuhaiCount(gStart[g]);
                        if (gConc[g]) ankou++;
                        if (IsDragon(gStart[g])) dragonK++;
                    }
                    else nSeq++;
                    if (gOpen[g] && gKan[g] != KAN_ANKAN) openCount++;
                    if (gType[g] == MT_F) { nF++; if (!gOpen[g]) closedF++; }
                }
                if (yh > 0) PushY(Y_YAKUHAI, (cMenzen ? yakuClosed[Y_YAKUHAI] : yakuOpen[Y_YAKUHAI]) * yh);
                if (nSeq == 4 && YakuhaiCount(pair) == 0) AddY(Y_JUNKO, 1);
                if (openCount >= 3) AddY(Y_SHINRYAKU, 1);
                if (nK == 4) AddY(Y_TOITOI, 1);
                if (ankou == 3) AddY(Y_SANANKOU, 1);
                // 三色・四色同順(対順子か完順子かもそろえる)
                for (int k = 0; k < 20; k++) patMask[k] = 0;
                for (int g = 0; g < gN; g++)
                {
                    if (gType[g] == MT_K) continue;
                    int key = (gType[g] == MT_T ? 0 : 10) + NumOf(gStart[g]);
                    patMask[key] |= 1 << SuitOf(gStart[g]);
                }
                int maxSuits = 0;
                for (int k = 0; k < 20; k++)
                {
                    int cnt = 0;
                    for (int b = 0; b < 4; b++) if ((patMask[k] & (1 << b)) != 0) cnt++;
                    if (cnt > maxSuits) maxSuits = cnt;
                }
                if (maxSuits >= 4) AddY(Y_YONSHOKU, 1);
                else if (maxSuits == 3) AddY(Y_SANSHOKU, 1);
                // 一気通貫
                for (int st = 0; st < 4; st++)
                {
                    int b = st * 9;
                    if ((HasSeq(MT_F, b) && HasSeq(MT_F, b + 3)) || (HasSeq(MT_T, b) && HasSeq(MT_T, b + 3) && HasSeq(MT_T, b + 6))) { AddY(Y_ITTSUU, 1); break; }
                }
                // 小三元
                if (dragonK == 2 && IsDragon(pair)) AddY(Y_SHOUSANGEN, 1);
                // 混全帯・純全帯
                bool everyYao = true, everyTerm = true;
                for (int g = 0; g < gN; g++)
                {
                    if (!GroupHasYaochu(gType[g], gStart[g])) everyYao = false;
                    if (!GroupHasTerminal(gType[g], gStart[g])) everyTerm = false;
                }
                if (!allYao && everyYao && IsYaochu(pair))
                {
                    if (hasHonor) AddY(Y_CHANTA, 1);
                    else if (everyTerm && IsTerminal(pair)) AddY(Y_JUNCHAN, 1);
                }
                // 算数
                int sgN = 0;
                for (int g = 0; g < gN; g++) if (gStart[g] >= 27 && gStart[g] < 36) sgN++;
                if (sgN >= 2 && SanyouSame(sgN)) AddY(sgN >= 3 ? Y_SANSUU3 : Y_SANSUU2, 1);
                // 完順子ボーナス
                int kTimes = rKanjunMode == 2 ? closedF : rKanjunMode == 1 ? MinI(2, closedF / 2) : 0;
                if (kTimes > 0) PushY(Y_KANJUN, yakuClosed[Y_KANJUN] * kTimes);
                // 完全完順
                if (nF == 4) AddY(Y_KANZEN, 1);
            }

            int yakuHan = 0;
            for (int k = 0; k < cYakuN; k++) yakuHan += cYakuHan[k];
            for (int k = 0; k < doraIndN; k++) cDora += allC[DoraFromIndicator(doraInd[k])];
            if (wcRiichi && rUseUra) for (int k = 0; k < uraIndN; k++) cUra += allC[DoraFromIndicator(uraInd[k])];
            int pen = wcWinType == WT_RON1 ? MinI(rKataRonPenalty, yakuHan - 1) : 0;
            if (pen > 0) PushY(Y_KATARON_PENALTY, -pen);
            if (cDora > 0) PushY(Y_DORA, cDora);
            if (cUra > 0) PushY(Y_URA, cUra);
            cYakuHanSum = yakuHan;
            cHan = yakuHan - MaxI(0, pen) + cDora + cUra;
            cYakuman = false;
            Consider(form, pair);
        }

        private bool HasK(int t)
        {
            for (int g = 0; g < gN; g++) if (gType[g] == MT_K && gStart[g] == t) return true;
            return false;
        }

        private bool HasSeq(int type, int start)
        {
            for (int g = 0; g < gN; g++) if (gType[g] == type && gStart[g] == start) return true;
            return false;
        }

        /// <summary>算用数牌の面子がちょうど n 個あり、数字の合計がすべて同じか</summary>
        private bool SanyouSame(int n)
        {
            int cnt = 0, sum0 = -1;
            bool same = true;
            for (int g = 0; g < gN; g++)
            {
                if (gStart[g] < 27 || gStart[g] >= 36) continue;
                int v = SansuuSum(gType[g], gStart[g]);
                if (sum0 < 0) sum0 = v; else if (v != sum0) same = false;
                cnt++;
            }
            return cnt == n && same;
        }

        /// <summary>候補をいちばん良い形と比べて、良ければ採用する</summary>
        private void Consider(int form, int pair)
        {
            bool take;
            if (!evOk) take = true;
            else if (cYakuman != evYakuman) take = cYakuman;
            else if ((cYakuHanSum > 0) != (evYakuHan > 0)) take = cYakuHanSum > 0;
            else take = cHan > evHan;
            if (!take) return;
            evOk = true; evYakuman = cYakuman; evHan = cHan; evYakuHan = cYakuHanSum; evDora = cDora; evUra = cUra;
            evForm = form; evPair = pair; evYakuN = cYakuN;
            for (int k = 0; k < cYakuN; k++) { evYakuId[k] = cYakuId[k]; evYakuHanArr[k] = cYakuHan[k]; }
            evGN = gN;
            for (int g = 0; g < gN; g++) { evGType[g] = gType[g]; evGStart[g] = gStart[g]; }
        }

        private bool ValidWin() { return evOk && (evYakuman || evYakuHan > 0); }

        /// <summary>
        /// パッドの表示用: 席 q が a と b(片ロンなら捨て牌とロン牌)で和了ると、どうなるか(状態は変えない)。
        /// 0 = 形にならない、1 = 形はそろうが役が無い(ドラだけでは和了れない)、2 = 和了れる
        /// </summary>
        public int WaitHint(int q, int a, int b)
        {
            CopyHand(q, tmpC);
            tmpC[a]++;
            if (b >= 0) tmpC[b]++;
            if (!IsAgari(tmpC, NeedMelds(q))) return 0;
            wcRonTakenN = 0;
            wcRonTaken[wcRonTakenN++] = a;
            if (b >= 0) wcRonTaken[wcRonTakenN++] = b;
            SetWinCtx(q, WT_RON1, false);
            CopyHand(q, tmpC);
            tmpC[a]++;
            if (b >= 0) tmpC[b]++;
            EvaluateWin(tmpC);
            return ValidWin() ? 2 : 1;
        }

        /// <summary>席 q の手牌(26 枚のとき)の待ち。waitN 組(waitA[k], waitB[k])を返す(状態は変えない)</summary>
        public int PadWaits(int q)
        {
            if (EffCount(q) != 26) { waitN = 0; return 0; }
            return WaitsOf(q);
        }

        // ================================================================ 点数
        private int CeilDiv(int a, int b) { return (a + b - 1) / b; }

        public int BasePoints(int han, bool yakuman)
        {
            if (yakuman) return rYakumanPoint;
            int h = MinI(han, rCapHan);
            if (h <= 0) return 0;
            int v = rBasePoint;
            for (int k = 1; k < h; k++) v *= 2;
            return v;
        }

        public int Mult10(bool isDealer, bool isFirst)
        {
            if (isDealer) return isFirst ? rMultDealerFirst : rMultDealerSecond;
            return isFirst ? rMultChildFirst : rMultChildSecond;
        }

        /// <summary>ロンの支払い(本場を除く) = base*m10/10 を100点単位に切り上げ</summary>
        public int RonPayment(int bse, int m10) { return CeilDiv(bse * m10, 1000) * 100; }

        // ================================================================ 選択肢
        private int[] optTmp;

        private void OptClear(int[] o)
        {
            for (int k = 0; k < OPT; k++) o[k] = 0;
            o[O_MODE] = -1; o[O_TILE] = -1; o[O_RONIDX] = -1; o[O_RONTILE] = -1; o[O_MELDTYPE] = MT_NONE; o[O_START] = -1;
        }

        private void LegalPush(int[] o)
        {
            if (legalN >= MAXOPT) return;
            for (int k = 0; k < OPT; k++) legal[legalN * OPT + k] = o[k];
            legalN++;
        }

        /// <summary>いま、その席ができるアクションを legal に入れて個数を返す</summary>
        public int BuildLegal(int seat)
        {
            legalN = 0;
            if (phase == PH_TURN && seat == turn) { TurnOptions(seat); return legalN; }
            if (phase == PH_CALLS && hasCall[seat] && !responded[seat])
            {
                for (int i = 0; i < coN[seat]; i++)
                {
                    int bse = (seat * MAXOPT + i) * OPT;
                    for (int k = 0; k < OPT; k++) optTmp[k] = coBuf[bse + k];
                    LegalPush(optTmp);
                }
                OptClear(optTmp); optTmp[O_TYPE] = A_PASS; optTmp[O_SEAT] = seat; LegalPush(optTmp);
                return legalN;
            }
            if (phase == PH_KYOKU_OVER)
            {
                if (seat == dealer && CanAgariYame())
                {
                    OptClear(optTmp); optTmp[O_TYPE] = A_NEXT; optTmp[O_SEAT] = seat; optTmp[O_MODE] = 1; LegalPush(optTmp);
                    OptClear(optTmp); optTmp[O_TYPE] = A_NEXT; optTmp[O_SEAT] = seat; optTmp[O_MODE] = 0; LegalPush(optTmp);
                }
                else
                {
                    OptClear(optTmp); optTmp[O_TYPE] = A_NEXT; optTmp[O_SEAT] = seat; LegalPush(optTmp);
                }
            }
            return legalN;
        }

        private bool CanKanNow() { return kanCount < rMaxKan && wallRinshanPos < wallRinshanLen; }

        private bool AnkanKeepsWaits(int s, int t)
        {
            int cnt = 0;
            for (int i = 0; i < drawnLen[s]; i++) if (drawn[s * MAXDRAWN + i] == t) cnt++;
            if (cnt != 1) return false;
            CopyHand(s, tmpC);
            for (int i = 0; i < drawnLen[s]; i++)
            {
                int d = drawn[s * MAXDRAWN + i];
                if (tmpC[d] <= 0) return false;
                tmpC[d]--;
            }
            // 前: 26枚相当の待ち
            for (int k = 0; k < NT; k++) ankTmp[k] = tmpC[k];
            int before = FindWaits(tmpC, NeedMelds(s));
            wKeepN = waitN;
            for (int k = 0; k < waitN; k++) { wKeepA[k] = waitA[k]; wKeepB[k] = waitB[k]; }
            // 後: 6枚を暗槓に回す
            for (int k = 0; k < NT; k++) tmpC[k] = ankTmp[k];
            if (tmpC[t] < 6) return false;
            tmpC[t] -= 6;
            int after = FindWaits(tmpC, NeedMelds(s) - 1);
            if (before == 0 || after != before) return false;
            for (int k = 0; k < waitN; k++) if (waitA[k] != wKeepA[k] || waitB[k] != wKeepB[k]) return false;
            return true;
        }

        private void TurnOptions(int s)
        {
            CopyHand(s, tmpC);
            if (!afterCall)
            {
                // ツモ和了
                if (EffCount(s) == 28)
                {
                    CopyHand(s, tmpC);
                    if (IsAgari(tmpC, NeedMelds(s)))
                    {
                        wcRonTakenN = 0;
                        SetWinCtx(s, WT_TSUMO, true);
                        CopyHand(s, tmpC);
                        EvaluateWin(tmpC);
                        if (ValidWin())
                        {
                            int bse = BasePoints(evHan, evYakuman);
                            OptClear(optTmp);
                            optTmp[O_TYPE] = A_TSUMO; optTmp[O_SEAT] = s; optTmp[O_HAN] = evHan; optTmp[O_YAKUMAN] = evYakuman ? 1 : 0;
                            optTmp[O_POINTS] = (bse * Mult10(s == dealer, first < 0) + 5) / 10;
                            LegalPush(optTmp);
                        }
                    }
                }
                if (CanKanNow() && LiveLeft() > 0 && mustDiscard == 2)
                {
                    for (int t = 0; t < NT; t++)
                    {
                        if (handC[s * NT + t] >= 7 && (!riichiOn[s] || AnkanKeepsWaits(s, t)))
                        {
                            OptClear(optTmp); optTmp[O_TYPE] = A_KAN; optTmp[O_SEAT] = s; optTmp[O_MODE] = KAN_ANKAN; optTmp[O_TILE] = t;
                            LegalPush(optTmp);
                        }
                    }
                    if (!riichiOn[s])
                    {
                        for (int i = 0; i < meldN[s]; i++)
                        {
                            int mi = s * 4 + i;
                            if (meldType[mi] == MT_K && meldOpen[mi] && meldKan[mi] == KAN_NONE && handC[s * NT + meldStart[mi]] >= 1)
                            {
                                OptClear(optTmp); optTmp[O_TYPE] = A_KAN; optTmp[O_SEAT] = s; optTmp[O_MODE] = KAN_KAKAN; optTmp[O_TILE] = meldStart[mi];
                                LegalPush(optTmp);
                            }
                        }
                    }
                }
                if (turns[s] == 0 && !anyCall && !isRinshan)
                {
                    int kinds = 0;
                    for (int t = 0; t < NT; t++) if (handC[s * NT + t] > 0 && IsYaochu(t)) kinds++;
                    if (kinds >= 14)
                    {
                        OptClear(optTmp); optTmp[O_TYPE] = A_ABORT; optTmp[O_SEAT] = s; optTmp[O_TILE] = REASON_KYUSHU;
                        LegalPush(optTmp);
                    }
                }
            }
            OptClear(optTmp);
            optTmp[O_TYPE] = A_DISCARD; optTmp[O_SEAT] = s; optTmp[O_COUNT] = mustDiscard;
            if (riichiOn[s])
            {
                int n = MinI(mustDiscard, drawnLen[s]);
                optTmp[O_FIXEDN] = n;
                for (int k = 0; k < n; k++) optTmp[O_FIXED + k] = drawn[s * MAXDRAWN + drawnLen[s] - n + k];
            }
            else
            {
                optTmp[O_FIXEDN] = -1;
                bool ok = IsMenzen(s) && !afterCall && mustDiscard == 2 && score[s] >= rRiichiMinScore && LiveLeft() >= 2;
                optTmp[O_RIICHIOK] = ok ? 1 : 0;
                optTmp[O_KUIKAEN] = kuikaeN;
                for (int k = 0; k < kuikaeN; k++) optTmp[O_KUIKAE + k] = kuikae[k];
            }
            LegalPush(optTmp);
        }

        // ---- 宣言(ロン・ポン・チー・大明槓) ----
        private void CoPush(int seat, int[] o)
        {
            if (coN[seat] >= MAXOPT) return;
            int bse = (seat * MAXOPT + coN[seat]) * OPT;
            for (int k = 0; k < OPT; k++) coBuf[bse + k] = o[k];
            coN[seat]++;
        }

        private bool TryRonWin(int q, int a, int b, int winTypeArg)
        {
            CopyHand(q, tmpC);
            tmpC[a]++;
            if (b >= 0) tmpC[b]++;
            if (!IsAgari(tmpC, NeedMelds(q))) return false;
            wcRonTakenN = 0;
            wcRonTaken[wcRonTakenN++] = a;
            if (b >= 0) wcRonTaken[wcRonTakenN++] = b;
            SetWinCtx(q, winTypeArg, false);
            CopyHand(q, tmpC);
            tmpC[a]++;
            if (b >= 0) tmpC[b]++;
            EvaluateWin(tmpC);
            return ValidWin();
        }

        private int RolePoints(int seat)
        {
            int bse = BasePoints(evHan, evYakuman);
            return RonPayment(bse, Mult10(seat == dealer, first < 0));
        }

        private void RonOptions(int q)
        {
            int start = coN[q];
            if (outRole[q] != ROLE_NONE || EffCount(q) != 26) return;
            if (dcN == 2 && TryRonWin(q, dcT[0], dcT[1], WT_RON2))
            {
                OptClear(optTmp);
                optTmp[O_TYPE] = A_RON; optTmp[O_SEAT] = q; optTmp[O_MODE] = RON_TWO;
                optTmp[O_HAN] = evHan; optTmp[O_YAKUMAN] = evYakuman ? 1 : 0; optTmp[O_POINTS] = RolePoints(q);
                CoPush(q, optTmp);
            }
            int nu = UniqDiscard();
            for (int u = 0; u < nu; u++)
            {
                int x = uniqBuf[u];
                for (int i = 0; i < ronTilesLen; i++)
                {
                    int r = ronTiles[i];
                    if (r < 0) continue;
                    bool seen = false;
                    for (int j = 0; j < i; j++) if (ronTiles[j] == r) seen = true;
                    if (seen) continue;
                    if (TryRonWin(q, x, r, WT_RON1))
                    {
                        OptClear(optTmp);
                        optTmp[O_TYPE] = A_RON; optTmp[O_SEAT] = q; optTmp[O_MODE] = RON_ONE; optTmp[O_TILE] = x; optTmp[O_RONIDX] = i; optTmp[O_RONTILE] = r;
                        optTmp[O_HAN] = evHan; optTmp[O_YAKUMAN] = evYakuman ? 1 : 0; optTmp[O_POINTS] = RolePoints(q);
                        CoPush(q, optTmp);
                    }
                }
            }
            if (coN[q] > start && IsFuriten(q, rFuriten))
            {
                furitenBlocked++;
                coN[q] = start;
            }
        }

        /// <summary>直前の打牌の牌を、出てきた順に重複なしで uniqBuf に入れる</summary>
        private int UniqDiscard()
        {
            int n = 0;
            for (int i = 0; i < dcN; i++)
            {
                bool dup = false;
                for (int j = 0; j < n; j++) if (uniqBuf[j] == dcT[i]) dup = true;
                if (!dup) uniqBuf[n++] = dcT[i];
            }
            return n;
        }

        private void MeldOptions(int q, int discarder)
        {
            int nu = UniqDiscard();
            for (int u = 0; u < nu; u++)
            {
                int t = uniqBuf[u];
                int dct = 0;
                for (int i = 0; i < dcN; i++) if (dcT[i] == t) dct++;
                for (int k = 1; k <= dct; k++)
                {
                    int need = 6 - k;
                    if (handC[q * NT + t] >= need)
                    {
                        OptClear(optTmp);
                        optTmp[O_TYPE] = A_PON; optTmp[O_SEAT] = q; optTmp[O_MELDTYPE] = MT_K; optTmp[O_START] = t;
                        optTmp[O_TAKEN] = k; for (int x = 0; x < k; x++) optTmp[O_TAKE + x] = t;
                        optTmp[O_GIVEN] = need; for (int x = 0; x < need; x++) optTmp[O_GIVE + x] = t;
                        CoPush(q, optTmp);
                    }
                }
                if (handC[q * NT + t] >= 6 && CanKanNow())
                {
                    OptClear(optTmp);
                    optTmp[O_TYPE] = A_KAN; optTmp[O_SEAT] = q; optTmp[O_MODE] = KAN_DAIMINKAN; optTmp[O_MELDTYPE] = MT_K; optTmp[O_START] = t; optTmp[O_TILE] = t;
                    optTmp[O_TAKEN] = 1; optTmp[O_TAKE] = t;
                    optTmp[O_GIVEN] = 6; for (int x = 0; x < 6; x++) optTmp[O_GIVE + x] = t;
                    CoPush(q, optTmp);
                }
            }
            if (q != (discarder + 1) % NP) return;
            int nSub = dcN == 2 ? 3 : 1;
            int chiStart = coN[q];
            for (int sub = 0; sub < nSub; sub++)
            {
                int sa, sb, sn;
                if (dcN == 2) { if (sub == 0) { sa = dcT[0]; sb = -1; sn = 1; } else if (sub == 1) { sa = dcT[1]; sb = -1; sn = 1; } else { sa = dcT[0]; sb = dcT[1]; sn = 2; } }
                else { sa = dcT[0]; sb = -1; sn = 1; }
                if (sa >= 36 || (sn == 2 && sb >= 36)) continue;
                int suit = sa / 9;
                if (sn == 2 && sb / 9 != suit) continue;
                int b = suit * 9;
                for (int ty = MT_T; ty <= MT_F; ty++)
                {
                    int span = ty == MT_T ? 3 : 6;
                    for (int st = b; st + span - 1 <= b + 8; st++)
                    {
                        for (int k = 0; k < NT; k++) tmpC2[k] = 0;
                        AddNeeds(tmpC2, ty, st);
                        bool ok = true;
                        if (--tmpC2[sa] < 0) ok = false;
                        if (sn == 2 && --tmpC2[sb] < 0) ok = false;
                        if (!ok) continue;
                        int gn = 0;
                        bool have = true;
                        OptClear(optTmp);
                        for (int t = st; t < st + span; t++)
                        {
                            for (int k = 0; k < tmpC2[t]; k++) optTmp[O_GIVE + gn++] = t;
                            if (handC[q * NT + t] < tmpC2[t]) have = false;
                        }
                        if (!have) continue;
                        // 同じ面子・同じ取り方の重複を除く
                        bool dup = false;
                        for (int i = chiStart; i < coN[q]; i++)
                        {
                            int bse = (q * MAXOPT + i) * OPT;
                            if (coBuf[bse + O_MELDTYPE] == ty && coBuf[bse + O_START] == st && coBuf[bse + O_TAKEN] == sn
                                && coBuf[bse + O_TAKE] == sa && (sn == 1 || coBuf[bse + O_TAKE + 1] == sb)) dup = true;
                        }
                        if (dup) continue;
                        optTmp[O_TYPE] = A_CHI; optTmp[O_SEAT] = q; optTmp[O_MELDTYPE] = ty; optTmp[O_START] = st;
                        optTmp[O_TAKEN] = sn; optTmp[O_TAKE] = sa; if (sn == 2) optTmp[O_TAKE + 1] = sb;
                        optTmp[O_GIVEN] = gn;
                        CoPush(q, optTmp);
                    }
                }
            }
        }

        private bool ComputeCallOptions()
        {
            bool any = false;
            for (int q = 0; q < NP; q++)
            {
                coN[q] = 0; hasCall[q] = false; responded[q] = false;
                if (q == dcSeat || outRole[q] != ROLE_NONE) continue;
                RonOptions(q);
                if (!dcChankan && !dcHoutei && !riichiOn[q]) MeldOptions(q, dcSeat);
                if (coN[q] > 0) { hasCall[q] = true; any = true; }
            }
            return any;
        }

        // ================================================================ 適用
        private bool SameOption(int[] a, int aBase, int[] b, int bBase)
        {
            if (a[aBase + O_TYPE] != b[bBase + O_TYPE]) return false;
            if (a[aBase + O_MODE] != b[bBase + O_MODE]) return false;
            if (a[aBase + O_TILE] != b[bBase + O_TILE]) return false;
            if (a[aBase + O_RONIDX] != b[bBase + O_RONIDX]) return false;
            if (a[aBase + O_MELDTYPE] != b[bBase + O_MELDTYPE]) return false;
            if (a[aBase + O_START] != b[bBase + O_START]) return false;
            int tn = a[aBase + O_TAKEN];
            if (tn != b[bBase + O_TAKEN]) return false;
            for (int k = 0; k < tn; k++) if (a[aBase + O_TAKE + k] != b[bBase + O_TAKE + k]) return false;
            int gn = a[aBase + O_GIVEN];
            if (gn != b[bBase + O_GIVEN]) return false;
            for (int k = 0; k < gn; k++) if (a[aBase + O_GIVE + k] != b[bBase + O_GIVE + k]) return false;
            return true;
        }

        private bool Fail(string msg) { lastError = msg; return false; }

        /// <summary>アクション(OPT 個の int)を適用する。できなければ false(lastError に理由)</summary>
        public bool ApplyAction(int[] act)
        {
            lastError = "";
            int seat = act[O_SEAT];
            int n = BuildLegal(seat);
            if (n == 0) return Fail("いまはその席の番ではありません");
            int ty = act[O_TYPE];
            if (phase == PH_TURN)
            {
                if (ty == A_DISCARD)
                {
                    int oi = -1;
                    for (int i = 0; i < n; i++) if (legal[i * OPT + O_TYPE] == A_DISCARD) oi = i;
                    int ob = oi * OPT;
                    int cnt = act[O_TAKEN];
                    if (cnt != mustDiscard) return Fail(mustDiscard + "枚切ってください");
                    int ta = act[O_TAKE], tb = cnt == 2 ? act[O_TAKE + 1] : -1;
                    if (handC[seat * NT + ta] < 1 || (tb >= 0 && handC[seat * NT + tb] < (tb == ta ? 2 : 1))) return Fail("手牌にない牌です");
                    if (legal[ob + O_FIXEDN] >= 0)
                    {
                        int fn = legal[ob + O_FIXEDN];
                        int fa = fn > 0 ? legal[ob + O_FIXED] : -1, fb = fn > 1 ? legal[ob + O_FIXED + 1] : -1;
                        bool same = cnt == fn && ((ta == fa && tb == fb) || (ta == fb && tb == fa));
                        if (!same) return Fail("リーチ中はツモ切りです");
                    }
                    for (int k = 0; k < kuikaeN; k++) if (kuikae[k] == ta || kuikae[k] == tb) return Fail("喰い替え禁止です");
                    bool riichi = act[O_MODE] == 1;
                    if (riichi && legal[ob + O_RIICHIOK] != 1) return Fail("リーチできません");
                    if (!DoDiscard(seat, ta, tb, cnt, riichi)) return false;
                }
                else
                {
                    int oi = -1;
                    for (int i = 0; i < n && oi < 0; i++) if (SameOption(legal, i * OPT, act, 0)) oi = i;
                    if (oi < 0) return Fail("その操作はできません");
                    if (ty == A_TSUMO) DoTsumo(seat);
                    else if (ty == A_KAN) DoSelfKan(seat, act[O_MODE], act[O_TILE]);
                    else if (ty == A_ABORT) { resSeat = seat; EndKyoku(RES_ABORT, act[O_TILE]); }
                }
            }
            else if (phase == PH_CALLS)
            {
                int oi = -1;
                if (ty == A_PASS) oi = n - 1;
                else for (int i = 0; i < n && oi < 0; i++) if (SameOption(legal, i * OPT, act, 0)) oi = i;
                if (oi < 0) return Fail("その宣言はできません");
                bool hadRon = false;
                for (int i = 0; i < coN[seat]; i++) if (coBuf[(seat * MAXOPT + i) * OPT + O_TYPE] == A_RON) hadRon = true;
                if (ty == A_PASS && hadRon)
                {
                    furitenTemp[seat] = true;                    // 同巡内フリテン
                    if (riichiOn[seat]) furitenPerm[seat] = true; // リーチ後フリテン
                }
                for (int k = 0; k < OPT; k++) respBuf[seat * OPT + k] = legal[oi * OPT + k];
                responded[seat] = true;
                bool all = true;
                for (int s = 0; s < NP; s++) if (hasCall[s] && !responded[s]) all = false;
                if (all) ResolveCalls();
            }
            else if (phase == PH_KYOKU_OVER)
            {
                ResolveKyoku(act[O_MODE]);
            }
            UpdatePending();
            step++;
            return true;
        }

        /// <summary>打牌以外のアクションが、その席の合法手の何番目にあたるか(無ければ -1)。同期で番号だけを送るのに使う</summary>
        public int FindLegal(int[] act)
        {
            int n = BuildLegal(act[O_SEAT]);
            if (phase == PH_CALLS && act[O_TYPE] == A_PASS) return n - 1;
            for (int i = 0; i < n; i++) if (SameOption(legal, i * OPT, act, 0)) return i;
            return -1;
        }

        private int[] riiC;

        /// <summary>
        /// リーチの打牌に使える牌(その牌を含む切り方で聴牌になるもの)を ok[牌] に入れ、その種類の数を返す(状態は変えない)。
        /// mustDiscard 枚(1 か 2)を切る組み合わせを全部試す。ブラウザ版 riichiTiles と同じ
        /// </summary>
        public int RiichiTiles(int s, bool[] ok)
        {
            if (riiC == null) riiC = new int[NT];
            for (int t = 0; t < NT; t++) { ok[t] = false; riiC[t] = handC[s * NT + t]; }
            int need = NeedMelds(s), n = 0;
            for (int a = 0; a < NT; a++)
            {
                if (riiC[a] == 0) continue;
                if (mustDiscard < 2)
                {
                    riiC[a]--;
                    if (FindWaits(riiC, need) > 0) ok[a] = true;
                    riiC[a]++;
                    continue;
                }
                for (int b = a; b < NT; b++)
                {
                    if (riiC[b] == 0 || (a == b && riiC[a] < 2)) continue;
                    if (ok[a] && ok[b]) continue;   // どちらも分かっている
                    riiC[a]--; riiC[b]--;
                    if (FindWaits(riiC, need) > 0) { ok[a] = true; ok[b] = true; }
                    riiC[a]++; riiC[b]++;
                }
            }
            for (int t = 0; t < NT; t++) if (ok[t]) n++;
            return n;
        }

        /// <summary>打牌ができるかだけを調べる(状態は変えない)。できなければ false(lastError に理由)</summary>
        public bool CheckDiscard(int[] act)
        {
            lastError = "";
            int seat = act[O_SEAT];
            int n = BuildLegal(seat);
            if (phase != PH_TURN) return Fail("いまは切れません");
            int oi = -1;
            for (int i = 0; i < n; i++) if (legal[i * OPT + O_TYPE] == A_DISCARD) oi = i;
            if (oi < 0) return Fail("いまは切れません");
            int ob = oi * OPT;
            int cnt = act[O_TAKEN];
            if (cnt != mustDiscard) return Fail(mustDiscard + "枚切ってください");
            int ta = act[O_TAKE], tb = cnt == 2 ? act[O_TAKE + 1] : -1;
            if (ta < 0 || ta >= NT || tb >= NT) return Fail("手牌にない牌です");
            if (handC[seat * NT + ta] < 1 || (tb >= 0 && handC[seat * NT + tb] < (tb == ta ? 2 : 1))) return Fail("手牌にない牌です");
            if (legal[ob + O_FIXEDN] >= 0)
            {
                int fn = legal[ob + O_FIXEDN];
                int fa = fn > 0 ? legal[ob + O_FIXED] : -1, fb = fn > 1 ? legal[ob + O_FIXED + 1] : -1;
                bool same = cnt == fn && ((ta == fa && tb == fb) || (ta == fb && tb == fa));
                if (!same) return Fail("リーチ中はツモ切りです");
            }
            for (int k = 0; k < kuikaeN; k++) if (kuikae[k] == ta || kuikae[k] == tb) return Fail("喰い替え禁止です");
            if (act[O_MODE] == 1)
            {
                if (legal[ob + O_RIICHIOK] != 1) return Fail("リーチできません");
                RemoveHand(seat, ta, 1);
                if (cnt == 2) RemoveHand(seat, tb, 1);
                bool tenpai = WaitsOf(seat) > 0;
                AddHand(seat, ta);
                if (cnt == 2) AddHand(seat, tb);
                if (!tenpai) return Fail("その切り方では聴牌になりません");
            }
            return true;
        }

        private bool DoDiscard(int s, int ta, int tb, int cnt, bool riichi)
        {
            RemoveHand(s, ta, 1);
            if (cnt == 2) RemoveHand(s, tb, 1);
            if (riichi && WaitsOf(s) == 0)
            {
                AddHand(s, ta); if (cnt == 2) AddHand(s, tb);
                return Fail("その切り方では聴牌になりません");
            }
            if (riichiOn[s]) riichiIppatsu[s] = false;
            discardSeq++;
            int ri = s * MAXRIVER + riverLen[s];
            rvN[ri] = cnt; rvT0[ri] = ta; rvT1[ri] = cnt == 2 ? tb : -1; rvTaken0[ri] = false; rvTaken1[ri] = false;
            rvRiichi[ri] = riichi; rvSeq[ri] = discardSeq; rvCalledBy[ri] = -1; rvRonBy[ri] = 0;
            riverLen[s]++;
            turns[s]++;
            if (riichi)
            {
                riichiDeclaring[s] = true;
                riichiDouble[s] = turns[s] == 1 && !anyCall;
            }
            if (!anyCall && turns[s] == 1) { firstRound[firstRoundN++] = ta; if (cnt == 2) firstRound[firstRoundN++] = tb; }
            drawnLen[s] = 0;
            afterCall = false;
            kuikaeN = 0;
            isRinshan = false;
            dcActive = true; dcSeat = s; dcN = cnt; dcT[0] = ta; dcT[1] = tb; dcRiichi = riichi; dcHoutei = LiveLeft() == 0; dcChankan = false; dcRiverIdx = riverLen[s] - 1;
            lastDiscardSeat = s; lastDiscardIdx = riverLen[s] - 1;
            if (ComputeCallOptions()) phase = PH_CALLS;
            else AfterDiscardPass();
            return true;
        }

        private void ConfirmRiichi(int s)
        {
            if (!riichiDeclaring[s]) return;
            riichiDeclaring[s] = false;
            riichiOn[s] = true;
            riichiIppatsu[s] = true;
            riichiTurn[s] = turns[s];
            riichiAt[s] = riverLen[s] > 0 ? rvSeq[s * MAXRIVER + riverLen[s] - 1] : 0;
            score[s] -= rRiichiStick;
            kyotaku += rRiichiStick;
        }

        private void FlipDora()
        {
            int k = doraFlips;
            int from = k * rDoraPerFlip, to = (k + 1) * rDoraPerFlip;
            if (to > wallDoraIndLen) return;
            for (int i = from; i < to; i++) doraInd[doraIndN++] = wallDoraInd[i];
            for (int i = from; i < to && i < wallUraIndLen; i++) uraInd[uraIndN++] = wallUraInd[i];
            doraFlips++;
        }

        private void FlipPending()
        {
            while (pendingDora > 0) { pendingDora--; FlipDora(); }
        }

        private void AfterDiscardPass()
        {
            int s = dcSeat;
            if (dcChankan)
            {
                turn = s;
                phase = PH_RINSHAN;
                return;
            }
            ConfirmRiichi(s);
            FlipPending();
            // 八風連打
            if (firstRoundN == 2 * NP && !anyCall)
            {
                int t0 = firstRound[0];
                if (t0 >= 36 && (t0 <= 39 || t0 >= 43))
                {
                    bool allSame = true;
                    for (int i = 0; i < firstRoundN; i++) if (firstRound[i] != t0) allSame = false;
                    if (allSame) { EndKyoku(RES_ABORT, REASON_HACHIFUU); return; }
                }
            }
            // 八家立直
            if (first < 0)
            {
                bool allRiichi = true;
                for (int q = 0; q < NP; q++) if (!riichiOn[q]) allRiichi = false;
                if (allRiichi) { EndKyoku(RES_ABORT, REASON_HACHIRIICHI); return; }
            }
            if (LiveLeft() == 0) { Exhaust(); return; }
            turn = NextActive(s);
            phase = PH_DRAW;
        }

        private void ResolveCalls()
        {
            bool anyRon = false;
            for (int s = 0; s < NP; s++) if (responded[s] && respBuf[s * OPT + O_TYPE] == A_RON) anyRon = true;
            if (anyRon) { ResolveRons(); return; }
            int bestSeat = -1, bestPrio = 0;
            for (int k = 0; k < NP; k++)
            {
                int s = (dcSeat + 1 + k) % NP;
                if (!responded[s]) continue;
                int ty = respBuf[s * OPT + O_TYPE];
                if (ty == A_PASS) continue;
                int prio = ty == A_CHI ? 1 : 2;
                if (bestSeat < 0 || prio > bestPrio) { bestSeat = s; bestPrio = prio; }
            }
            if (bestSeat >= 0) DoCall(bestSeat);
            else AfterDiscardPass();
        }

        private void MarkTaken(int ri, int[] tiles, int tBase, int n, int who)
        {
            for (int k = 0; k < n; k++)
            {
                int t = tiles[tBase + k];
                if (rvT0[ri] == t && !rvTaken0[ri]) rvTaken0[ri] = true;
                else if (rvN[ri] == 2 && rvT1[ri] == t && !rvTaken1[ri]) rvTaken1[ri] = true;
            }
            if (who >= 0) rvCalledBy[ri] = who;
        }

        private void DoCall(int qs)
        {
            int fs = dcSeat;
            ConfirmRiichi(fs);
            FlipPending();
            int rb = qs * OPT;
            MarkTaken(fs * MAXRIVER + dcRiverIdx, respBuf, rb + O_TAKE, respBuf[rb + O_TAKEN], qs);
            int gn = respBuf[rb + O_GIVEN];
            for (int k = 0; k < gn; k++) RemoveHand(qs, respBuf[rb + O_GIVE + k], 1);
            bool isKan = respBuf[rb + O_TYPE] == A_KAN;
            int mi = qs * 4 + meldN[qs];
            meldType[mi] = respBuf[rb + O_MELDTYPE]; meldStart[mi] = respBuf[rb + O_START]; meldOpen[mi] = true;
            meldKan[mi] = isKan ? KAN_DAIMINKAN : KAN_NONE; meldFrom[mi] = fs;
            int tn = respBuf[rb + O_TAKEN];
            meldTakeN[mi] = tn; meldTake0[mi] = tn > 0 ? respBuf[rb + O_TAKE] : -1; meldTake1[mi] = tn > 1 ? respBuf[rb + O_TAKE + 1] : -1;
            meldN[qs]++;
            anyCall = true;
            for (int s = 0; s < NP; s++) riichiIppatsu[s] = false;
            turn = qs;
            drawnLen[qs] = 0;
            if (isKan)
            {
                kanCount++;
                pendingDora++;
                mustDiscard = 1;
                afterCall = false;
                kuikaeN = 0;
                phase = PH_RINSHAN;
            }
            else
            {
                mustDiscard = tn;
                afterCall = true;
                kuikaeN = 0;
                for (int k = 0; k < tn; k++)
                {
                    int t = respBuf[rb + O_TAKE + k];
                    bool dup = false;
                    for (int j = 0; j < kuikaeN; j++) if (kuikae[j] == t) dup = true;
                    if (!dup) kuikae[kuikaeN++] = t;
                }
                phase = PH_TURN;
            }
        }

        private void DoSelfKan(int s, int mode, int t)
        {
            kanCount++;
            anyCall = true;
            for (int q = 0; q < NP; q++) riichiIppatsu[q] = false;
            mustDiscard = 2;
            if (mode == KAN_ANKAN)
            {
                RemoveHand(s, t, 7);
                // ツモ牌のうち t は、手牌に t が残っていなければ消す
                if (handC[s * NT + t] == 0)
                {
                    int w = 0;
                    for (int i = 0; i < drawnLen[s]; i++)
                    {
                        int d = drawn[s * MAXDRAWN + i];
                        if (d != t) drawn[s * MAXDRAWN + w++] = d;
                    }
                    drawnLen[s] = w;
                }
                int mi = s * 4 + meldN[s];
                meldType[mi] = MT_K; meldStart[mi] = t; meldOpen[mi] = false; meldKan[mi] = KAN_ANKAN; meldFrom[mi] = -1;
                meldTakeN[mi] = 0; meldTake0[mi] = -1; meldTake1[mi] = -1;
                meldN[s]++;
                FlipDora(); // 暗槓は即めくる
                phase = PH_RINSHAN;
            }
            else
            {
                RemoveHand(s, t, 1);
                for (int i = 0; i < drawnLen[s]; i++)
                {
                    if (drawn[s * MAXDRAWN + i] != t) continue;
                    for (int j = i; j < drawnLen[s] - 1; j++) drawn[s * MAXDRAWN + j] = drawn[s * MAXDRAWN + j + 1];
                    drawnLen[s]--;
                    break;
                }
                for (int i = 0; i < meldN[s]; i++)
                {
                    int mi = s * 4 + i;
                    if (meldType[mi] == MT_K && meldStart[mi] == t && meldOpen[mi] && meldKan[mi] == KAN_NONE) { meldKan[mi] = KAN_KAKAN; break; }
                }
                pendingDora++;
                // 搶槓のチェック
                dcActive = true; dcSeat = s; dcN = 1; dcT[0] = t; dcT[1] = -1; dcRiichi = false; dcHoutei = false; dcChankan = true; dcRiverIdx = -1;
                if (ComputeCallOptions()) phase = PH_CALLS;
                else phase = PH_RINSHAN;
            }
        }

        private void DoTsumo(int s)
        {
            int role = first < 0 ? ROLE_FIRST : ROLE_SECOND;
            SettleWin(s, role, WT_TSUMO, -1, -1, -1);
            if (role == ROLE_FIRST)
            {
                drawnLen[s] = 0;
                if (LiveLeft() == 0) { Exhaust(); return; }
                turn = NextActive(s);
                phase = PH_DRAW;
            }
            else EndKyoku(RES_WIN, 0);
        }

        private void ResolveRons()
        {
            // 親から巡る順に、ロンを宣言した人
            int[] avail = new int[ronTilesLen];
            for (int i = 0; i < ronTilesLen; i++) avail[i] = ronTiles[i];
            int[] wSeat = new int[NP]; int[] wOpt = new int[NP * OPT]; int wn = 0;
            for (int k = 0; k < NP; k++)
            {
                int s = (dealer + k) % NP;
                if (!responded[s] || respBuf[s * OPT + O_TYPE] != A_RON) continue;
                int pickBase = -1; int[] pickArr = null;
                if (RonUsable(respBuf, s * OPT, avail)) { pickArr = respBuf; pickBase = s * OPT; }
                else
                {
                    int bestPts = -1;
                    for (int i = 0; i < coN[s]; i++)
                    {
                        int ob = (s * MAXOPT + i) * OPT;
                        if (coBuf[ob + O_TYPE] != A_RON || !RonUsable(coBuf, ob, avail)) continue;
                        if (pickBase < 0 || coBuf[ob + O_POINTS] > bestPts) { pickArr = coBuf; pickBase = ob; bestPts = coBuf[ob + O_POINTS]; }
                    }
                }
                if (pickBase < 0) continue;
                for (int f = 0; f < OPT; f++) wOpt[wn * OPT + f] = pickArr[pickBase + f];
                if (wOpt[wn * OPT + O_MODE] == RON_ONE) avail[wOpt[wn * OPT + O_RONIDX]] = -1;
                wSeat[wn] = s; wn++;
            }
            int ds = dcSeat;
            riichiDeclaring[ds] = false; // 宣言牌へのロン: リーチ不成立
            bool endsNow = first >= 0 || wn > 1;
            int ri = dcRiverIdx >= 0 ? ds * MAXRIVER + dcRiverIdx : -1;
            for (int k = 0; k < wn; k++)
            {
                int role = first < 0 && k == 0 ? ROLE_FIRST : ROLE_SECOND;
                int ob = k * OPT;
                bool two = wOpt[ob + O_MODE] == RON_TWO;
                SettleWin(wSeat[k], role, two ? WT_RON2 : WT_RON1, ds, wOpt[ob + O_TILE], wOpt[ob + O_RONTILE]);
                if (ri >= 0)
                {
                    rvRonBy[ri] |= 1 << wSeat[k];
                    if (two) MarkTaken(ri, dcT, 0, dcN, -1);
                    else MarkTaken(ri, wOpt, ob + O_TILE, 1, -1);
                }
            }
            for (int i = 0; i < ronTilesLen; i++) ronTiles[i] = avail[i];
            if (endsNow || wn == 0)
            {
                if (wn == 0) { AfterDiscardPass(); return; }
                EndKyoku(RES_WIN, 0);
                return;
            }
            // 先発のみ: 続行(放銃者の次から。搶槓なら槓の続き)
            if (dcChankan)
            {
                turn = ds;
                phase = PH_RINSHAN;
                return;
            }
            FlipPending();
            if (LiveLeft() == 0) { Exhaust(); return; }
            turn = NextActive(ds);
            phase = PH_DRAW;
        }

        private bool RonUsable(int[] arr, int ob, int[] avail)
        {
            if (arr[ob + O_MODE] == RON_TWO) return true;
            return avail[arr[ob + O_RONIDX]] == arr[ob + O_RONTILE];
        }

        /// <summary>和了の精算。use/ronTile は片ロンの牌(ほかは -1)</summary>
        private void SettleWin(int s, int role, int wtype, int from, int use, int ronTile)
        {
            CopyHand(s, tmpC);
            wcRonTakenN = 0;
            if (wtype == WT_RON2) { for (int i = 0; i < dcN; i++) { tmpC[dcT[i]]++; wcRonTaken[wcRonTakenN++] = dcT[i]; } }
            else if (wtype == WT_RON1) { tmpC[use]++; tmpC[ronTile]++; wcRonTaken[wcRonTakenN++] = use; wcRonTaken[wcRonTakenN++] = ronTile; }
            SetWinCtx(s, wtype, wtype == WT_TSUMO);
            EvaluateWin(tmpC);
            bool isDealer = s == dealer;
            bool isFirst = role == ROLE_FIRST;
            int bse = BasePoints(evHan, evYakuman);
            int m10 = Mult10(isDealer, isFirst);
            int hb = isFirst ? honba : 0;
            int wi = winsN;
            for (int q = 0; q < NP; q++) winDeltas[wi * NP + q] = 0;
            if (wtype == WT_TSUMO)
            {
                int wsum = 0;
                for (int q = 0; q < NP; q++)
                {
                    if (q == s || outRole[q] != ROLE_NONE) continue;
                    wsum += q == dealer ? rDealerTsumoWeight10 : 10;
                }
                for (int q = 0; q < NP; q++)
                {
                    if (q == s || outRole[q] != ROLE_NONE) continue;
                    int wgt = q == dealer ? rDealerTsumoWeight10 : 10;
                    int pay = CeilDiv(bse * m10 * wgt, wsum * 1000) * 100 + hb * rHonbaTsumoEach;
                    winDeltas[wi * NP + q] -= pay; winDeltas[wi * NP + s] += pay;
                }
            }
            else
            {
                int pay = RonPayment(bse, m10) + hb * rHonbaRon;
                winDeltas[wi * NP + from] -= pay; winDeltas[wi * NP + s] += pay;
            }
            int stick = kyotaku;
            kyotaku = 0;
            winDeltas[wi * NP + s] += stick;
            for (int q = 0; q < NP; q++) score[q] += winDeltas[wi * NP + q];
            if (wtype == WT_TSUMO) { winTileA[wi] = drawnLen[s] > 0 ? drawn[s * MAXDRAWN + drawnLen[s] - 1] : -1; winTileB[wi] = drawnLen[s] > 1 ? drawn[s * MAXDRAWN + drawnLen[s] - 2] : -1; }
            else { winTileA[wi] = wcRonTakenN > 0 ? wcRonTaken[0] : -1; winTileB[wi] = wcRonTakenN > 1 ? wcRonTaken[1] : -1; }
            outRole[s] = role;
            for (int t = 0; t < NT; t++) handC[s * NT + t] = tmpC[t];
            handLen[s] = SumC(tmpC);
            drawnLen[s] = 0;
            if (isFirst) first = s;
            winSeat[wi] = s; winRole[wi] = role; winType[wi] = wtype; winFrom[wi] = from; winRonTile[wi] = ronTile;
            winHan[wi] = evHan; winYakuman[wi] = evYakuman; winForm[wi] = evForm;
            winBase[wi] = bse; winM10[wi] = m10; winHonba[wi] = hb; winStick[wi] = stick; winTotal[wi] = winDeltas[wi * NP + s];
            winRiichi[wi] = riichiOn[s];
            winYakuN[wi] = evYakuN;
            for (int k = 0; k < evYakuN; k++) { winYakuId[wi * MAXYAKU + k] = evYakuId[k]; winYakuHan[wi * MAXYAKU + k] = evYakuHanArr[k]; }
            if (winsN < MAXWINS - 1) winsN++;
        }

        private void Exhaust()
        {
            resTenpaiN = 0;
            for (int k = 0; k < NP; k++)
            {
                int s = (dealer + k) % NP;
                if (outRole[s] != ROLE_NONE) continue;
                if (WaitsOf(s) > 0) resTenpai[resTenpaiN++] = s;
            }
            resHadWin = winsN > 0;
            for (int s = 0; s < NP; s++) resDeltas[s] = 0;
            if (!resHadWin)
            {
                int notenN = 0;
                for (int s = 0; s < NP; s++) if (outRole[s] == ROLE_NONE && !InTenpai(s)) notenN++;
                if (resTenpaiN > 0 && notenN > 0)
                {
                    int each = CeilDiv(rNotenTotal, notenN * 100) * 100;
                    int collected = 0;
                    for (int s = 0; s < NP; s++)
                    {
                        if (outRole[s] != ROLE_NONE || InTenpai(s)) continue;
                        resDeltas[s] = -each; collected += each;
                    }
                    int share = (collected / (resTenpaiN * 100)) * 100;
                    int rest = collected - share * resTenpaiN;
                    for (int k = 0; k < resTenpaiN; k++)
                    {
                        int g = share;
                        if (rest >= 100) { g += 100; rest -= 100; }
                        resDeltas[resTenpai[k]] = g;
                    }
                    for (int s = 0; s < NP; s++) score[s] += resDeltas[s];
                }
            }
            EndKyoku(RES_EXHAUST, 0);
        }

        private bool InTenpai(int s)
        {
            for (int k = 0; k < resTenpaiN; k++) if (resTenpai[k] == s) return true;
            return false;
        }

        private void EndKyoku(int type, int reason)
        {
            for (int s = 0; s < NP; s++) riichiDeclaring[s] = false;
            resType = type; resReason = reason;
            phase = PH_KYOKU_OVER;
        }

        // ================================================================ 局・試合の終わり(連荘・アガリ止め・順位)
        private bool oRenchan, oYame; private int oHonba;

        private void Outcome()
        {
            bool hadWin = winsN > 0;
            bool dealerWon = false, dealerFirst = false;
            for (int k = 0; k < winsN; k++) if (winSeat[k] == dealer) { dealerWon = true; if (winRole[k] == ROLE_FIRST) dealerFirst = true; }
            if (resType == RES_ABORT) { oRenchan = true; oHonba = honba + 1; }
            else if (hadWin)
            {
                bool keeps = rRenchanFirst ? dealerFirst : dealerWon;
                oRenchan = keeps; oHonba = keeps ? honba + 1 : 0;
            }
            else
            {
                oRenchan = InTenpai(dealer); oHonba = honba + 1;
            }
            oYame = false;
            if (rAgariYame && oRenchan && kyoku >= rKyokuCount && resType != RES_ABORT && (dealerWon || (!hadWin && InTenpai(dealer))))
            {
                int top = int.MinValue;
                for (int s = 0; s < NP; s++) if (score[s] > top) top = score[s];
                int topN = 0;
                for (int s = 0; s < NP; s++) if (score[s] == top) topN++;
                oYame = score[dealer] == top && topN == 1;
            }
        }

        public bool CanAgariYame()
        {
            if (phase != PH_KYOKU_OVER) return false;
            Outcome();
            return oYame;
        }

        /// <summary>局の終わり: 次局の親・本場・終局。yame: -1=指定なし(止める) 0=続ける 1=止める</summary>
        private void ResolveKyoku(int yame)
        {
            Outcome();
            bool tobi = false;
            for (int s = 0; s < NP; s++) if (score[s] < 0) tobi = true;
            tobi = tobi && rTobiEnd;
            bool over = tobi;
            if (!over && kyoku >= rKyokuCount)
            {
                if (!oRenchan) over = true;
                else if (oYame && yame != 0) over = true;
            }
            honba = oHonba;
            if (over)
            {
                phase = PH_MATCH_OVER;
                SettleMatch();
                return;
            }
            if (!oRenchan)
            {
                kyoku++;
                dealer = (dealer + 1) % NP;
            }
            phase = PH_DEAL;
        }

        /// <summary>終局の順位(同点は起家から巡る順)。供託は1位が受け取る</summary>
        private void SettleMatch()
        {
            for (int s = 0; s < NP; s++) mrSeat[s] = s;
            for (int i = 0; i < NP; i++)
            {
                for (int j = i + 1; j < NP; j++)
                {
                    int a = mrSeat[i], b = mrSeat[j];
                    bool swap = score[b] > score[a] || (score[b] == score[a] && (b - initialDealer + NP) % NP < (a - initialDealer + NP) % NP);
                    if (swap) { mrSeat[i] = b; mrSeat[j] = a; }
                }
            }
            if (kyotaku > 0)
            {
                score[mrSeat[0]] += kyotaku;
                kyotaku = 0;
            }
            for (int i = 0; i < NP; i++)
            {
                mrScore[i] = score[mrSeat[i]];
                mrUma[i] = rUseUma ? rUma[i] : 0;
                mrFinal10[i] = (mrScore[i] - rStartScore) / 100 + mrUma[i] * 10;
            }
            matchOver = true;
        }

        // ================================================================ 自動進行
        private void UpdatePending()
        {
            pendN = 0;
            if (phase == PH_TURN) { pendKind = PEND_TURN; pendSeats[pendN++] = turn; }
            else if (phase == PH_CALLS)
            {
                pendKind = PEND_CALLS;
                for (int s = 0; s < NP; s++) if (hasCall[s] && !responded[s]) pendSeats[pendN++] = s;
            }
            else if (phase == PH_KYOKU_OVER) pendKind = PEND_NEXT;
            else if (phase == PH_MATCH_OVER) pendKind = PEND_END;
            else pendKind = PEND_NONE;
        }

        /// <summary>入力待ちでない限り自動で進める(配牌・ツモ・嶺上)</summary>
        public void Advance()
        {
            for (int guard = 0; guard < 8; guard++)
            {
                if (phase == PH_DEAL)
                {
                    SaveKyokuStart();
                    StartKyoku();
                    for (int s = 0; s < NP; s++) kyokuStartScore[s] = score[s];
                    phase = PH_DRAW;
                    continue;
                }
                if (phase == PH_DRAW)
                {
                    if (LiveLeft() < 2) { Exhaust(); break; }
                    int p = turn;
                    int t0 = wallLive[wallPos], t1 = wallLive[wallPos + 1];
                    wallPos += 2;
                    AddHand(p, t0); AddHand(p, t1);
                    drawn[p * MAXDRAWN] = t0; drawn[p * MAXDRAWN + 1] = t1; drawnLen[p] = 2;
                    furitenTemp[p] = false;
                    isRinshan = false;
                    afterCall = false;
                    mustDiscard = 2;
                    kuikaeN = 0;
                    phase = PH_TURN;
                    break;
                }
                if (phase == PH_RINSHAN)
                {
                    int p = turn;
                    int t = wallRinshan[wallRinshanPos++];
                    AddHand(p, t);
                    if (drawnLen[p] < MAXDRAWN) drawn[p * MAXDRAWN + drawnLen[p]++] = t;
                    isRinshan = true;
                    afterCall = false;
                    phase = PH_TURN;
                    break;
                }
                break;
            }
            UpdatePending();
        }
    }
}
