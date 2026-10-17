using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 卓1つ。試合を進め(SqmEngine + SqmNpc)、牌・文字・ボタンを卓の上に並べる。
    /// いまはローカル(自分 + NPC 7人)だけ。複数人の同期は次の段階で足す。
    /// 物(牌・ボタン・文字)は、エディタのメニュー「平方麻雀 → 卓をシーンに置く」(SqmBuilder)で作って、ここの配列に入れる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmTable : UdonSharpBehaviour
    {
        // ---------------- つなぐもの(SqmBuilder が入れる) ----------------
        public SqmEngine engine;
        public SqmNpc npc;
        public Material[] faceMats;          // 牌の表面 47種
        public Transform[] wallStacks;       // 壁の幢 525
        public Transform[] handTiles;        // 手牌 8席 × 28(SLOT_HAND)
        public MeshRenderer[] handFaces;
        public Transform[] riverTiles;       // 河 8席 × 36(SLOT_RIVER)
        public MeshRenderer[] riverFaces;
        public Transform[] meldTiles;        // 副露 8席 × 28(SLOT_MELD)
        public MeshRenderer[] meldFaces;
        public Transform[] centerTiles;      // 中央: ドラ表示牌 18 + ロン牌 12
        public MeshRenderer[] centerFaces;
        public TextMeshPro holo;             // 卓の上空の表示
        public Transform holoRoot;           // 上空の表示の向きを変える用(Udon では TextMeshPro.transform が使えない)
        public TextMeshPro[] seatLabels;     // 席の名前・点数 8
        public GameObject[] joinButtons;     // 席の「ここで打つ」 8
        public Transform controls;           // 自分の前に出る操作ボタンのまとまり
        public GameObject discardButton;
        public GameObject riichiButton;
        public TextMeshPro riichiLabel;
        public GameObject[] optionButtons;   // 宣言などの選択肢 12
        public TextMeshPro[] optionLabels;

        // ---------------- 大きさ(m) ----------------
        public const float TOP = 0.76f;      // 卓の天板の高さ
        public const float TW = 0.026f, TH = 0.035f, TT = 0.02f; // 牌の幅・高さ・厚み
        public const float R_HAND = 1.06f, R_MELD = 0.965f;
        public const int SLOT_HAND = 28, SLOT_RIVER = 36, SLOT_MELD = 28, N_DORA = 18, N_RON = 12, N_OPT = 12;
        public const int WALL_PER_SIDE = 75, WALLLESS_SIDE = 7;
        public const int NP = 8, NT = 47;

        // ---------------- 状態 ----------------
        [System.NonSerialized] public int humanSeat = -1;   // 自分の席(-1 = 観戦)
        private bool running;
        private float wait;                                  // 次の NPC の手までの待ち(秒)
        private bool dirty;
        private int[] handSlotTile;                          // 手牌の枠に入っている牌(-1 = 空)
        private int sel0 = -1, sel1 = -1;                    // 選んだ手牌の枠
        private bool riichiArmed;
        private int[] optBuf; private int optN;              // いま出している選択肢(engine.legal の写し)
        private int[] act;
        private string message = "";
        private bool ready;

        private string[] cpuNames, yakuJa, tileNames;

        public const float NPC_DELAY = 0.45f, RESULT_DELAY = 6f;

        private void Start()
        {
            Init();
            Refresh();
        }

        private void Init()
        {
            if (ready) return;
            ready = true;
            handSlotTile = new int[NP * SLOT_HAND];
            optBuf = new int[N_OPT * SqmEngine.OPT];
            act = new int[SqmEngine.OPT];
            seatStyle = new int[NP]; tmpC = new int[NT]; tmpDr = new int[8];
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

        // ================================================================ 試合の開始
        public void StartMatch()
        {
            Init();
            uint seed = (uint)UnityEngine.Random.Range(1, int.MaxValue);
            engine.NewMatch(seed);
            // 10人から8人を選び、席に座らせる(名前と性格はいつも組)
            int[] order = new int[10];
            for (int i = 0; i < 10; i++) order[i] = i;
            for (int i = 9; i > 0; i--) { int j = UnityEngine.Random.Range(0, i + 1); int x = order[i]; order[i] = order[j]; order[j] = x; }
            for (int s = 0; s < NP; s++) { seatStyle[s] = order[s]; npc.SetSeat(s, SqmNpc.LV_NORMAL, order[s]); }
            running = true;
            wait = 0.5f;
            ClearSelection();
            message = "";
            Refresh();
        }

        private int[] seatStyle, tmpC, tmpDr;

        private string NameOf(int s)
        {
            if (s == humanSeat) return "あなた";
            return cpuNames[seatStyle[s]];
        }

        // ---- 席を選ぶボタン(Join0..Join7) と 観戦 ----
        public void Join0() { Join(0); } public void Join1() { Join(1); } public void Join2() { Join(2); } public void Join3() { Join(3); }
        public void Join4() { Join(4); } public void Join5() { Join(5); } public void Join6() { Join(6); } public void Join7() { Join(7); }
        public void Watch() { Join(-1); }

        private void Join(int seat)
        {
            Init();
            humanSeat = seat;
            StartMatch();
        }

        public void NewMatch() { StartMatch(); }

        // ================================================================ 進行
        private void Update()
        {
            BillboardHolo();
            if (!running) return;
            if (wait > 0) { wait -= Time.deltaTime; return; }
            Step();
            if (dirty) { dirty = false; Refresh(); }
        }

        /// <summary>1回分だけ進める(自動進行・NPC の手・局の終わり)</summary>
        private void Step()
        {
            int pk = engine.pendKind;
            if (pk == SqmEngine.PEND_NONE) { engine.Advance(); dirty = true; return; }
            if (pk == SqmEngine.PEND_END) { running = false; dirty = true; return; }
            if (pk == SqmEngine.PEND_NEXT)
            {
                // 自分が親でアガリ止めを選べるときは、ボタンを待つ
                if (humanSeat == engine.dealer && engine.CanAgariYame()) return;
                ApplyNext(-1);
                return;
            }
            // 入力待ちの席のうち NPC の分を1つ進める。自分だけが残っていたら待つ
            for (int i = 0; i < engine.pendN; i++)
            {
                int seat = engine.pendSeats[i];
                if (seat == humanSeat) continue;
                uint nseed = engine.HashSeed(engine.seed.ToString() + ":" + engine.step + ":" + seat);
                npc.Decide(seat, nseed);
                bool discard = npc.action[SqmEngine.O_TYPE] == SqmEngine.A_DISCARD;
                if (!engine.ApplyAction(npc.action)) message = "NPC エラー: " + engine.lastError;
                dirty = true;
                if (discard) wait = NPC_DELAY;
                return;
            }
            // 自分の番: リーチ中のツモ切りは自動
            if (engine.pendKind == SqmEngine.PEND_TURN && humanSeat >= 0 && engine.riichiOn[humanSeat])
            {
                int n = engine.BuildLegal(humanSeat);
                bool canWin = false;
                for (int i = 0; i < n; i++) if (engine.legal[i * SqmEngine.OPT + SqmEngine.O_TYPE] == SqmEngine.A_TSUMO || engine.legal[i * SqmEngine.OPT + SqmEngine.O_TYPE] == SqmEngine.A_KAN) canWin = true;
                if (!canWin)
                {
                    for (int i = 0; i < n; i++)
                    {
                        int b = i * SqmEngine.OPT;
                        if (engine.legal[b + SqmEngine.O_TYPE] != SqmEngine.A_DISCARD) continue;
                        ClearAct(humanSeat); act[SqmEngine.O_TYPE] = SqmEngine.A_DISCARD; act[SqmEngine.O_MODE] = 0;
                        int fn = engine.legal[b + SqmEngine.O_FIXEDN];
                        act[SqmEngine.O_TAKEN] = fn;
                        for (int k = 0; k < fn; k++) act[SqmEngine.O_TAKE + k] = engine.legal[b + SqmEngine.O_FIXED + k];
                        engine.ApplyAction(act);
                        dirty = true; wait = NPC_DELAY;
                        return;
                    }
                }
            }
        }

        private void ApplyNext(int yame)
        {
            ClearAct(engine.dealer);
            act[SqmEngine.O_TYPE] = SqmEngine.A_NEXT; act[SqmEngine.O_MODE] = yame;
            engine.ApplyAction(act);
            dirty = true;
            wait = 0.3f;
        }

        private void ClearAct(int seat)
        {
            for (int k = 0; k < SqmEngine.OPT; k++) act[k] = 0;
            act[SqmEngine.O_SEAT] = seat;
            act[SqmEngine.O_MODE] = -1; act[SqmEngine.O_TILE] = -1; act[SqmEngine.O_RONIDX] = -1; act[SqmEngine.O_RONTILE] = -1;
            act[SqmEngine.O_MELDTYPE] = SqmEngine.MT_NONE; act[SqmEngine.O_START] = -1;
        }

        // ================================================================ 自分の操作
        public void ClickTile(int slot)
        {
            if (humanSeat < 0 || slot / SLOT_HAND != humanSeat) return;
            if (engine.pendKind != SqmEngine.PEND_TURN || engine.turn != humanSeat) return;
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
            if (humanSeat < 0 || engine.pendKind != SqmEngine.PEND_TURN || engine.turn != humanSeat) return;
            if (engine.riichiOn[humanSeat])
            {
                // リーチ中: ツモ和了や槓をせずにツモ切りする
                int ln = engine.BuildLegal(humanSeat);
                for (int i = 0; i < ln; i++)
                {
                    int b = i * SqmEngine.OPT;
                    if (engine.legal[b + SqmEngine.O_TYPE] != SqmEngine.A_DISCARD) continue;
                    ClearAct(humanSeat); act[SqmEngine.O_TYPE] = SqmEngine.A_DISCARD; act[SqmEngine.O_MODE] = 0;
                    int fn = engine.legal[b + SqmEngine.O_FIXEDN];
                    act[SqmEngine.O_TAKEN] = fn;
                    for (int k = 0; k < fn; k++) act[SqmEngine.O_TAKE + k] = engine.legal[b + SqmEngine.O_FIXED + k];
                }
                if (!engine.ApplyAction(act)) message = engine.lastError;
                ClearSelection(); wait = NPC_DELAY; Refresh();
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
            if (!engine.ApplyAction(act)) { message = engine.lastError; Refresh(); return; }
            message = "";
            ClearSelection();
            wait = NPC_DELAY;
            Refresh();
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
            if (i >= optN) return;
            for (int k = 0; k < SqmEngine.OPT; k++) act[k] = optBuf[i * SqmEngine.OPT + k];
            if (act[SqmEngine.O_TYPE] == SqmEngine.A_NEXT) { ApplyNext(act[SqmEngine.O_MODE]); Refresh(); return; }
            if (!engine.ApplyAction(act)) message = engine.lastError;
            else message = "";
            ClearSelection();
            wait = 0.2f;
            Refresh();
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

        private void RefreshWall()
        {
            int live = engine.LiveLeft();
            int visible = live / 2;
            if (visible > 525) visible = 525;
            if (engine.phase == SqmEngine.PH_DEAL && !running) visible = 525;
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
            if (seatLabels == null || s >= seatLabels.Length || seatLabels[s] == null) return;
            string wind = WindName(engine.SeatWindOf(s, engine.dealer));
            string txt = wind + " " + NameOf(s) + "\n" + engine.score[s].ToString("N0");
            if (engine.riichiOn[s]) txt += "  リーチ";
            if (engine.outRole[s] == SqmEngine.ROLE_FIRST) txt += "  先発";
            if (engine.outRole[s] == SqmEngine.ROLE_SECOND) txt += "  後発";
            if (running && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == s) txt = "▶ " + txt;
            seatLabels[s].text = txt;
            if (joinButtons != null && s < joinButtons.Length && joinButtons[s] != null)
            {
                bool showJoin = !running || humanSeat < 0;
                if (joinButtons[s].activeSelf != showJoin) joinButtons[s].SetActive(showJoin);
            }
        }

        private string WindName(int t) { return tileNames[t - 36]; }

        private void RefreshControls()
        {
            optN = 0;
            bool myTurn = running && humanSeat >= 0 && engine.pendKind == SqmEngine.PEND_TURN && engine.turn == humanSeat;
            bool myCall = false;
            if (running && humanSeat >= 0 && engine.pendKind == SqmEngine.PEND_CALLS)
                for (int i = 0; i < engine.pendN; i++) if (engine.pendSeats[i] == humanSeat) myCall = true;
            bool myNext = running && humanSeat >= 0 && engine.pendKind == SqmEngine.PEND_NEXT && humanSeat == engine.dealer && engine.CanAgariYame();
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
            string t = "";
            if (!running && engine.phase != SqmEngine.PH_MATCH_OVER)
            {
                holo.text = "<size=140%>平方麻雀²</size>\n席の「ここで打つ」を押すと始まります";
                return;
            }
            t += "<size=130%>東" + engine.kyoku + "局</size>  " + engine.honba + "本場  供託 " + engine.kyotaku.ToString("N0") + "\n";
            t += "残り " + engine.LiveLeft() + "\n";
            if (engine.phase == SqmEngine.PH_KYOKU_OVER || engine.phase == SqmEngine.PH_MATCH_OVER) t += ResultText();
            if (engine.phase == SqmEngine.PH_MATCH_OVER)
            {
                t += "\n<size=120%>終局</size>\n";
                for (int i = 0; i < NP; i++) t += (i + 1) + "位 " + NameOf(engine.mrSeat[i]) + "  " + engine.mrScore[i].ToString("N0") + "\n";
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
