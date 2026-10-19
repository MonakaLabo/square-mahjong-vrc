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
        public MeshRenderer[] callTiles;     // 鳴ける牌 2(捨てられた牌)
        public GameObject callLabel;

        // 点数
        public GameObject scoreRoot;
        public GameObject scoreBoxes;        // 8席の箱(試合中)
        public MeshRenderer[] boxFills;      // 8(マス p: 0 = 下中 … ホロと同じ並び)
        public TextMeshPro[] boxTexts;
        public TextMeshPro scoreCenter;
        public Material matBox, matBoxMe;
        public TextMeshPro resultTable;      // 終局の結果の表

        // ルール・設定(上下に送るリスト)
        public GameObject listRoot;
        public TextMeshPro listTitle;
        public TextMeshPro[] rowLabels, rowValues;   // 6
        public GameObject[] rowLeft, rowRight;       // ◀ ▶

        public const string VERSION = "0.3.4";
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
            bool over = table.IsLive() && engine.phase == SqmEngine.PH_MATCH_OVER;
            if (over && !wasOver) tab = TAB_SCORE;   // 終局したら点数タブへ
            wasOver = over;

            for (int i = 0; i < tabBacks.Length; i++)
            {
                Material m = i == tab ? matTabOn : matTabOff;
                if (tabBacks[i].sharedMaterial != m) tabBacks[i].sharedMaterial = m;
            }
            if (table.IsLive())
                inGameText.text = "東" + engine.kyoku + "局 " + engine.honba + "本場\n残 " + engine.LiveLeft().ToString("0000");
            else inGameText.text = "—";
            infoText.text = table.PadInfo();

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
            if (live)
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
            int s = 0;
            if (!confirm)
            {
                if (!live)
                {
                    if (table.IsHostMe() || table.NoHost()) Slot(s++, ACT_NEWGAME, 0, "NEW GAME", BTN_DISCARD);
                }
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
                    for (int g = 0; g < 9 && s < 4; g++)
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

        /// <summary>手牌の位置: 門前は半分ずつ2段(26 枚なら 13 + 13)、ツモ牌は上の段のさらに上の右</summary>
        private void PlaceHand(int mainN, int n)
        {
            const float STEP = 0.024f, Y1 = 0.004f, Y2 = -0.031f, YD = 0.04f;
            int row1 = (mainN + 1) / 2;
            for (int i = 0; i < handTiles.Length; i++)
            {
                Vector3 p;
                if (i < mainN)
                {
                    int r = i < row1 ? 0 : 1, c = i < row1 ? i : i - row1;
                    int rowN = r == 0 ? row1 : mainN - row1;
                    p = new Vector3(-(rowN - 1) * STEP / 2 + c * STEP, r == 0 ? Y1 : Y2, 0);
                }
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
            if (g == 1) return "片ロン";
            if (g == 2) return "二枚ロン";
            if (g == 3) return "ポン";
            if (g == 4) return "チー";
            if (g == 5) return "カン";
            if (g == 6) return "十四種十四牌";
            if (g == 7) return "続ける/止める";
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
            SetActive(scoreBoxes, !over);
            SetActive(resultTable.gameObject, over);
            if (over) { resultTable.text = table.PadResultTable(); return; }
            int me = table.humanSeat;
            bool live = table.IsLive();
            scoreCenter.text = live ? table.PadCenterText() : "";
            for (int p = 0; p < 8; p++)
            {
                int s = (me + p) % 8;
                Material f = p == 0 ? matBoxMe : matBox;
                if (boxFills[p].sharedMaterial != f) boxFills[p].sharedMaterial = f;
                boxTexts[p].text = live ? table.PadSeatBox(s) : table.SeatDisplayName(s);
            }
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

        // 設定の並び: ふつうの項目(BaseCount 個) → デバッグ(debugEnabled のときだけ 3 個) → いちばん下は作者とバージョン(押せない)
        private int BaseCount() { return table.IsHostMe() ? 7 + KickCount() : 5; }
        private int DebugCount() { return table.debugEnabled ? 3 : 0; }
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
            if (d >= 0) return d <= 1;
            return i <= 2;
        }

        private string SettingValue(int i)
        {
            if (IsCreditRow(i)) return "v" + VERSION;
            int d = DebugRow(i);
            if (d == 0) return debugHint ? "ON" : "OFF";
            if (d == 1) return engine.RigName(rigSel);
            if (d == 2) return table.CanRig() ? "実行 ▶" : "(ホストだけ)";
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
