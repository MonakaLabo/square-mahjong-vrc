using TMPro;
using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 手元のパッドの中身(1組だけ)。座った人の手元で、その人の席のパッド(SqmPadBody)に付けて表示する。ほかの人には見えない。
    /// 見た目はホロと同じ紺と水色。ボタンは上下の辺が水平な六角形で、並んだボタン(タブなど)は両端だけとがらせてつなげる。
    /// 上の帯 = タブ(対局・点数・ルール・設定、デバッグモードなら Debug も)と局・残り枚数、下の帯 = ・INFO・ とお知らせ。
    /// 対局: 手牌(平方麻雀は 13 + 13 の2段、普通の麻雀は1段)・ツモ牌(右、2段の間の高さ)・副露(下の段の右)・待ち(小さい牌)、
    ///       操作ボタン(右下。候補が複数ある鳴きは、押すと候補に切り替わる)、和了・流局の「確認」。試合が無いときはゲーム前設定。
    /// 点数: 自分が下の真ん中になるよう8席を並べる(表)か、点数の推移(グラフ)。終局したら自動でこのタブになり、結果の表を出す。
    /// ルール: 項目を上下に送って、左右で値を変える。設定・Debug: 項目を枠に分けて、全部を1画面に出す。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmPad : UdonSharpBehaviour
    {
        public SqmTable table;
        public SqmEngine engine;
        public GameObject content;           // 中身の全体(座っていないときは隠す)

        // ---- 上の帯: タブ(0 対局 1 点数 2 ルール 3 設定 4 Debug)と局・残り ----
        public GameObject[] tabRoots;        // 5(Debug はデバッグモードのときだけ出す)
        public MeshRenderer[] tabFills, tabBorders;   // 5(設定は、右端のとき・Debug の隣のときで形が違うので、右端の形は下の2つ)
        public TextMeshPro[] tabLabels;
        public GameObject[] tabGlows;
        public GameObject tab3Mid, tab3End;  // 設定タブの形(Debug の隣 = 右も四角 / 右端 = 右をとがらせる)
        public MeshRenderer tab3EndFill, tab3EndBorder;
        public TextMeshPro statusText, statusLeft;   // 「東1局 0本場　残」と残り枚数(7セグ)
        // ---- 下の帯: ・INFO・ とお知らせ、点数タブの表/グラフの切り替え ----
        public TextMeshPro infoTag, infoText;
        public GameObject toggleRoot;
        public MeshRenderer[] toggleFills, toggleBorders;   // 2(0 表 1 グラフ)
        public TextMeshPro[] toggleLabels;

        // ---- 色(SqmBuilder が入れる) ----
        public Material matOnFill, matOnBorder, matOffFill, matOffBorder;   // 選んでいるボタン(水色)・選んでいないボタン
        public Material matDbgOnFill, matDbgOnBorder, matDbgOffBorder;     // Debug タブ(橙)
        public Material matBtnFill, matBtnBorder;                          // ふつうのボタン
        public Material matDimFill, matDimBorder, matDimOnFill;            // ゲーム前設定の選べないボタン
        public Material matBox, matBoxMe, matBoxEmpty;                     // 点数の箱・自分の箱・三人打ちの空いた席の台形
        public Material matLine, matLineMe, matLineTurn;                   // 点数の箱の縁(ふつう・自分・手番)
        public Material matMeterOn, matMeterOff, matKnobOn, matKnobOff;
        public Material[] switchOn;          // スイッチの ON の色 3(0 掴める 1 切るべき牌 2 自動プレイ)
        public Material[] btnFills, btnBorders;   // 操作ボタンの色 BTN_* の順
        public Color[] btnText;
        public Color colText, colMuted, colDark, colCyan, colYellow, colRed, colOrange;
        public Material[] faceMats;          // 牌の絵 49(光の影響を受けない)
        public Material matBack;             // 伏せた牌

        // ---- 対局 ----
        public GameObject gameRoot;
        public TextMeshPro gameMessage, gameSub;   // 案内(1行目)と、その補足(リーチのとき・デバッグ)
        public GameObject lastRoot;          // 直前の打牌(右上の枠: 席風・牌・名前)
        public TextMeshPro lastWind, lastName;
        public MeshRenderer[] lastTiles;     // 2
        public MeshRenderer[] handTiles;     // 30(門前 26 + ツモ 2 + 予備)
        public GameObject[] handLift;        // 選んだ牌の印 30
        public GameObject[] handDim;         // リーチで切れない牌を暗くする 30
        public GameObject[] handHint;        // デバッグ: 切るべき牌の印 30
        public MeshRenderer[] meldTiles;     // 副露 24(小さめ。下の段の右に寄せる)
        public TextMeshPro drawLabel;        // 「ツモ」(ツモ牌の上)
        public GameObject callLabel;         // 「鳴ける牌」(ツモ牌と同じ所)
        public MeshRenderer[] callTiles;     // 鳴ける牌 2(捨てられた牌)
        public TextMeshPro waitLabel, waitNote;
        public MeshRenderer[] waitTiles;     // 待ち 16(小さい牌。平方麻雀は2枚で1組)
        public GameObject[] waitDim;         // 役が無い組を暗くする 16
        public TextMeshPro[] waitPlus;       // 組の間の「+」 8
        public GameObject[] btnRoots;        // 操作ボタン 6(右下に右寄せで並べる)
        public MeshRenderer[] btnFillR, btnBorderR;
        public TextMeshPro[] btnLabels;
        public GameObject confirmRoot;       // 「確認」
        // ゲーム前設定(試合が無いとき): 0 平方 1 四人 2 三人 3 東風戦 4 半荘 / NEW GAME
        public GameObject setupRoot, setupNew;
        public MeshRenderer[] setupFills, setupBorders;
        public TextMeshPro[] setupLabels;

        // ---- 点数 ----
        public GameObject scoreRoot;
        public GameObject scoreBoxes;        // 8席の箱(試合中)
        public GameObject[] boxFrames;       // 8(箱の縁と下地。普通の麻雀では隠して、台形を出す)
        public MeshRenderer[] boxBorders, boxFills;
        public GameObject[] boxGlows;        // 手番の人の箱の光
        public GameObject[] boxParts;        // 8(箱の中身: 席風・名前・点数・順位と差・札)
        public TextMeshPro[] boxWind, boxName, boxScore, boxSub, boxTags;
        public GameObject trapRoot;          // 普通の麻雀の台形 4つ(0 下 / 1 右 / 2 上 / 3 左)
        public MeshRenderer[] trapFills;
        public TextMeshPro scoreCenter;
        public TextMeshPro resultTable;      // 終局の結果の表
        // 点数の推移の折れ線グラフ
        public GameObject graphRoot;
        public LineRenderer[] graphLines;    // 8(線 = 平方麻雀は卓の席、普通の麻雀はエンジンの席)
        public LineRenderer graphBase;       // 開始の点数の線
        public LineRenderer graphDot;        // 自分の今の点
        public LineRenderer[] gridLines;     // 目盛りの横線 5
        public TextMeshPro[] gridLabels;     // 5
        public TextMeshPro[] graphTicks;     // 横軸の目盛り(局の名前)。点が多いときは間引く
        public LineRenderer[] legendBars;    // 凡例 8(今の点数の順)
        public TextMeshPro[] legendNames, legendScores;
        public GameObject legendMe;          // 凡例の自分の行の枠

        // ---- ルール(上下に送るリスト) ----
        public GameObject listRoot;
        public TextMeshPro listTitle, listPage;
        public TextMeshPro[] rowLabels, rowValues;   // ROWS
        public GameObject[] rowLeft, rowRight;       // ◀ ▶
        public GameObject scrollThumb;
        public const float SCROLL_TOP = 0.055f, SCROLL_H = 0.138f;

        // ---- 設定 ----
        public GameObject settingRoot;
        public MeshRenderer[] meterSegs;     // 20(0〜9 効果音 10〜19 声)
        public TextMeshPro[] meterVals;      // 2
        public MeshRenderer[] switchTrack, switchKnobR;   // 3
        public GameObject[] switchKnob;
        public TextMeshPro[] switchText;
        public TextMeshPro setSeatName;
        public GameObject hostRoot, guestNote;
        public GameObject[] kickRoots;       // 7
        public TextMeshPro[] kickLabels;
        // ---- Debug ----
        public GameObject debugRoot;
        public TextMeshPro dbgSpeed, dbgRig, dbgRigNote;

        public const string VERSION = "0.6.0";
        public const string CREDIT = "Made by SavannaMonaka with Claude";
        public const int TAB_GAME = 0, TAB_SCORE = 1, TAB_RULE = 2, TAB_SETTING = 3, TAB_DEBUG = 4;
        public const int ROWS = 7;
        public const int BTN_DISCARD = 0, BTN_RIICHI = 1, BTN_RIICHI_ON = 2, BTN_KAN = 3, BTN_PON = 4, BTN_CHI = 5, BTN_WIN = 6, BTN_PASS = 7, BTN_OTHER = 8;
        // 手牌の並び(m)
        public const float HAND_ST = 0.0248f, HAND_Y1 = 0.03f, HAND_Y2 = -0.007f, HAND_GAP = 0.014f;
        public const float HAND_X0 = -(15 * HAND_ST + HAND_GAP) / 2 + HAND_ST / 2;   // 13 枚 + すき間 + ツモ 2 枚を真ん中に
        public const float DRAW_X = HAND_X0 + 13 * HAND_ST + HAND_GAP;              // ツモ牌の列
        public const float DRAW_Y = (HAND_Y1 + HAND_Y2) / 2;
        public const float BTN_Y = -0.077f, BTN_W = 0.062f, BTN_STEP = 0.068f;
        public const float WAIT_X = -0.164f, WAIT_Y = -0.046f;

        // 操作ボタンの中身
        private const int ACT_NONE = 0, ACT_DISCARD = 1, ACT_RIICHI = 2, ACT_GROUP = 3, ACT_CAND = 4, ACT_CANCEL = 5, ACT_MORE = 6;
        private int[] slotAct, slotArg;
        private int tab;
        private int candGroup = -1, candPage;   // 候補を出している鳴きの種類と、その何ページ目か
        private int listTop;
        private bool showGraph;   // 点数タブで、表の代わりにグラフを出す
        private bool wasOver;
        private bool ready;
        private int[] legendOrder;

        // 設定(手元だけ)
        [System.NonSerialized] public bool grabbable;
        [System.NonSerialized] public bool debugHint;   // デバッグ: 切るべき牌を表示
        private int rigSel;                             // デバッグ: 次の局で出す役(ひな形の番号)
        private int sfxStep = 8, voiceStep = 10;

        private void Init()
        {
            if (ready) return;
            ready = true;
            slotAct = new int[6]; slotArg = new int[6];
            legendOrder = new int[8];
        }

        // ================================================================ 押されたとき
        public void OnKey(int code)
        {
            Init();
            if (table == null || table.humanSeat < 0) return;
            table.PlaySfx(SqmTable.SFX_CLICK);
            if (code < 30) { table.ClickTile(table.humanSeat * SqmTable.SLOT_HAND + code); return; }
            if (code >= 100 && code < 105) { tab = code - 100; listTop = 0; Refresh(); return; }
            if (code >= 200 && code < 206) { PressSlot(code - 200); return; }
            if (code == 210) { table.Confirm(); return; }
            if (code >= 300 && code < 300 + ROWS * 2) { PressRow((code - 300) / 2, (code - 300) % 2 == 0 ? -1 : 1); return; }
            if (code == 320) { listTop = Mathf.Max(0, listTop - ROWS); Refresh(); return; }
            if (code == 321) { listTop += ROWS; Refresh(); return; }
            if (code == 331 || code == 332) { showGraph = code == 332; Refresh(); return; }
            if (code >= 400 && code < 405) { PressSetup(code - 400); return; }
            if (code == 405) { table.NewMatch(); return; }
            if (code >= 500 && code < 530) { PressSetting(code); Refresh(); return; }
        }

        private void PressSlot(int i)
        {
            int a = slotAct[i], arg = slotArg[i];
            if (a == ACT_DISCARD) table.Discard();
            else if (a == ACT_RIICHI) table.ToggleRiichi();
            else if (a == ACT_GROUP)
            {
                // 候補が1つならすぐ、複数なら候補を出す
                int first = -1, n = 0;
                for (int k = 0; k < table.optN; k++) if (GroupOf(k) == arg) { if (first < 0) first = k; n++; }
                if (n == 1) table.PadOption(first);
                else if (n > 1) { candGroup = arg; candPage = 0; Refresh(); }
            }
            else if (a == ACT_CAND) { candGroup = -1; table.PadOption(arg); }
            else if (a == ACT_CANCEL) { candGroup = -1; Refresh(); }
            else if (a == ACT_MORE) { candPage++; Refresh(); }
        }

        /// <summary>ゲーム前設定のボタン: 人数(0〜2)・局数(3〜4)。変えられるのはホストだけ(平方のときは局数を選べない)</summary>
        private void PressSetup(int i)
        {
            if (!table.CanSetup()) return;
            int mode = table.SetupMode(), rounds = table.SetupRounds();
            if (i < 3)
            {
                if (i != SqmTable.MODE_SQUARE && !table.HasStdMode()) return;
                mode = i;
            }
            else
            {
                if (mode == SqmTable.MODE_SQUARE) return;
                rounds = i == 3 ? 1 : 2;
            }
            table.RequestSetup(mode, rounds);
        }

        // 設定・Debug のボタン: 500 効果音− 501 効果音+ 502 声− 503 声+ 504 掴める 505 元の位置 506 席を立つ 507 新しい試合 508 Reset 510〜516 立たせる
        //                       520 切るべき牌 521 自動プレイ 522 速度− 523 速度+ 524 役◀ 525 役▶ 526 仕込む
        private void PressSetting(int code)
        {
            if (code == 500 || code == 501) { sfxStep = Mathf.Clamp(sfxStep + (code == 500 ? -1 : 1), 0, 10); table.sfxVolume = sfxStep / 10f; }
            else if (code == 502 || code == 503) { voiceStep = Mathf.Clamp(voiceStep + (code == 502 ? -1 : 1), 0, 10); table.voiceVolume = voiceStep / 10f; }
            else if (code == 504) { grabbable = !grabbable; table.RefreshPads(); }
            else if (code == 505) { if (table.padBodies != null && table.humanSeat >= 0) table.padBodies[table.humanSeat].ReturnHome(); }
            else if (code == 506) table.Leave();
            else if (code == 507) { if (table.IsHostMe()) table.NewMatch(); }
            else if (code == 508) { if (table.IsHostMe()) table.ResetMatch(); }
            else if (code >= 510 && code < 517) { int s = KickSeat(code - 510); if (s >= 0 && table.IsHostMe()) table.KickSeat(s); }
            if (!table.debugEnabled) return;
            if (code == 520) debugHint = !debugHint;
            else if (code == 521) table.debugAutoPlay = !table.debugAutoPlay;
            else if (code == 522 || code == 523)
            {
                // ×1 → ×2 → ×4 → ×8(− で逆)
                int sp = table.debugSpeed;
                if (code == 523) sp = sp >= 8 ? 1 : sp * 2;
                else sp = sp <= 1 ? 8 : sp / 2;
                table.debugSpeed = sp;
            }
            else if (code == 524 || code == 525) { int rn = table.RigCount(); rigSel = ((rigSel + (code == 524 ? -1 : 1)) % rn + rn) % rn; }
            else if (code == 526) table.RequestRig(rigSel);
        }

        // ================================================================ 表示
        public void Refresh()
        {
            Init();
            if (table == null) return;
            int me = table.humanSeat;
            bool show = me >= 0;
            if (content != null && content.activeSelf != show) content.SetActive(show);
            if (!show) { candGroup = -1; return; }
            if (table.padBodies != null && me < table.padBodies.Length && table.padBodies[me] != null)
            {
                Transform anchor = table.padBodies[me].contentAnchor;
                if (anchor != null && content.transform.parent != anchor) content.transform.SetParent(anchor, false);
            }
            bool over = table.MatchOver();
            if (over && !wasOver) tab = TAB_SCORE;   // 終局したら点数タブへ
            wasOver = over;
            bool dbg = table.debugEnabled;
            if (tab == TAB_DEBUG && !dbg) tab = TAB_SETTING;

            RefreshTabs(dbg);
            RefreshBars();
            SetActive(toggleRoot, tab == TAB_SCORE && table.IsLive());
            SetActive(gameRoot, tab == TAB_GAME);
            SetActive(scoreRoot, tab == TAB_SCORE);
            SetActive(listRoot, tab == TAB_RULE);
            SetActive(settingRoot, tab == TAB_SETTING);
            SetActive(debugRoot, tab == TAB_DEBUG);
            if (tab == TAB_GAME) RefreshGame();
            else if (tab == TAB_SCORE) RefreshScore(over);
            else if (tab == TAB_RULE) RefreshList();
            else if (tab == TAB_SETTING) RefreshSetting();
            else RefreshDebug();
        }

        private void SetActive(GameObject g, bool on) { if (g != null && g.activeSelf != on) g.SetActive(on); }

        private void SetMat(MeshRenderer r, Material m) { if (r != null && m != null && r.sharedMaterial != m) r.sharedMaterial = m; }

        /// <summary>選んでいる/いないボタンの色(水色に塗る/縁だけ)</summary>
        private void Paint(MeshRenderer fill, MeshRenderer border, TextMeshPro label, bool on)
        {
            SetMat(fill, on ? matOnFill : matOffFill);
            SetMat(border, on ? matOnBorder : matOffBorder);
            if (label != null) label.color = on ? colDark : colMuted;
        }

        private void RefreshTabs(bool dbg)
        {
            SetActive(tabRoots[TAB_DEBUG], dbg);
            // 設定タブは、Debug が無ければ右端なので右をとがらせる
            SetActive(tab3Mid, dbg);
            SetActive(tab3End, !dbg);
            for (int i = 0; i < 5; i++)
            {
                bool on = i == tab;
                if (i == TAB_DEBUG)
                {
                    SetMat(tabFills[i], on ? matDbgOnFill : matOffFill);
                    SetMat(tabBorders[i], on ? matDbgOnBorder : matDbgOffBorder);
                    tabLabels[i].color = on ? colDark : colOrange;
                }
                else Paint(tabFills[i], tabBorders[i], tabLabels[i], on);
                SetActive(tabGlows[i], on);
            }
            Paint(tab3EndFill, tab3EndBorder, null, tab == TAB_SETTING);
        }

        /// <summary>上の帯の局・残りと、下の帯のお知らせ</summary>
        private void RefreshBars()
        {
            if (table.IsLive())
            {
                statusText.text = table.PadKyokuLabel() + "　残";
                statusLeft.text = table.PadLeftLabel();
            }
            else
            {
                statusText.text = "試合はまだ始まっていません";
                statusLeft.text = "";
            }
            infoText.text = table.PadInfo();
            Color c = colMuted;
            if (table.PadHasMessage()) c = colRed;
            else if (table.padMyTurn || table.padConfirm) c = colCyan;
            else if (!table.IsLive() && (table.IsHostMe() || table.NoHost())) c = colYellow;
            infoTag.color = c;
            infoText.color = c;
        }

        // ---------------------------------------------------------------- 対局
        private void RefreshGame()
        {
            bool live = table.IsLive();
            bool std = table.StdLive();
            bool confirm = table.padConfirm;
            gameMessage.text = table.PadGuide();
            gameSub.text = live && !confirm && table.padRiichiOk && !table.riichiArmed ? "リーチを押すと切れる牌が表示されます。" : "";
            // 直前の打牌
            int ls = live ? table.LastDiscardSeat() : -1;
            SetActive(lastRoot, ls >= 0);
            if (ls >= 0)
            {
                lastWind.text = table.LastDiscardWind();
                lastName.text = table.SeatDisplayName(ls);
                for (int k = 0; k < lastTiles.Length; k++) Tile(lastTiles[k], table.LastDiscardTile(k));
            }
            // デバッグ: 切るべき牌(強い NPC の判断)。手牌の枠のうち、その牌のものに印をつける
            bool hintOn = debugHint && table.debugEnabled && live;
            int hintSlotA = -1, hintSlotB = -1;
            string hint = hintOn ? table.PadHint() : "";
            if (hintOn && table.hintTileA >= 0)
            {
                for (int i = handTiles.Length - 1; i >= 0; i--)
                {
                    int t = i < SqmTable.SLOT_HAND ? table.PadSlotTile(i) : -1;
                    if (t < 0) continue;
                    if (hintSlotA < 0 && t == table.hintTileA) hintSlotA = i;
                    else if (hintSlotB < 0 && t == table.hintTileB) hintSlotB = i;
                }
            }
            if (hint.Length > 0) gameSub.text = "<color=#ffb35c>[DEBUG] " + hint + "</color>";
            // 手牌
            int n = 0, mainN = live ? table.PadMainN() : 0;
            for (int i = 0; i < handTiles.Length; i++)
            {
                int t = live && i < SqmTable.SLOT_HAND ? table.PadSlotTile(i) : -1;
                Tile(handTiles[i], t);
                bool sel = t >= 0 && table.PadSelected(i);
                SetActive(handLift[i], sel);
                SetActive(handDim[i], t >= 0 && table.PadTileDim(i));
                SetActive(handHint[i], t >= 0 && hintOn && (i == hintSlotA || i == hintSlotB));
                if (t >= 0) n = i + 1;
            }
            PlaceHand(mainN, std);
            SetActive(drawLabel.gameObject, n > mainN);
            PlaceMelds(live, std);
            // 鳴ける牌(捨てられた牌)。ツモ牌と同じ所に出す
            bool anyCall = false;
            for (int k = 0; k < callTiles.Length; k++) { int t = live ? table.PadCallTile(k) : -1; Tile(callTiles[k], t); if (t >= 0) anyCall = true; }
            SetActive(callLabel, anyCall && n <= mainN);
            RefreshWaits(live, std);

            // 操作ボタン
            for (int i = 0; i < 6; i++) { slotAct[i] = ACT_NONE; slotArg[i] = 0; }
            // 候補を出している鳴きが、もうできなくなっていたら閉じる
            if (candGroup >= 0)
            {
                bool still = false;
                for (int k = 0; k < table.optN; k++) if (GroupOf(k) == candGroup) still = true;
                if (!still) candGroup = -1;
            }
            SetActive(confirmRoot, confirm);
            RefreshSetup(!live);
            int s = 0;
            if (!confirm && live)
            {
                if (candGroup >= 0)
                {
                    // 候補(1ページ 4 つ + 次へ + やめる)
                    int idx = 0, shown = 0, more = 0;
                    for (int k = 0; k < table.optN; k++)
                    {
                        if (GroupOf(k) != candGroup) continue;
                        if (idx >= candPage * 4 && shown < 4) { Slot(s++, ACT_CAND, k, table.OptionText(k * SqmEngine.OPT), BtnStyle(candGroup)); shown++; }
                        else if (idx >= candPage * 4 + 4) more++;
                        idx++;
                    }
                    if (shown == 0 && candPage > 0) { candPage = 0; RefreshGame(); return; }
                    if (more > 0) Slot(s++, ACT_MORE, 0, "次へ", BTN_OTHER);
                    Slot(s++, ACT_CANCEL, 0, "やめる", BTN_PASS);
                }
                else
                {
                    // 鳴き・和了などの種類ごとに1つ
                    for (int g = 0; g < 10 && s < 4; g++)
                    {
                        bool has = false;
                        for (int k = 0; k < table.optN; k++) if (GroupOf(k) == g) has = true;
                        if (has) Slot(s++, ACT_GROUP, g, GroupName(g), BtnStyle(g));
                    }
                    if (table.padRiichiOk) Slot(s++, ACT_RIICHI, 0, table.riichiArmed ? "リーチ ON" : "リーチ", table.riichiArmed ? BTN_RIICHI_ON : BTN_RIICHI);
                    if (table.padDiscardOk) Slot(s++, ACT_DISCARD, 0, "確定", BTN_DISCARD);
                }
            }
            // 右下に右寄せで並べる(ボタンはひとつずつ独立した六角形)
            for (int i = 0; i < 6; i++)
            {
                bool on = i < s;
                SetActive(btnRoots[i], on);
                if (on) btnRoots[i].transform.localPosition = new Vector3(0.192f - BTN_W / 2 - (s - 1 - i) * BTN_STEP, BTN_Y, 0);
            }
        }

        /// <summary>ゲーム前設定の表示: 選んでいるものは水色。平方のときは局数を選べない色に。NEW GAME はホストだけ(ホストがいなければ、だれでも)</summary>
        private void RefreshSetup(bool show)
        {
            SetActive(setupRoot, show);
            if (!show || setupFills == null) return;
            gameMessage.text = "";
            gameSub.text = "";
            int mode = table.SetupMode(), rounds = table.SetupRounds();
            for (int i = 0; i < setupFills.Length; i++)
            {
                bool on = i < 3 ? i == mode : (i == 3 ? rounds == 1 : rounds == 2);
                bool dim = i >= 3 && mode == SqmTable.MODE_SQUARE;
                if (i > 0 && i < 3 && !table.HasStdMode()) dim = true;
                if (dim)
                {
                    SetMat(setupFills[i], on ? matDimOnFill : matDimFill);
                    SetMat(setupBorders[i], matDimBorder);
                    setupLabels[i].color = colMuted;
                }
                else Paint(setupFills[i], setupBorders[i], setupLabels[i], on);
                if (!dim && !on) setupLabels[i].color = colText;
            }
            SetActive(setupNew, table.IsHostMe() || table.NoHost());
        }

        /// <summary>
        /// 手牌の位置: 平方麻雀は半分ずつ2段(13 + 13)、普通の麻雀は上の段に1段。どちらも左から詰める。
        /// ツモ牌は右の列に、上の段と下の段の間の高さで。選んだ牌は少し浮かせる
        /// </summary>
        private void PlaceHand(int mainN, bool std)
        {
            int row1 = std ? mainN : (mainN + 1) / 2;
            for (int i = 0; i < handTiles.Length; i++)
            {
                Vector3 p;
                if (i < mainN)
                {
                    int r = i < row1 ? 0 : 1, c = i < row1 ? i : i - row1;
                    p = new Vector3(HAND_X0 + c * HAND_ST, r == 0 ? HAND_Y1 : HAND_Y2, 0);
                }
                else p = new Vector3(DRAW_X + (i - mainN) * HAND_ST, DRAW_Y, 0);
                if (handLift[i].activeSelf) p.y += 0.005f;
                // 板から数 mm 離す(近すぎると VR で奥行きの判定が揺れて、ちらついたり消えたりする)
                p.z = -0.003f;
                handTiles[i].gameObject.transform.localPosition = p;
                handLift[i].transform.localPosition = p + new Vector3(0, 0, 0.0015f);
                handDim[i].transform.localPosition = p + new Vector3(0, 0, -0.0015f);
                handHint[i].transform.localPosition = p + new Vector3(0, 0.019f, -0.001f);
            }
        }

        /// <summary>副露: 小さい牌で、下の段の右(ツモ牌の列の手前)に右寄せ。面子の間は少しあける</summary>
        private void PlaceMelds(bool live, bool std)
        {
            int me = table.humanSeat;
            int m = 0;
            if (live && std)
            {
                for (; m < meldTiles.Length; m++)
                {
                    int t = table.stdView.PadMeldTile(m);
                    if (t == -1) break;
                    Tile(meldTiles[m], t);
                }
            }
            else if (live)
            {
                for (int k = 0; k < engine.meldN[me] && m < meldTiles.Length; k++)
                {
                    int mi = me * 4 + k;
                    int ty = engine.meldType[mi], st = engine.meldStart[mi];
                    int cnt = 6 + (engine.meldKan[mi] != SqmEngine.KAN_NONE ? 1 : 0);
                    for (int j = 0; j < cnt && m < meldTiles.Length; j++)
                    {
                        int t = ty == SqmEngine.MT_K ? st : ty == SqmEngine.MT_T ? st + j / 2 : st + j;
                        if (j >= 6) t = st;
                        bool back = engine.meldKan[mi] == SqmEngine.KAN_ANKAN && (j == 0 || j == cnt - 1);
                        Tile(meldTiles[m], back ? -2 : t);
                        m++;
                    }
                }
            }
            for (int i = m; i < meldTiles.Length; i++) Tile(meldTiles[i], -1);
            if (m == 0) return;
            // 面子の始まりを数えて、右から置く
            const float MW = 0.0122f, MGAP = 0.003f;
            float x = DRAW_X - HAND_ST / 2 - 0.004f - MW / 2;
            float y = HAND_Y2 - 0.031f / 2 + 0.0156f / 2;
            for (int i = m - 1; i >= 0; i--)
            {
                meldTiles[i].gameObject.transform.localPosition = new Vector3(x, y, -0.003f);
                x -= MW;
                if (i > 0 && MeldStart(i, std)) x -= MGAP;
            }
        }

        /// <summary>副露の i 枚目が新しい面子の始まりか</summary>
        private bool MeldStart(int i, bool std)
        {
            if (std) return table.stdView.PadMeldStart(i);
            int me = table.humanSeat, n = 0;
            for (int k = 0; k < engine.meldN[me]; k++)
            {
                if (n == i) return true;
                n += 6 + (engine.meldKan[me * 4 + k] != SqmEngine.KAN_NONE ? 1 : 0);
            }
            return false;
        }

        /// <summary>待ち: 小さい牌で左下に(平方麻雀は2枚の組。役が無い組は暗く)。そのあとに、出しきれない数・フリテン</summary>
        private void RefreshWaits(bool live, bool std)
        {
            if (live) table.PadWaitsUpdate();
            int wn = live ? table.padWaitN : 0;
            bool pair = !std;
            bool anyNoYaku = false;
            float x = WAIT_X;
            for (int k = 0; k < waitPlus.Length; k++)
            {
                bool on = k < wn;
                int ia = pair ? k * 2 : k, ib = pair ? k * 2 + 1 : -1;
                if (on)
                {
                    bool dim = table.padWaitNoYaku[k];
                    if (dim) anyNoYaku = true;
                    WaitTile(ia, table.padWaitA[k], x, dim);
                    if (pair)
                    {
                        waitPlus[k].gameObject.transform.localPosition = new Vector3(x + 0.0105f, WAIT_Y, -0.005f);
                        WaitTile(ib, table.padWaitB[k], x + 0.021f, dim);
                    }
                    x += pair ? 0.04f : 0.0135f;
                }
                SetActive(waitPlus[k].gameObject, on && pair);
            }
            // 使わなかった小さい牌を隠す
            int used = pair ? wn * 2 : wn;
            for (int i = used; i < waitTiles.Length; i++) { Tile(waitTiles[i], -1); SetActive(waitDim[i], false); }
            SetActive(waitLabel.gameObject, wn > 0);
            string note = "";
            if (wn > 0 && table.padWaitMore > 0) note += "ほか" + table.padWaitMore + (pair ? "組" : "種") + "　";
            if (anyNoYaku) note += "暗い組は役なし　";
            if (live && table.padFuriten) note += "<color=#ff6a5a>フリテン</color>";
            waitNote.text = note;
            waitNote.gameObject.transform.localPosition = new Vector3(x + 0.002f + 0.075f, WAIT_Y, -0.005f);
        }

        private void WaitTile(int i, int t, float x, bool dim)
        {
            if (i < 0 || i >= waitTiles.Length) return;
            Tile(waitTiles[i], t);
            waitTiles[i].gameObject.transform.localPosition = new Vector3(x, WAIT_Y, -0.003f);
            SetActive(waitDim[i], dim && t >= 0);
            waitDim[i].transform.localPosition = new Vector3(x, WAIT_Y, -0.0045f);
        }

        private void Slot(int i, int act, int arg, string label, int style)
        {
            if (i >= 6) return;
            slotAct[i] = act; slotArg[i] = arg;
            btnLabels[i].text = label;
            if (style < btnFills.Length)
            {
                SetMat(btnFillR[i], btnFills[style]);
                SetMat(btnBorderR[i], btnBorders[style]);
                btnLabels[i].color = btnText[style];
            }
        }

        private void Tile(MeshRenderer r, int t)
        {
            bool show = t != -1;
            if (r.gameObject.activeSelf != show) r.gameObject.SetActive(show);
            if (!show) return;
            Material m = t >= 0 ? faceMats[t] : matBack;   // -2 = 伏せた牌
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }

        // 鳴き・和了の種類: 0 ツモ 1 片ロン 2 二枚ロン 3 ポン 4 チー 5 カン 6 十四種十四牌 7 続ける/止める 8 パス 9 北抜き
        private int GroupOf(int k)
        {
            if (table.StdLive()) return table.stdView.OptGroup(k);
            int ty = table.OptType(k);
            if (ty == SqmEngine.A_TSUMO) return 0;
            if (ty == SqmEngine.A_RON) return table.OptMode(k) == SqmEngine.RON_TWO ? 2 : 1;
            if (ty == SqmEngine.A_PON) return 3;
            if (ty == SqmEngine.A_CHI) return 4;
            if (ty == SqmEngine.A_KAN) return 5;
            if (ty == SqmEngine.A_ABORT) return 6;
            if (ty == SqmEngine.A_NEXT) return 7;
            if (ty == SqmEngine.A_PASS) return 8;
            return -1;
        }

        private string GroupName(int g)
        {
            if (g == 0) return "ツモ";
            if (g == 1) return table.StdLive() ? "ロン" : "片ロン";
            if (g == 2) return "二枚ロン";
            if (g == 3) return "ポン";
            if (g == 4) return "チー";
            if (g == 5) return "カン";
            if (g == 6) return table.StdLive() ? "九種九牌" : "十四種十四牌";
            if (g == 7) return "続ける/止める";
            if (g == 9) return "北抜き";
            return "パス";
        }

        private int BtnStyle(int g)
        {
            if (g <= 2) return BTN_WIN;
            if (g == 3) return BTN_PON;
            if (g == 4) return BTN_CHI;
            if (g == 5) return BTN_KAN;
            if (g == 8) return BTN_PASS;
            return BTN_OTHER;
        }

        // ---------------------------------------------------------------- 点数
        private void RefreshScore(bool over)
        {
            bool graph = showGraph && table.GraphReady();
            // 下の帯の切り替え: 終局したら「表」は「結果」
            toggleLabels[0].text = over ? "結果" : "表";
            Paint(toggleFills[0], toggleBorders[0], toggleLabels[0], !graph);
            Paint(toggleFills[1], toggleBorders[1], toggleLabels[1], graph);
            SetActive(graphRoot, graph);
            if (graph)
            {
                SetActive(scoreBoxes, false);
                SetActive(resultTable.gameObject, false);
                DrawGraph();
                return;
            }
            SetActive(scoreBoxes, !over);
            SetActive(resultTable.gameObject, over);
            if (over) { resultTable.text = table.PadResultTable(); return; }
            int me = table.humanSeat;
            bool live = table.IsLive();
            scoreCenter.text = live ? table.PadCenterText() : "";
            bool std = table.StdLive();
            SetActive(trapRoot, std);
            for (int p = 0; p < 8; p++)
            {
                int s = (me + p) % 8;
                // 普通の麻雀は、四角の箱を隠して、上下左右(p = 0・2・4・6)の台形に使っている席だけ出す
                bool used = !std || table.StdEngineSeat(s) >= 0;
                SetActive(boxFrames[p], !std);
                SetActive(boxParts[p], used);
                if (std && p % 2 == 0)
                {
                    // 台形は4つとも出す。三人打ちの空いた席は、文字なしの灰色
                    SetMat(trapFills[p / 2], used ? (p == 0 ? matBoxMe : matBox) : matBoxEmpty);
                }
                if (!used) continue;
                bool turn = false, riichi = false;
                if (live)
                {
                    table.PadSeatParts(s);
                    boxWind[p].text = table.padSWind;
                    boxName[p].text = table.padSName;
                    boxScore[p].text = table.padSScore.ToString();
                    boxSub[p].text = table.padSSub;
                    boxTags[p].text = table.padSTags;
                    turn = table.padSTurn;
                    riichi = table.padSRiichi;
                }
                else
                {
                    boxWind[p].text = "";
                    boxName[p].text = table.SeatDisplayName(s);
                    boxScore[p].text = "";
                    boxSub[p].text = "";
                    boxTags[p].text = "";
                }
                bool mine = p == 0;
                boxWind[p].color = mine ? colYellow : riichi ? colRed : colText;
                boxName[p].color = mine ? colYellow : colText;
                SetMat(boxFills[p], mine ? matBoxMe : matBox);
                SetMat(boxBorders[p], mine ? matLineMe : turn ? matLineTurn : matLine);
                SetActive(boxGlows[p], turn && !std);
            }
        }

        // ---- 折れ線グラフ ----
        private const float GX0 = -0.165f, GX1 = 0.06f, GY0 = -0.07f, GY1 = 0.068f, GZ = -0.004f;
        private const float LEG_Y0 = 0.072f, LEG_STEP = 0.0205f;
        private Color[] lineColor;
        private string[] lineHex;

        private void InitColors()
        {
            if (lineColor != null) return;
            // 席ごとの色(はっきり見分けられる8色)
            lineHex = new string[] { "3ec6e0", "f08a24", "d9468f", "4cb050", "e0b820", "8a63d2", "e04848", "2ba89a" };
            lineColor = new Color[] {
                new Color(0.243f, 0.776f, 0.878f), new Color(0.941f, 0.541f, 0.141f), new Color(0.851f, 0.275f, 0.561f), new Color(0.298f, 0.690f, 0.314f),
                new Color(0.878f, 0.722f, 0.125f), new Color(0.541f, 0.388f, 0.824f), new Color(0.878f, 0.282f, 0.282f), new Color(0.169f, 0.659f, 0.604f) };
        }

        /// <summary>
        /// 点数の推移を描く: 横は局(本場も1局。最初は開始の点数)、縦は点数(きりのいい点数ごとに横線と目盛り)。
        /// 自分の線は濃く太く、今の点に丸を打つ。ほかは彩度を落として細く。右の凡例は今の点数の順で、点数も出す
        /// </summary>
        private void DrawGraph()
        {
            InitColors();
            SqmScoreLog lg = table.scoreLog;
            int n = lg.lgN, lines = lg.lgLines;
            int lo = int.MaxValue, hi = int.MinValue;
            for (int i = 0; i < n; i++)
                for (int l = 0; l < lines; l++)
                {
                    int v = lg.At(i, l);
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
            int start = lg.At(0, 0);
            if (start < lo) lo = start;
            if (start > hi) hi = start;
            int span = hi - lo;
            if (span < 2000) { lo -= (2000 - span) / 2; hi = lo + 2000; span = 2000; }
            float pad = span * 0.08f;
            float vlo = lo - pad, vhi = hi + pad;
            // 目盛り: 横線が 4 本くらいになる、きりのいい間隔
            int[] steps = new int[] { 1000, 2000, 5000, 10000, 20000, 50000, 100000, 200000, 500000 };
            int step = steps[steps.Length - 1];
            for (int i = 0; i < steps.Length; i++) if ((vhi - vlo) / steps[i] <= 4.5f) { step = steps[i]; break; }
            int first = Mathf.CeilToInt(vlo / step) * step;
            for (int g = 0; g < gridLines.Length; g++)
            {
                int v = first + g * step;
                bool on = v <= vhi;
                SetActive(gridLines[g].gameObject, on);
                SetActive(gridLabels[g].gameObject, on);
                if (!on) continue;
                Vector3 a = GraphPoint(0, 2, v, vlo, vhi, false) + new Vector3(0, 0, 0.0006f);
                Vector3 b = GraphPoint(1, 2, v, vlo, vhi, false) + new Vector3(0, 0, 0.0006f);
                gridLines[g].positionCount = 2;
                gridLines[g].SetPosition(0, a);
                gridLines[g].SetPosition(1, b);
                gridLabels[g].text = (v / 1000) + "k";
                gridLabels[g].gameObject.transform.localPosition = new Vector3(GX0 + 0.017f, a.y + 0.005f, -0.005f);
            }
            int meLine = -1;
            for (int l = 0; l < graphLines.Length; l++)
            {
                LineRenderer lr = graphLines[l];
                bool use = l < lines && n > 0;
                SetActive(lr.gameObject, use);
                if (!use) continue;
                int ts = table.GraphLineSeat(l);
                bool me = ts >= 0 && ts == table.humanSeat;
                if (me) meLine = l;
                Color c = lineColor[l];
                if (!me)
                {
                    float gray = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    c = Color.Lerp(c, new Color(gray, gray, gray, 1f), 0.45f);
                    c.a = 0.6f;
                }
                lr.startColor = c;
                lr.endColor = c;
                lr.widthMultiplier = me ? 0.0026f : 0.0011f;
                lr.positionCount = n < 2 ? 2 : n;
                for (int i = 0; i < n; i++) lr.SetPosition(i, GraphPoint(i, n, lg.At(i, l), vlo, vhi, me));
                if (n < 2) lr.SetPosition(1, GraphPoint(1, 2, lg.At(0, l), vlo, vhi, me));
            }
            // 自分の今の点
            SetActive(graphDot.gameObject, meLine >= 0 && n > 0);
            if (meLine >= 0 && n > 0)
            {
                Vector3 p = GraphPoint(n < 2 ? 1 : n - 1, n < 2 ? 2 : n, lg.At(n - 1, meLine), vlo, vhi, true) + new Vector3(0, 0, -0.0003f);
                graphDot.positionCount = 2;
                graphDot.SetPosition(0, p);
                graphDot.SetPosition(1, p + new Vector3(0.0001f, 0, 0));
                graphDot.startColor = lineColor[meLine];
                graphDot.endColor = lineColor[meLine];
            }
            if (graphBase != null)
            {
                graphBase.positionCount = 2;
                graphBase.SetPosition(0, GraphPoint(0, 2, start, vlo, vhi, false) + new Vector3(0, 0, 0.0005f));
                graphBase.SetPosition(1, GraphPoint(1, 2, start, vlo, vhi, false) + new Vector3(0, 0, 0.0005f));
            }
            // 横軸の目盛り: 局の名前。目盛りの数より点が多ければ、等しい間隔で間引く
            int slots = graphTicks.Length;
            int tstep = n <= slots ? 1 : (n + slots - 1) / slots;
            int tk = 0;
            for (int i = 0; i < n && tk < slots; i += tstep)
            {
                TextMeshPro t = graphTicks[tk++];
                SetActive(t.gameObject, true);
                t.text = table.GraphTick(lg.KyokuAt(i));
                Vector3 p = GraphPoint(i, n, 0, 0f, 1f, false);
                t.gameObject.transform.localPosition = new Vector3(p.x, GY0 - 0.0115f, -0.005f);
            }
            for (; tk < slots; tk++) SetActive(graphTicks[tk].gameObject, false);
            // 凡例: 今の点数の順
            int cnt = lines < 8 ? lines : 8;
            if (n == 0) cnt = 0;
            for (int l = 0; l < cnt; l++) legendOrder[l] = l;
            for (int i = 1; i < cnt; i++)
            {
                int x = legendOrder[i], vx = lg.At(n - 1, x);
                int j = i - 1;
                while (j >= 0 && lg.At(n - 1, legendOrder[j]) < vx) { legendOrder[j + 1] = legendOrder[j]; j--; }
                legendOrder[j + 1] = x;
            }
            bool meShown = false;
            for (int k = 0; k < legendBars.Length; k++)
            {
                bool on = k < cnt;
                SetActive(legendBars[k].gameObject, on);
                SetActive(legendNames[k].gameObject, on);
                SetActive(legendScores[k].gameObject, on);
                if (!on) continue;
                int l = legendOrder[k];
                int ts = table.GraphLineSeat(l);
                bool me = ts >= 0 && ts == table.humanSeat;
                float y = LEG_Y0 - k * LEG_STEP;
                legendBars[k].positionCount = 2;
                legendBars[k].SetPosition(0, new Vector3(0.084f, y, GZ));
                legendBars[k].SetPosition(1, new Vector3(0.094f, y, GZ));
                legendBars[k].startColor = lineColor[l];
                legendBars[k].endColor = lineColor[l];
                legendNames[k].text = ts >= 0 ? table.SeatDisplayName(ts) : "";
                legendNames[k].color = me ? colYellow : colText;
                legendScores[k].text = lg.At(n - 1, l).ToString("N0");
                legendScores[k].color = me ? colText : colMuted;
                if (me)
                {
                    meShown = true;
                    legendMe.transform.localPosition = new Vector3(0.1385f, y, 0);
                }
            }
            SetActive(legendMe, meShown);
        }

        private Vector3 GraphPoint(int i, int n, int v, float vlo, float vhi, bool front)
        {
            float x = n < 2 ? GX0 : GX0 + (GX1 - GX0) * i / (n - 1);
            float y = GY0 + (GY1 - GY0) * (v - vlo) / (vhi - vlo);
            return new Vector3(x, y, GZ - (front ? 0.0005f : 0));
        }

        // ---------------------------------------------------------------- ルール
        private void RefreshList()
        {
            int n = table.RuleCount();
            if (listTop > Mathf.Max(0, n - 1)) listTop = 0;
            bool edit = table.CanEditRules();
            listTitle.text = table.RuleTitle() + "　<size=72%><color=#ffd170>" + (edit ? "ホストが変更できます" : "開始前にホストが決めます") + "</color></size>";
            int last = Mathf.Min(listTop + ROWS, n);
            listPage.text = (listTop + 1) + "-" + last + " / " + n;
            for (int r = 0; r < ROWS; r++)
            {
                int i = listTop + r;
                bool has = i < n;
                rowLabels[r].text = has ? table.RuleName(i) : "";
                rowValues[r].text = has ? table.RuleOptLabel(i, table.RuleValue(i)) : "";
                SetActive(rowLeft[r], has && edit);
                SetActive(rowRight[r], has && edit);
            }
            // スクロールバー: 見えている範囲
            if (scrollThumb != null && n > 0)
            {
                float t0 = SCROLL_H * listTop / n, t1 = SCROLL_H * last / n;
                scrollThumb.transform.localPosition = new Vector3(0.195f, SCROLL_TOP - (t0 + t1) / 2, -0.002f);
                scrollThumb.transform.localScale = new Vector3(0.004f, t1 - t0, 1);
            }
        }

        private void PressRow(int r, int dir)
        {
            int i = listTop + r;
            if (i < table.RuleCount()) table.RequestRule(i, dir);
        }

        // ---------------------------------------------------------------- 設定・Debug
        private int KickCount()
        {
            if (!table.IsHostMe()) return 0;
            int c = 0;
            for (int s = 0; s < 8; s++) if (table.SeatPlayer(s) != 0 && s != table.humanSeat) c++;
            return c;
        }

        private int KickSeat(int k)
        {
            int c = 0;
            for (int s = 0; s < 8; s++) if (table.SeatPlayer(s) != 0 && s != table.humanSeat) { if (c == k) return s; c++; }
            return -1;
        }

        private void RefreshSetting()
        {
            for (int i = 0; i < 10; i++)
            {
                SetMat(meterSegs[i], i < sfxStep ? matMeterOn : matMeterOff);
                SetMat(meterSegs[10 + i], i < voiceStep ? matMeterOn : matMeterOff);
            }
            meterVals[0].text = (sfxStep * 10) + "%";
            meterVals[1].text = (voiceStep * 10) + "%";
            Switch(0, grabbable);
            setSeatName.text = table.SeatDisplayName(table.humanSeat);
            bool host = table.IsHostMe();
            SetActive(hostRoot, host);
            SetActive(guestNote, !host);
            int kc = KickCount();
            for (int k = 0; k < kickRoots.Length; k++)
            {
                bool on = k < kc;
                SetActive(kickRoots[k], on);
                if (on) kickLabels[k].text = "立たせる: " + table.SeatDisplayName(KickSeat(k));
            }
        }

        private void RefreshDebug()
        {
            Switch(1, debugHint);
            Switch(2, table.debugAutoPlay);
            dbgSpeed.text = "×" + table.debugSpeed;
            if (rigSel >= table.RigCount()) rigSel = 0;
            dbgRig.text = table.RigNameOf(rigSel);
            dbgRigNote.text = table.CanRig() ? "配牌でひな形の牌が来て、2巡目のツモで和了牌が来ます" : "仕込めるのはホストだけです";
        }

        /// <summary>スイッチ i の見た目(ON: 色を塗ってつまみを右に / OFF: 暗くしてつまみを左に)</summary>
        private void Switch(int i, bool on)
        {
            SetMat(switchTrack[i], on ? switchOn[i] : matMeterOff);
            SetMat(switchKnobR[i], on ? matKnobOn : matKnobOff);
            switchKnob[i].transform.localPosition = new Vector3(on ? 0.009f : -0.009f, 0, -0.0025f);
            switchText[i].text = on ? "ON" : "OFF";
            switchText[i].color = on ? colDark : colText;
            switchText[i].gameObject.transform.localPosition = new Vector3(on ? -0.006f : 0.006f, 0, -0.005f);
        }
    }
}
