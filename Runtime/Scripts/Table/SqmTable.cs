using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon.Common.Interfaces;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 卓1つ。試合を進め(SqmEngine + SqmNpc)、牌・文字・ボタンを卓の上に並べる。
    ///
    /// ホスト(sHost)は最初に座った人。試合の開始(やルールの設定)ができる。ホストが抜けたり立ったりしたら、座っている人からランダムに次のホストを選ぶ。
    /// ホストは、同期の持ち主(Networking の Owner)も引き受ける。だれも座っていないときは、VRChat が決めた持ち主が NPC だけの試合を進める。
    ///
    /// 同期: 卓の持ち主だけが試合を進める。NPC の手・自動の進行・人の操作を、1つずつ番号(code)にして記録に足し、
    /// 直近 RING 個をリングで全員へ送る。ほかの人は、同じ種(シード)から同じ記録を手元のエンジンで再生する。
    /// エンジンは同じ操作なら同じ結果になる(JS 版と照合済み)ので、盤面そのものは送らない。
    /// 人の操作は、ホストへネットワークイベント(NetAct)で頼む。ホストが確かめて記録に足す。
    /// 途中から来た人や遅れた人は、ホストに記録すべて(SqmArchive)を頼んで追いつく。
    /// ホストが抜けたら、VRChat が次の持ち主を決める。手元に同じ記録があるので、そのまま続けられる。
    /// 人が抜けた席は NPC が打つ。
    ///
    /// 物(牌・ボタン・文字)は、エディタのメニュー「平方麻雀 → 卓をシーンに置く」(SqmBuilder)で作って、ここの配列に入れる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SqmTable : UdonSharpBehaviour
    {
        // ---------------- つなぐもの(SqmBuilder が入れる) ----------------
        public SqmEngine engine;
        public SqmNpc npc;
        public SqmArchive archive;
        public SqmPad pad;                   // 手元のパッドの中身(1組)
        public SqmPadBody[] padBodies;       // 席ごとのパッドの本体 8
        // デバッグモード(切るべき牌の表示・次の局の仕込み)は、Scripting Define Symbols に SQM_DEBUG があるプロジェクトでだけ使える。
        // 無いプロジェクトでは、いつも OFF で、Inspector にも出ない
#if SQM_DEBUG
        [Tooltip("デバッグ用の機能(切るべき牌の表示・次の局の仕込み)をパッドの設定タブに出す")]
        public bool debugEnabled;
        public GameObject debugToggle;       // これが表示中(Active)の間だけデバッグモードにする(LuraSwitch などのスイッチで切り替える用。空なら debugEnabled のまま)
#else
        [System.NonSerialized] public bool debugEnabled;
#endif
        public Material[] faceMats;          // 牌の表面 47種
        public Transform[] wallStacks;       // 壁の幢 525
        public Transform[] handTiles;        // 手牌 8席 × 28(SLOT_HAND)
        public MeshRenderer[] handFaces;
        public SqmTileClick[] handClicks;    // 手牌のクリック。自分の席の分だけ押せるようにする
        public Transform[] riverTiles;       // 河 8席 × 36(SLOT_RIVER)
        public MeshRenderer[] riverFaces;
        public Transform[] meldTiles;        // 副露 8席 × 28(SLOT_MELD)
        public MeshRenderer[] meldFaces;
        public Transform[] centerTiles;      // 中央: ドラ表示牌 18 + ロン牌 12
        public MeshRenderer[] centerFaces;
        public Transform holoRoot;           // 上空のホロ全体(見る人の方へ向ける)
        public Transform centerButtons;      // 中央の Resync・Reset(見ている人の席へ向ける)
        public MeshRenderer[] centerLights;  // 中央のパネルの光る縁 8(リーチした席の辺が赤くなる)
        public MeshRenderer[] rimLights;     // 縁の光る線 8
        public Material matLightIdle, matLightTurn, matLightRiichi;
        public TextMeshPro holoTitle;        // ホロの題字(試合が無いとき)
        public Transform kyokuBanner;        // 局の始まりに出す「東2局」(ホロの手前)
        public TextMeshPro kyokuBannerText;
        public AudioClip[] kyokuVoices;      // 「東一局」〜「東八局」の声(無ければ効果音)
        public AudioClip[] honbaVoices;      // 「一本場」〜「五本場」
        public AudioClip voiceOorasu, voiceRyuukyoku, voiceShuukyoku;
        public TextMeshPro holo;             // ホロのお知らせ(下の行)
        public GameObject holoPlay;          // 試合中だけ出す部分
        public TextMeshPro holoRound, holoLeft, holoSticks, holoSetTime, holoKyokuTime, holoCenter;
        public GameObject[] holoGauge;       // 残り枚数の円のゲージ(上から時計回り)
        public MeshRenderer[] holoDora;      // ドラ表示牌 18
        public MeshRenderer[] holoRon;       // ロン牌 12
        public Material[] holoFaceMats;      // ホロ用の牌の絵(光の影響を受けない) 47
        public MeshRenderer[] holoCellFrame, holoCellFill;   // 見取り図のマス 8(枠・中)
        public TextMeshPro[] holoCellWind, holoCellName, holoCellScore;
        public Material holoMatLine, holoMatRed, holoMatYellow, holoMatFill, holoMatTurn;
        // 結果の画面(和了・流局・終局)。出ているあいだは holoPlay を隠す
        public GameObject resultRoot;
        public TextMeshPro resTitle, resHead, resYaku0, resYaku1, resYaku2, resHan, resCalc, resPoints, resBody, resRankL, resRankR, resFoot, resDoraLabel, resUraLabel;
        public MeshRenderer[] resHand;       // 手牌 36
        public GameObject[] resHandMark;     // 和了牌の赤枠 36
        public MeshRenderer[] resDora, resUra;   // 18 ずつ
        public GameObject confirmButton;     // 「確認」
        public AudioSource sfx;              // 効果音
        public AudioClip[] sfxClips;         // SFX_* の順
        [System.NonSerialized] public float sfxVolume = 0.8f;   // 効果音の音量(パッドで変える)
        public AudioSource[] voiceSources;   // 宣言の声を鳴らす所(席ごと 8)
        public AudioClip[] voiceClips;       // VOICE_* の順
        [System.NonSerialized] public float voiceVolume = 1f;   // 声の音量(パッドで変える)
        public TextMeshPro[] seatLabels;     // 席の名前 8
        public TextMeshPro[] windLabels;     // 席風 8
        public TextMeshPro[] scoreLabels;    // 点数(7セグ) 8。この3つは卓の下の写し場にあり、labelCamera で画像に写して縁に貼る
        public Camera labelCamera;           // 縁の文字を画像に写すカメラ(文字が変わったときだけ動かす)
        public GameObject[] joinButtons;     // 席の「ここで打つ」 8
        public GameObject leaveButton;       // 「席を立つ」
        public GameObject newMatchButton;    // 「新しい試合」
        public Transform controls;           // 自分の前に出る操作ボタンのまとまり
        public GameObject discardButton;
        public GameObject[] discardButtons; // 卓の上の「確定」 8(自分の席のぶんだけ出す)
        public GameObject riichiButton;
        public TextMeshPro riichiLabel;
        public GameObject[] optionButtons;   // 宣言などの選択肢 12
        public TextMeshPro[] optionLabels;

        // ---------------- 大きさ(m) ----------------
        public const float TOP = 0.76f;      // 卓の天板の高さ
        public const float TW = 0.026f, TH = 0.035f, TT = 0.02f; // 牌の幅・高さ・厚み
        public const float R_HAND = 1.06f, R_MELD = 0.965f, R_LABEL = 1.15f;
        public const float FELT_AP = 1.09f, RIM_AP = 1.21f, RIM_H = 0.012f;   // 天板(フェルト)の端・縁の外側(中心から辺まで)・縁の高さ
        public const float CENTER_AP = 0.2f, CENTER_H = 0.004f;   // 中央のパネルの大きさ・高さ
        public const int SLOT_HAND = 28, SLOT_RIVER = 36, SLOT_MELD = 28, N_DORA = 18, N_RON = 12, N_OPT = 12;
        public const int WALL_PER_SIDE = 75, WALLLESS_SIDE = 7;
        public const int NP = 8, NT = 47;
        public const int RING = 64;          // 毎回送る直近の記録の数
        public const int LOG_MAX = 32768;    // 1試合の記録の上限(1試合はふつう 1万手まで)
        // 途中参加の再生は、1フレームにこの秒数まで(Udon では1手の適用が重く、決まった手数ずつだと数秒止まることがある)
        public const float REPLAY_BUDGET = 0.004f;

        // ---------------- 同期する状態(ホストが書く) ----------------
        [UdonSynced] private int sMatch;                     // 試合の番号(0 = まだ無い)
        [UdonSynced] private int sSeed;
        [UdonSynced] private int sHost;                      // ホストの playerId(0 = いない)
        [UdonSynced] private int sStartMs, sKyokuMs, sKyokuEndMs, sEndMs;   // 試合・局の開始/終了の時刻(サーバー時刻 ms、0 = まだ)
        [UdonSynced] private int sRigN;                      // デバッグの仕込み(局の番号・席・ひな形)。途中参加の再生でも同じ並べ替えになるよう同期する
        [UdonSynced] private int[] sRigK = new int[SqmEngine.RIG_MAX], sRigS = new int[SqmEngine.RIG_MAX], sRigT = new int[SqmEngine.RIG_MAX];
        [UdonSynced] private int sRigPendT = -1, sRigPendS = -1;   // 試合の前に頼まれた仕込み(次の試合の最初の局)
        [UdonSynced] private int sPageN, sPage, sAckMask;    // 結果の画面: ページ数(0 = 出ていない)・いまのページ・確認待ちの席のビット
        [UdonSynced] private int sEvtWinFrom, sEvtWinTo;     // その出来事で見せる和了(engine.win* の番号の範囲)
        [UdonSynced] private bool sEvtTail;                  // 最後に流局のページがある
        [UdonSynced] private int sWinsSeen, sWinsKyoku, sKyokuOverSeen;   // ホスト: どこまで出来事にしたか
        [UdonSynced] private int[] sPlayer = new int[NP];    // 席の人の playerId(0 = NPC)
        [UdonSynced] private int[] sStyle = new int[NP];     // 席の NPC の性格(名前と組)
        [UdonSynced] private int sCount;                     // 記録の数
        [UdonSynced] private int[] sRing = new int[RING];    // 記録 i は sRing[i % RING]

        // ---------------- 手元の状態 ----------------
        [System.NonSerialized] public int humanSeat = -1;   // 自分の席(-1 = 座っていない)
        private int lMatch;                                  // 手元のエンジンが表している試合(0 = 試合なし)
        private int lIdleMatch;                              // Reset で「試合なし」になったときの番号
        private int lCount;                                  // 手元で再生した記録の数
        private int[] lLog;                                  // 手元の記録(ホストになったときに配れるよう、全員が持つ)
        private int lLogFrom;                                // lLog が正しいのはこの手目から(局の始めから復元したときは、それより前は -1)
        private int[] lSnap;                                 // 今の局の始めの状態(engine.ksBuf の写し)。途中参加の人へ送る
        private int lSnapAt, lSnapSeq = -1;                  // その局が始まった時点で適用済みだった手数 / 局の番号
        private int replayTo;                                // 記録すべてから再生しているときの行き先(0 = していない)
        private float archiveAskWait;                        // 記録すべてを頼み直すまでの待ち
        private bool archiveDirty;                           // ホスト: 記録すべてを送る
        private float archiveSendWait;
        private int sentAt = -1;                             // 自分の操作を送ったときの sCount(返事が来るまで二度押ししない)
        private float sentWait;
        private float wait;                                  // ホスト: 次の手までの待ち(秒)
        private float claimWait;
        private float pageWait;                              // ホスト: だれも座っていないとき、次のページまでの待ち
        private int ackSentPage = -1;                        // 自分が確認を送ったページ(二重に送らないため)
        private int ackSentStart;                            // そのときの試合の開始時刻(sStartMs)。試合が変われば同じページでも送り直せる
        private bool dirty;
        private int[] handSlotTile;                          // 手牌の枠に入っている牌(-1 = 空)
        private int sel0 = -1, sel1 = -1;                    // 選んだ手牌の枠
        [System.NonSerialized] public bool riichiArmed;
        private int[] optBuf; [System.NonSerialized] public int optN;              // いま出している選択肢(engine.legal の写し)
        private int[] act;
        private string message = "";
        private bool ready;
        private int[] tmpC, tmpDr;

        private string[] cpuNames, yakuJa, tileNames;

        public const float NPC_DELAY = 0.45f;

        private void Start()
        {
            Init();
            UpdateMySeat();
            Refresh();
        }

        private void Init()
        {
            if (ready) return;
            ready = true;
            handSlotTile = new int[NP * SLOT_HAND];
            optBuf = new int[N_OPT * SqmEngine.OPT];
            act = new int[SqmEngine.OPT];
            lLog = new int[LOG_MAX];
            lSnap = new int[SqmEngine.KS_LEN];
            tmpC = new int[NT]; tmpDr = new int[8];
            handMainN = new int[NP];
            InitRules();
            if (sPlayer == null || sPlayer.Length != NP) sPlayer = new int[NP];
            if (sStyle == null || sStyle.Length != NP) sStyle = new int[NP];
            if (sRing == null || sRing.Length != RING) sRing = new int[RING];
            cpuNames = new string[] { "ドラ丸", "ツモ子", "リーチ侍", "片ロン姫", "カン太郎", "役満爺", "ノーテン王", "算用博士", "風林課長", "先発隊長" };
            yakuJa = new string[SqmEngine.N_YAKU];
            string[] ja = new string[] { "立直", "ダブル立直", "一発", "門前清自摸和", "断幺九", "役牌", "順子手", "静かなること林の如く",
                "疾きこと風の如く", "嶺上開花", "搶槓", "海底摸月", "河底撈魚", "二枚ロン", "侵掠すること火の如く", "動かざること山の如し",
                "三暗刻", "三色同順", "一気通貫", "小三元", "混老頭", "混全帯幺九", "算数", "算数(三面)", "混一色", "純全帯幺九",
                "四色同順", "十四対子", "清一色", "完順子ボーナス", "完全完順", "片ロン減翻", "ドラ", "裏ドラ" };
            for (int i = 0; i < ja.Length; i++) yakuJa[i] = ja[i];
            string[] ym = new string[] { "国士無双", "九蓮宝燈", "天和", "地和", "字一色", "風林火山", "大三元", "大四喜", "四暗刻", "清老頭", "四槓子", "算用大全" };
            for (int i = 0; i < ym.Length; i++) yakuJa[40 + i] = ym[i];
            tileNames = new string[] { "東", "南", "西", "北", "白", "發", "中", "風", "林", "火", "山" };
            engine.SetDefaultRules();
        }

        /// <summary>同期の持ち主か(試合を進める役)。ゲームのホスト(sHost)は、ふつうこの持ち主も兼ねる</summary>
        private bool IsHost() { return Networking.IsOwner(gameObject); }
        private int MyId() { VRCPlayerApi lp = Networking.LocalPlayer; return Utilities.IsValid(lp) ? lp.playerId : -1; }
        private bool Running() { return lMatch > 0 && engine.pendKind != SqmEngine.PEND_END; }
        private bool CaughtUp()
        {
            if (sSeed == 0) return replayTo == 0;   // 試合なし
            return lMatch == sMatch && lCount == sCount && replayTo == 0;
        }
        private bool IsHuman(int s) { return sPlayer[s] != 0; }

        private string NameOf(int s)
        {
            if (sPlayer[s] != 0)
            {
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(sPlayer[s]);
                if (Utilities.IsValid(p)) return p.displayName;
                return "?";
            }
            if (lMatch == 0) return "";
            return cpuNames[sStyle[s]];
        }

        /// <summary>自分の席を、同期された席の表から求める</summary>
        private void UpdateMySeat()
        {
            int mine = -1;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (Utilities.IsValid(lp)) for (int s = 0; s < NP; s++) if (sPlayer[s] == lp.playerId) mine = s;
            if (mine == humanSeat) return;
            humanSeat = mine;
            ClearSelection();
            if (handClicks == null) return;
            // 他人の席の手牌は押せない(手のマークも出ない)ようにする
            for (int i = 0; i < handClicks.Length; i++)
                if (handClicks[i] != null) handClicks[i].DisableInteractive = i / SLOT_HAND != humanSeat;
        }

        // ================================================================ 記録の番号(code)
        // 下位3ビット = 席、次の2ビット = 種類。
        //   0: 合法手の番号(6ビット目から)
        //   1: 打牌。5ビット目 = リーチ、6〜11 = 1枚目 + 1、12〜17 = 2枚目 + 1(0 = なし)
        //   2: 局の終わりの「次へ」。6〜7 = mode + 1
        private const int K_LEGAL = 0, K_DISCARD = 1, K_NEXT = 2;

        private int EncodeAct(int[] a)
        {
            int seat = a[SqmEngine.O_SEAT];
            if (a[SqmEngine.O_TYPE] == SqmEngine.A_DISCARD)
            {
                int cnt = a[SqmEngine.O_TAKEN];
                int ta = a[SqmEngine.O_TAKE], tb = cnt == 2 ? a[SqmEngine.O_TAKE + 1] : -1;
                return seat | (K_DISCARD << 3) | ((a[SqmEngine.O_MODE] == 1 ? 1 : 0) << 5) | ((ta + 1) << 6) | ((tb + 1) << 12);
            }
            if (a[SqmEngine.O_TYPE] == SqmEngine.A_NEXT) return EncodeNext(seat, a[SqmEngine.O_MODE]);
            int idx = engine.FindLegal(a);
            if (idx < 0) return -1;
            return seat | (K_LEGAL << 3) | (idx << 6);
        }

        private int EncodeNext(int seat, int mode) { return seat | (K_NEXT << 3) | ((mode + 1) << 6); }

        /// <summary>記録1つをエンジンに適用する(その前に、自動の進行を済ませる)</summary>
        private bool ApplyCode(int code)
        {
            AdvanceAll();
            int seat = code & 7, kind = (code >> 3) & 3;
            ClearAct(seat);
            if (kind == K_DISCARD)
            {
                int ta = ((code >> 6) & 63) - 1, tb = ((code >> 12) & 63) - 1;
                act[SqmEngine.O_TYPE] = SqmEngine.A_DISCARD;
                act[SqmEngine.O_MODE] = (code >> 5) & 1;
                act[SqmEngine.O_TAKEN] = tb >= 0 ? 2 : 1;
                act[SqmEngine.O_TAKE] = ta;
                if (tb >= 0) act[SqmEngine.O_TAKE + 1] = tb;
            }
            else if (kind == K_NEXT)
            {
                act[SqmEngine.O_TYPE] = SqmEngine.A_NEXT;
                act[SqmEngine.O_MODE] = ((code >> 6) & 3) - 1;
            }
            else
            {
                int idx = code >> 6;
                int n = engine.BuildLegal(seat);
                if (idx >= n) return false;
                for (int k = 0; k < SqmEngine.OPT; k++) act[k] = engine.legal[idx * SqmEngine.OPT + k];
                act[SqmEngine.O_SEAT] = seat;
            }
            bool ok = engine.ApplyAction(act);
            AdvanceAll();
            if (ok) NoteKyokuStart(lCount + 1);   // 呼ぶ側は lCount 手目を適用している
            return ok;
        }

        /// <summary>新しい局が始まっていたら、その局の始めの状態と、それまでに適用した手数(at)を覚える</summary>
        private void NoteKyokuStart(int at)
        {
            if (engine.kyokuSeq == lSnapSeq) return;
            lSnapSeq = engine.kyokuSeq;
            lSnapAt = at;
            for (int i = 0; i < SqmEngine.KS_LEN; i++) lSnap[i] = engine.ksBuf[i];
        }

        /// <summary>
        /// 局の始めの状態 snap から復元する(at = その時点で適用済みの手数)。その局の手だけを再生すれば追いつける。
        /// それより前の記録は持たないので -1 にしておく(ホストになっても、持っていない手は配らない)
        /// </summary>
        private bool RestoreLocal(int at, int[] snap)
        {
            if (sSeed == 0 || at < 0 || at > LOG_MAX) return false;
            ApplyRules(sRuleUsed);
            if (!engine.RestoreKyokuStart((uint)sSeed, snap)) return false;
            CopyRig();
            for (int s = 0; s < NP; s++) npc.SetSeat(s, SqmNpc.LV_NORMAL, sStyle[s]);
            lMatch = sMatch;
            lCount = at;
            lLogFrom = at;
            for (int i = 0; i < at; i++) lLog[i] = -1;
            replayTo = 0;
            lSnapSeq = -1;
            AdvanceAll();
            NoteKyokuStart(at);
            ClearSelection();
            return true;
        }

        /// <summary>入力待ちが無いあいだ、自動で進める(配牌・ツモなど)。ホストも他の人も同じ所で止まる</summary>
        private void AdvanceAll()
        {
            for (int guard = 0; guard < 64 && engine.pendKind == SqmEngine.PEND_NONE; guard++) engine.Advance();
        }

        // ================================================================ ホスト: 試合を進める
        /// <summary>ホストが記録に1つ足す(手元で適用できたものだけ)</summary>
        private bool Commit(int code)
        {
            if (code < 0 || lCount >= LOG_MAX) return false;
            if (!ApplyCode(code)) { message = "エラー: " + engine.lastError; dirty = true; return false; }
            lLog[lCount] = code;
            lCount++;
            sRing[sCount % RING] = code;
            sCount = lCount;
            if (((code >> 3) & 3) == K_NEXT && engine.pendKind != SqmEngine.PEND_END) StartKyokuClock();
            SfxFor(code);
            ClearSelection();
            dirty = true;
            HostCheckEvent();
            RequestSerialization();
            return true;
        }

        /// <summary>ホスト: 次の局が始まった時刻を記録する</summary>
        private void StartKyokuClock()
        {
            sKyokuMs = Networking.GetServerTimeInMilliseconds();
            sKyokuEndMs = 0;
        }

        private void HostNewMatch()
        {
            Init();
            sMatch = Mathf.Max(sMatch, Mathf.Max(lMatch, lIdleMatch)) + 1;
            sSeed = UnityEngine.Random.Range(1, int.MaxValue);
            // 10人から8人を選び、席に座らせる(名前と性格はいつも組)
            int[] order = new int[10];
            for (int i = 0; i < 10; i++) order[i] = i;
            for (int i = 9; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); int x = order[i]; order[i] = order[j]; order[j] = x; }
            for (int s = 0; s < NP; s++) sStyle[s] = order[s];
            for (int i = 0; i < N_RULE; i++) sRuleUsed[i] = sRule[i];   // この試合のルール
            sRigN = 0;
            if (sRigPendT >= 0 && sRigPendS >= 0) { sRigK[0] = 0; sRigS[0] = sRigPendS; sRigT[0] = sRigPendT; sRigN = 1; }
            sRigPendT = -1; sRigPendS = -1;
            sCount = 0;
            sStartMs = Networking.GetServerTimeInMilliseconds();
            if (sStartMs == 0) sStartMs = 1;
            sKyokuMs = sStartMs; sKyokuEndMs = 0; sEndMs = 0;
            sPageN = 0; sPage = 0; sAckMask = 0; sEvtWinFrom = 0; sEvtWinTo = 0; sEvtTail = false;
            sWinsSeen = 0; sWinsKyoku = -1; sKyokuOverSeen = -1;
            ResetLocal();
            wait = 0.5f;
            message = "";
            RequestSerialization();
            dirty = true;
        }

        // ---- 局の始まりの演出 ----
        public const float BANNER_TIME = 2.2f;
        private int bannerSeq = -1;
        private float bannerT, bannerFull = BANNER_TIME;

        /// <summary>局の始まりの演出中か(終わるまで、だれも牌を切れない)</summary>
        public bool BannerShowing() { return bannerT > 0; }

        /// <summary>新しい局が始まったら、ホロの手前に「東2局」を出して声(か効果音)を鳴らす。追いつき中は出さない</summary>
        private void CheckKyokuBanner()
        {
            // 「どの試合の何局目か」で覚える(Reset して新しい試合を始めたときも、東1局から出すように)
            int key = lMatch * 1000 + engine.kyokuSeq;
            if (lMatch == 0 || key == bannerSeq) return;
            bannerSeq = key;
            if (replayTo > 0 || engine.kyokuSeq <= 0 || engine.phase == SqmEngine.PH_MATCH_OVER) return;
            if (kyokuBannerText != null)
            {
                string t = "東" + engine.kyoku + "局";
                if (engine.honba > 0) t += "<size=45%>  " + engine.honba + "本場</size>";
                if (engine.kyoku >= engine.rKyokuCount) t += "\n<size=40%>オーラス</size>";
                kyokuBannerText.text = t;
            }
            bannerT = BANNER_TIME;
            // 声は「東八局」→「二本場」→「オーラス」のように続けて鳴らす
            bannerQN = 0; bannerQi = 0;
            AudioClip v = kyokuVoices != null && engine.kyoku >= 1 && engine.kyoku <= kyokuVoices.Length ? kyokuVoices[engine.kyoku - 1] : null;
            if (v == null) { PlaySfx(SFX_KYOKU); return; }
            bannerQ[bannerQN++] = v;
            if (engine.honba >= 1 && honbaVoices != null && engine.honba <= honbaVoices.Length && honbaVoices[engine.honba - 1] != null) bannerQ[bannerQN++] = honbaVoices[engine.honba - 1];
            if (engine.kyoku >= engine.rKyokuCount && voiceOorasu != null) bannerQ[bannerQN++] = voiceOorasu;
            // 声が長いときは、言い終わるまで帯を出しておく
            float total = 0.4f;
            for (int i = 0; i < bannerQN; i++) total += bannerQ[i].length + 0.08f;
            if (total > bannerT) bannerT = total;
            bannerFull = bannerT;
            PlayBannerNext();
        }

        private AudioClip[] bannerQ = new AudioClip[3];
        private int bannerQN, bannerQi;

        public void PlayBannerNext()
        {
            if (bannerQi >= bannerQN) return;
            AudioClip c = bannerQ[bannerQi++];
            PlayCenterVoice(c);
            if (bannerQi < bannerQN) SendCustomEventDelayedSeconds(nameof(PlayBannerNext), c.length + 0.08f);
        }

        /// <summary>卓の中央から声を鳴らす(局・流局・終局の読み上げ)</summary>
        private void PlayCenterVoice(AudioClip c)
        {
            if (c == null || sfx == null || voiceVolume <= 0) return;
            sfx.PlayOneShot(c, voiceVolume);
        }

        private int resultVoiceKey = -1;

        /// <summary>結果の画面が新しく出たとき、流局・終局を読み上げる(追いつき中は読まない)</summary>
        private void ResultVoice(int page)
        {
            int key = page == 100 ? lMatch * 100000 : page == 99 ? lMatch * 100000 + engine.kyokuSeq * 10 + 1 : -1;
            if (key < 0 || key == resultVoiceKey) return;
            resultVoiceKey = key;
            if (replayTo > 0) return;
            PlayCenterVoice(page == 100 ? voiceShuukyoku : voiceRyuukyoku);
        }

        /// <summary>帯がすっと開いて、少し止まって、閉じる</summary>
        private void AnimateBanner()
        {
            if (kyokuBanner == null) return;
            bool on = bannerT > 0;
            if (kyokuBanner.gameObject.activeSelf != on) kyokuBanner.gameObject.SetActive(on);
            if (!on) return;
            bannerT -= Time.deltaTime;
            if (bannerT <= 0)
            {
                dirty = true;   // 終わったら、操作ボタンを出し直す
                if (noticeLater) { noticeLater = false; if (wasMyTurn || wasMyCall) PlaySfx(SFX_TURN); }
            }
            float e = bannerFull - bannerT;                // 経った時間
            float open = Mathf.Clamp01(e / 0.25f);         // 0.25 秒で開く
            float close = Mathf.Clamp01(bannerT / 0.35f);  // 最後の 0.35 秒で閉じる
            float k = Mathf.Min(open, close);
            k = 1f - (1f - k) * (1f - k);                  // なめらかに
            kyokuBanner.localScale = new Vector3(0.6f + 0.4f * k, k, 1f);
        }

        private void Update()
        {
#if SQM_DEBUG
            if (debugToggle != null && debugToggle.activeInHierarchy != debugEnabled) SetDebug(debugToggle.activeInHierarchy);
#endif
            BillboardHolo();
            AnimateBanner();
            if (replayTo > 0) ReplayChunk();
            if (sentAt >= 0) { sentWait -= Time.deltaTime; if (sentWait <= 0 || sentAt != sCount) sentAt = -1; }
            // ホストに選ばれていて、追いついたら、同期の持ち主を引き受ける
            if (claimWait > 0) claimWait -= Time.deltaTime;
            else if (sHost != 0 && sHost == MyId() && !IsHost() && CaughtUp()) { claimWait = 2f; Networking.SetOwner(Networking.LocalPlayer, gameObject); }
            if (IsHost())
            {
                if (archiveDirty) { archiveSendWait -= Time.deltaTime; if (archiveSendWait <= 0) SendArchive(); }
                if (sPageN > 0)
                {
                    // 結果の画面: だれも座っていなければ時間で進める(座っている人がいれば「確認」を待つ)
                    if (HumanMask() == 0) { pageWait -= Time.deltaTime; if (pageWait <= 0) HostNextPage(); }
                }
                else if (CaughtUp() && Running() && bannerT <= 0)   // 局の始まりの演出が終わるまでは進めない
                {
                    if (wait > 0) wait -= Time.deltaTime;
                    else HostStep();
                }
            }
            else if (archiveAskWait > 0)
            {
                archiveAskWait -= Time.deltaTime;
                if (archiveAskWait <= 0 && lMatch == sMatch && lCount < sCount && replayTo == 0) AskArchive();
            }
            if (dirty) { dirty = false; Refresh(); CheckKyokuBanner(); }
            clockWait -= Time.deltaTime;
            if (clockWait <= 0) { clockWait = 0.1f; RefreshHoloTime(); }
        }

        private float clockWait;

        /// <summary>ホストが1回分だけ進める(自動進行・NPC の手・人の自動ツモ切り・局の終わり)</summary>
        private void HostStep()
        {
            AdvanceAll();
            int pk = engine.pendKind;
            if (pk == SqmEngine.PEND_END)
            {
                if (sEndMs == 0) { sEndMs = Networking.GetServerTimeInMilliseconds(); if (sKyokuEndMs == 0) sKyokuEndMs = sEndMs; RequestSerialization(); }
                dirty = true;
                return;
            }
            if (pk == SqmEngine.PEND_NONE) { dirty = true; return; }
            if (HostCheckEvent()) return;
            if (pk == SqmEngine.PEND_NEXT)
            {
                if (sKyokuEndMs == 0) { sKyokuEndMs = Networking.GetServerTimeInMilliseconds(); RequestSerialization(); }
                int d = engine.dealer;
                // 人の親がアガリ止めを選べるときは、その人のボタンを待つ
                if (IsHuman(d) && engine.CanAgariYame()) return;
                if (Commit(EncodeNext(d, -1))) wait = 0.3f;
                return;
            }
            // 入力待ちの席のうち NPC の分を1つ進める
            for (int i = 0; i < engine.pendN; i++)
            {
                int seat = engine.pendSeats[i];
                if (IsHuman(seat)) continue;
                uint nseed = engine.HashSeed(engine.seed.ToString() + ":" + engine.step + ":" + seat);
                npc.Decide(seat, nseed);
                bool discard = npc.action[SqmEngine.O_TYPE] == SqmEngine.A_DISCARD;
                if (!Commit(EncodeAct(npc.action))) { message = "NPC エラー: " + engine.lastError; wait = 2f; return; }
                if (discard) wait = NPC_DELAY;
                return;
            }
            // 人の番: リーチ中で、ツモ和了・槓ができないときのツモ切りは自動
            if (pk == SqmEngine.PEND_TURN && IsHuman(engine.turn) && engine.riichiOn[engine.turn])
            {
                int t = engine.turn;
                int n = engine.BuildLegal(t);
                for (int i = 0; i < n; i++)
                {
                    int ty = engine.legal[i * SqmEngine.OPT + SqmEngine.O_TYPE];
                    if (ty == SqmEngine.A_TSUMO || ty == SqmEngine.A_KAN) return;
                }
                if (BuildTsumogiri(t) && Commit(EncodeAct(act))) wait = NPC_DELAY;
            }
        }

        /// <summary>act に、リーチ中のツモ切りを入れる</summary>
        private bool BuildTsumogiri(int seat)
        {
            int n = engine.BuildLegal(seat);
            for (int i = 0; i < n; i++)
            {
                int b = i * SqmEngine.OPT;
                if (engine.legal[b + SqmEngine.O_TYPE] != SqmEngine.A_DISCARD) continue;
                int fn = engine.legal[b + SqmEngine.O_FIXEDN];
                if (fn <= 0) return false;
                ClearAct(seat);
                act[SqmEngine.O_TYPE] = SqmEngine.A_DISCARD; act[SqmEngine.O_MODE] = 0;
                act[SqmEngine.O_TAKEN] = fn;
                for (int k = 0; k < fn; k++) act[SqmEngine.O_TAKE + k] = engine.legal[b + SqmEngine.O_FIXED + k];
                return true;
            }
            return false;
        }

        private void ClearAct(int seat)
        {
            for (int k = 0; k < SqmEngine.OPT; k++) act[k] = 0;
            act[SqmEngine.O_SEAT] = seat;
            act[SqmEngine.O_MODE] = -1; act[SqmEngine.O_TILE] = -1; act[SqmEngine.O_RONIDX] = -1; act[SqmEngine.O_RONTILE] = -1;
            act[SqmEngine.O_MELDTYPE] = SqmEngine.MT_NONE; act[SqmEngine.O_START] = -1;
        }

        // ================================================================ 同期を受け取る
        /// <summary>手元のエンジンを、同期された試合の最初の状態にする</summary>
        private void ResetLocal()
        {
            if (sSeed == 0)
            {
                // 試合なし(Reset された)
                engine.SetDefaultRules();
                lMatch = 0; lIdleMatch = sMatch; lCount = 0; replayTo = 0;
                ClearSelection();
                return;
            }
            ApplyRules(sRuleUsed);
            engine.NewMatch((uint)sSeed);
            CopyRig();
            for (int s = 0; s < NP; s++) npc.SetSeat(s, SqmNpc.LV_NORMAL, sStyle[s]);
            lMatch = sMatch;
            lCount = 0;
            lLogFrom = 0;
            replayTo = 0;
            lSnapSeq = -1;
            AdvanceAll();
            NoteKyokuStart(0);
            ClearSelection();
        }

        public override void OnDeserialization()
        {
            Init();
            UpdateMySeat();
            CopyRig();   // デバッグの仕込みは、追いつく(配牌する)前にエンジンへ写す
            if (sMatch != lMatch && sMatch > 0 && !(sSeed == 0 && lIdleMatch == sMatch)) ResetLocal();
            else if (lMatch == sMatch && lCount > sCount && replayTo == 0)
            {
                // 持ち主が代わって、自分が足した手の一部が取り消された: 手元の記録の頭から sCount 手目までを再生し直す
                int keep = sCount;
                if (keep >= lSnapAt && lSnapAt >= lLogFrom)
                {
                    // 今の局の始めから再生し直せば足りる
                    int at = lSnapAt;
                    int[] snap = new int[SqmEngine.KS_LEN];
                    for (int i = 0; i < SqmEngine.KS_LEN; i++) snap[i] = lSnap[i];
                    if (RestoreLocal(at, snap)) replayTo = keep;
                    else { ResetLocal(); AskArchive(); }
                }
                else if (lLogFrom == 0) { ResetLocal(); replayTo = keep; }
                else { ResetLocal(); AskArchive(); }   // 手元に古い記録が無い
            }
            CatchUp();
            dirty = true;
        }

        /// <summary>リングに残っている分で追いつく。足りなければ記録すべてを頼む</summary>
        private void CatchUp()
        {
            if (replayTo > 0 || lMatch != sMatch || lCount >= sCount) return;
            if (lCount < sCount - RING) { AskArchive(); return; }
            for (int i = lCount; i < sCount; i++)
            {
                int code = sRing[i % RING];
                if (code < 0) { AskArchive(); return; }   // ホストが持っていない古い手
                if (!ApplyCode(code)) { message = "同期をやり直しています"; ResetLocal(); AskArchive(); return; }
                lLog[i] = code;
                lCount = i + 1;
                if (sCount - i <= 4) SfxFor(code);   // 大きく遅れて追いつくときは、最後の数手だけ鳴らす
            }
            ClearSelection();
        }

        private void AskArchive()
        {
            archiveAskWait = 5f;   // 返事が来なければ、また頼む
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetAskArchive));
        }

        [NetworkCallable]
        public void NetAskArchive()
        {
            if (!IsHost()) return;
            if (!archiveDirty) archiveSendWait = 0.5f;   // 続けて頼まれても、まとめて1回送る
            archiveDirty = true;
        }

        private void SendArchive()
        {
            archiveDirty = false;
            if (archive == null || !CaughtUp()) return;
            if (!Networking.IsOwner(archive.gameObject)) Networking.SetOwner(Networking.LocalPlayer, archive.gameObject);
            // 送るのは、今の局の始めの状態と、そこからの手だけ(試合の最初から送ると、受け取った人の再生がとても重い)
            int from = lSnapAt;
            int[] log = new int[lCount - from];
            for (int i = from; i < lCount; i++) log[i - from] = lLog[i];
            int[] snap = new int[SqmEngine.KS_LEN];
            for (int i = 0; i < SqmEngine.KS_LEN; i++) snap[i] = lSnap[i];
            archive.aMatch = lMatch;
            archive.aCount = lCount;
            archive.aBase = from;
            archive.aSnap = snap;
            archive.aLog = log;
            archive.RequestSerialization();
        }

        /// <summary>記録すべてが届いた。最初から再生し直す(何フレームかに分けて)</summary>
        public void OnArchive()
        {
            Init();
            if (IsHost() || archive.aMatch != sMatch || sMatch == 0) return;
            if (lMatch == sMatch && replayTo == 0 && lCount >= archive.aCount) return;
            int n = archive.aCount, from = archive.aBase;
            if (n > LOG_MAX || from < 0 || from > n || archive.aLog == null || archive.aLog.Length < n - from) return;
            if (archive.aSnap == null || archive.aSnap.Length < SqmEngine.KS_LEN) return;
            // 局の始めから復元して、その局の手だけを再生する
            if (!RestoreLocal(from, archive.aSnap)) return;
            for (int i = from; i < n; i++) lLog[i] = archive.aLog[i - from];
            replayTo = n;
            archiveAskWait = 0;
            if (n == from) { replayTo = 0; CatchUp(); dirty = true; }
        }

        private void ReplayChunk()
        {
            // 時間で区切る(少なくとも1手は進める)。重い手が続いても、画面が止まらないように
            float t0 = Time.realtimeSinceStartup;
            for (int i = lCount; i < replayTo; i++)
            {
                if (!ApplyCode(lLog[i])) { message = "同期に失敗しました"; replayTo = 0; ResetLocal(); archiveAskWait = 5f; return; }
                lCount = i + 1;
                if (Time.realtimeSinceStartup - t0 > REPLAY_BUDGET) break;
            }
            if (lCount >= replayTo)
            {
                replayTo = 0;
                message = "";
                CatchUp();
                dirty = true;
            }
        }

        // ================================================================ ホストの交代・人の出入り
        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            Init();
            if (!Utilities.IsValid(player) || !player.isLocal) return;
            // 自分がホストになった。手元の記録を正として続ける
            if (sSeed == 0) { }   // 試合なし
            else if (lMatch == sMatch && replayTo == 0)
            {
                sCount = lCount;
                for (int i = 0; i < RING && i < lCount; i++) sRing[(lCount - 1 - i) % RING] = lLog[lCount - 1 - i];
            }
            else if (lMatch != sMatch || replayTo > 0)
            {
                // 追いついていない: 手元で再生できた所までを正とする
                sMatch = lMatch; replayTo = 0; sCount = lCount;
                for (int i = 0; i < RING && i < lCount; i++) sRing[(lCount - 1 - i) % RING] = lLog[lCount - 1 - i];
            }
            CleanSeats();
            FixHost();
            HostFixAck();
            wait = 1f;
            RequestSerialization();
            dirty = true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!IsHost() || !Utilities.IsValid(player)) return;
            bool changed = false;
            for (int s = 0; s < NP; s++) if (sPlayer[s] == player.playerId) { sPlayer[s] = 0; changed = true; }
            if (FixHost()) changed = true;
            if (changed) { HostFixAck(); RequestSerialization(); dirty = true; }
        }

        /// <summary>もういない人の席を空ける(NPC が打つ)</summary>
        private void CleanSeats()
        {
            for (int s = 0; s < NP; s++)
            {
                if (sPlayer[s] == 0) continue;
                VRCPlayerApi p = VRCPlayerApi.GetPlayerById(sPlayer[s]);
                if (!Utilities.IsValid(p)) sPlayer[s] = 0;
            }
            UpdateMySeat();
        }

        /// <summary>
        /// 持ち主: ホストが座っていなければ選び直す(最初に座った人 → その人が抜けたら、座っている人からランダム)。変えたら true。
        /// 選ばれた人は、同期を受け取って追いついてから、自分で持ち主を引き受ける(Update)。
        /// </summary>
        private bool FixHost()
        {
            if (sHost != 0) for (int s = 0; s < NP; s++) if (sPlayer[s] == sHost) return false;
            int n = 0;
            for (int s = 0; s < NP; s++) if (sPlayer[s] != 0) n++;
            int pick = n > 0 ? UnityEngine.Random.Range(0, n) : -1;
            int next = 0;
            for (int s = 0; s < NP; s++) if (sPlayer[s] != 0) { if (pick == 0) next = sPlayer[s]; pick--; }
            if (next == sHost) return false;
            sHost = next;
            return true;
        }

        // ================================================================ 席・試合のボタン
        public void Join0() { RequestSeat(0); } public void Join1() { RequestSeat(1); } public void Join2() { RequestSeat(2); } public void Join3() { RequestSeat(3); }
        public void Join4() { RequestSeat(4); } public void Join5() { RequestSeat(5); } public void Join6() { RequestSeat(6); } public void Join7() { RequestSeat(7); }
        public void Leave() { RequestSeat(-1); }

        private void RequestSeat(int seat)
        {
            Init();
            if (IsHost()) HostSeat(seat, Networking.LocalPlayer.playerId);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetSeat), seat);
        }

        [NetworkCallable]
        public void NetSeat(int seat)
        {
            if (!IsHost()) return;
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p)) return;
            HostSeat(seat, p.playerId);
        }

        /// <summary>ホスト: 席に座らせる(seat = -1 なら立たせる)。空いている席(NPC の席)なら試合の途中でも座れる</summary>
        private void HostSeat(int seat, int pid)
        {
            if (seat >= NP) return;
            if (seat >= 0 && sPlayer[seat] != 0 && sPlayer[seat] != pid) return;
            for (int s = 0; s < NP; s++) if (sPlayer[s] == pid) sPlayer[s] = 0;
            if (seat >= 0) sPlayer[seat] = pid;
            FixHost();
            HostFixAck();
            UpdateMySeat();
            RequestSerialization();
            dirty = true;
        }

        public void NewMatch()
        {
            Init();
            if (IsHost()) { if (MayStart(Networking.LocalPlayer.playerId)) HostNewMatch(); }
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetNewMatch));
        }

        [NetworkCallable]
        public void NetNewMatch()
        {
            if (!IsHost()) return;
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p)) return;
            if (MayStart(p.playerId)) HostNewMatch();
        }

        /// <summary>試合を始められるのはホストだけ(だれも座っていなければ、だれでも NPC だけの試合を始められる)</summary>
        private bool MayStart(int pid)
        {
            if (sHost != 0) return pid == sHost;
            return !Running();
        }

        // ---- 卓の中央の Resync・Reset ----
        /// <summary>自分の手元だけ、記録すべてをホストからもらい直して作り直す</summary>
        public void Resync()
        {
            Init();
            PlaySfx(SFX_CLICK);
            if (IsHost() || sMatch == 0) { dirty = true; return; }
            message = "同期をやり直しています";
            ResetLocal();
            AskArchive();
            dirty = true;
        }

        /// <summary>試合をやめて、だれもいない状態に戻す。人が座っていればホストだけ、NPC だけの試合ならだれでも</summary>
        public void ResetMatch()
        {
            Init();
            PlaySfx(SFX_CLICK);
            if (IsHost()) { if (MayReset(MyId())) HostReset(); }
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetReset));
        }

        [NetworkCallable]
        public void NetReset()
        {
            if (!IsHost()) return;
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p)) return;
            if (MayReset(p.playerId)) HostReset();
        }

        private bool MayReset(int pid)
        {
            if (HumanMask() == 0) return true;
            return sHost != 0 && pid == sHost;
        }

        /// <summary>ホスト: 試合を消す(sSeed = 0 は「試合なし」)</summary>
        private void HostReset()
        {
            sMatch = Mathf.Max(sMatch, Mathf.Max(lMatch, lIdleMatch)) + 1;
            sSeed = 0;
            sCount = 0;
            sPageN = 0; sPage = 0; sAckMask = 0;
            sStartMs = 0; sEndMs = 0; sKyokuMs = 0; sKyokuEndMs = 0;
            ResetLocal();
            message = "";
            RequestSerialization();
            SendCustomNetworkEvent(NetworkEventTarget.All, nameof(NetReturnPads));   // パッドを元の場所へ
            dirty = true;
        }

        // ================================================================ 自分の操作
        private bool MyTurn()
        {
            return Running() && CaughtUp() && bannerT <= 0 && humanSeat >= 0 && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == humanSeat;
        }

        public void ClickTile(int slot)
        {
            if (humanSeat < 0 || slot / SLOT_HAND != humanSeat) return;
            if (!MyTurn()) return;
            if (handSlotTile[slot] < 0) return;
            if (riichiArmed && riichiTile != null && !riichiTile[handSlotTile[slot]]) return;   // リーチで切れない牌
            if (sel0 == slot) { sel0 = sel1; sel1 = -1; }
            else if (sel1 == slot) sel1 = -1;
            else if (sel0 < 0) sel0 = slot;
            else if (engine.mustDiscard >= 2 && sel1 < 0) sel1 = slot;
            else { sel0 = sel1 >= 0 ? sel1 : sel0; sel1 = slot; if (engine.mustDiscard < 2) { sel0 = slot; sel1 = -1; } }
            Refresh();
        }

        public void Discard()
        {
            if (!MyTurn() || sentAt >= 0) return;
            if (engine.riichiOn[humanSeat])
            {
                // リーチ中: ツモ和了や槓をせずにツモ切りする
                if (BuildTsumogiri(humanSeat)) SendCode(EncodeAct(act));
                return;
            }
            int need = engine.mustDiscard;
            int n = (sel0 >= 0 ? 1 : 0) + (sel1 >= 0 ? 1 : 0);
            if (n != need) { message = need + "枚選んでください"; Refresh(); return; }
            ClearAct(humanSeat);
            act[SqmEngine.O_TYPE] = SqmEngine.A_DISCARD;
            act[SqmEngine.O_TAKEN] = need;
            act[SqmEngine.O_TAKE] = handSlotTile[sel0];
            if (need == 2) act[SqmEngine.O_TAKE + 1] = handSlotTile[sel1];
            act[SqmEngine.O_MODE] = riichiArmed ? 1 : 0;
            if (!engine.CheckDiscard(act)) { message = engine.lastError; Refresh(); return; }
            SendCode(EncodeAct(act));
        }

        public void ToggleRiichi()
        {
            if (!riichiArmed && !padRiichiOk) return;
            riichiArmed = !riichiArmed;
            sel0 = -1; sel1 = -1;   // 選び直してもらう
            Refresh();
        }

        public void Opt0() { ClickOption(0); } public void Opt1() { ClickOption(1); } public void Opt2() { ClickOption(2); }
        public void Opt3() { ClickOption(3); } public void Opt4() { ClickOption(4); } public void Opt5() { ClickOption(5); }
        public void Opt6() { ClickOption(6); } public void Opt7() { ClickOption(7); } public void Opt8() { ClickOption(8); }
        public void Opt9() { ClickOption(9); } public void Opt10() { ClickOption(10); } public void Opt11() { ClickOption(11); }

        private void ClickOption(int i)
        {
            if (i >= optN || sentAt >= 0 || !CaughtUp()) return;
            for (int k = 0; k < SqmEngine.OPT; k++) act[k] = optBuf[i * SqmEngine.OPT + k];
            act[SqmEngine.O_SEAT] = humanSeat;
            SendCode(EncodeAct(act));
        }

        /// <summary>自分の操作をホストへ送る(自分がホストなら、そのまま記録に足す)</summary>
        private void SendCode(int code)
        {
            if (code < 0) { message = "その操作はできません"; Refresh(); return; }
            message = "";
            if (IsHost())
            {
                if (Commit(code)) wait = NPC_DELAY;
                return;
            }
            sentAt = sCount;
            sentWait = 3f;
            SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetAct), sMatch, sCount, code);
            ClearSelection();
            Refresh();
        }

        [NetworkCallable]
        public void NetAct(int match, int count, int code)
        {
            if (!IsHost() || match != sMatch || count != sCount || !CaughtUp()) return;
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p) || sPlayer[code & 7] != p.playerId) return;
            if (Commit(code)) wait = NPC_DELAY;
        }

        private void ClearSelection() { sel0 = -1; sel1 = -1; riichiArmed = false; }

        // ================================================================ 表示
        private Vector3 U(int s) { float a = s * 45f * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a)); }   // 卓の中心 → 席
        private Vector3 R(int s) { float a = s * 45f * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }    // 席から見て右

        private string TileCode(int t)
        {
            if (t < 36) return (t % 9 + 1).ToString() + "mpsa".Substring(t / 9, 1);
            return tileNames[t - 36];
        }

        /// <summary>牌を置く。faceUp=false なら伏せる(表面を消す)。flat=true なら寝かせる(表を上、牌の上端を卓の中心へ)</summary>
        private void Place(Transform tr, MeshRenderer face, int tile, Vector3 pos, int seat, bool flat, bool visible, bool faceUp, bool sideways)
        {
            if (!visible) { if (tr.gameObject.activeSelf) tr.gameObject.SetActive(false); return; }
            if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
            Vector3 u = U(seat);
            Quaternion rot = flat ? Quaternion.LookRotation(Vector3.up, -u) : Quaternion.LookRotation(u, Vector3.up);
            if (sideways) rot = Quaternion.AngleAxis(90f, Vector3.up) * rot;
            tr.localPosition = pos;
            tr.localRotation = rot;
            bool show = faceUp && tile >= 0;
            if (face.enabled != show) face.enabled = show;
            if (show) face.sharedMaterial = faceMats[tile];
        }

        public void Refresh()
        {
            if (!ready) Init();
            RefreshWall();
            for (int s = 0; s < NP; s++) { RefreshHand(s); RefreshRiver(s); RefreshMelds(s); RefreshLabel(s); }
            RefreshCenter();
            RefreshControls();
            RefreshHolo();
            RefreshPads();
        }

        /// <summary>だれか座っているか(座っている人がいれば、観戦者にも手牌は見せない)</summary>
        private bool AnyHuman()
        {
            for (int s = 0; s < NP; s++) if (sPlayer[s] != 0) return true;
            return false;
        }

        private void RefreshWall()
        {
            int live = engine.LiveLeft();
            int visible = live / 2;
            if (visible > 525) visible = 525;
            if (engine.phase == SqmEngine.PH_DEAL && !Running()) visible = 525;
            int drawnStacks = 525 - visible;
            // 引く順: 局ごとに決まるランダムな席の前の壁から始めて、その壁が切れたら上家(左隣)の壁へ。
            // 壁の中は、その席から見て右端の列から左へ、列の中は奥(卓の中心側)から手前へ。
            // 始める壁は局の種から決める(全員で同じになる。山の中身を引く乱数には触らない)
            int startSide = (int)((engine.kyokuSeed >> 8) % 7u);
            if (startSide >= WALLLESS_SIDE) startSide++;
            int k = 0;
            for (int sidx = 0; sidx < 8; sidx++)
            {
                int side = (startSide - sidx + 8) % 8;
                if (side == WALLLESS_SIDE) continue;
                Vector3 u = U(side), r = R(side);
                for (int j = 0; j < WALL_PER_SIDE; j++, k++)
                {
                    int slot = side * WALL_PER_SIDE + j;
                    if (side > WALLLESS_SIDE) slot -= WALL_PER_SIDE;
                    Transform tr = wallStacks[slot];
                    bool show = k >= drawnStacks;
                    if (tr.gameObject.activeSelf != show) tr.gameObject.SetActive(show);
                    if (!show) continue;
                    int col = 24 - j / 3, row = j % 3;     // 右端の列から左へ、内側の列から外側へ
                    float rad = 0.81f + row * 0.036f;
                    Vector3 pos = u * rad + r * ((col - 12) * 0.0262f) + Vector3.up * (TOP + TT);
                    tr.localPosition = pos;
                    tr.localRotation = Quaternion.LookRotation(-u, Vector3.up);
                }
            }
        }

        private void RefreshHand(int s)
        {
            int baseSlot = s * SLOT_HAND;
            // 表が見えるのは: 自分の手牌 / 観戦者(席に着いていない人)にはいつも全員の手牌 / 和了った人の手牌 / 局が終わったあと
            bool reveal = s == humanSeat || humanSeat < 0 || engine.outRole[s] != SqmEngine.ROLE_NONE || engine.phase == SqmEngine.PH_KYOKU_OVER;
            // ツモった牌は右端に離して置く
            int dn = engine.drawnLen[s];
            int[] c = tmpC;
            for (int t = 0; t < NT; t++) c[t] = engine.handC[s * NT + t];
            int[] dr = tmpDr;
            int drN = 0;
            if (engine.outRole[s] == SqmEngine.ROLE_NONE)
            {
                for (int i = 0; i < dn; i++) { int t = engine.drawn[s * SqmEngine.MAXDRAWN + i]; if (c[t] > 0) { c[t]--; dr[drN++] = t; } }
            }
            int n = 0;
            for (int t = 0; t < NT; t++) for (int k = 0; k < c[t]; k++) if (n < SLOT_HAND) handSlotTile[baseSlot + n++] = t;
            int mainN = n;
            handMainN[s] = mainN;
            for (int i = 0; i < drN && n < SLOT_HAND; i++) handSlotTile[baseSlot + n++] = dr[i];
            for (int i = n; i < SLOT_HAND; i++) handSlotTile[baseSlot + i] = -1;
            Vector3 u = U(s), r = R(s);
            // 門前の部分だけで中央にそろえ、ツモ牌はその右に足す(ツモっても手牌がずれないように)
            float width = mainN * (TW + 0.001f);
            for (int i = 0; i < SLOT_HAND; i++)
            {
                int slot = baseSlot + i;
                int t = handSlotTile[slot];
                if (t < 0 || engine.phase == SqmEngine.PH_DEAL) { Place(handTiles[slot], handFaces[slot], -1, Vector3.zero, s, false, false, false, false); continue; }
                float x = -width / 2 + i * (TW + 0.001f) + TW / 2 + (i >= mainN ? 0.012f : 0);
                // 和了った人の手牌は、倒して(表を上にして)見せる
                if (engine.outRole[s] != SqmEngine.ROLE_NONE)
                {
                    Vector3 lay = u * (R_HAND + 0.005f) + r * x + Vector3.up * (TOP + TT / 2);
                    Place(handTiles[slot], handFaces[slot], t, lay, s, true, true, true, false);
                    continue;
                }
                float lift = (slot == sel0 || slot == sel1) ? 0.012f : 0;
                Vector3 pos = u * R_HAND + r * x + Vector3.up * (TOP + TH / 2 + lift);
                Place(handTiles[slot], handFaces[slot], t, pos, s, false, true, reveal, false);
            }
        }

        private void RefreshRiver(int s)
        {
            int baseSlot = s * SLOT_RIVER;
            // 河の新しい方から(鳴かれた牌は除く)最大 SLOT_RIVER 枚を、古い順に並べる
            int total = 0;
            int startGroup = engine.riverLen[s];
            while (startGroup > 0)
            {
                int ri = s * SqmEngine.MAXRIVER + startGroup - 1;
                int cnt = (engine.rvTaken0[ri] ? 0 : 1) + (engine.rvN[ri] == 2 && !engine.rvTaken1[ri] ? 1 : 0);
                if (total + cnt > SLOT_RIVER) break;
                total += cnt;
                startGroup--;
            }
            Vector3 u = U(s), r = R(s);
            int n = 0;
            for (int g = startGroup; g < engine.riverLen[s]; g++)
            {
                int ri = s * SqmEngine.MAXRIVER + g;
                for (int k = 0; k < engine.rvN[ri]; k++)
                {
                    bool taken = k == 0 ? engine.rvTaken0[ri] : engine.rvTaken1[ri];
                    if (taken || n >= SLOT_RIVER) continue;
                    int t = k == 0 ? engine.rvT0[ri] : engine.rvT1[ri];
                    int col = n % 12, row = n / 12;
                    float x = (col - 5.5f) * (TW + 0.001f);
                    float rad = 0.745f - row * (TH + 0.002f);
                    Vector3 pos = u * rad + r * x + Vector3.up * (TOP + TT / 2);
                    bool side = engine.rvRiichi[ri] && k == 0;
                    Place(riverTiles[baseSlot + n], riverFaces[baseSlot + n], t, pos, s, true, true, true, side);
                    n++;
                }
            }
            for (int i = n; i < SLOT_RIVER; i++) Place(riverTiles[baseSlot + i], riverFaces[baseSlot + i], -1, Vector3.zero, s, true, false, false, false);
        }

        private void RefreshMelds(int s)
        {
            int baseSlot = s * SLOT_MELD;
            Vector3 u = U(s), r = R(s);
            int n = 0;
            float x = 0.40f;   // 右端から左へ
            for (int m = 0; m < engine.meldN[s]; m++)
            {
                int mi = s * 4 + m;
                int ty = engine.meldType[mi], st = engine.meldStart[mi];
                int cnt = ty == SqmEngine.MT_T ? 6 : 6;
                for (int k = 0; k < cnt + (engine.meldKan[mi] != SqmEngine.KAN_NONE ? 1 : 0) && n < SLOT_MELD; k++)
                {
                    int t = ty == SqmEngine.MT_K ? st : ty == SqmEngine.MT_T ? st + k / 2 : st + k;
                    if (k >= cnt) t = st;
                    bool back = engine.meldKan[mi] == SqmEngine.KAN_ANKAN && (k == 0 || k == cnt);
                    Vector3 pos = u * R_MELD + r * (x - TW / 2) + Vector3.up * (TOP + TT / 2);
                    Place(meldTiles[baseSlot + n], meldFaces[baseSlot + n], t, pos, s, true, true, !back, false);
                    x -= TW + 0.001f;
                    n++;
                }
                x -= 0.01f;
            }
            for (int i = n; i < SLOT_MELD; i++) Place(meldTiles[baseSlot + i], meldFaces[baseSlot + i], -1, Vector3.zero, s, true, false, false, false);
        }

        /// <summary>
        /// 中央のパネル: 奥にロン牌の列、真ん中に Resync・Reset、手前にドラ表示牌(増えたら手前へ段を足す)。見ている人の席から見た向き
        /// </summary>
        private void RefreshCenter()
        {
            int view = humanSeat >= 0 ? humanSeat : 0;
            Vector3 u = U(view), r = R(view);
            const int PER_ROW = 8;
            float y = TOP + CENTER_H + TT / 2, step = TW + 0.002f, rowStep = TH + 0.003f;
            int dn = engine.doraIndN;
            for (int i = 0; i < N_DORA; i++)
            {
                bool show = i < dn && engine.phase != SqmEngine.PH_DEAL;
                int row = i / PER_ROW, inRow = Mathf.Min(PER_ROW, dn - row * PER_ROW);
                Vector3 pos = u * (0.06f + row * rowStep) + r * ((i % PER_ROW - (inRow - 1) / 2f) * step) + Vector3.up * y;
                Place(centerTiles[i], centerFaces[i], show ? engine.doraInd[i] : -1, pos, view, true, show, true, false);
            }
            int rn = engine.ronTilesLen;
            for (int i = 0; i < N_RON; i++)
            {
                bool show = i < rn && engine.phase != SqmEngine.PH_DEAL;
                int t = show ? engine.ronTiles[i] : -1;
                int row = i / PER_ROW, inRow = Mathf.Min(PER_ROW, rn - row * PER_ROW);
                Vector3 pos = -u * (0.06f + row * rowStep) + r * ((i % PER_ROW - (inRow - 1) / 2f) * step) + Vector3.up * y;
                Place(centerTiles[N_DORA + i], centerFaces[N_DORA + i], t, pos, view, true, show, t >= 0, false);
            }
            if (centerButtons != null) centerButtons.localRotation = Quaternion.Euler(0, -view * 45f, 0);
        }

        private void RefreshLabel(int s)
        {
            bool live = lMatch > 0;
            bool riichi = live && engine.riichiOn[s] && engine.phase != SqmEngine.PH_DEAL;
            if (centerLights != null && s < centerLights.Length && centerLights[s] != null)
            {
                Material cm = riichi ? matLightRiichi : matLightIdle;
                if (cm != null && centerLights[s].sharedMaterial != cm) centerLights[s].sharedMaterial = cm;
            }
            if (rimLights != null && s < rimLights.Length && rimLights[s] != null)
            {
                bool turn = Running() && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == s;
                Material m = turn ? matLightTurn : riichi ? matLightRiichi : matLightIdle;
                if (m != null && rimLights[s].sharedMaterial != m) rimLights[s].sharedMaterial = m;
            }
            if (seatLabels != null && s < seatLabels.Length && seatLabels[s] != null)
            {
                string txt = NameOf(s);
                if (!live && sPlayer[s] == 0) txt = "空席";
                if (sPlayer[s] != 0 && sPlayer[s] == sHost) txt = "<color=#40e0ff>HOST</color> " + txt;
                if (live && engine.riichiOn[s]) txt += "  リーチ";
                if (live && engine.outRole[s] == SqmEngine.ROLE_FIRST) txt += "  先発";
                if (live && engine.outRole[s] == SqmEngine.ROLE_SECOND) txt += "  後発";
                if (Running() && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == s) txt = "▶ " + txt;
                seatLabels[s].text = txt;
            }
            if (windLabels != null && s < windLabels.Length && windLabels[s] != null)
                windLabels[s].text = live ? WindName(engine.SeatWindOf(s, engine.dealer)) : "";
            if (scoreLabels != null && s < scoreLabels.Length && scoreLabels[s] != null)
                scoreLabels[s].text = live ? engine.score[s].ToString() : "";
            CheckLabelChanged(s);
            if (joinButtons != null && s < joinButtons.Length && joinButtons[s] != null)
            {
                // 座っていない人に、空いている席だけ出す。NPC だけの試合中はどこにも座れない
                bool showJoin = humanSeat < 0 && sPlayer[s] == 0 && !(Running() && HumanMask() == 0);
                if (joinButtons[s].activeSelf != showJoin) joinButtons[s].SetActive(showJoin);
            }
        }

        private string WindName(int t) { return tileNames[t - 36]; }

        private string[] labelShown;
        private int labelOffFrame;

        /// <summary>席 s の縁の文字が、前に写したときから変わっていたら写し直す</summary>
        private void CheckLabelChanged(int s)
        {
            if (labelCamera == null) return;
            if (labelShown == null) labelShown = new string[NP];
            string key = (seatLabels != null && seatLabels[s] != null ? seatLabels[s].text : "")
                + "|" + (windLabels != null && windLabels[s] != null ? windLabels[s].text : "")
                + "|" + (scoreLabels != null && scoreLabels[s] != null ? scoreLabels[s].text : "");
            if (labelShown[s] == key) return;
            labelShown[s] = key;
            RenderLabels();
        }

        /// <summary>縁の文字の画像を写す: カメラを数フレームだけ動かして止める(続けて変わったら、止めるのを延ばす)</summary>
        private void RenderLabels()
        {
            if (labelCamera == null) return;
            if (!labelCamera.enabled) labelCamera.enabled = true;
            labelOffFrame = Time.frameCount + 2;
            SendCustomEventDelayedFrames(nameof(StopLabelCamera), 3);
        }

        public void StopLabelCamera()
        {
            if (labelCamera != null && Time.frameCount >= labelOffFrame) labelCamera.enabled = false;
        }

        private void RefreshControls()
        {
            optN = 0;
            bool ok = Running() && CaughtUp() && humanSeat >= 0 && sentAt < 0 && sPageN == 0 && bannerT <= 0;
            if (confirmButton != null)
            {
                bool showOk = humanSeat >= 0 && sPageN > 0 && (sAckMask & (1 << humanSeat)) != 0;
                if (confirmButton.activeSelf != showOk) confirmButton.SetActive(showOk);
            }
            bool myTurn = ok && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == humanSeat;
            bool myCall = false;
            if (ok && engine.pendKind == SqmEngine.PEND_CALLS)
                for (int i = 0; i < engine.pendN; i++) if (engine.pendSeats[i] == humanSeat) myCall = true;
            bool myNext = ok && engine.pendKind == SqmEngine.PEND_NEXT && humanSeat == engine.dealer && engine.CanAgariYame();
            bool riichiOk = false;
            if (myTurn || myCall || myNext)
            {
                int n = engine.BuildLegal(humanSeat);
                for (int i = 0; i < n && optN < N_OPT; i++)
                {
                    int b = i * SqmEngine.OPT;
                    int ty = engine.legal[b + SqmEngine.O_TYPE];
                    if (ty == SqmEngine.A_DISCARD) { riichiOk = engine.legal[b + SqmEngine.O_RIICHIOK] == 1; continue; }
                    for (int k = 0; k < SqmEngine.OPT; k++) optBuf[optN * SqmEngine.OPT + k] = engine.legal[b + k];
                    optN++;
                }
            }
            if (controls != null && humanSeat >= 0)
            {
                Vector3 u = U(humanSeat);
                controls.localPosition = u * 1.12f + Vector3.up * (TOP + 0.25f);
                controls.localRotation = Quaternion.LookRotation(-u, Vector3.up);
            }
            // リーチ中は、ツモ和了・槓を見送ってツモ切りするときだけ「切る」を出す
            if (discardButton != null) discardButton.SetActive(myTurn && (!engine.riichiOn[humanSeat] || optN > 0));
            if (riichiButton != null) riichiButton.SetActive(myTurn && riichiOk);
            if (riichiLabel != null) riichiLabel.text = riichiArmed ? "リーチ ON" : "リーチ";
            padMyTurn = myTurn;
            // リーチは、聴牌にできる切り方があるときだけ(自分の番ごとに1回だけ調べる)
            if (myTurn && riichiOk)
            {
                if (riichiTile == null) riichiTile = new bool[NT];
                if (riichiTileStep != engine.step) { riichiTileStep = engine.step; riichiTileN = engine.RiichiTiles(humanSeat, riichiTile); }
            }
            padRiichiOk = myTurn && riichiOk && riichiTileN > 0;
            if (!padRiichiOk) riichiArmed = false;
            padDiscardOk = myTurn && (!engine.riichiOn[humanSeat] || optN > 0);
            padConfirm = humanSeat >= 0 && sPageN > 0 && (sAckMask & (1 << humanSeat)) != 0;
            if (discardButtons != null)
                for (int s = 0; s < discardButtons.Length; s++)
                {
                    bool on = s == humanSeat && padDiscardOk;
                    if (discardButtons[s] != null && discardButtons[s].activeSelf != on) discardButtons[s].SetActive(on);
                }
            if (optionButtons != null)
                for (int i = 0; i < N_OPT && i < optionButtons.Length; i++)
                {
                    bool show = i < optN;
                    if (optionButtons[i].activeSelf != show) optionButtons[i].SetActive(show);
                    if (show) optionLabels[i].text = OptionText(i * SqmEngine.OPT);
                }
            // 「席を立つ」「新しい試合」は試合が無いときだけ卓に出す(試合中はパッドから)
            bool idle = !Running();
            bool showLeave = idle && humanSeat >= 0;
            if (leaveButton != null && leaveButton.activeSelf != showLeave) leaveButton.SetActive(showLeave);
            if (newMatchButton != null)
            {
                bool showNew = idle && (sHost == 0 || sHost == MyId());
                if (newMatchButton.activeSelf != showNew) newMatchButton.SetActive(showNew);
            }
        }

        public string OptionText(int b)
        {
            int ty = optBuf[b + SqmEngine.O_TYPE];
            if (ty == SqmEngine.A_TSUMO) return "ツモ " + optBuf[b + SqmEngine.O_HAN] + "翻";
            if (ty == SqmEngine.A_RON)
            {
                if (optBuf[b + SqmEngine.O_MODE] == SqmEngine.RON_TWO) return "二枚ロン " + optBuf[b + SqmEngine.O_POINTS];
                return "片ロン " + TileCode(optBuf[b + SqmEngine.O_TILE]) + "+" + TileCode(optBuf[b + SqmEngine.O_RONTILE]) + " " + optBuf[b + SqmEngine.O_POINTS];
            }
            if (ty == SqmEngine.A_PON) return "ポン " + TileCode(optBuf[b + SqmEngine.O_START]) + "×" + optBuf[b + SqmEngine.O_TAKEN];
            if (ty == SqmEngine.A_CHI)
            {
                int st = optBuf[b + SqmEngine.O_START];
                string kind = optBuf[b + SqmEngine.O_MELDTYPE] == SqmEngine.MT_T ? "対順" : "完順";
                return "チー " + kind + " " + TileCode(st) + "〜";
            }
            if (ty == SqmEngine.A_KAN)
            {
                int m = optBuf[b + SqmEngine.O_MODE];
                string k = m == SqmEngine.KAN_ANKAN ? "暗槓 " : m == SqmEngine.KAN_KAKAN ? "加槓 " : "大明槓 ";
                return k + TileCode(optBuf[b + SqmEngine.O_TILE]);
            }
            if (ty == SqmEngine.A_ABORT) return "十四種十四牌";
            if (ty == SqmEngine.A_PASS) return "パス";
            if (ty == SqmEngine.A_NEXT) return optBuf[b + SqmEngine.O_MODE] == 1 ? "止める" : optBuf[b + SqmEngine.O_MODE] == 0 ? "続ける" : "次へ";
            return "?";
        }

        // ================================================================ 結果の画面と「確認」
        // 和了(先発・後発)・流局が起きたら、ホストが「出来事」を作る。出来事は何枚かの画面(ページ)からなり、
        // 和了は1人1枚(親から巡る順)、流局はその後ろに1枚。座っている人(NPC を除く)全員が「確認」を押したら次のページへ。
        // 最後のページが終わったら試合を続ける。だれも座っていなければ、PAGE_AUTO 秒で進める。
        public const float PAGE_AUTO = 4f;

        /// <summary>座っている人の席のビット</summary>
        private int HumanMask()
        {
            int m = 0;
            for (int s = 0; s < NP; s++) if (sPlayer[s] != 0) m |= 1 << s;
            return m;
        }

        /// <summary>ホスト: 新しい和了・流局があれば、出来事を作る(true = 作った)</summary>
        private bool HostCheckEvent()
        {
            if (sPageN > 0 || lMatch == 0) return false;
            int seen = sWinsKyoku == engine.kyokuSeq ? sWinsSeen : 0;
            bool over = engine.phase == SqmEngine.PH_KYOKU_OVER && sKyokuOverSeen != engine.kyokuSeq;
            bool newWin = engine.winsN > seen;
            if (!newWin && !over) return false;
            sEvtWinFrom = seen;
            sEvtWinTo = engine.winsN;
            sEvtTail = over && engine.resType != SqmEngine.RES_WIN;
            sWinsSeen = engine.winsN;
            sWinsKyoku = engine.kyokuSeq;
            if (over) sKyokuOverSeen = engine.kyokuSeq;
            sPageN = (sEvtWinTo - sEvtWinFrom) + (sEvtTail ? 1 : 0);
            sPage = 0;
            sAckMask = HumanMask();
            pageWait = PAGE_AUTO;
            RequestSerialization();
            dirty = true;
            return sPageN > 0;
        }

        private void HostNextPage()
        {
            sPage++;
            if (sPage >= sPageN) { sPageN = 0; sPage = 0; sAckMask = 0; wait = 0.3f; }
            else { sAckMask = HumanMask(); pageWait = PAGE_AUTO; }
            RequestSerialization();
            dirty = true;
        }

        private void HostAck(int seat, int page)
        {
            if (sPageN == 0 || page != sPage) return;
            sAckMask &= ~(1 << seat);
            if (sAckMask == 0) HostNextPage();
            else { RequestSerialization(); dirty = true; }
        }

        /// <summary>人が抜けたり立ったりしたら、確認待ちから外す</summary>
        private void HostFixAck()
        {
            if (sPageN == 0) return;
            int had = sAckMask;
            sAckMask &= HumanMask();
            if (had != 0 && sAckMask == 0) HostNextPage();
        }

        public void Confirm()
        {
            if (humanSeat < 0 || sPageN == 0 || (sAckMask & (1 << humanSeat)) == 0) return;
            int key = sPage + sEvtWinFrom * 100 + sWinsKyoku * 10000;
            if (ackSentPage == key && ackSentStart == sStartMs) return;   // 局の番号は試合ごとに 0 から数え直すので、試合の開始時刻も比べる
            ackSentPage = key;
            ackSentStart = sStartMs;
            PlaySfx(SFX_CLICK);
            if (IsHost()) HostAck(humanSeat, sPage);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetAck), sPage);
            dirty = true;
        }

        [NetworkCallable]
        public void NetAck(int page)
        {
            if (!IsHost()) return;
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p)) return;
            for (int s = 0; s < NP; s++) if (sPlayer[s] == p.playerId) HostAck(s, page);
        }

        // ---- ホストが参加者を席から立たせる(パッドから呼ぶ) ----
        public void Kick0() { RequestKick(0); } public void Kick1() { RequestKick(1); } public void Kick2() { RequestKick(2); } public void Kick3() { RequestKick(3); }
        public void Kick4() { RequestKick(4); } public void Kick5() { RequestKick(5); } public void Kick6() { RequestKick(6); } public void Kick7() { RequestKick(7); }

        private void RequestKick(int seat)
        {
            if (sHost == 0 || sHost != MyId()) return;
            if (IsHost()) HostKick(seat);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetKick), seat);
        }

        [NetworkCallable]
        public void NetKick(int seat)
        {
            if (!IsHost()) return;
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p) || p.playerId != sHost) return;
            HostKick(seat);
        }

        private void HostKick(int seat)
        {
            if (seat < 0 || seat >= NP || sPlayer[seat] == 0 || sPlayer[seat] == sHost) return;
            sPlayer[seat] = 0;
            FixHost();
            HostFixAck();
            UpdateMySeat();
            RequestSerialization();
            dirty = true;
        }

        // ---- 結果の画面を描く ----
        private int[] pageOrder;                             // 出来事の和了を、親から巡る順に並べた番号

        /// <summary>いま見せる結果のページ(-1 = なし、0〜 = 和了、99 = 流局、100 = 終局)</summary>
        private int CurrentPage()
        {
            if (lMatch == 0) return -1;
            if (sPageN > 0 && lMatch == sMatch && replayTo == 0 && sEvtWinTo <= engine.winsN)
            {
                int wins = sEvtWinTo - sEvtWinFrom;
                if (sPage < wins) return sPage;
                if (sEvtTail && engine.phase == SqmEngine.PH_KYOKU_OVER) return 99;
                return -1;
            }
            if (engine.phase == SqmEngine.PH_MATCH_OVER) return 100;
            return -1;
        }

        private void RefreshResult(int page)
        {
            bool show = page >= 0;
            if (resultRoot != null && resultRoot.activeSelf != show) resultRoot.SetActive(show);
            if (!show) return;
            for (int i = 0; i < resHand.Length; i++) ResTile(i, -1, false);
            for (int i = 0; i < resDora.Length; i++) HoloTile(resDora[i], -1);
            for (int i = 0; i < resUra.Length; i++) HoloTile(resUra[i], -1);
            resHead.text = ""; resYaku0.text = ""; resYaku1.text = ""; resYaku2.text = ""; resHan.text = ""; resCalc.text = ""; resPoints.text = "";
            resBody.text = ""; resRankL.text = ""; resRankR.text = ""; resDoraLabel.text = ""; resUraLabel.text = "";
            string foot = "";
            if (page == 100) RenderFinal();
            else if (page == 99) RenderDraw();
            else RenderWin(page);
            if (page != 100)
            {
                // 確認待ちの人
                string waitList = "";
                for (int s = 0; s < NP; s++) if ((sAckMask & (1 << s)) != 0) waitList += (waitList.Length > 0 ? "、" : "") + NameOf(s);
                if (sAckMask != 0) foot = "確認待ち: " + waitList;
                else if (HumanMask() == 0) foot = "まもなく続きます";
                if (humanSeat >= 0 && (sAckMask & (1 << humanSeat)) != 0) foot = "<color=#ffd070>「確認」を押してください</color>   " + foot;
            }
            else foot = "このセットは終局しました　詳細な結果はパッドをご確認ください";
            resFoot.text = foot;
        }

        private void RenderWin(int page)
        {
            int n = sEvtWinTo - sEvtWinFrom;
            if (pageOrder == null) pageOrder = new int[16];
            for (int i = 0; i < n; i++) pageOrder[i] = sEvtWinFrom + i;
            // 親から巡る順に並べる
            for (int i = 1; i < n; i++)
            {
                int x = pageOrder[i]; int kx = (engine.winSeat[x] - engine.dealer + NP) % NP;
                int j = i - 1;
                while (j >= 0 && (engine.winSeat[pageOrder[j]] - engine.dealer + NP) % NP > kx) { pageOrder[j + 1] = pageOrder[j]; j--; }
                pageOrder[j + 1] = x;
            }
            int w = pageOrder[page];
            int s = engine.winSeat[w];
            string role = engine.winRole[w] == SqmEngine.ROLE_FIRST ? "先発" : "後発";
            resTitle.text = role + "和了　東" + engine.kyoku + "局";
            int wt = engine.winType[w];
            string kind = wt == SqmEngine.WT_TSUMO ? "ツモ" : wt == SqmEngine.WT_RON2 ? "2枚ロン" : "片ロン";
            string head = SeatName(s) + " <color=#ff5a48>" + kind + "</color>";
            if (wt != SqmEngine.WT_TSUMO && engine.winFrom[w] >= 0) head += "  <size=55%>放銃：" + SeatName(engine.winFrom[w]) + "</size>";
            resHead.text = head;
            ShowHand(s, engine.winTileA[w], engine.winTileB[w]);
            // ドラ・裏ドラ
            resDoraLabel.text = "ドラ";
            for (int i = 0; i < resDora.Length && i < engine.doraIndN; i++) HoloTile(resDora[i], engine.doraInd[i]);
            if (engine.winRiichi[w] && engine.rUseUra)
            {
                resUraLabel.text = "裏ドラ";
                for (int i = 0; i < resUra.Length && i < engine.uraIndN; i++) HoloTile(resUra[i], engine.uraInd[i]);
            }
            // 役(3列。多いときは文字が小さくなる)
            int yn = engine.winYakuN[w];
            int per = (yn + 2) / 3;
            if (per < 1) per = 1;
            string c0 = "", c1 = "", c2 = "";
            for (int k = 0; k < yn; k++)
            {
                int id = engine.winYakuId[w * SqmEngine.MAXYAKU + k];
                int han = engine.winYakuHan[w * SqmEngine.MAXYAKU + k];
                string line = yakuJa[id] + "<pos=78%>" + (engine.winYakuman[w] && id >= 40 ? "役満" : han + "翻") + "\n";
                if (k < per) c0 += line; else if (k < per * 2) c1 += line; else c2 += line;
            }
            resYaku0.text = c0; resYaku1.text = c1; resYaku2.text = c2;
            // 翻・点数
            int hanAll = engine.winHan[w];
            resHan.text = engine.winYakuman[w] ? "立方満" : hanAll + "<size=60%>翻</size> " + LimitName(hanAll);
            int m10 = engine.winM10[w];
            string calc = engine.winBase[w].ToString("N0") + " ×" + (m10 / 10) + "." + (m10 % 10);
            if (engine.winHonba[w] > 0) calc += "  " + engine.winHonba[w] + "本場";
            if (engine.winStick[w] > 0) calc += "  供託 +" + engine.winStick[w].ToString("N0");
            resCalc.text = calc;
            resPoints.text = engine.winTotal[w].ToString("N0") + "<size=60%>点</size>";
        }

        private string LimitName(int han)
        {
            if (han >= 8) return "平方満";
            if (han == 7) return "倍満";
            if (han == 6) return "跳満";
            if (han == 5) return "満貫";
            return "";
        }

        private string SeatName(int s)
        {
            return WindName(engine.SeatWindOf(s, engine.dealer)) + "家 " + NameOf(s);
        }

        private void RenderDraw()
        {
            if (engine.resType == SqmEngine.RES_ABORT)
            {
                resTitle.text = "流局";
                int r = engine.resReason;
                if (r == SqmEngine.REASON_KYUSHU)
                {
                    resHead.text = SeatName(engine.resSeat) + " 十四種十四牌";
                    ShowHand(engine.resSeat, -1, -1);
                    resBody.text = "\n\n\n十四種以上の么九牌がそろったため、この局は流局となります。";
                }
                else if (r == SqmEngine.REASON_HACHIFUU)
                {
                    resHead.text = "八風連打";
                    resBody.text = "\n八家すべてが最初に同じ風牌を切ったため、この局は流局となります。";
                }
                else
                {
                    resHead.text = "八家立直";
                    resBody.text = "\n八家すべてが立直を宣言したため、この局は流局となります。";
                }
                return;
            }
            resTitle.text = "荒牌流局";
            int active = 0;
            for (int s = 0; s < NP; s++) if (engine.outRole[s] == SqmEngine.ROLE_NONE) active++;
            int tn = engine.resTenpaiN;
            if (engine.resHadWin) { resBody.text = "\n先発の和了があったため、不聴罰符の支払いはありません。"; return; }
            if (tn == 0) { resBody.text = "\n全員が不聴のため、不聴罰符の支払いはありません。"; return; }
            if (tn >= active) { resBody.text = "\n全員が聴牌のため、不聴罰符の支払いはありません。"; return; }
            resHead.text = "聴牌 " + tn + "名  不聴罰符 " + engine.rNotenTotal.ToString("N0") + "点";
            string list = "\n";
            for (int k = 0; k < tn; k++)
            {
                int s = engine.resTenpai[k];
                list += (k > 0 ? ", " : "") + WindName(engine.SeatWindOf(s, engine.dealer)) + " " + NameOf(s);
            }
            resBody.text = list;
        }

        private void RenderFinal()
        {
            resTitle.text = "終局";
            string l = "", r = "";
            string[] ord = new string[] { "1st.", "2nd.", "3rd.", "4th.", "5th.", "6th.", "7th.", "8th." };
            for (int i = 0; i < NP; i++)
            {
                string line = ord[i] + "<pos=16%>" + NameOf(engine.mrSeat[i]) + "<pos=66%>" + ScoreText(engine.mrScore[i]) + "\n";
                if (i < 4) l += line; else r += line;
            }
            resRankL.text = l;
            resRankR.text = r;
        }

        /// <summary>
        /// ホロの各席の点数(7セグ): 6 文字にそろえ、足りない桁は頭に薄い 0 を足す(中央揃えでも桁が変わって位置がずれないように)。
        /// 負の値は「-」も1文字に数える(-4000 → -04000)。7 桁以上はそのまま
        /// </summary>
        private string SegScore(int v)
        {
            string body = (v < 0 ? -v : v).ToString();
            int width = v < 0 ? 5 : 6;
            string pad = "";
            for (int i = body.Length; i < width; i++) pad += "0";
            return (v < 0 ? "-" : "") + (pad.Length > 0 ? "<alpha=#38>" + pad + "<alpha=#FF>" : "") + body;
        }

        /// <summary>点数を 10^6 の位まで(頭の 0 は灰色)。負の値は赤で「-」をつける</summary>
        private string ScoreText(int v)
        {
            if (v < 0) return "<color=#ff5a48>-" + (-v).ToString("N0") + "</color>";
            string full = v.ToString("0000000");
            string withComma = full.Substring(0, 1) + "," + full.Substring(1, 3) + "," + full.Substring(4, 3);
            int lead = 0;
            while (lead < withComma.Length - 1 && (withComma[lead] == '0' || withComma[lead] == ',')) lead++;
            if (lead == 0) return withComma;
            return "<color=#4a5a66>" + withComma.Substring(0, lead) + "</color>" + withComma.Substring(lead);
        }

        /// <summary>手牌(門前は並べて、副露は後ろに)。a・b は赤枠をつける和了牌</summary>
        private void ShowHand(int s, int a, int b)
        {
            int n = 0;
            bool markA = a >= 0, markB = b >= 0;
            for (int t = 0; t < NT; t++)
            {
                for (int k = 0; k < engine.handC[s * NT + t] && n < resHand.Length; k++)
                {
                    bool mark = false;
                    if (markA && t == a) { mark = true; markA = false; }
                    else if (markB && t == b) { mark = true; markB = false; }
                    ResTile(n++, t, mark);
                }
            }
            for (int m = 0; m < engine.meldN[s] && n < resHand.Length; m++)
            {
                n++;   // 副露の前はひとつ空ける
                int mi = s * 4 + m;
                int ty = engine.meldType[mi], st = engine.meldStart[mi];
                int cnt = 6 + (engine.meldKan[mi] != SqmEngine.KAN_NONE ? 1 : 0);
                for (int k = 0; k < cnt && n < resHand.Length; k++)
                {
                    int t = ty == SqmEngine.MT_K ? st : ty == SqmEngine.MT_T ? st + k / 2 : st + k;
                    if (k >= 6) t = st;
                    ResTile(n++, t, false);
                }
            }
        }

        private void ResTile(int i, int tile, bool mark)
        {
            if (i >= resHand.Length) return;
            HoloTile(resHand[i], tile);
            if (resHandMark != null && i < resHandMark.Length && resHandMark[i] != null)
            {
                bool m = mark && tile >= 0;
                if (resHandMark[i].activeSelf != m) resHandMark[i].SetActive(m);
            }
        }

        // ================================================================ 効果音
        public const int SFX_DISCARD = 0, SFX_CALL = 1, SFX_RIICHI = 2, SFX_WIN = 3, SFX_END = 4, SFX_TURN = 5, SFX_CLICK = 6, SFX_KYOKU = 7;

        public void PlaySfx(int i)
        {
            if (sfx == null || sfxClips == null || i >= sfxClips.Length || sfxClips[i] == null || sfxVolume <= 0) return;
            if (i == SFX_TURN && padAudio != null && padAudio.gameObject.activeInHierarchy) { padAudio.PlayOneShot(sfxClips[i], sfxVolume); return; }
            sfx.PlayOneShot(sfxClips[i], sfxVolume);
        }

        /// <summary>記録1つを再生した直後に、その手に合った音を鳴らす(追いつき中は鳴らさない)</summary>
        private void SfxFor(int code)
        {
            VoiceFor(code);
            int kind = (code >> 3) & 3;
            if (kind == K_DISCARD) PlaySfx(((code >> 5) & 1) == 1 ? SFX_RIICHI : SFX_DISCARD);
            else if (kind == K_LEGAL)
            {
                int ty = act[SqmEngine.O_TYPE];
                if (ty == SqmEngine.A_RON || ty == SqmEngine.A_TSUMO) PlaySfx(SFX_WIN);
                else if (ty == SqmEngine.A_PON || ty == SqmEngine.A_CHI || ty == SqmEngine.A_KAN) PlaySfx(SFX_CALL);
                else if (ty == SqmEngine.A_ABORT) PlaySfx(SFX_END);
            }
            if (engine.phase == SqmEngine.PH_KYOKU_OVER && kind != K_NEXT && !(kind == K_LEGAL && (act[SqmEngine.O_TYPE] == SqmEngine.A_RON || act[SqmEngine.O_TYPE] == SqmEngine.A_TSUMO))) PlaySfx(SFX_END);
            bool myTurn = humanSeat >= 0 && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == humanSeat;
            if (myTurn && !wasMyTurn) Notice();
            wasMyTurn = myTurn;
            bool myCall = false;
            if (humanSeat >= 0 && engine.pendKind == SqmEngine.PEND_CALLS)
                for (int i = 0; i < engine.pendN; i++) if (engine.pendSeats[i] == humanSeat) myCall = true;
            if (myCall && !wasMyCall) Notice();   // 鳴ける(ロンできる)ときも知らせる
            wasMyCall = myCall;
        }

        private bool wasMyTurn, wasMyCall, noticeLater;

        /// <summary>自分の番・鳴けるときの notice。局の始まりの演出中(これから始まる場合も)は、局を言い終わってから鳴らす</summary>
        private void Notice()
        {
            bool bannerSoon = bannerT > 0 || (lMatch > 0 && replayTo == 0 && lMatch * 1000 + engine.kyokuSeq != bannerSeq);
            if (bannerSoon) noticeLater = true;
            else PlaySfx(SFX_TURN);
        }
        public AudioSource padAudio;         // パッドから鳴らす音(自分の番・鳴けるときの notice)

        // ---- 宣言の声(宣言した席の位置から鳴らす) ----
        public const int VOICE_RIICHI = 0, VOICE_PON = 1, VOICE_CHI = 2, VOICE_KAN = 3, VOICE_RON = 4, VOICE_TSUMO = 5, VOICE_KYUSHU = 6,
            VOICE_RON_KATA = 7, VOICE_RON_NIMAI = 8, VOICE_DOUBLE_RIICHI = 9;

        private void PlayVoice(int seat, int i, int fallback)
        {
            if (voiceSources == null || voiceClips == null || seat < 0 || seat >= voiceSources.Length || voiceSources[seat] == null || voiceVolume <= 0) return;
            AudioClip clip = i < voiceClips.Length ? voiceClips[i] : null;
            if (clip == null && fallback >= 0 && fallback < voiceClips.Length) clip = voiceClips[fallback];
            if (clip == null) return;
            voiceSources[seat].PlayOneShot(clip, voiceVolume);
        }

        /// <summary>記録1つを再生した直後に、宣言なら声を鳴らす</summary>
        private void VoiceFor(int code)
        {
            int seat = code & 7, kind = (code >> 3) & 3;
            if (kind == K_DISCARD)
            {
                if (((code >> 5) & 1) == 1) PlayVoice(seat, engine.riichiDouble[seat] ? VOICE_DOUBLE_RIICHI : VOICE_RIICHI, VOICE_RIICHI);
                return;
            }
            if (kind != K_LEGAL) return;
            int ty = act[SqmEngine.O_TYPE];
            if (ty == SqmEngine.A_PON) PlayVoice(seat, VOICE_PON, -1);
            else if (ty == SqmEngine.A_CHI) PlayVoice(seat, VOICE_CHI, -1);
            else if (ty == SqmEngine.A_KAN) PlayVoice(seat, VOICE_KAN, -1);
            else if (ty == SqmEngine.A_TSUMO) PlayVoice(seat, VOICE_TSUMO, -1);
            else if (ty == SqmEngine.A_RON) PlayVoice(seat, act[SqmEngine.O_MODE] == SqmEngine.RON_TWO ? VOICE_RON_NIMAI : VOICE_RON_KATA, VOICE_RON);
            else if (ty == SqmEngine.A_ABORT && act[SqmEngine.O_TILE] == SqmEngine.REASON_KYUSHU) PlayVoice(seat, VOICE_KYUSHU, -1);
        }

        // ================================================================ パッド(SqmPad / SqmPadBody)から使うもの
        [System.NonSerialized] public bool padMyTurn, padRiichiOk, padDiscardOk, padConfirm;
        private bool[] riichiTile;                           // リーチの打牌に使える牌(自分の番ごとに調べる)
        private int riichiTileStep = -1, riichiTileN;

        private string padWaitText = "";
        private int padWaitStep = -1;

        /// <summary>
        /// パッドの「待ち」の行: 聴牌していれば、和了れる2枚の組み合わせを出す(役が無い組は「役なし」)。フリテンならそう書く。
        /// 手牌が変わったときだけ数え直す
        /// </summary>
        public string PadWaitText()
        {
            if (humanSeat < 0 || lMatch == 0 || !Running() || engine.outRole[humanSeat] != SqmEngine.ROLE_NONE) return "";
            if (padWaitStep == engine.step) return padWaitText;
            padWaitStep = engine.step;
            int n = engine.PadWaits(humanSeat);
            if (n == 0) { padWaitText = ""; return padWaitText; }
            // 数えている途中で待ちの配列が上書きされないよう、先に写しておく
            int show = n < 8 ? n : 8;
            int[] wa = new int[show], wb = new int[show];
            for (int k = 0; k < show; k++) { wa[k] = engine.waitA[k]; wb[k] = engine.waitB[k]; }
            string t = "待ち ";

            for (int k = 0; k < show; k++)
            {
                int h = engine.WaitHint(humanSeat, wa[k], wb[k]);

                t += (k > 0 ? "  " : "") + TileCode(wa[k]) + "+" + TileCode(wb[k]) + (h == 1 ? "<color=#c03030>(役なし)</color>" : "");
            }
            if (n > show) t += "  ほか" + (n - show) + "通り";
            if (engine.IsFuriten(humanSeat, engine.rFuriten)) t += "  <color=#c03030>フリテン</color>";
            padWaitText = t;
            return padWaitText;
        }

        /// <summary>パッドの牌を暗くするか(リーチ ON のとき、聴牌にならない牌)</summary>
        public bool PadTileDim(int i)
        {
            int t = PadSlotTile(i);
            return riichiArmed && t >= 0 && riichiTile != null && !riichiTile[t];
        }
        private int[] handMainN;

        public bool IsLive() { return lMatch > 0; }
        public bool IsHostMe() { return sHost != 0 && sHost == MyId(); }
        public bool NoHost() { return sHost == 0; }
        public int SeatPlayer(int s) { return sPlayer[s]; }
        public string SeatDisplayName(int s) { string n = NameOf(s); return n.Length > 0 ? n : (sPlayer[s] == 0 ? "空席" : "?"); }
        /// <summary>鳴ける牌(捨てられた牌、搶槓なら加槓の牌)。k 枚目。無ければ -1</summary>
        public int PadCallTile(int k)
        {
            if (humanSeat < 0 || lMatch == 0 || engine.pendKind != SqmEngine.PEND_CALLS || optN == 0 || !engine.dcActive) return -1;
            return k < engine.dcN ? engine.dcT[k] : -1;
        }

        public int PadMainN() { return humanSeat >= 0 && handMainN != null ? handMainN[humanSeat] : 0; }
        public int PadSlotTile(int i) { return humanSeat >= 0 ? handSlotTile[humanSeat * SLOT_HAND + i] : -1; }
        public bool PadSelected(int i) { int slot = humanSeat * SLOT_HAND + i; return slot == sel0 || slot == sel1; }
        public int OptType(int k) { return optBuf[k * SqmEngine.OPT + SqmEngine.O_TYPE]; }
        public int OptMode(int k) { return optBuf[k * SqmEngine.OPT + SqmEngine.O_MODE]; }
        public void PadOption(int k) { ClickOption(k); }
        public void KickSeat(int s) { RequestKick(s); }

        /// <summary>パッドの INFORMATION の1行</summary>
        public string PadInfo()
        {
            if (message.Length > 0) return "<color=#c03030>" + message + "</color>";
            if (humanSeat < 0) return "";
            if (lMatch == 0) return IsHostMe() ? "あなたがホストです。「NEW GAME」で始めます。" : "ホストが試合を始めるのを待っています。";
            if (replayTo > 0) return "追いついています…";
            if (padConfirm) return "「確認」を押してください。";
            if (sPageN > 0) return "ほかの人の確認を待っています。";
            if (engine.phase == SqmEngine.PH_MATCH_OVER) return "このセットは終局しました。";
            if (padMyTurn) return "あなたの番です。";
            if (engine.pendKind == SqmEngine.PEND_CALLS) for (int i = 0; i < engine.pendN; i++) if (engine.pendSeats[i] == humanSeat) return "鳴きますか？";
            if (engine.pendKind == SqmEngine.PEND_TURN) return NameOf(engine.turn) + " の番です。";
            return "";
        }

        /// <summary>パッドの対局タブの案内</summary>
        public string PadGuide()
        {
            if (humanSeat < 0 || lMatch == 0) return "";
            if (padConfirm) return "和了・流局です。内容を確かめて「確認」を押してください。";
            if (padMyTurn)
            {
                if (engine.riichiOn[humanSeat]) return "リーチ中です。ツモ切りは自動です(和了・槓のときだけ選べます)。";
                if (engine.mustDiscard >= 2) return "あなたの番です。切る牌を2枚選び、確定ボタンを押してください。";
                return "あなたの番です。切る牌を選択し、確定ボタンを押してください。";
            }
            if (optN > 0) return "鳴きますか？";
            if (engine.outRole[humanSeat] != SqmEngine.ROLE_NONE) return "和了しました。ほかの人の和了を待っています。";
            return "";
        }

        /// <summary>点数タブの真ん中</summary>
        public string PadCenterText()
        {
            int sticks = engine.rRiichiStick > 0 ? engine.kyotaku / engine.rRiichiStick : 0;
            return "<size=130%>東" + engine.kyoku + "局</size>\n残 " + engine.LiveLeft().ToString("0000") + "\n千点棒 ×" + sticks + "\n百点棒 ×" + engine.honba;
        }

        /// <summary>点数タブの席の箱: 席風・名前 / 順位・点数・自分との差 / リーチ・先発・後発</summary>
        public string PadSeatBox(int s)
        {
            int rank = 1;
            for (int q = 0; q < NP; q++)
            {
                if (q == s) continue;
                int kq = (q - engine.dealer + NP) % NP, ks = (s - engine.dealer + NP) % NP;
                if (engine.score[q] > engine.score[s] || (engine.score[q] == engine.score[s] && kq < ks)) rank++;
            }
            string[] ord = new string[] { "1st.", "2nd.", "3rd.", "4th.", "5th.", "6th.", "7th.", "8th." };
            string t = "<size=125%>" + WindName(engine.SeatWindOf(s, engine.dealer)) + "家</size> " + NameOf(s) + "\n";
            t += ord[rank - 1] + " " + engine.score[s].ToString("N0");
            if (humanSeat >= 0 && s != humanSeat)
            {
                int d = engine.score[s] - engine.score[humanSeat];
                if (d > 0) t += " <color=#d04040>+" + d.ToString("N0") + "</color>";
                else if (d < 0) t += " <color=#3070c0>" + d.ToString("N0") + "</color>";
                else t += " ±0";
            }
            t += "\n";
            if (engine.riichiOn[s]) t += "<color=#d06010>リーチ</color> ";
            if (engine.outRole[s] == SqmEngine.ROLE_FIRST) t += "<color=#b08000>先発</color>";
            if (engine.outRole[s] == SqmEngine.ROLE_SECOND) t += "<color=#b08000>後発</color>";
            if (Running() && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == s) t += " <color=#2a8aa8>▶手番</color>";
            return t;
        }

        /// <summary>終局の結果の表(順位・名前・持ち点・ウマ・最終)</summary>
        public string PadResultTable()
        {
            string t = "<size=120%>終局の結果</size>\n<size=80%>順位<pos=12%>名前<pos=52%>持ち点<pos=72%>ウマ<pos=86%>最終</size>\n";
            for (int i = 0; i < NP; i++)
            {
                int s = engine.mrSeat[i];
                string line = (i + 1) + "位<pos=12%>" + NameOf(s) + "<pos=52%>" + ScoreText(engine.mrScore[i]) + "<pos=72%>" + Signed(engine.mrUma[i]) + "<pos=86%>" + Final10(engine.mrFinal10[i]);
                if (s == humanSeat) line = "<color=#2a8aa8>" + line + "</color>";
                t += line + "\n";
            }
            return t;
        }

        private string Signed(int v) { return v > 0 ? "+" + v : v.ToString(); }

        private string Final10(int v)
        {
            int a = v < 0 ? -v : v;
            string s = (a / 10) + "." + (a % 10);
            return v > 0 ? "+" + s : v < 0 ? "<color=#d04040>-" + s + "</color>" : s;
        }

        /// <summary>パッドの本体の表示(待機中の JOIN / NEW GAME、ほかの席の名前、掴めるか)と中身</summary>
        public void RefreshPads()
        {
            if (padBodies != null)
            {
                bool npcOnly = Running() && HumanMask() == 0;
                for (int s = 0; s < padBodies.Length; s++)
                {
                    SqmPadBody b = padBodies[s];
                    if (b == null) continue;
                    bool mine = s == humanSeat;
                    bool idle = humanSeat < 0 && sPlayer[s] == 0 && !npcOnly;
                    bool join = idle;
                    bool newGame = idle && sHost == 0 && !Running();
                    // 見せるのは、自分の席の板と、座れる空席の板だけ(NPC だけの試合中は全部隠す)
                    b.SetVisible(mine || idle);
                    b.ShowIdle(idle, join, newGame, "");
                    b.SetGrabbable(mine && pad != null && pad.grabbable);
                }
            }
            if (pad != null) pad.Refresh();
        }

        [NetworkCallable]
        public void NetReturnPads()
        {
            if (padBodies == null) return;
            for (int s = 0; s < padBodies.Length; s++) if (padBodies[s] != null) padBodies[s].ReturnHomeIfOwner();
        }

        // ================================================================ ルール(開始前にホストが決める。パッドのルールタブ)
        // 値は「選択肢の番号」で持つ。sRule = いま決めている値、sRuleUsed = いまの試合を始めたときの値(途中参加の再生にも使う)
        public const int N_RULE = 12;
        [UdonSynced] private int[] sRule = new int[N_RULE];
        [UdonSynced] private int[] sRuleUsed = new int[N_RULE];
        private string[] ruleNames;
        private string[][] ruleOpts;
        private int[] ruleDefault;

        private void InitRules()
        {
            if (ruleNames != null) return;
            ruleNames = new string[] { "持ち点", "ドラ表示牌", "ロン牌の枚数", "フリテン", "静かなること林の如く", "完順子ボーナス",
                "和了による連荘", "片ロンの減翻", "裏ドラ", "アガリ止め", "トビ終了(0点未満で終局)", "順位点(ウマ)" };
            ruleOpts = new string[N_RULE][];
            ruleOpts[0] = new string[] { "100,000", "150,000", "200,000", "250,000" };
            ruleOpts[1] = new string[] { "最初1枚・槓ごとに1枚", "最初2枚・槓ごとに2枚" };
            ruleOpts[2] = new string[] { "0枚", "2枚", "4枚", "6枚", "8枚", "10枚", "12枚" };
            ruleOpts[3] = new string[] { "なし(見逃しのみ)", "ゆるい", "厳しい" };
            ruleOpts[4] = new string[] { "なし", "門前・リーチなし", "門前" };
            ruleOpts[5] = new string[] { "なし(完全完順のみ)", "2つで1翻(最大2翻)", "1つにつき1翻" };
            ruleOpts[6] = new string[] { "親が先発のときだけ", "親が和了すれば" };
            ruleOpts[7] = new string[] { "なし", "1翻" };
            ruleOpts[8] = new string[] { "あり", "なし" };
            ruleOpts[9] = new string[] { "あり", "なし" };
            ruleOpts[10] = new string[] { "あり", "なし" };
            ruleOpts[11] = new string[] { "あり", "なし" };
            ruleDefault = new int[] { 1, 0, 2, 0, 1, 0, 0, 0, 0, 0, 0, 0 };
            if (sRule == null || sRule.Length != N_RULE) sRule = new int[N_RULE];
            if (sRuleUsed == null || sRuleUsed.Length != N_RULE) sRuleUsed = new int[N_RULE];
        }

        public int RuleCount() { return N_RULE; }
        public string RuleName(int i) { InitRules(); return ruleNames[i]; }
        public int RuleValue(int i) { InitRules(); return RuleIdx(sRule, i); }
        public string RuleOptLabel(int i, int v) { InitRules(); return ruleOpts[i][v]; }
        public bool CanEditRules() { return IsHostMe() && !Running(); }

        /// <summary>保存されている値(0 = 既定、それ以外は 選択肢の番号 + 1)を選択肢の番号に</summary>
        private int RuleIdx(int[] arr, int i)
        {
            int v = arr[i] - 1;
            if (v < 0 || v >= ruleOpts[i].Length) v = ruleDefault[i];
            return v;
        }

        public void RequestRule(int i, int dir)
        {
            InitRules();
            if (i < 0 || i >= N_RULE || !CanEditRules()) return;
            int n = ruleOpts[i].Length;
            int v = (RuleIdx(sRule, i) + dir + n) % n;
            if (IsHost()) HostSetRule(i, v);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetRule), i, v);
        }

        [NetworkCallable]
        public void NetRule(int i, int v)
        {
            if (!IsHost()) return;
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p) || p.playerId != sHost || Running()) return;
            HostSetRule(i, v);
        }

        private void HostSetRule(int i, int v)
        {
            InitRules();
            if (i < 0 || i >= N_RULE || v < 0 || v >= ruleOpts[i].Length) return;
            sRule[i] = v + 1;
            RequestSerialization();
            dirty = true;
        }

        /// <summary>エンジンにルールを入れる(既定のルールに、決めた値を重ねる)</summary>
        private void ApplyRules(int[] arr)
        {
            InitRules();
            engine.SetDefaultRules();
            int[] score = new int[] { 100000, 150000, 200000, 250000 };
            engine.rStartScore = score[RuleIdx(arr, 0)];
            engine.rDoraPerFlip = RuleIdx(arr, 1) + 1;
            engine.rRonTileCount = RuleIdx(arr, 2) * 2;
            engine.rFuriten = RuleIdx(arr, 3);
            engine.rHayashi = RuleIdx(arr, 4);
            engine.rKanjunMode = RuleIdx(arr, 5);
            engine.rRenchanFirst = RuleIdx(arr, 6) == 0;
            engine.rKataRonPenalty = RuleIdx(arr, 7);
            engine.rUseUra = RuleIdx(arr, 8) == 0;
            engine.rAgariYame = RuleIdx(arr, 9) == 0;
            engine.rTobiEnd = RuleIdx(arr, 10) == 0;
            engine.rUseUma = RuleIdx(arr, 11) == 0;
        }

        // ================================================================ デバッグ
        [System.NonSerialized] public int hintTileA = -1, hintTileB = -1;
        private int hintStep = -1;
        private string hintText = "";

        /// <summary>同期された仕込みを、エンジンに写す(配牌の前に効く)</summary>
        private void CopyRig()
        {
            engine.InitRig();
            int n = sRigN < 0 ? 0 : sRigN > SqmEngine.RIG_MAX ? SqmEngine.RIG_MAX : sRigN;
            for (int i = 0; i < n; i++) { engine.rigKey[i] = sRigK[i]; engine.rigSeat[i] = sRigS[i]; engine.rigTpl[i] = sRigT[i]; }
            engine.rigCount = n;
        }

        /// <summary>仕込めるのはホストだけ(だれも座っていなければ、だれでも)</summary>
        /// <summary>デバッグモードを入れる/切る(スイッチの Udon イベントからも呼べる: DebugOn / DebugOff / DebugToggle)</summary>
        public void SetDebug(bool on)
        {
#if SQM_DEBUG
            if (debugEnabled == on) return;
            debugEnabled = on;
            RefreshPads();
#endif
        }

        public void DebugOn() { SetDebug(true); }
        public void DebugOff() { SetDebug(false); }
        public void DebugToggle() { SetDebug(!debugEnabled); }

        public bool CanRig() { return debugEnabled && humanSeat >= 0 && (sHost == 0 || sHost == MyId()); }

        /// <summary>デバッグ: 次の局で、自分の席に役のひな形を仕込む(試合の前なら、次の試合の最初の局)</summary>
        public void RequestRig(int tpl)
        {
            if (!CanRig() || tpl < 0 || tpl >= SqmEngine.RIG_N) return;
            if (IsHost()) HostRig(humanSeat, tpl);
            else SendCustomNetworkEvent(NetworkEventTarget.Owner, nameof(NetRig), humanSeat, tpl);
            message = "次の局に「" + engine.RigName(tpl) + "」を仕込みました";
            dirty = true;
        }

        [NetworkCallable]
        public void NetRig(int seat, int tpl)
        {
            if (!IsHost() || !debugEnabled) return;
            VRCPlayerApi p = NetworkCalling.CallingPlayer;
            if (!Utilities.IsValid(p) || seat < 0 || seat >= NP || sPlayer[seat] != p.playerId) return;
            if (sHost != 0 && p.playerId != sHost) return;
            HostRig(seat, tpl);
        }

        private void HostRig(int seat, int tpl)
        {
            if (tpl < 0 || tpl >= SqmEngine.RIG_N) return;
            if (!Running()) { sRigPendT = tpl; sRigPendS = seat; }
            else
            {
                if (sRigN >= SqmEngine.RIG_MAX) return;
                // 次の配牌のときのエンジンの局番号(配り終えると 1 増える)
                sRigK[sRigN] = engine.kyokuSeq; sRigS[sRigN] = seat; sRigT[sRigN] = tpl; sRigN++;
                CopyRig();
            }
            RequestSerialization();
            dirty = true;
        }

        /// <summary>デバッグ: いま切るべき牌(強い NPC が自分の席ならどうするか)。自分の番だけ、手番ごとに1回考える</summary>
        public string PadHint()
        {
            if (!MyTurn()) { hintTileA = -1; hintTileB = -1; return ""; }
            if (hintStep == engine.step) return hintText;
            hintStep = engine.step;
            hintTileA = -1; hintTileB = -1;
            npc.SetSeat(humanSeat, SqmNpc.LV_STRONG, sStyle[humanSeat]);
            npc.Decide(humanSeat, engine.HashSeed("hint:" + engine.step));
            npc.SetSeat(humanSeat, SqmNpc.LV_NORMAL, sStyle[humanSeat]);   // 人が抜けたときに NPC が打つ設定へ戻す
            int ty = npc.action[SqmEngine.O_TYPE];
            if (ty == SqmEngine.A_DISCARD)
            {
                int cnt = npc.action[SqmEngine.O_TAKEN];
                hintTileA = cnt > 0 ? npc.action[SqmEngine.O_TAKE] : -1;
                hintTileB = cnt > 1 ? npc.action[SqmEngine.O_TAKE + 1] : -1;
                hintText = "切るべき牌: " + (hintTileA >= 0 ? TileCode(hintTileA) : "") + (hintTileB >= 0 ? " " + TileCode(hintTileB) : "");
                if (npc.action[SqmEngine.O_MODE] == 1) hintText += "(リーチ)";
            }
            else if (ty == SqmEngine.A_TSUMO) hintText = "ツモ和了できます";
            else if (ty == SqmEngine.A_KAN) hintText = "カン(" + TileCode(npc.action[SqmEngine.O_TILE]) + ")";
            else if (ty == SqmEngine.A_ABORT) hintText = "十四種十四牌";
            else hintText = "";
            return hintText;
        }

        // ================================================================ 上空のホロ
        // 左: 局・残り枚数(円のゲージ)・点棒 / 中: ドラ表示牌・ロン牌・時間 / 右: 8席の見取り図(見ている人の席が下の真ん中)
        // 見取り図のマス p: 0 = 下中(自分), 1 = 右下, 2 = 右中, 3 = 右上, 4 = 上中(対面), 5 = 左上, 6 = 左中, 7 = 左下。マス p には席 (基準 + p) % 8

        private int liveMax, liveMaxSeq = -1;                // 残り枚数のゲージの満タン(局ごと)

        private void RefreshHolo()
        {
            if (holo == null) return;
            bool live = lMatch > 0;
            if (holoTitle != null) holoTitle.text = "";
            if (!live)
            {
                if (holoPlay != null && holoPlay.activeSelf) holoPlay.SetActive(false);
                RefreshResult(-1);
                holo.text = "";
                if (holoTitle != null) holoTitle.text = "<size=180%>平方麻雀²</size>\n手元のパッドの「JOIN」で席に着きます。最初に座った人がホストです\nホストがパッドの「NEW GAME」で始めます\n<size=80%>だれも座らずに「NEW GAME」を押すと、NPC だけで打ちます</size>";
                return;
            }
            string top = "";
            if (replayTo > 0) top += "追いついています… " + lCount + " / " + replayTo + "  ";
            if (message.Length > 0) top += "<color=#ff8080>" + message + "</color>";
            holo.text = top;

            // ---- 左: 局・残り・点棒 ----
            if (holoRound != null) holoRound.text = "東" + engine.kyoku + "局";
            int left = engine.LiveLeft();
            if (holoLeft != null) holoLeft.text = left.ToString("0000");
            if (engine.kyokuSeq != liveMaxSeq) { liveMaxSeq = engine.kyokuSeq; liveMax = 0; }
            if (left > liveMax) liveMax = left;
            if (holoGauge != null)
            {
                int on = liveMax > 0 ? Mathf.CeilToInt(holoGauge.Length * (float)left / liveMax) : 0;
                for (int i = 0; i < holoGauge.Length; i++)
                {
                    bool show = i < on;
                    if (holoGauge[i] != null && holoGauge[i].activeSelf != show) holoGauge[i].SetActive(show);
                }
            }
            int sticks = engine.rRiichiStick > 0 ? engine.kyotaku / engine.rRiichiStick : 0;
            if (holoSticks != null) holoSticks.text = "×" + sticks + "\n×" + engine.honba;

            // ---- 中: ドラ表示牌・ロン牌 ----
            bool dealt = engine.phase != SqmEngine.PH_DEAL;
            if (holoDora != null)
                for (int i = 0; i < holoDora.Length; i++) HoloTile(holoDora[i], dealt && i < engine.doraIndN ? engine.doraInd[i] : -1);
            if (holoRon != null)
                for (int i = 0; i < holoRon.Length; i++) HoloTile(holoRon[i], dealt && i < engine.ronTilesLen ? engine.ronTiles[i] : -1);
            RefreshHoloTime();

            // ---- 右: 見取り図 ----
            int view = humanSeat >= 0 ? humanSeat : 0;
            bool anyFirst = false;
            for (int s = 0; s < NP; s++) if (engine.outRole[s] == SqmEngine.ROLE_FIRST) anyFirst = true;
            if (holoCenter != null) holoCenter.text = anyFirst ? "後発" : "先発";
            for (int p = 0; p < NP; p++)
            {
                int s = (view + p) % NP;
                bool turn = Running() && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == s;
                bool riichi = engine.riichiOn[s];
                bool me = s == humanSeat;
                string w = WindName(engine.SeatWindOf(s, engine.dealer));
                if (engine.outRole[s] != SqmEngine.ROLE_NONE) w = "<alpha=#60>" + w;
                if (turn) w = "<color=#06202c>" + w + "</color>";
                else if (riichi) w = "<color=#ff6a5a>" + w + "</color>";
                else if (me) w = "<color=#ffd070>" + w + "</color>";
                if (holoCellWind != null && holoCellWind[p] != null) holoCellWind[p].text = w;
                if (holoCellFrame != null && holoCellFrame[p] != null)
                {
                    Material fm = riichi ? holoMatRed : me ? holoMatYellow : holoMatLine;
                    if (holoCellFrame[p].sharedMaterial != fm) holoCellFrame[p].sharedMaterial = fm;
                }
                if (holoCellFill != null && holoCellFill[p] != null)
                {
                    Material fl = turn ? holoMatTurn : holoMatFill;
                    if (holoCellFill[p].sharedMaterial != fl) holoCellFill[p].sharedMaterial = fl;
                }
                if (holoCellName != null && holoCellName[p] != null)
                {
                    string nm = me ? "あなた" : NameOf(s);
                    holoCellName[p].text = me ? "<color=#ffd070>" + nm + "</color>" : nm;
                }
                if (holoCellScore != null && holoCellScore[p] != null) holoCellScore[p].text = SegScore(engine.score[s]);
            }

            // ---- 結果の画面(出ているあいだは、ふだんの表示を隠す) ----
            int page = CurrentPage();
            if (holoPlay != null && holoPlay.activeSelf != (page < 0)) holoPlay.SetActive(page < 0);
            RefreshResult(page);
            ResultVoice(page);
        }

        private void HoloTile(MeshRenderer r, int tile)
        {
            if (r == null) return;
            bool show = tile >= 0;
            if (r.gameObject.activeSelf != show) r.gameObject.SetActive(show);
            if (show && holoFaceMats != null && tile < holoFaceMats.Length) r.sharedMaterial = holoFaceMats[tile];
        }

        /// <summary>セット時間(試合の開始から)と現局の時間。ホストが同期した開始時刻から求めるので、全員で同じになる</summary>
        private void RefreshHoloTime()
        {
            if (lMatch == 0 || sStartMs == 0) return;
            int now = Networking.GetServerTimeInMilliseconds();
            // どちらもセットの開始からの秒で数える(局の時間も同じ瞬間に1秒進むように)
            int setEnd = Sec((sEndMs != 0 ? sEndMs : now) - sStartMs);
            int kyokuEnd = Sec((sKyokuEndMs != 0 ? sKyokuEndMs : now) - sStartMs);
            int kyokuStart = Sec(sKyokuMs - sStartMs);
            if (holoSetTime != null) holoSetTime.text = Clock(setEnd);
            if (holoKyokuTime != null) holoKyokuTime.text = Clock(kyokuEnd - kyokuStart);
        }

        private int Sec(int ms) { return ms <= 0 ? 0 : ms / 1000; }

        private string Clock(int sec)
        {
            if (sec < 0) sec = 0;
            int h = sec / 3600, m = sec / 60 % 60, ss = sec % 60;
            return h.ToString("00") + ":" + m.ToString("00") + ":" + ss.ToString("00");
        }

        /// <summary>上空の表示を、自分の頭の方へ向ける</summary>
        private void BillboardHolo()
        {
            if (holoRoot == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 d = holoRoot.position - head;
            d.y = 0;
            if (d.sqrMagnitude > 0.0001f) holoRoot.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }
}
