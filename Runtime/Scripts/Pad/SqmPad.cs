using TMPro;
using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 手元のパッドの中身(1組だけ)。座った人の手元で、その人の席のパッド(SqmPadBody)に付けて表示する。ほかの人には見えない。
    /// 上の帯 = タブ(対局・点数・ルール・設定)、下の帯 = IN GAME(局・本場・残り)と INFORMATION(お知らせ)。
    /// 対局: 手牌(2段)・ツモ牌・副露、操作ボタン(候補が複数ある鳴きは、押すと候補に切り替わる)、和了・流局の「確認」。
    /// 点数: 自分が下の真ん中になるよう8席を並べる。終局したら自動でこのタブになり、結果の表を出す。
    /// ルール・設定: 項目を上下に送って、左右で値を変える。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmPad : UdonSharpBehaviour
    {
        public SqmTable table;
        public SqmEngine engine;
        public GameObject content;           // 中身の全体(座っていないときは隠す)

        // 帯
        public MeshRenderer[] tabBacks;      // 4
        public Material matTabOn, matTabOff;
        public TextMeshPro inGameText, infoText;

        // 対局
        public GameObject gameRoot;
        public TextMeshPro gameMessage;
        public TextMeshPro waitText;         // 待ち(和了れる2枚の組み合わせ・役なし・フリテン)
        public MeshRenderer[] handTiles;     // 30(門前 28 + ツモ 2)
        public GameObject[] handLift;        // 選んだ牌の印 30
        public GameObject[] handDim;         // リーチで切れない牌を暗くする 30
        public GameObject[] handHint;        // デバッグ: 切るべき牌の印 30
        public MeshRenderer[] meldTiles;     // 副露 24(小さめ)
        public Material[] faceMats;          // 牌の絵 47(光の影響を受けない)
        public GameObject[] btnRoots;        // 操作ボタン 6
        public MeshRenderer[] btnBacks;
        public TextMeshPro[] btnLabels;
        public Material[] btnMats;           // BTN_* の順
        public GameObject confirmRoot;       // 大きな「確認」
        // ゲーム前設定(試合が無いとき): 0 平方 1 四人 2 三人 3 東風戦 4 半荘 / NEW GAME
        public GameObject setupRoot, setupNew;
        public MeshRenderer[] setupBacks;
        public TextMeshPro[] setupLabels;
        public Material matSetupOn, matSetupOff, matSetupDim, matSetupDimOn;
        public MeshRenderer[] callTiles;     // 鳴ける牌 2(捨てられた牌)
        public GameObject callLabel;

        // 点数
        public GameObject scoreRoot;
        public GameObject scoreBoxes;        // 8席の箱(試合中)
        public MeshRenderer[] boxFills;      // 8(マス p: 0 = 下中 … ホロと同じ並び)
        public GameObject[] boxFrames;       // 8(箱の枠。普通の麻雀では隠して、台形を出す)
        public GameObject trapRoot;          // 普通の麻雀の台形 4つ(0 下 / 1 右 / 2 上 / 3 左)
        public GameObject[] trapFrames;
        public MeshRenderer[] trapFills;
        public TextMeshPro[] boxTexts;
        public TextMeshPro scoreCenter;
        public Material matBox, matBoxMe;
        public Material matBoxEmpty;         // 三人打ちの空いた席の台形(文字なしの灰色)
        public TextMeshPro resultTable;      // 終局の結果の表
        // 点数の推移の折れ線グラフ(点数タブで表と切り替える。切り替えボタンは下の帯の右端)
        public GameObject graphRoot, graphToggle;
        public LineRenderer[] graphLines;    // 8(線 = 平方麻雀は卓の席、普通の麻雀はエンジンの席)
        public LineRenderer graphBase;       // 開始の点数の線
        public TextMeshPro graphTop, graphBottom, graphLegend, graphToggleLabel;
        public TextMeshPro[] graphTicks;     // 横軸の目盛り(局の名前)。点が多いときは間引く

        // ルール・設定(上下に送るリスト)
        public GameObject listRoot;
        public TextMeshPro listTitle;
        public TextMeshPro[] rowLabels, rowValues;   // 6
        public GameObject[] rowLeft, rowRight;       // ◀ ▶

        public const string VERSION = "0.5.0";
        public const string CREDIT = "Made by SavannaMonaka with Claude";
        public const int TAB_GAME = 0, TAB_SCORE = 1, TAB_RULE = 2, TAB_SETTING = 3;
        public const int ROWS = 6;
        public const int BTN_DISCARD = 0, BTN_RIICHI = 1, BTN_RIICHI_ON = 2, BTN_KAN = 3, BTN_PON = 4, BTN_CHI = 5, BTN_WIN = 6, BTN_PASS = 7, BTN_OTHER = 8;

        // 操作ボタンの中身
        private const int ACT_NONE = 0, ACT_DISCARD = 1, ACT_RIICHI = 2, ACT_GROUP = 3, ACT_CAND = 4, ACT_CANCEL = 5, ACT_MORE = 6, ACT_NEWGAME = 7;
        private int[] slotAct, slotArg;
        private int tab;
        private int candGroup = -1, candPage;   // 候補を出している鳴きの種類と、その何ページ目か
        private int listTop;
        private bool showGraph;   // 点数タブで、表の代わりにグラフを出す
        private bool wasOver;
        private bool ready;

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
        }

        // ================================================================ 押されたとき
        public void OnKey(int code)
        {
            Init();
            if (table == null || table.humanSeat < 0) return;
            table.PlaySfx(SqmTable.SFX_CLICK);
            if (code < 30) { table.ClickTile(table.humanSeat * SqmTable.SLOT_HAND + code); return; }
            if (code >= 100 && code < 104) { tab = code - 100; listTop = 0; Refresh(); return; }
            if (code >= 200 && code < 206) { PressSlot(code - 200); return; }
            if (code == 210) { table.Confirm(); return; }
            if (code >= 300 && code < 300 + ROWS * 2) { PressRow((code - 300) / 2, (code - 300) % 2 == 0 ? -1 : 1); return; }
            if (code == 320) { listTop = Mathf.Max(0, listTop - 1); Refresh(); return; }
            if (code == 321) { listTop++; Refresh(); return; }
            if (code >= 400 && code < 405) { PressSetup(code - 400); return; }
            if (code == 330) { showGraph = !showGraph; Refresh(); return; }
            if (code == 405) { table.NewMatch(); return; }
        }

        private void PressSlot(int i)
        {
            int a = slotAct[i], arg = slotArg[i];
            if (a == ACT_DISCARD) table.Discard();
            else if (a == ACT_RIICHI) table.ToggleRiichi();
            else if (a == ACT_NEWGAME) table.NewMatch();
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

            for (int i = 0; i < tabBacks.Length; i++)
            {
                Material m = i == tab ? matTabOn : matTabOff;
                if (tabBacks[i].sharedMaterial != m) tabBacks[i].sharedMaterial = m;
            }
            if (table.StdLive()) inGameText.text = table.stdView.PadInGameText();
            else if (table.IsLive())
                inGameText.text = "東" + engine.kyoku + "局 " + engine.honba + "本場\n残 " + engine.LiveLeft().ToString("0000");
            else inGameText.text = "—";
            infoText.text = table.PadInfo();

            SetActive(graphToggle, tab == TAB_SCORE && table.IsLive());
            SetActive(gameRoot, tab == TAB_GAME);
            SetActive(scoreRoot, tab == TAB_SCORE);
            SetActive(listRoot, tab == TAB_RULE || tab == TAB_SETTING);
            if (tab == TAB_GAME) RefreshGame();
            else if (tab == TAB_SCORE) RefreshScore(over);
            else RefreshList();
        }

        private void SetActive(GameObject g, bool on) { if (g != null && g.activeSelf != on) g.SetActive(on); }

        // ---------------------------------------------------------------- 対局
        private void RefreshGame()
        {
            int me = table.humanSeat;
            bool live = table.IsLive();
            gameMessage.text = table.PadGuide();
            if (waitText != null) waitText.text = live ? table.PadWaitText() : "";
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
            if (hint.Length > 0) gameMessage.text = "<color=#a05000>[DEBUG] " + hint + "</color>";
            // 手牌: 門前は 14 枚ずつ2段、ツモ牌は上の段の右
            int n = 0, mainN = live ? table.PadMainN() : 0;
            for (int i = 0; i < handTiles.Length; i++)
            {
                int t = live && i < SqmTable.SLOT_HAND ? table.PadSlotTile(i) : -1;
                Tile(handTiles[i], t);
                bool sel = t >= 0 && table.PadSelected(i);
                SetActive(handLift[i], sel);
                if (handDim != null) SetActive(handDim[i], t >= 0 && table.PadTileDim(i));
                if (handHint != null) SetActive(handHint[i], t >= 0 && hintOn && (i == hintSlotA || i == hintSlotB));
                if (t >= 0) n = i + 1;
            }
            PlaceHand(mainN, n);
            // 副露(上の段の左)
            int m = 0;
            if (live && table.StdLive())
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
            // 鳴ける牌(捨てられた牌)
            bool anyCall = false;
            if (callTiles != null)
                for (int k = 0; k < callTiles.Length; k++) { int t = table.PadCallTile(k); Tile(callTiles[k], t); if (t >= 0) anyCall = true; }
            SetActive(callLabel, anyCall);

            // 操作ボタン
            for (int i = 0; i < 6; i++) { slotAct[i] = ACT_NONE; slotArg[i] = 0; }
            // 候補を出している鳴きが、もうできなくなっていたら閉じる
            if (candGroup >= 0)
            {
                bool still = false;
                for (int k = 0; k < table.optN; k++) if (GroupOf(k) == candGroup) still = true;
                if (!still) candGroup = -1;
            }
            bool confirm = table.padConfirm;
            SetActive(confirmRoot, confirm);
            RefreshSetup(!live);
            int s = 0;
            if (!confirm)
            {
                if (!live) { }
                else if (candGroup >= 0)
                {
                    // 候補(1ページ 4 つ + 次へ + やめる)
                    int idx = 0, shown = 0, more = 0;
                    for (int k = 0; k < table.optN; k++)
                    {
                        if (GroupOf(k) != candGroup) continue;
                        if (idx >= candPage * 4 && shown < 4) { Slot(s++, ACT_CAND, k, table.OptionText(k * SqmEngine.OPT), BtnMat(candGroup)); shown++; }
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
                        if (has) Slot(s++, ACT_GROUP, g, GroupName(g), BtnMat(g));
                    }
                    if (table.padRiichiOk) Slot(s++, ACT_RIICHI, 0, table.riichiArmed ? "リーチ ON" : "リーチ", table.riichiArmed ? BTN_RIICHI_ON : BTN_RIICHI);
                    if (table.padDiscardOk) Slot(s++, ACT_DISCARD, 0, "確定", BTN_DISCARD);
                }
            }
            for (int i = 0; i < 6; i++) SetActive(btnRoots[i], slotAct[i] != ACT_NONE);
        }

        /// <summary>ゲーム前設定の表示: 選んでいるものは赤。平方のときは局数をグレーに。NEW GAME はホストだけ(ホストがいなければ、だれでも)</summary>
        private void RefreshSetup(bool show)
        {
            SetActive(setupRoot, show);
            if (!show || setupBacks == null) return;
            gameMessage.text = "";
            int mode = table.SetupMode(), rounds = table.SetupRounds();
            for (int i = 0; i < setupBacks.Length; i++)
            {
                bool on = i < 3 ? i == mode : (i == 3 ? rounds == 1 : rounds == 2);
                bool dim = i >= 3 && mode == SqmTable.MODE_SQUARE;
                if (i > 0 && i < 3 && !table.HasStdMode()) dim = true;
                Material m = dim ? (on ? matSetupDimOn : matSetupDim) : (on ? matSetupOn : matSetupOff);
                if (m != null && setupBacks[i].sharedMaterial != m) setupBacks[i].sharedMaterial = m;
            }
            SetActive(setupNew, table.IsHostMe() || table.NoHost());
        }

        /// <summary>手牌の位置: 門前は半分ずつ2段(26 枚なら 13 + 13)、ツモ牌は上の段のさらに上の右。普通の麻雀は1段</summary>
        private void PlaceHand(int mainN, int n)
        {
            const float STEP = 0.024f, Y1 = 0.004f, Y2 = -0.031f, YD = 0.04f;
            int row1 = table.StdLive() ? mainN : (mainN + 1) / 2;
            for (int i = 0; i < handTiles.Length; i++)
            {
                Vector3 p;
                if (i < mainN)
                {
                    int r = i < row1 ? 0 : 1, c = i < row1 ? i : i - row1;
                    int rowN = r == 0 ? row1 : mainN - row1;
                    p = new Vector3(-(rowN - 1) * STEP / 2 + c * STEP, r == 0 ? Y1 : Y2, 0);
                }
                else if (table.StdLive()) p = new Vector3((mainN - 1) * STEP / 2 + STEP + 0.006f + (i - mainN) * STEP, Y1, 0);
                else p = new Vector3(0.135f + (i - mainN) * STEP, YD, 0);
                // 板から数 mm 離す(近すぎると VR で奥行きの判定が揺れて、ちらついたり消えたりする)
                p.z = -0.003f;
                handTiles[i].transform.localPosition = p;
                handLift[i].transform.localPosition = p + new Vector3(0, 0, 0.0015f);
                if (handDim != null) handDim[i].transform.localPosition = p + new Vector3(0, 0, -0.0015f);
                if (handHint != null) handHint[i].transform.localPosition = p + new Vector3(0, 0.019f, -0.001f);
            }
        }

        private void Slot(int i, int act, int arg, string label, int mat)
        {
            if (i >= 6) return;
            slotAct[i] = act; slotArg[i] = arg;
            btnLabels[i].text = label;
            if (btnMats != null && mat < btnMats.Length && btnBacks[i].sharedMaterial != btnMats[mat]) btnBacks[i].sharedMaterial = btnMats[mat];
        }

        private void Tile(MeshRenderer r, int t)
        {
            bool show = t != -1;
            if (r.gameObject.activeSelf != show) r.gameObject.SetActive(show);
            if (!show) return;
            Material m = t >= 0 ? faceMats[t] : matTabOn;   // -2 = 伏せた牌
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }

        // 鳴き・和了の種類: 0 ツモ 1 片ロン 2 二枚ロン 3 ポン 4 チー 5 カン 6 十四種十四牌 7 続ける/止める 8 パス
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

        private int BtnMat(int g)
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
            if (graphToggleLabel != null) graphToggleLabel.text = showGraph ? (over ? "結果" : "表") : "グラフ";
            bool graph = showGraph && table.GraphReady();
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
                if (boxFrames != null && p < boxFrames.Length) SetActive(boxFrames[p], !std);
                SetActive(boxFills[p].gameObject, !std);
                SetActive(boxTexts[p].gameObject, used);
                Material f = p == 0 ? matBoxMe : matBox;
                if (std && p % 2 == 0 && trapFrames != null && p / 2 < trapFrames.Length)
                {
                    // 台形は4つとも出す。三人打ちの空いた席は、文字なしの灰色
                    SetActive(trapFrames[p / 2], true);
                    SetActive(trapFills[p / 2].gameObject, true);
                    Material tf = used ? f : (matBoxEmpty != null ? matBoxEmpty : matBox);
                    if (trapFills[p / 2].sharedMaterial != tf) trapFills[p / 2].sharedMaterial = tf;
                }
                if (!used) continue;
                if (boxFills[p].sharedMaterial != f) boxFills[p].sharedMaterial = f;
                boxTexts[p].text = live ? table.PadSeatBox(s) : table.SeatDisplayName(s);
            }
        }

        // ---- 折れ線グラフ ----
        private const float GX0 = -0.165f, GX1 = 0.08f, GY0 = -0.047f, GY1 = 0.058f, GZ = -0.004f;
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

        /// <summary>点数の推移を描く: 横は局(本場も1局。最初は開始の点数)、縦は点数。自分の線は濃く太く、ほかは彩度を落として細く</summary>
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
            float pad = span * 0.06f;
            float vlo = lo - pad, vhi = hi + pad;
            string legend = "";
            for (int l = 0; l < graphLines.Length; l++)
            {
                LineRenderer lr = graphLines[l];
                bool use = l < lines && n > 0;
                if (lr.gameObject.activeSelf != use) lr.gameObject.SetActive(use);
                if (!use) continue;
                int ts = table.GraphLineSeat(l);
                bool me = ts >= 0 && ts == table.humanSeat;
                Color c = lineColor[l];
                if (!me)
                {
                    float gray = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
                    c = Color.Lerp(c, new Color(gray, gray, gray, 1f), 0.55f);
                    c.a = 0.75f;
                }
                lr.startColor = c;
                lr.endColor = c;
                lr.widthMultiplier = me ? 0.0028f : 0.0013f;
                lr.positionCount = n < 2 ? 2 : n;
                for (int i = 0; i < n; i++) lr.SetPosition(i, GraphPoint(i, n, lg.At(i, l), vlo, vhi, me));
                if (n < 2) lr.SetPosition(1, GraphPoint(1, 2, lg.At(0, l), vlo, vhi, me));
                string name = ts >= 0 ? table.SeatDisplayName(ts) : "";
                if (me) name = "<b>" + name + "</b>";
                legend += "<color=#" + lineHex[l] + (me ? "" : "99") + ">━</color> " + name + "\n";
            }
            if (graphBase != null)
            {
                graphBase.positionCount = 2;
                graphBase.SetPosition(0, GraphPoint(0, 2, start, vlo, vhi, false) + new Vector3(0, 0, 0.0005f));
                graphBase.SetPosition(1, GraphPoint(1, 2, start, vlo, vhi, false) + new Vector3(0, 0, 0.0005f));
            }
            // 横軸の目盛り: 局の名前。目盛りの数より点が多ければ、等しい間隔で間引く
            if (graphTicks != null)
            {
                int slots = graphTicks.Length;
                int step = n <= slots ? 1 : (n + slots - 1) / slots;
                int k = 0;
                for (int i = 0; i < n && k < slots; i += step)
                {
                    TextMeshPro t = graphTicks[k++];
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                    t.text = table.GraphTick(lg.KyokuAt(i));
                    Vector3 p = GraphPoint(i, n, 0, 0f, 1f, false);
                    t.gameObject.transform.localPosition = new Vector3(p.x, GY0 - 0.0115f, -0.005f);
                }
                for (; k < slots; k++) if (graphTicks[k].gameObject.activeSelf) graphTicks[k].gameObject.SetActive(false);
            }
            if (graphTop != null) graphTop.text = hi.ToString("N0");
            if (graphBottom != null) graphBottom.text = lo.ToString("N0");
            if (graphLegend != null) graphLegend.text = legend;
        }

        private Vector3 GraphPoint(int i, int n, int v, float vlo, float vhi, bool front)
        {
            float x = n < 2 ? GX0 : GX0 + (GX1 - GX0) * i / (n - 1);
            float y = GY0 + (GY1 - GY0) * (v - vlo) / (vhi - vlo);
            return new Vector3(x, y, GZ - (front ? 0.0005f : 0));
        }

        // ---------------------------------------------------------------- ルール・設定
        private void RefreshList()
        {
            bool rule = tab == TAB_RULE;
            int n = rule ? table.RuleCount() : SettingCount();
            if (listTop > Mathf.Max(0, n - ROWS)) listTop = Mathf.Max(0, n - ROWS);
            bool edit = rule && table.CanEditRules();
            listTitle.text = rule ? (edit ? "ルール(ホストが変更できます)" : "ルール(開始前にホストが決めます)") : "設定";
            for (int r = 0; r < ROWS; r++)
            {
                int i = listTop + r;
                bool has = i < n;
                rowLabels[r].text = has ? (rule ? table.RuleName(i) : SettingName(i)) : "";
                rowValues[r].text = has ? (rule ? table.RuleOptLabel(i, table.RuleValue(i)) : SettingValue(i)) : "";
                bool arrows = has && (rule ? edit : SettingHasArrows(i));
                bool action = has && !rule && !SettingHasArrows(i) && !IsCreditRow(i);
                SetActive(rowLeft[r], arrows);
                SetActive(rowRight[r], arrows || action);
            }
        }

        private void PressRow(int r, int dir)
        {
            int i = listTop + r;
            if (tab == TAB_RULE) { table.RequestRule(i, dir); return; }
            if (i >= SettingCount() || IsCreditRow(i)) return;
            DoSetting(i, dir);
            Refresh();
        }

        // 設定の項目: 0 効果音 1 声 2 掴めるようにする 3 元の位置に戻す 4 席を立つ / ホストだけ 5 新しい試合 6 Reset 7〜 立たせる
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

        // 設定の並び: ふつうの項目(BaseCount 個) → デバッグ(debugEnabled のときだけ 5 個) → いちばん下は作者とバージョン(押せない)
        private int BaseCount() { return table.IsHostMe() ? 7 + KickCount() : 5; }
        private int DebugCount() { return table.debugEnabled ? 5 : 0; }
        private int SettingCount() { return BaseCount() + DebugCount() + 1; }
        private bool IsCreditRow(int i) { return i == SettingCount() - 1; }
        private int DebugRow(int i) { int d = i - BaseCount(); return d >= 0 && d < DebugCount() ? d : -1; }

        private string SettingName(int i)
        {
            if (IsCreditRow(i)) return "<size=80%>" + CREDIT + "</size>";
            int d = DebugRow(i);
            if (d == 0) return "<color=#a05000>[DEBUG]</color> 切るべき牌を表示";
            if (d == 1) return "<color=#a05000>[DEBUG]</color> 次の局で出す役";
            if (d == 2) return "<color=#a05000>[DEBUG]</color> 自分の席に仕込む";
            if (d == 3) return "<color=#a05000>[DEBUG]</color> NPC による自動プレイ";
            if (d == 4) return "<color=#a05000>[DEBUG]</color> NPC のプレイ速度(ホスト)";
            if (i == 0) return "効果音の音量";
            if (i == 1) return "声の音量";
            if (i == 2) return "パッドを掴めるようにする";
            if (i == 3) return "パッドを元の位置に戻す";
            if (i == 4) return "席を立つ";
            if (i == 5) return "新しい試合";
            if (i == 6) return "Reset(試合をやめる)";
            int s = KickSeat(i - 7);
            return s >= 0 ? "立たせる: " + table.SeatDisplayName(s) : "";
        }

        private bool SettingHasArrows(int i)
        {
            int d = DebugRow(i);
            if (d >= 0) return d <= 1 || d >= 3;
            return i <= 2;
        }

        private string SettingValue(int i)
        {
            if (IsCreditRow(i)) return "v" + VERSION;
            int d = DebugRow(i);
            if (d == 0) return debugHint ? "ON" : "OFF";
            if (d == 1) return engine.RigName(rigSel);
            if (d == 2) return table.CanRig() ? "実行 ▶" : "(ホストだけ)";
            if (d == 3) return table.debugAutoPlay ? "ON" : "OFF";
            if (d == 4) return "×" + table.debugSpeed;
            if (i == 0) return Bar(sfxStep);
            if (i == 1) return Bar(voiceStep);
            if (i == 2) return grabbable ? "ON" : "OFF";
            return "実行 ▶";
        }

        private string Bar(int step) { return step * 10 + "%"; }

        private void DoSetting(int i, int dir)
        {
            int d = DebugRow(i);
            if (d == 0) { debugHint = !debugHint; return; }
            if (d == 1) { rigSel = (rigSel + dir + SqmEngine.RIG_N) % SqmEngine.RIG_N; return; }
            if (d == 2) { table.RequestRig(rigSel); return; }
            if (d == 3) { table.debugAutoPlay = !table.debugAutoPlay; return; }
            if (d == 4)
            {
                // ×1 → ×2 → ×4 → ×8(◀ で逆)
                int sp = table.debugSpeed;
                if (dir > 0) sp = sp >= 8 ? 1 : sp * 2;
                else sp = sp <= 1 ? 8 : sp / 2;
                table.debugSpeed = sp;
                return;
            }
            if (i == 0) { sfxStep = Mathf.Clamp(sfxStep + dir, 0, 10); table.sfxVolume = sfxStep / 10f; }
            else if (i == 1) { voiceStep = Mathf.Clamp(voiceStep + dir, 0, 10); table.voiceVolume = voiceStep / 10f; }
            else if (i == 2) { grabbable = !grabbable; table.RefreshPads(); }
            else if (i == 3) { if (table.padBodies != null && table.humanSeat >= 0) table.padBodies[table.humanSeat].ReturnHome(); }
            else if (i == 4) table.Leave();
            else if (i == 5) table.NewMatch();
            else if (i == 6) table.ResetMatch();
            else { int s = KickSeat(i - 7); if (s >= 0) table.KickSeat(s); }
        }
    }
}
