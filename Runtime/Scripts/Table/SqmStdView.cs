using TMPro;
using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 普通の麻雀(SqmStdEngine)の見た目と操作。同期・席・ホストは SqmTable が受け持ち、ここは手元の表示だけ。
    /// エンジンの席 es(0〜3)は、卓の席 table.StdTableSeat(es)(ホストの席から1つおき)に座る。
    ///   卓: 手牌・河(6枚ずつ)・副露・北抜き・壁(各席の前に1列。サイコロの割れ目から上家の方へ取り、王牌は割れ目の手前の7幢)・ドラ表示(王牌の上)
    ///   操作: 牌を選んで「確定」、または同じ牌を続けて2回選ぶと打牌。リーチは「リーチ」を押してから切る牌を選ぶ
    ///   ホロ・結果の画面・パッドの文も、SqmTable から呼ばれてここで作る
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmStdView : UdonSharpBehaviour
    {
        public SqmTable table;
        public SqmStdEngine engine;
        public SqmStdNpc npc;
        public AudioClip[] kyokuVoicesSouth;  // 「南一局」〜「南四局」
        public AudioClip[] stdVoices;         // STDV_* の順

        public const int STDV_PEI = 0, STDV_SUUFUU = 1, STDV_SUUCHA = 2, STDV_SUUKAN = 3, STDV_SANCHA = 4,
            STDV_MANGAN = 5, STDV_HANEMAN = 6, STDV_BAIMAN = 7, STDV_SANBAIMAN = 8, STDV_YAKUMAN = 9, STDV_KAZOE = 10, STDV_KYUSHU = 11;

        private const int OPT = SqmStdEngine.OPT;
        private const int NC = SqmStdEngine.NC;
        private const int SLOT_HAND = SqmTable.SLOT_HAND, SLOT_RIVER = SqmTable.SLOT_RIVER, SLOT_MELD = SqmTable.SLOT_MELD;
        private const float TW = SqmTable.TW, TH = SqmTable.TH, TT = SqmTable.TT, TOP = SqmTable.TOP;
        public const int N_OPT = 12;
        private const int O_IDX = 7;          // 選択肢の写しに入れておく、BuildLegal での番号

        private bool ready;
        private int[] handCode;               // 卓の席ごとの手牌の枠の牌(コード。-1 = 空): [卓の席 * SLOT_HAND + i]
        private int[] handMainN;              // 卓の席ごとの、ツモ牌を除いた手牌の数
        private int[] optBuf;                 // 自分の選択肢(打牌以外)の写し
        [System.NonSerialized] public int optN;
        private bool[] riichiCode;            // リーチで切れる牌(コードごと)
        private int sel = -1;                 // 選んだ手牌の枠(卓の席 * SLOT_HAND + i)
        [System.NonSerialized] public bool riichiArmed;
        [System.NonSerialized] public bool myTurn, riichiOk, discardOk;
        private int[] sortTmp;

        private void Init()
        {
            if (ready) return;
            ready = true;
            handCode = new int[8 * SLOT_HAND];
            handMainN = new int[8];
            optBuf = new int[N_OPT * OPT];
            riichiCode = new bool[NC];
            sortTmp = new int[NC];
        }

        // ================================================================ 席・牌
        public int TS(int es) { return table.StdTableSeat(es); }
        public int ES(int ts) { return table.StdEngineSeat(ts); }
        public int MyES() { return table.humanSeat >= 0 ? ES(table.humanSeat) : -1; }

        /// <summary>コード → 牌の絵の番号(平方麻雀の 47 種 + 赤5筒 47・赤5索 48)</summary>
        public int Face(int code)
        {
            if (code < 0) return -1;
            if (code < 27) return code;
            if (code < SqmStdEngine.NK) return code + 9;   // 字牌: 東南西北白發中 = 36〜42
            if (code == SqmStdEngine.RED0 + 1) return 47;
            if (code == SqmStdEngine.RED0 + 2) return 48;
            return 4;   // 赤5萬は使わない(来たら普通の5萬)
        }

        public string TileName(int code)
        {
            if (code < 0) return "";
            int k = engine.KindOf(code);
            if (k >= 27) return "東南西北白發中".Substring(k - 27, 1);
            string s = ((k % 9) + 1) + "萬筒索".Substring(k / 9, 1);
            return engine.IsRed(code) ? "赤" + s : s;
        }

        public string WindName(int k) { return "東南西北".Substring(k - 27, 1); }

        public string SeatNameEs(int es) { return WindName(engine.SeatWind(es)) + "家 " + table.SeatNameOf(TS(es)); }

        public string StyleNameOf(int style) { return "CPU・" + (style == 0 ? "攻め" : style == 1 ? "守り" : style == 2 ? "鳴き" : "門前"); }

        public bool Running() { return engine.pendKind != SqmStdEngine.PEND_END; }

        // ================================================================ 卓の上
        public void RefreshTable()
        {
            Init();
            RefreshWall();
            for (int ts = 0; ts < 8; ts++) { RefreshHand(ts); RefreshRiver(ts); RefreshMelds(ts); }
        }

        // ---- 壁 ----
        private int StacksPerWall() { return engine.NP == 3 ? 18 : 17; }

        /// <summary>割れ目のサイコロ(局の種から。表示だけに使う)</summary>
        private int Dice() { int v = (int)(engine.kyokuSeed >> 1); return v % 6 + 1 + (v / 6) % 6 + 1; }   // U# は uint の % を使えない

        /// <summary>山の牌の番号 t(engine.wall の並び)が、もう取られたか</summary>
        private bool Taken(int t)
        {
            int n = engine.wallN;
            if (t < engine.wallPos) return true;
            int r = engine.replN;
            if (t >= n - 4) return n - 1 - t < (r < 4 ? r : 4);
            int k = n - 15 - t;   // 嶺上が足りなくなってから山の最後から取った分
            return k >= 4 && k < r;
        }

        private void RefreshWall()
        {
            Transform[] stacks = table.wallStacks;
            int used = 0;
            bool live = engine.phase != SqmStdEngine.PH_DEAL && engine.wallN > 0;
            int W = engine.NP, S = StacksPerWall(), T = W * S;
            int d = Dice();
            int owner = (engine.dealer + d - 1) % W;
            if (live)
            {
                for (int g = 0; g < T && used < stacks.Length; g++)
                {
                    int k = d + g;
                    int w = ((owner - k / S) % W + W) % W;
                    int j = k % S;
                    int left = (Taken(2 * g) ? 0 : 1) + (Taken(2 * g + 1) ? 0 : 1);
                    bool doraHere = false;
                    int doraI = T - 3 - g;   // この幢に載るドラ表示の番号
                    if (doraI >= 0 && doraI < engine.doraN) doraHere = true;
                    Transform tr = stacks[used++];
                    if (left == 0) { if (tr.gameObject.activeSelf) tr.gameObject.SetActive(false); continue; }
                    if (!tr.gameObject.activeSelf) tr.gameObject.SetActive(true);
                    int ts = TS(w);
                    Vector3 u = table.U(ts), r = table.R(ts);
                    float x = ((S - 1) / 2f - j) * 0.0262f;   // 持ち主から見て右端が j = 0
                    // 1枚だけ残った幢・ドラ表示を表にした幢は、下の段だけ見せる(天板に半分沈める)
                    bool single = left == 1 || doraHere;
                    float y = TOP + TT + (single ? -TT : 0);
                    tr.localPosition = u * 0.83f + r * x + Vector3.up * y;
                    tr.localRotation = Quaternion.LookRotation(-u, Vector3.up);
                    if (doraHere) PlaceDora(doraI, u * 0.83f + r * x, ts);
                }
            }
            for (int i = used; i < stacks.Length; i++) if (stacks[i].gameObject.activeSelf) stacks[i].gameObject.SetActive(false);
            // 中央の牌は、ドラ表示(王牌の上)にだけ使う
            Transform[] ct = table.centerTiles;
            for (int i = live ? engine.doraN : 0; i < ct.Length; i++) table.PlaceTile(ct[i], table.centerFaces[i], -1, Vector3.zero, 0, true, false, false, false);
        }

        private void PlaceDora(int i, Vector3 at, int ts)
        {
            if (i >= table.centerTiles.Length) return;
            Vector3 pos = at + Vector3.up * (TOP + TT + TT / 2);
            table.PlaceTile(table.centerTiles[i], table.centerFaces[i], Face(engine.DoraIndicator(i)), pos, ts, true, true, true, false);
        }

        // ---- 手牌 ----
        private bool Reveal(int es)
        {
            if (es == MyES() || table.humanSeat < 0) return true;
            if (engine.phase != SqmStdEngine.PH_KYOKU_OVER) return false;
            for (int w = 0; w < engine.winsN; w++) if (engine.winSeat[w] == es && engine.winTile[w] >= 0) return true;
            return engine.resType == SqmStdEngine.RES_EXHAUST && engine.resTenpai[es];
        }

        private bool Won(int es)
        {
            if (engine.phase != SqmStdEngine.PH_KYOKU_OVER) return false;
            for (int w = 0; w < engine.winsN; w++) if (engine.winSeat[w] == es && engine.winTile[w] >= 0) return true;
            return false;
        }

        /// <summary>手牌をコード順に並べる(赤5 は 5 の後ろ)。ツモ牌は右に離す</summary>
        private void SortHand(int ts, int es)
        {
            int b = ts * SLOT_HAND;
            int n = 0;
            int drawn = (engine.phase == SqmStdEngine.PH_TURN && engine.turn == es) ? engine.drawnCode[es] : -1;
            for (int k = 0; k < SqmStdEngine.NK; k++)
            {
                for (int v = 0; v < 2; v++)
                {
                    int code = v == 0 ? k : (k >= 9 && k < 27 && k % 9 == 4 ? SqmStdEngine.RED0 + k / 9 : -1);
                    if (code < 0) continue;
                    int cnt = engine.handC[es * NC + code];
                    if (code == drawn) cnt--;
                    for (int c = 0; c < cnt && n < SLOT_HAND; c++) handCode[b + n++] = code;
                }
            }
            handMainN[ts] = n;
            if (drawn >= 0 && n < SLOT_HAND) handCode[b + n++] = drawn;
            for (int i = n; i < SLOT_HAND; i++) handCode[b + i] = -1;
        }

        private void RefreshHand(int ts)
        {
            int b = ts * SLOT_HAND;
            int es = ES(ts);
            if (es < 0 || engine.phase == SqmStdEngine.PH_DEAL)
            {
                for (int i = 0; i < SLOT_HAND; i++) { handCode[b + i] = -1; table.PlaceTile(table.handTiles[b + i], table.handFaces[b + i], -1, Vector3.zero, ts, false, false, false, false); }
                handMainN[ts] = 0;
                return;
            }
            SortHand(ts, es);
            bool reveal = Reveal(es), won = Won(es);
            int mainN = handMainN[ts];
            Vector3 u = table.U(ts), r = table.R(ts);
            float width = mainN * (TW + 0.001f);
            for (int i = 0; i < SLOT_HAND; i++)
            {
                int slot = b + i;
                int code = handCode[slot];
                if (code < 0) { table.PlaceTile(table.handTiles[slot], table.handFaces[slot], -1, Vector3.zero, ts, false, false, false, false); continue; }
                float x = -width / 2 + i * (TW + 0.001f) + TW / 2 + (i >= mainN ? 0.012f : 0);
                if (won)
                {
                    Vector3 lay = u * (SqmTable.R_HAND + 0.005f) + r * x + Vector3.up * (TOP + TT / 2);
                    table.PlaceTile(table.handTiles[slot], table.handFaces[slot], Face(code), lay, ts, true, true, true, false);
                    continue;
                }
                float lift = slot == sel ? 0.012f : 0;
                Vector3 pos = u * SqmTable.R_HAND + r * x + Vector3.up * (TOP + TH / 2 + lift);
                table.PlaceTile(table.handTiles[slot], table.handFaces[slot], Face(code), pos, ts, false, true, reveal, false);
            }
        }

        // ---- 河(6枚ずつ。リーチの牌は横に) ----
        private void RefreshRiver(int ts)
        {
            int b = ts * SLOT_RIVER;
            int es = ES(ts);
            int n = 0;
            if (es >= 0 && engine.phase != SqmStdEngine.PH_DEAL)
            {
                Vector3 u = table.U(ts), r = table.R(ts);
                float x = 0;
                int col = 0, row = 0;
                for (int i = 0; i < engine.riverN[es] && n < SLOT_RIVER; i++)
                {
                    int o = es * SqmStdEngine.MAXRV + i;
                    int f = engine.rvF[o];
                    if ((f & SqmStdEngine.RV_CALLED) != 0) continue;
                    if (col == 6 && row < 3) { col = 0; row++; x = 0; }
                    bool side = (f & SqmStdEngine.RV_RIICHI) != 0;
                    float wdt = side ? TH : TW;
                    float cx = -3 * (TW + 0.001f) + x + wdt / 2;
                    float rad = 0.70f - row * (TH + 0.002f) + (side ? (TH - TW) / 2 : 0);   // 横にした牌も底辺をそろえる
                    Vector3 pos = u * rad + r * cx + Vector3.up * (TOP + TT / 2);
                    table.PlaceTile(table.riverTiles[b + n], table.riverFaces[b + n], Face(engine.rvC[o]), pos, ts, true, true, true, side);
                    x += wdt + 0.001f;
                    col++;
                    n++;
                }
            }
            for (int i = n; i < SLOT_RIVER; i++) table.PlaceTile(table.riverTiles[b + i], table.riverFaces[b + i], -1, Vector3.zero, ts, true, false, false, false);
        }

        // ---- 副露(右端から左へ。鳴いた牌は横に、暗槓は両端を伏せる)・北抜き ----
        private void RefreshMelds(int ts)
        {
            int b = ts * SLOT_MELD;
            int es = ES(ts);
            int n = 0;
            if (es >= 0 && engine.phase != SqmStdEngine.PH_DEAL)
            {
                Vector3 u = table.U(ts), r = table.R(ts);
                float x = 0.42f;
                for (int m = 0; m < engine.meldN[es]; m++)
                {
                    int mi = es * 4 + m;
                    int ty = engine.mType[mi];
                    // 鳴いた牌の位置: 上家から = 左端、対面から = 真ん中、下家から = 右端
                    int rel = (engine.mFrom[mi] - es + engine.NP) % engine.NP;
                    int cnt = (ty == SqmStdEngine.M_CHI || ty == SqmStdEngine.M_PON) ? 3 : 4;
                    int sidePos = ty == SqmStdEngine.M_ANKAN ? -1 : rel == engine.NP - 1 ? 0 : rel == 1 ? cnt - 1 : 1;
                    for (int k = cnt - 1; k >= 0 && n < SLOT_MELD; k--)
                    {
                        int code = MeldTile(mi, k, sidePos);
                        bool side = k == sidePos;
                        bool back = ty == SqmStdEngine.M_ANKAN && (k == 0 || k == 3);
                        float wdt = side ? TH : TW;
                        // 横にした牌は奥行きが TW になるので、底辺(自分に近い辺)が縦の牌とそろうよう手前へずらす
                        float rad = SqmTable.R_MELD + (side ? (TH - TW) / 2 : 0);
                        Vector3 pos = u * rad + r * (x - wdt / 2) + Vector3.up * (TOP + TT / 2);
                        table.PlaceTile(table.meldTiles[b + n], table.meldFaces[b + n], Face(code), pos, ts, true, true, !back, side);
                        x -= wdt + 0.001f;
                        n++;
                    }
                    x -= 0.008f;
                }
                for (int k = 0; k < engine.kitaN[es] && n < SLOT_MELD; k++)
                {
                    Vector3 pos = u * (SqmTable.R_MELD - TH - 0.004f) + r * (0.42f - k * (TW + 0.001f) - TW / 2) + Vector3.up * (TOP + TT / 2);
                    table.PlaceTile(table.meldTiles[b + n], table.meldFaces[b + n], Face(SqmStdEngine.NORTH), pos, ts, true, true, true, false);
                    n++;
                }
            }
            for (int i = n; i < SLOT_MELD; i++) table.PlaceTile(table.meldTiles[b + i], table.meldFaces[b + i], -1, Vector3.zero, ts, true, false, false, false);
        }

        /// <summary>副露の k 枚目(左から)。鳴いた牌は sidePos の位置に置く</summary>
        private int MeldTile(int mi, int k, int sidePos)
        {
            int ty = engine.mType[mi];
            if (ty == SqmStdEngine.M_ANKAN)
                return k == 0 ? engine.mT0[mi] : k == 1 ? engine.mT1[mi] : k == 2 ? engine.mT2[mi] : engine.mT3[mi];
            if (k == sidePos) return engine.mCalled[mi];
            // 手から出した牌(加槓は足した牌も)を、鳴いた牌の位置を飛ばして並べる
            int q = k > sidePos ? k - 1 : k;
            if (ty == SqmStdEngine.M_CHI)
            {
                int a = engine.mT0[mi], b = engine.mT1[mi];
                if (engine.KindOf(a) > engine.KindOf(b)) { int x = a; a = b; b = x; }
                return q == 0 ? a : b;
            }
            if (ty == SqmStdEngine.M_MINKAN) return q == 0 ? engine.mT0[mi] : q == 1 ? engine.mT1[mi] : engine.mT2[mi];
            if (ty == SqmStdEngine.M_KAKAN) return q == 0 ? engine.mT0[mi] : q == 1 ? engine.mT1[mi] : engine.mT3[mi];
            return q == 0 ? engine.mT0[mi] : engine.mT1[mi];
        }

        // ================================================================ 縁の文字
        public string LabelText(int ts, bool running)
        {
            int es = ES(ts);
            if (es < 0) return "";
            string txt = table.SeatNameOf(ts);
            if (table.SeatIsHost(ts)) txt = "<color=#40e0ff>HOST</color> " + txt;
            if (engine.riichiOn[es]) txt += "  リーチ";
            if (running && engine.pendKind == SqmStdEngine.PEND_TURN && engine.turn == es) txt = "▶ " + txt;
            return txt;
        }

        public string WindLabel(int ts) { int es = ES(ts); return es >= 0 ? WindName(engine.SeatWind(es)) : ""; }
        public string ScoreLabel(int ts) { int es = ES(ts); return es >= 0 ? engine.score[es].ToString() : ""; }
        public bool RiichiAt(int ts) { int es = ES(ts); return es >= 0 && engine.riichiOn[es] && engine.phase != SqmStdEngine.PH_DEAL; }
        public bool TurnAt(int ts) { int es = ES(ts); return es >= 0 && engine.pendKind == SqmStdEngine.PEND_TURN && engine.turn == es; }

        // ================================================================ 局の名前・声
        public string RoundName() { return (engine.round == 0 ? "東" : engine.round == 1 ? "南" : "西") + engine.kyokuNo + "局"; }
        public bool IsAllLast() { return engine.round == engine.rRounds - 1 && engine.kyokuNo == engine.NP; }

        public AudioClip KyokuVoice()
        {
            int k = engine.kyokuNo - 1;
            if (engine.round == 0) return table.kyokuVoices != null && k < table.kyokuVoices.Length ? table.kyokuVoices[k] : null;
            return kyokuVoicesSouth != null && k < kyokuVoicesSouth.Length ? kyokuVoicesSouth[k] : null;
        }

        public AudioClip StdVoice(int i) { return stdVoices != null && i >= 0 && i < stdVoices.Length ? stdVoices[i] : null; }

        // ================================================================ 操作(自分の番・鳴き)
        /// <summary>自分の選択肢を調べ直す(打牌以外を optBuf に写す)。canAct = 操作を受け付けられる状態か</summary>
        public void RefreshControls(bool canAct)
        {
            Init();
            optN = 0;
            int es = MyES();
            myTurn = canAct && es >= 0 && engine.pendKind == SqmStdEngine.PEND_TURN && engine.turn == es;
            bool myCall = false;
            if (canAct && es >= 0 && engine.pendKind == SqmStdEngine.PEND_CALLS)
                for (int i = 0; i < engine.pendN; i++) if (engine.pendSeats[i] == es) myCall = true;
            riichiOk = false;
            for (int c = 0; c < NC; c++) riichiCode[c] = false;
            if (myTurn || myCall)
            {
                int n = engine.BuildLegal(es);
                for (int i = 0; i < n; i++)
                {
                    int b = i * OPT;
                    int ty = engine.legal[b + SqmStdEngine.O_TYPE];
                    if (ty == SqmStdEngine.A_DISCARD)
                    {
                        if (engine.legal[b + SqmStdEngine.O_RIICHI] == 1) { riichiOk = true; riichiCode[engine.legal[b + SqmStdEngine.O_TILE]] = true; }
                        continue;
                    }
                    if (optN >= N_OPT) continue;
                    for (int k = 0; k < OPT; k++) optBuf[optN * OPT + k] = engine.legal[b + k];
                    optBuf[optN * OPT + O_IDX] = i;
                    optN++;
                }
            }
            if (!riichiOk) riichiArmed = false;
            // リーチ中は、ツモ和了・槓・北抜きを見送ってツモ切りするときだけ「確定」を出す
            discardOk = myTurn && (!engine.riichiOn[es] || optN > 0);
            if (!myTurn) sel = -1;
        }

        public void ClickTile(int slot)
        {
            Init();
            if (!myTurn || slot / SLOT_HAND != table.humanSeat) return;
            int code = handCode[slot];
            if (code < 0) return;
            int es = MyES();
            if (engine.riichiOn[es]) return;   // リーチ中は選べない(ツモ切りは「確定」か自動)
            if (riichiArmed && !riichiCode[code]) return;
            if (sel == slot) { Discard(); return; }   // 同じ牌を続けて2回で打牌
            sel = slot;
            table.Refresh();
        }

        public void Discard()
        {
            if (!myTurn) return;
            int es = MyES();
            int code;
            if (engine.riichiOn[es]) code = engine.drawnCode[es];
            else
            {
                if (sel < 0) { table.ShowMessage("切る牌を選んでください"); return; }
                code = handCode[sel];
            }
            int idx = FindDiscard(es, code, riichiArmed && !engine.riichiOn[es]);
            if (idx < 0) { table.ShowMessage("その牌は切れません"); return; }
            sel = -1;
            riichiArmed = false;
            table.SendStdCode(es | (idx << 3));
        }

        private int FindDiscard(int es, int code, bool riichi)
        {
            int n = engine.BuildLegal(es);
            for (int i = 0; i < n; i++)
            {
                int b = i * OPT;
                if (engine.legal[b + SqmStdEngine.O_TYPE] == SqmStdEngine.A_DISCARD && engine.legal[b + SqmStdEngine.O_TILE] == code
                    && engine.legal[b + SqmStdEngine.O_RIICHI] == (riichi ? 1 : 0)) return i;
            }
            return -1;
        }

        public void ToggleRiichi()
        {
            if (!riichiArmed && !riichiOk) return;
            riichiArmed = !riichiArmed;
            if (riichiArmed && sel >= 0 && !riichiCode[handCode[sel]]) sel = -1;
            table.Refresh();
        }

        public void ClickOption(int i)
        {
            if (i < 0 || i >= optN) return;
            int es = MyES();
            if (es < 0) return;
            sel = -1;
            riichiArmed = false;
            table.SendStdCode(es | (optBuf[i * OPT + O_IDX] << 3));
        }

        public void ClearSelection() { sel = -1; riichiArmed = false; }

        public int OptType(int k) { return optBuf[k * OPT + SqmStdEngine.O_TYPE]; }

        /// <summary>パッドのボタンの種類: 0 ツモ 1 ロン 3 ポン 4 チー 5 カン 6 九種九牌 8 パス 9 北(抜き)</summary>
        public int OptGroup(int k)
        {
            int ty = OptType(k);
            if (ty == SqmStdEngine.A_TSUMO) return 0;
            if (ty == SqmStdEngine.A_RON) return 1;
            if (ty == SqmStdEngine.A_PON) return 3;
            if (ty == SqmStdEngine.A_CHI) return 4;
            if (ty == SqmStdEngine.A_ANKAN || ty == SqmStdEngine.A_KAKAN || ty == SqmStdEngine.A_MINKAN) return 5;
            if (ty == SqmStdEngine.A_KYUSHU) return 6;
            if (ty == SqmStdEngine.A_PASS) return 8;
            if (ty == SqmStdEngine.A_KITA) return 9;
            return -1;
        }

        public string OptionText(int k)
        {
            int b = k * OPT;
            int ty = optBuf[b + SqmStdEngine.O_TYPE];
            int t = optBuf[b + SqmStdEngine.O_TILE];
            if (ty == SqmStdEngine.A_TSUMO) return "ツモ";
            if (ty == SqmStdEngine.A_RON) return "ロン";
            if (ty == SqmStdEngine.A_PON) return "ポン " + TileName(optBuf[b + SqmStdEngine.O_A]) + TileName(optBuf[b + SqmStdEngine.O_B]);
            if (ty == SqmStdEngine.A_CHI) return "チー " + TileName(optBuf[b + SqmStdEngine.O_A]) + TileName(optBuf[b + SqmStdEngine.O_B]);
            if (ty == SqmStdEngine.A_ANKAN) return "暗槓 " + TileName(t);
            if (ty == SqmStdEngine.A_KAKAN) return "加槓 " + TileName(t);
            if (ty == SqmStdEngine.A_MINKAN) return "大明槓 " + TileName(t);
            if (ty == SqmStdEngine.A_KYUSHU) return "九種九牌";
            if (ty == SqmStdEngine.A_KITA) return "北抜き";
            if (ty == SqmStdEngine.A_PASS) return "パス";
            return "?";
        }

        // ---- パッドの手牌 ----
        public int PadSlotTile(int i)
        {
            int ts = table.humanSeat;
            if (ts < 0 || i >= SLOT_HAND) return -1;
            return Face(handCode[ts * SLOT_HAND + i]);
        }

        public int PadMainN() { return table.humanSeat >= 0 ? handMainN[table.humanSeat] : 0; }
        public bool PadSelected(int i) { return table.humanSeat >= 0 && table.humanSeat * SLOT_HAND + i == sel; }

        public bool PadTileDim(int i)
        {
            int ts = table.humanSeat;
            if (ts < 0 || !riichiArmed) return false;
            int code = handCode[ts * SLOT_HAND + i];
            return code >= 0 && !riichiCode[code];
        }

        /// <summary>パッドの副露の k 枚目(-1 = なし、-2 = 伏せた牌)。北抜きも後ろに並べる</summary>
        public int PadMeldTile(int k)
        {
            int es = MyES();
            if (es < 0) return -1;
            int n = 0;
            for (int m = 0; m < engine.meldN[es]; m++)
            {
                int mi = es * 4 + m;
                int ty = engine.mType[mi];
                int cnt = (ty == SqmStdEngine.M_CHI || ty == SqmStdEngine.M_PON) ? 3 : 4;
                for (int j = 0; j < cnt; j++, n++)
                {
                    if (n != k) continue;
                    if (ty == SqmStdEngine.M_ANKAN && (j == 0 || j == 3)) return -2;
                    return Face(MeldTile(mi, j, -1));
                }
            }
            for (int j = 0; j < engine.kitaN[es]; j++, n++) if (n == k) return Face(SqmStdEngine.NORTH);
            return -1;
        }

        /// <summary>鳴ける牌(捨てられた牌・加槓の牌)</summary>
        public int PadCallTile(int k)
        {
            if (k != 0 || optN == 0 || engine.pendKind != SqmStdEngine.PEND_CALLS) return -1;
            return Face(engine.dcCode);
        }

        private int waitStepKey = -1;
        private string waitText = "";

        /// <summary>パッドの「待ち」の行(聴牌していれば待ちの牌と残り枚数、フリテン)</summary>
        public string PadWaitText()
        {
            int es = MyES();
            if (es < 0) return "";
            int key = engine.kyokuSeq * 1000 + engine.riverN[es] * 10 + engine.meldN[es] + engine.handN[es] * 100000;
            if (key == waitStepKey) return waitText;
            waitStepKey = key;
            waitText = "";
            if ((engine.handN[es] + 3 * engine.meldN[es]) % 3 != 1) return waitText;   // 13 枚の形のときだけ
            int n = engine.WaitsOf(es);
            if (n == 0) return waitText;
            string t = "待ち ";
            for (int i = 0; i < n; i++) t += (i > 0 ? " " : "") + TileName(engine.waitK[i]);
            if (engine.IsFuriten(es)) t += "  <color=#c03030>フリテン</color>";
            waitText = t;
            return waitText;
        }

        public string PadGuide(bool confirm)
        {
            int es = MyES();
            if (es < 0) return "";
            if (confirm) return "和了・流局です。内容を確かめて「確認」を押してください。";
            if (myTurn)
            {
                if (engine.riichiOn[es]) return "リーチ中です。ツモ切りは自動です(和了・槓のときだけ選べます)。";
                if (riichiArmed) return "リーチ: 切る牌を選んでください(同じ牌をもう一度で確定)。";
                return "あなたの番です。切る牌を選び、もう一度押すか「確定」で切ります。";
            }
            if (optN > 0) return "鳴きますか？";
            return "";
        }

        public string PadInGameText()
        {
            return RoundName() + " " + engine.honba + "本場\n残 " + engine.LiveLeft().ToString("00");
        }

        public string PadCenterText()
        {
            return "<size=130%>" + RoundName() + "</size>\n残 " + engine.LiveLeft().ToString("00") + "\n千点棒 ×" + engine.kyotaku + "\n百点棒 ×" + engine.honba;
        }

        public string PadSeatBox(int es)
        {
            string[] ord = new string[] { "1st.", "2nd.", "3rd.", "4th." };
            int me = MyES();
            string t = "<size=125%>" + WindName(engine.SeatWind(es)) + "家</size> " + table.SeatNameOf(TS(es)) + "\n";
            t += ord[engine.RankOf(es)] + " " + engine.score[es].ToString("N0");
            if (me >= 0 && es != me)
            {
                int d = engine.score[es] - engine.score[me];
                if (d > 0) t += " <color=#d04040>+" + d.ToString("N0") + "</color>";
                else if (d < 0) t += " <color=#3070c0>" + d.ToString("N0") + "</color>";
                else t += " ±0";
            }
            t += "\n";
            if (engine.riichiOn[es]) t += "<color=#d06010>リーチ</color> ";
            if (engine.pendKind == SqmStdEngine.PEND_TURN && engine.turn == es) t += " <color=#2a8aa8>▶手番</color>";
            return t;
        }

        public string PadResultTable()
        {
            string t = "<size=120%>終局の結果</size>\n<size=80%>順位<pos=12%>名前<pos=52%>持ち点<pos=78%>最終</size>\n";
            for (int i = 0; i < engine.NP; i++)
            {
                int es = engine.mrSeat[i];
                string line = (i + 1) + "位<pos=12%>" + table.SeatNameOf(TS(es)) + "<pos=52%>" + engine.mrScore[i].ToString("N0") + "<pos=78%>" + Final10(engine.mrFinal10[i]);
                if (es == MyES()) line = "<color=#2a8aa8>" + line + "</color>";
                t += line + "\n";
            }
            return t;
        }

        private string Final10(int v)
        {
            int a = v < 0 ? -v : v;
            string s = (a / 10) + "." + (a % 10);
            return v > 0 ? "+" + s : v < 0 ? "<color=#d04040>-" + s + "</color>" : s;
        }

        // ================================================================ 結果の画面
        public string LimitName(int lim, int man)
        {
            if (lim == SqmStdEngine.LIM_YAKUMAN) return man >= 3 ? man + "倍役満" : man == 2 ? "ダブル役満" : "役満";
            if (lim == SqmStdEngine.LIM_KAZOE) return "数え役満";
            if (lim == SqmStdEngine.LIM_SANBAIMAN) return "三倍満";
            if (lim == SqmStdEngine.LIM_BAIMAN) return "倍満";
            if (lim == SqmStdEngine.LIM_HANEMAN) return "跳満";
            if (lim == SqmStdEngine.LIM_MANGAN) return "満貫";
            return "";
        }

        /// <summary>和了の点数の区切りの声(無ければ null)</summary>
        public AudioClip LimitVoice(int w)
        {
            int lim = engine.winLimit[w];
            if (lim == SqmStdEngine.LIM_YAKUMAN) return StdVoice(STDV_YAKUMAN);
            if (lim == SqmStdEngine.LIM_KAZOE) return StdVoice(STDV_KAZOE);
            if (lim == SqmStdEngine.LIM_SANBAIMAN) return StdVoice(STDV_SANBAIMAN);
            if (lim == SqmStdEngine.LIM_BAIMAN) return StdVoice(STDV_BAIMAN);
            if (lim == SqmStdEngine.LIM_HANEMAN) return StdVoice(STDV_HANEMAN);
            if (lim == SqmStdEngine.LIM_MANGAN) return StdVoice(STDV_MANGAN);
            return null;
        }

        public AudioClip AbortVoice()
        {
            int r = engine.resReason;
            if (r == SqmStdEngine.AB_SUUFUU) return StdVoice(STDV_SUUFUU);
            if (r == SqmStdEngine.AB_SUUCHA) return StdVoice(STDV_SUUCHA);
            if (r == SqmStdEngine.AB_SUUKAN) return StdVoice(STDV_SUUKAN);
            if (r == SqmStdEngine.AB_SANCHA) return StdVoice(STDV_SANCHA);
            return null;
        }

        /// <summary>和了の画面(w = engine.win* の番号)</summary>
        public void RenderWin(int w)
        {
            SqmTable t = table;
            int es = engine.winSeat[w];
            bool nagashi = engine.winTile[w] < 0;
            t.resTitle.text = (nagashi ? "流し満貫　" : "和了　") + RoundName();
            string kind = engine.winFrom[w] < 0 ? "ツモ" : "ロン";
            string head = SeatNameEs(es) + (nagashi ? "" : " <color=#ff5a48>" + kind + "</color>");
            if (engine.winFrom[w] >= 0) head += "  <size=55%>放銃：" + SeatNameEs(engine.winFrom[w]) + "</size>";
            if (engine.winPao[w] >= 0) head += "  <size=55%>責任払い：" + SeatNameEs(engine.winPao[w]) + "</size>";
            t.resHead.text = head;
            ShowHand(es, nagashi ? -1 : engine.winTile[w], engine.winFrom[w] >= 0);
            t.resDoraLabel.text = "ドラ";
            for (int i = 0; i < t.resDora.Length && i < engine.doraN; i++) t.HoloTile(t.resDora[i], Face(engine.DoraIndicator(i)));
            if (engine.riichiOn[es])
            {
                t.resUraLabel.text = "裏ドラ";
                for (int i = 0; i < t.resUra.Length && i < engine.doraN; i++) t.HoloTile(t.resUra[i], Face(engine.UraIndicator(i)));
            }
            int yn = engine.winYakuN[w];
            int per = (yn + 2) / 3;
            if (per < 1) per = 1;
            string c0 = "", c1 = "", c2 = "";
            for (int k = 0; k < yn; k++)
            {
                int id = engine.winYakuId[w * SqmStdEngine.MAXYAKU + k];
                int han = engine.winYakuHan[w * SqmStdEngine.MAXYAKU + k];
                // 役満の行は、翻の欄を少し左から(「役満×2」が右端で折り返さないように。一番長い「国士無双十三面待ち」にも重ならない所)
                string v = id >= 40 ? (han >= 2 ? "役満×" + han : "役満") : han + "翻";
                string line = engine.YakuName(id) + (id >= 40 ? "<pos=70%>" : "<pos=78%>") + v + "\n";
                if (k < per) c0 += line; else if (k < per * 2) c1 += line; else c2 += line;
            }
            t.resYaku0.text = c0; t.resYaku1.text = c1; t.resYaku2.text = c2;
            int lim = engine.winLimit[w];
            string ln = LimitName(lim, engine.winYakuman[w]);
            if (lim == SqmStdEngine.LIM_YAKUMAN) t.resHan.text = ln;
            else t.resHan.text = engine.winHan[w] + "<size=60%>翻</size>" + (engine.winFu[w] > 0 && lim == SqmStdEngine.LIM_NONE ? " " + engine.winFu[w] + "<size=60%>符</size>" : "") + " " + ln;
            t.resCalc.text = (es == engine.dealer ? "親" : "子") + (engine.winFrom[w] >= 0 ? " ロン" : " ツモ");
            t.resPoints.text = engine.winPoints[w].ToString("N0") + "<size=60%>点</size>";
        }

        public void RenderDraw()
        {
            SqmTable t = table;
            if (engine.resType == SqmStdEngine.RES_ABORT)
            {
                t.resTitle.text = "途中流局";
                int r = engine.resReason;
                string name = r == SqmStdEngine.AB_KYUSHU ? "九種九牌" : r == SqmStdEngine.AB_SUUFUU ? "四風連打" : r == SqmStdEngine.AB_SUUCHA ? "四家立直"
                    : r == SqmStdEngine.AB_SUUKAN ? "四槓散了" : "三家和";
                t.resHead.text = name;
                if (r == SqmStdEngine.AB_KYUSHU) { ShowHand(engine.turn, -1, false); t.resHead.text = SeatNameEs(engine.turn) + " 九種九牌"; }
                t.resBody.text = "\n\nこの局は流局となります(親は続けて、本場が1つ増えます)。";
                return;
            }
            t.resTitle.text = "流局";
            int tn = 0;
            string list = "";
            for (int es = 0; es < engine.NP; es++)
            {
                if (!engine.resTenpai[es]) continue;
                list += (tn > 0 ? "、" : "") + SeatNameEs(es);
                tn++;
            }
            t.resHead.text = tn == 0 ? "全員ノーテン" : tn == engine.NP ? "全員聴牌" : "聴牌 " + tn + "名";
            string body = tn > 0 ? "\n聴牌: " + list : "";
            if (engine.resReason == SqmStdEngine.EX_NAGASHI) body += "\n流し満貫があったため、聴牌料の支払いはありません。";
            else if (tn > 0 && tn < engine.NP) body += "\n聴牌料 " + engine.rNotenTotal.ToString("N0") + "点";
            t.resBody.text = body;
        }

        public void RenderFinal()
        {
            SqmTable t = table;
            t.resTitle.text = "終局　" + t.MatchTypeName();
            t.CenterRanks(true);
            string l = "";
            string[] ord = new string[] { "1st.", "2nd.", "3rd.", "4th." };
            for (int i = 0; i < engine.NP; i++)
                l += ord[i] + "<pos=16%>" + table.SeatNameOf(TS(engine.mrSeat[i])) + "<pos=66%>" + engine.mrScore[i].ToString("N0") + "\n";
            t.resRankL.text = l;
            t.resRankR.text = "";
        }

        /// <summary>結果の手牌: 門前を並べ、和了牌は右に離して赤枠、副露は後ろに</summary>
        private void ShowHand(int es, int winCode, bool ron)
        {
            SqmTable t = table;
            int n = 0;
            int skip = ron ? -1 : winCode;   // ツモは手の中の和了牌を1枚抜いて右に出す
            for (int k = 0; k < SqmStdEngine.NK; k++)
            {
                for (int v = 0; v < 2; v++)
                {
                    int code = v == 0 ? k : (k >= 9 && k < 27 && k % 9 == 4 ? SqmStdEngine.RED0 + k / 9 : -1);
                    if (code < 0) continue;
                    int cnt = engine.handC[es * NC + code];
                    if (code == skip) { cnt--; skip = -1; }
                    for (int c = 0; c < cnt; c++) t.ResTile(n++, Face(code), false);
                }
            }
            if (winCode >= 0) { n++; t.ResTile(n++, Face(winCode), true); }
            for (int m = 0; m < engine.meldN[es]; m++)
            {
                n++;
                int mi = es * 4 + m;
                int ty = engine.mType[mi];
                int cnt = (ty == SqmStdEngine.M_CHI || ty == SqmStdEngine.M_PON) ? 3 : 4;
                for (int j = 0; j < cnt; j++) t.ResTile(n++, Face(MeldTile(mi, j, -1)), false);
            }
        }

        // ================================================================ ホロ
        public void RefreshHolo(int view)
        {
            SqmTable t = table;
            if (t.holoRound != null) t.holoRound.text = RoundName();
            if (t.holoSticks != null) t.holoSticks.text = "×" + engine.kyotaku + "\n×" + engine.honba;
            bool dealt = engine.phase != SqmStdEngine.PH_DEAL;
            if (t.holoDora != null)
                for (int i = 0; i < t.holoDora.Length; i++) t.HoloTile(t.holoDora[i], dealt && i < engine.doraN ? Face(engine.DoraIndicator(i)) : -1);
            if (t.holoRon != null) for (int i = 0; i < t.holoRon.Length; i++) t.HoloTile(t.holoRon[i], -1);
            if (t.holoCenter != null) t.holoCenter.text = "";
        }
    }
}
