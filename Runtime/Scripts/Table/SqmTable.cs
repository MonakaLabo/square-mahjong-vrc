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
    /// 同期: 卓の持ち主(ホスト)だけが試合を進める。NPC の手・自動の進行・人の操作を、1つずつ番号(code)にして記録に足し、
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
        public TextMeshPro holo;             // 卓の上空の表示
        public Transform holoRoot;           // 上空の表示の向きを変える用(Udon では TextMeshPro.transform が使えない)
        public TextMeshPro[] seatLabels;     // 席の名前 8
        public TextMeshPro[] windLabels;     // 席風 8
        public TextMeshPro[] scoreLabels;    // 点数(7セグ) 8
        public GameObject[] joinButtons;     // 席の「ここで打つ」 8
        public GameObject leaveButton;       // 「席を立つ」
        public GameObject newMatchButton;    // 「新しい試合」
        public Transform controls;           // 自分の前に出る操作ボタンのまとまり
        public GameObject discardButton;
        public GameObject riichiButton;
        public TextMeshPro riichiLabel;
        public GameObject[] optionButtons;   // 宣言などの選択肢 12
        public TextMeshPro[] optionLabels;

        // ---------------- 大きさ(m) ----------------
        public const float TOP = 0.76f;      // 卓の天板の高さ
        public const float TW = 0.026f, TH = 0.035f, TT = 0.02f; // 牌の幅・高さ・厚み
        public const float R_HAND = 1.06f, R_MELD = 0.965f, R_LABEL = 1.13f;
        public const int SLOT_HAND = 28, SLOT_RIVER = 36, SLOT_MELD = 28, N_DORA = 18, N_RON = 12, N_OPT = 12;
        public const int WALL_PER_SIDE = 75, WALLLESS_SIDE = 7;
        public const int NP = 8, NT = 47;
        public const int RING = 64;          // 毎回送る直近の記録の数
        public const int LOG_MAX = 32768;    // 1試合の記録の上限(1試合はふつう 1万手まで)
        public const int REPLAY_PER_FRAME = 120;

        // ---------------- 同期する状態(ホストが書く) ----------------
        [UdonSynced] private int sMatch;                     // 試合の番号(0 = まだ無い)
        [UdonSynced] private int sSeed;
        [UdonSynced] private int[] sPlayer = new int[NP];    // 席の人の playerId(0 = NPC)
        [UdonSynced] private int[] sStyle = new int[NP];     // 席の NPC の性格(名前と組)
        [UdonSynced] private int sCount;                     // 記録の数
        [UdonSynced] private int[] sRing = new int[RING];    // 記録 i は sRing[i % RING]

        // ---------------- 手元の状態 ----------------
        [System.NonSerialized] public int humanSeat = -1;   // 自分の席(-1 = 座っていない)
        private int lMatch;                                  // 手元のエンジンが表している試合
        private int lCount;                                  // 手元で再生した記録の数
        private int[] lLog;                                  // 手元の記録(ホストになったときに配れるよう、全員が持つ)
        private int replayTo;                                // 記録すべてから再生しているときの行き先(0 = していない)
        private float archiveAskWait;                        // 記録すべてを頼み直すまでの待ち
        private bool archiveDirty;                           // ホスト: 記録すべてを送る
        private float archiveSendWait;
        private int sentAt = -1;                             // 自分の操作を送ったときの sCount(返事が来るまで二度押ししない)
        private float sentWait;
        private float wait;                                  // ホスト: 次の手までの待ち(秒)
        private bool resultShown;                            // ホスト: 局の結果を見せる間を取ったか
        private bool dirty;
        private int[] handSlotTile;                          // 手牌の枠に入っている牌(-1 = 空)
        private int sel0 = -1, sel1 = -1;                    // 選んだ手牌の枠
        private bool riichiArmed;
        private int[] optBuf; private int optN;              // いま出している選択肢(engine.legal の写し)
        private int[] act;
        private string message = "";
        private bool ready;
        private int[] tmpC, tmpDr;

        private string[] cpuNames, yakuJa, tileNames;

        public const float NPC_DELAY = 0.45f, RESULT_DELAY = 6f;

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
            tmpC = new int[NT]; tmpDr = new int[8];
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

        private bool IsHost() { return Networking.IsOwner(gameObject); }
        private bool Running() { return lMatch > 0 && engine.pendKind != SqmEngine.PEND_END; }
        private bool CaughtUp() { return lMatch == sMatch && lCount == sCount && replayTo == 0; }
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
            return ok;
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
            ClearSelection();
            dirty = true;
            RequestSerialization();
            return true;
        }

        private void HostNewMatch()
        {
            Init();
            sMatch = (sMatch > lMatch ? sMatch : lMatch) + 1;
            sSeed = UnityEngine.Random.Range(1, int.MaxValue);
            // 10人から8人を選び、席に座らせる(名前と性格はいつも組)
            int[] order = new int[10];
            for (int i = 0; i < 10; i++) order[i] = i;
            for (int i = 9; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); int x = order[i]; order[i] = order[j]; order[j] = x; }
            for (int s = 0; s < NP; s++) sStyle[s] = order[s];
            sCount = 0;
            ResetLocal();
            wait = 0.5f;
            resultShown = false;
            message = "";
            RequestSerialization();
            dirty = true;
        }

        private void Update()
        {
            BillboardHolo();
            if (replayTo > 0) ReplayChunk();
            if (sentAt >= 0) { sentWait -= Time.deltaTime; if (sentWait <= 0 || sentAt != sCount) sentAt = -1; }
            if (IsHost())
            {
                if (archiveDirty) { archiveSendWait -= Time.deltaTime; if (archiveSendWait <= 0) SendArchive(); }
                if (CaughtUp() && Running())
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
            if (dirty) { dirty = false; Refresh(); }
        }

        /// <summary>ホストが1回分だけ進める(自動進行・NPC の手・人の自動ツモ切り・局の終わり)</summary>
        private void HostStep()
        {
            AdvanceAll();
            int pk = engine.pendKind;
            if (pk == SqmEngine.PEND_NONE || pk == SqmEngine.PEND_END) { dirty = true; return; }
            if (pk == SqmEngine.PEND_NEXT)
            {
                int d = engine.dealer;
                // 人の親がアガリ止めを選べるときは、その人のボタンを待つ
                if (IsHuman(d) && engine.CanAgariYame()) return;
                if (!resultShown) { resultShown = true; wait = RESULT_DELAY; dirty = true; return; }
                resultShown = false;
                if (Commit(EncodeNext(d, -1))) wait = 0.3f;
                return;
            }
            resultShown = false;
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
            engine.NewMatch((uint)sSeed);
            for (int s = 0; s < NP; s++) npc.SetSeat(s, SqmNpc.LV_NORMAL, sStyle[s]);
            lMatch = sMatch;
            lCount = 0;
            replayTo = 0;
            AdvanceAll();
            ClearSelection();
        }

        public override void OnDeserialization()
        {
            Init();
            UpdateMySeat();
            if (sMatch != lMatch && sMatch > 0) ResetLocal();
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
                if (!ApplyCode(code)) { message = "同期をやり直しています"; ResetLocal(); AskArchive(); return; }
                lLog[i] = code;
                lCount = i + 1;
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
            int[] log = new int[lCount];
            for (int i = 0; i < lCount; i++) log[i] = lLog[i];
            archive.aMatch = lMatch;
            archive.aCount = lCount;
            archive.aLog = log;
            archive.RequestSerialization();
        }

        /// <summary>記録すべてが届いた。最初から再生し直す(何フレームかに分けて)</summary>
        public void OnArchive()
        {
            Init();
            if (IsHost() || archive.aMatch != sMatch || sMatch == 0) return;
            if (lMatch == sMatch && replayTo == 0 && lCount >= archive.aCount) return;
            int n = archive.aCount;
            if (n > LOG_MAX || archive.aLog == null || archive.aLog.Length < n) return;
            ResetLocal();
            for (int i = 0; i < n; i++) lLog[i] = archive.aLog[i];
            replayTo = n;
            archiveAskWait = 0;
            if (n == 0) { replayTo = 0; CatchUp(); dirty = true; }
        }

        private void ReplayChunk()
        {
            int end = lCount + REPLAY_PER_FRAME;
            if (end > replayTo) end = replayTo;
            for (int i = lCount; i < end; i++)
            {
                if (!ApplyCode(lLog[i])) { message = "同期に失敗しました"; replayTo = 0; ResetLocal(); archiveAskWait = 5f; return; }
                lCount = i + 1;
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
            if (lMatch == sMatch && replayTo == 0)
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
            wait = 1f;
            RequestSerialization();
            dirty = true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            if (!IsHost() || !Utilities.IsValid(player)) return;
            bool changed = false;
            for (int s = 0; s < NP; s++) if (sPlayer[s] == player.playerId) { sPlayer[s] = 0; changed = true; }
            if (changed) { RequestSerialization(); dirty = true; }
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

        /// <summary>試合中にやり直せるのは、座っている人だけ(だれも座っていなければ、だれでも)</summary>
        private bool MayStart(int pid)
        {
            if (!Running()) return true;
            bool anyone = false;
            for (int s = 0; s < NP; s++) { if (sPlayer[s] == pid) return true; if (sPlayer[s] != 0) anyone = true; }
            return !anyone;
        }

        // ================================================================ 自分の操作
        private bool MyTurn()
        {
            return Running() && CaughtUp() && humanSeat >= 0 && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == humanSeat;
        }

        public void ClickTile(int slot)
        {
            if (humanSeat < 0 || slot / SLOT_HAND != humanSeat) return;
            if (!MyTurn()) return;
            if (handSlotTile[slot] < 0) return;
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
            riichiArmed = !riichiArmed;
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
            // 引く順: 親の前の壁から、壁のある7辺を巡る。辺の中は内側の列から
            int startSide = engine.dealer == WALLLESS_SIDE ? 0 : engine.dealer;
            int k = 0;
            for (int sidx = 0; sidx < 8; sidx++)
            {
                int side = (startSide + sidx) % 8;
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
            bool reveal = s == humanSeat || (humanSeat < 0 && !AnyHuman()) || engine.outRole[s] != SqmEngine.ROLE_NONE || engine.phase == SqmEngine.PH_KYOKU_OVER;
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
            for (int i = 0; i < drN && n < SLOT_HAND; i++) handSlotTile[baseSlot + n++] = dr[i];
            for (int i = n; i < SLOT_HAND; i++) handSlotTile[baseSlot + i] = -1;
            Vector3 u = U(s), r = R(s);
            float width = n * (TW + 0.001f) + (drN > 0 ? 0.012f : 0);
            for (int i = 0; i < SLOT_HAND; i++)
            {
                int slot = baseSlot + i;
                int t = handSlotTile[slot];
                if (t < 0 || engine.phase == SqmEngine.PH_DEAL) { Place(handTiles[slot], handFaces[slot], -1, Vector3.zero, s, false, false, false, false); continue; }
                float x = -width / 2 + i * (TW + 0.001f) + TW / 2 + (i >= mainN ? 0.012f : 0);
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

        private void RefreshCenter()
        {
            int view = humanSeat >= 0 ? humanSeat : 0;
            Vector3 u = U(view), r = R(view);
            for (int i = 0; i < N_DORA; i++)
            {
                bool show = i < engine.doraIndN && engine.phase != SqmEngine.PH_DEAL;
                Vector3 pos = u * 0.06f + r * ((i - (engine.doraIndN - 1) / 2f) * (TW + 0.002f)) + Vector3.up * (TOP + TT / 2);
                Place(centerTiles[i], centerFaces[i], show ? engine.doraInd[i] : -1, pos, view, true, show, true, false);
            }
            for (int i = 0; i < N_RON; i++)
            {
                bool show = i < engine.ronTilesLen && engine.phase != SqmEngine.PH_DEAL;
                int t = show ? engine.ronTiles[i] : -1;
                Vector3 pos = -u * 0.06f + r * ((i - (engine.ronTilesLen - 1) / 2f) * (TW + 0.002f)) + Vector3.up * (TOP + TT / 2);
                Place(centerTiles[N_DORA + i], centerFaces[N_DORA + i], t, pos, view, true, show, t >= 0, false);
            }
        }

        private void RefreshLabel(int s)
        {
            bool live = lMatch > 0;
            if (seatLabels != null && s < seatLabels.Length && seatLabels[s] != null)
            {
                string txt = NameOf(s);
                if (!live && sPlayer[s] == 0) txt = "空席";
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
            if (joinButtons != null && s < joinButtons.Length && joinButtons[s] != null)
            {
                bool showJoin = humanSeat < 0 && sPlayer[s] == 0;
                if (joinButtons[s].activeSelf != showJoin) joinButtons[s].SetActive(showJoin);
            }
        }

        private string WindName(int t) { return tileNames[t - 36]; }

        private void RefreshControls()
        {
            optN = 0;
            bool ok = Running() && CaughtUp() && humanSeat >= 0 && sentAt < 0;
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
            for (int i = 0; i < N_OPT; i++)
            {
                bool show = i < optN;
                if (optionButtons[i].activeSelf != show) optionButtons[i].SetActive(show);
                if (show) optionLabels[i].text = OptionText(i * SqmEngine.OPT);
            }
            if (leaveButton != null && leaveButton.activeSelf != (humanSeat >= 0)) leaveButton.SetActive(humanSeat >= 0);
            if (newMatchButton != null)
            {
                bool showNew = !Running() || humanSeat >= 0;
                if (newMatchButton.activeSelf != showNew) newMatchButton.SetActive(showNew);
            }
        }

        private string OptionText(int b)
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

        private void RefreshHolo()
        {
            if (holo == null) return;
            if (lMatch == 0)
            {
                holo.text = "<size=140%>平方麻雀²</size>\n席の「ここで打つ」で座って、「新しい試合」で始めます\n<size=80%>だれも座らなければ、NPC だけで打ちます</size>";
                return;
            }
            string t = "";
            if (replayTo > 0) t += "<size=80%>追いついています… " + lCount + " / " + replayTo + "</size>\n";
            t += "<size=130%>東" + engine.kyoku + "局</size>  " + engine.honba + "本場  供託 " + engine.kyotaku.ToString("N0") + "\n";
            t += "残り " + engine.LiveLeft() + "\n";
            if (engine.phase == SqmEngine.PH_KYOKU_OVER || engine.phase == SqmEngine.PH_MATCH_OVER) t += ResultText();
            if (engine.phase == SqmEngine.PH_MATCH_OVER)
            {
                t += "\n<size=120%>終局</size>\n";
                for (int i = 0; i < NP; i++) t += (i + 1) + "位 " + NameOf(engine.mrSeat[i]) + "  " + engine.mrScore[i].ToString("N0") + "\n";
            }
            else
            {
                // 全席の点数(自分の席は色を変える)
                t += "<size=75%>";
                for (int s = 0; s < NP; s++)
                {
                    string line = WindName(engine.SeatWindOf(s, engine.dealer)) + " " + NameOf(s) + "  " + engine.score[s].ToString("N0");
                    if (s == humanSeat) line = "<color=#ffd070>" + line + "</color>";
                    if (Running() && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == s) line = "▶ " + line;
                    t += line + "\n";
                }
                t += "</size>";
            }
            if (message.Length > 0) t += "\n<color=#ff8080>" + message + "</color>";
            holo.text = t;
        }

        private string ResultText()
        {
            string t = "";
            if (engine.resType == SqmEngine.RES_EXHAUST && engine.winsN == 0) t += "流局\n";
            if (engine.resType == SqmEngine.RES_ABORT) t += "途中流局\n";
            for (int w = 0; w < engine.winsN; w++)
            {
                string role = engine.winRole[w] == SqmEngine.ROLE_FIRST ? "先発" : "後発";
                string kind = engine.winType[w] == SqmEngine.WT_TSUMO ? "ツモ" : engine.winType[w] == SqmEngine.WT_RON2 ? "二枚ロン" : "片ロン";
                t += NameOf(engine.winSeat[w]) + " " + role + " " + kind + "  " + (engine.winYakuman[w] ? "立方満" : engine.winHan[w] + "翻") + "  +" + engine.winTotal[w].ToString("N0") + "\n";
                string ys = "";
                for (int k = 0; k < engine.winYakuN[w]; k++)
                {
                    int id = engine.winYakuId[w * SqmEngine.MAXYAKU + k];
                    ys += (k > 0 ? "・" : "") + yakuJa[id];
                }
                t += "<size=80%>" + ys + "</size>\n";
            }
            return t;
        }

        /// <summary>上空の表示を、自分の頭の方へ向ける</summary>
        private void BillboardHolo()
        {
            if (holo == null) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (lp == null) return;
            Vector3 head = lp.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            Vector3 d = holoRoot.position - head;
            d.y = 0;
            if (d.sqrMagnitude > 0.0001f) holoRoot.rotation = Quaternion.LookRotation(d, Vector3.up);
        }
    }
}
