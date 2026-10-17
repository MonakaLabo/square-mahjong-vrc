using System.IO;
using TMPro;
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace MonakaLabo.SquareMahjong.EditorTools
{
    /// <summary>
    /// メニュー「Tools/平方麻雀/卓をシーンに置く」: 卓・牌・文字・ボタンを作って U# の部品をつなぎ、
    /// 開いているシーンに置いてプレハブ(Runtime/Prefabs/SquareMahjongTable.prefab)にも保存する。
    /// </summary>
    public static class SqmBuilder
    {
        const string PKG = "Packages/jp.monakalabo.square-mahjong";
        const string GEN = PKG + "/Runtime/Generated";
        const string PREFAB = PKG + "/Runtime/Prefabs/SquareMahjongTable.prefab";
        const float TOP = SqmTable.TOP, TW = SqmTable.TW, TH = SqmTable.TH, TT = SqmTable.TT;

        static Material matBody, matBack, matFelt, matRim, matButton, matButtonHot, matSegBox;
        static Material matHoloPanel, matHoloLine, matHoloRed, matHoloYellow, matHoloFill, matHoloTurn, matHoloSegBox;
        static Material[] matHoloFaces;
        static Material[] matFaces;
        static TMP_FontAsset font, segFont, holoFont;

        [MenuItem("Tools/平方麻雀/卓をシーンに置く")]
        public static void Build()
        {
            // U# のスクリプトには対のプログラムアセット(.asset)が要る。見つからないときは何も作らずに止める
            foreach (var type in new[] { typeof(SqmEngine), typeof(SqmNpc), typeof(SqmTable), typeof(SqmArchive), typeof(SqmTileClick), typeof(SqmButton), typeof(SqmPad), typeof(SqmPadKey), typeof(SqmPadBody) })
            {
                if (UdonSharpEditorUtility.GetUdonSharpProgramAsset(type) == null)
                {
                    EditorUtility.DisplayDialog("平方麻雀", type.Name + " の U# プログラムアセットが見つかりません。\nU# のコンパイルが終わってから、もう一度実行してください。", "OK");
                    return;
                }
            }
            MakeAssets();
            FindFont();
            if (holoFont == null) holoFont = font;

            var root = new GameObject("SquareMahjongTable");
            Undo.RegisterCreatedObjectUndo(root, "平方麻雀の卓");
            // 同期する SqmTable / SqmArchive と、同期しないエンジン・NPC は、別の GameObject に置く(同期の方式が混ざらないように)
            var table = UdonSharpUndo.AddComponent<SqmTable>(root);
            var logic = Group(root, "Logic");
            var engine = UdonSharpUndo.AddComponent<SqmEngine>(logic);
            var npc = UdonSharpUndo.AddComponent<SqmNpc>(logic);
            var archive = UdonSharpUndo.AddComponent<SqmArchive>(Group(root, "Archive"));
            npc.engine = engine;
            table.engine = engine;
            table.npc = npc;
            table.archive = archive;
            archive.table = table;
            table.faceMats = matFaces;

            // ---- 卓 ----
            var top = new GameObject("TableTop");
            top.transform.SetParent(root.transform, false);
            top.AddComponent<MeshFilter>().sharedMesh = OctagonMesh();
            top.AddComponent<MeshRenderer>().sharedMaterials = new[] { matFelt, matRim };

            // ---- 壁(幢 525) ----
            var wallRoot = Group(root, "Wall");
            table.wallStacks = new Transform[525];
            for (int i = 0; i < 525; i++) table.wallStacks[i] = MakeStack(wallRoot, "stack_" + i.ToString("000")).transform;

            // ---- 手牌・河・副露・中央 ----
            table.handTiles = new Transform[8 * SqmTable.SLOT_HAND];
            table.handFaces = new MeshRenderer[8 * SqmTable.SLOT_HAND];
            table.handClicks = new SqmTileClick[8 * SqmTable.SLOT_HAND];
            var hands = Group(root, "Hands");
            for (int i = 0; i < table.handTiles.Length; i++)
            {
                var t = MakeTile(hands, "hand_" + (i / SqmTable.SLOT_HAND) + "_" + (i % SqmTable.SLOT_HAND).ToString("00"), out MeshRenderer face);
                var box = t.AddComponent<BoxCollider>();
                box.size = new Vector3(TW, TH, TT * 1.5f);
                box.isTrigger = true;   // 人はすり抜ける(Interact はできる)
                var click = UdonSharpUndo.AddComponent<SqmTileClick>(t);
                click.table = table;
                click.index = i;
                table.handClicks[i] = click;
                table.handTiles[i] = t.transform;
                table.handFaces[i] = face;
            }
            table.riverTiles = MakeTiles(root, "Rivers", "river_", 8 * SqmTable.SLOT_RIVER, out table.riverFaces);
            table.meldTiles = MakeTiles(root, "Melds", "meld_", 8 * SqmTable.SLOT_MELD, out table.meldFaces);
            table.centerTiles = MakeTiles(root, "Center", "center_", SqmTable.N_DORA + SqmTable.N_RON, out table.centerFaces);

            // ---- 文字 ----
            BuildHolo(root, table);
            BuildAudio(root, table);
            // 席ごとに、卓の縁へ寝かせて並べる(その席から読める向き): 左から 席風・名前・点数(7セグ)
            table.seatLabels = new TextMeshPro[8];
            table.windLabels = new TextMeshPro[8];
            table.scoreLabels = new TextMeshPro[8];
            var labels = Group(root, "SeatLabels");
            for (int s = 0; s < 8; s++)
            {
                Vector3 u = U(s), r = R(s);
                Vector3 at = u * SqmTable.R_LABEL + Vector3.up * (TOP + 0.002f);
                Quaternion flat = Quaternion.LookRotation(Vector3.down, -u);
                var seat = Group(labels, "seat_" + s);
                table.windLabels[s] = Text(seat, "wind", at - r * 0.34f, flat, 0.45f, new Vector2(0.07f, 0.07f));
                table.seatLabels[s] = Text(seat, "name", at - r * 0.06f, flat, 0.24f, new Vector2(0.44f, 0.06f));
                table.seatLabels[s].enableWordWrapping = false;
                table.seatLabels[s].overflowMode = TextOverflowModes.Ellipsis;
                table.scoreLabels[s] = SegDisplay(seat, at + r * 0.27f, u);
            }

            // ---- 卓の中央(王牌部)の Resync・Reset(牌と同じ大きさ) ----
            var center = Group(root, "CenterButtons");
            // 寝かせて、文字を上に向ける(0番の席から読める向き)
            var faceUp = Quaternion.LookRotation(Vector3.down, -U(0));
            Button(center, "resync", "Resync", table, "Resync", new Vector3(-0.016f, TOP + TT / 2, 0), faceUp, new Vector3(TW, TH, TT), out _);
            Button(center, "reset", "Reset", table, "ResetMatch", new Vector3(0.016f, TOP + TT / 2, 0), faceUp, new Vector3(TW, TH, TT), out _);

            // ---- 卓の上の「確定」(打牌の確定。自分の番だけ、自分の席に出る) ----
            table.discardButtons = new GameObject[8];
            var discards = Group(root, "DiscardButtons");
            for (int s = 0; s < 8; s++)
            {
                Vector3 u = U(s), r = R(s);
                table.discardButtons[s] = Button(discards, "discard_" + s, "確定", table, "Discard",
                    u * 1.11f + r * 0.43f + Vector3.up * (TOP + 0.008f), Quaternion.LookRotation(Vector3.down, -u), new Vector3(0.07f, 0.045f, 0.016f), out _);
                table.discardButtons[s].SetActive(false);
            }

            // ---- 手元のパッド(席ごとの板 + 中身1組)。席に着く・試合を始める・打つ・鳴く・確認はパッドで ----
            BuildPads(root, table);

            // U# の部品に入れた値を、裏の UdonBehaviour に写す
            foreach (var b in root.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); }
            Directory.CreateDirectory(Path.GetDirectoryName(PREFAB));
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PREFAB, InteractionMode.UserAction);
            Selection.activeGameObject = root;
            Debug.Log("[平方麻雀] 卓を置きました: " + PREFAB + (font == null ? "(日本語のフォントが見つからないので、文字が □ になるかもしれません)" : "(フォント: " + font.name + ")")
                + (segFont == null ? "(7セグのフォント DSEG7 が見つからないので、点数はふつうの文字です)" : "(7セグ: " + segFont.name + ")"));
        }

        // ================================================================ 手元のパッド
        // 本体(板)は席ごとに8枚。卓の右寄りに少し傾けて付ける。中身(SqmPad)は1組で、座った人の手元で自分の席の板に付ける。
        // 板の中の座標(m): 原点が板の中央、x が右、y が上。読む人は -Z 側から見る。板の内側は 0.40 × 0.25
        const float PW = 0.42f, PH = 0.27f;
        static readonly Color PAD_TEXT = new Color(0.24f, 0.33f, 0.38f);
        static Material matPadPanel, matPadBg, matPadFrame, matPadLine, matPadTabOn, matPadLift;
        static Material[] matPadBtn;

        static void PadAssets()
        {
            matPadPanel = HoloMat("PadPanel", new Color(0.886f, 0.973f, 0.988f, 1f), Q_BACK);
            matPadBg = HoloMat("PadBg", new Color(0.886f, 0.973f, 0.988f, 1f), Q_FILL);
            matPadFrame = Mat("PadFrame", new Color(0.49f, 0.51f, 0.51f), null);
            matPadLine = HoloMat("PadLine", new Color(0.29f, 0.4f, 0.44f, 1f));
            matPadTabOn = HoloMat("PadTabOn", new Color(0.54f, 0.77f, 0.84f, 1f), Q_FILL);
            matPadLift = HoloMat("PadLift", new Color(0.96f, 0.72f, 0.24f, 1f));
            // SqmPad.BTN_* の順: 確定・リーチ・リーチON・カン・ポン・チー・和了・パス・その他
            Color[] c = {
                new Color(0.71f, 0.9f, 0.95f), new Color(0.97f, 0.79f, 0.56f), new Color(1f, 0.62f, 0.25f), new Color(0.97f, 0.54f, 0.82f),
                new Color(0.62f, 0.89f, 0.64f), new Color(0.66f, 0.78f, 1f), new Color(1f, 0.54f, 0.5f), new Color(0.82f, 0.85f, 0.86f), new Color(0.88f, 0.78f, 1f) };
            matPadBtn = new Material[c.Length];
            for (int i = 0; i < c.Length; i++) matPadBtn[i] = HoloMat("PadBtn_" + i, new Color(c[i].r, c[i].g, c[i].b, 1f), Q_FILL);
        }

        static void BuildPads(GameObject root, SqmTable table)
        {
            PadAssets();
            var pads = Group(root, "Pads");
            table.padBodies = new SqmPadBody[8];
            for (int s = 0; s < 8; s++) table.padBodies[s] = PadBody(pads, table, s);

            // 中身(1組)。SqmPad は、いつも動いている別の GameObject に置く
            var logic = Group(pads, "PadLogic");
            var pad = UdonSharpUndo.AddComponent<SqmPad>(logic);
            table.pad = pad;
            pad.table = table;
            pad.engine = table.engine;
            pad.faceMats = matHoloFaces;
            pad.matTabOn = matPadTabOn; pad.matTabOff = matPadBg;
            pad.matBox = matPadBg; pad.matBoxMe = matPadTabOn;
            pad.btnMats = matPadBtn;

            var content = Group(table.padBodies[0].contentAnchor.gameObject, "PadContent");
            pad.content = content;

            // ---- 上の帯: タブ ----
            string[] tabs = { "対局", "点数", "ルール", "設定" };
            pad.tabBacks = new MeshRenderer[4];
            for (int i = 0; i < 4; i++)
            {
                var g = PadBtn(content, "tab_" + i, tabs[i], matPadBg, new Vector2(-0.147f + i * 0.098f, 0.104f), new Vector2(0.096f, 0.034f), 0.12f, out _, out pad.tabBacks[i]);
                PadKey(g, pad, 100 + i);
            }
            PadLine(content, new Vector2(0, 0.0865f), new Vector2(0.4f, 0.0015f));
            // ---- 下の帯: IN GAME と INFORMATION ----
            // 小さい見出し(IN GAME / INFORMATION)と本文は、別々の文字にする(1つにすると行が重なる)
            PadLine(content, new Vector2(0, -0.0815f), new Vector2(0.4f, 0.0015f));
            PadLine(content, new Vector2(-0.11f, -0.103f), new Vector2(0.0015f, 0.043f));
            PText(content, "InGameLabel", new Vector2(-0.155f, -0.088f), 0.03f, new Vector2(0.088f, 0.01f), TextAlignmentOptions.Center).text = "IN GAME";
            var infoLabel = PText(content, "InfoLabel", new Vector2(0.045f, -0.088f), 0.03f, new Vector2(0.3f, 0.01f), TextAlignmentOptions.Center);
            infoLabel.text = "<cspace=0.8em>INFORMATION</cspace>";
            pad.inGameText = PText(content, "InGame", new Vector2(-0.155f, -0.107f), 0.055f, new Vector2(0.088f, 0.03f), TextAlignmentOptions.Center);
            pad.infoText = PText(content, "Info", new Vector2(0.045f, -0.106f), 0.08f, new Vector2(0.3f, 0.028f), TextAlignmentOptions.Center);

            // ---- 対局 ----
            var game = Group(content, "Game");
            pad.gameRoot = game;
            pad.gameMessage = PText(game, "Message", new Vector2(0, 0.07f), 0.075f, new Vector2(0.38f, 0.026f), TextAlignmentOptions.Center);
            pad.handTiles = new MeshRenderer[30];
            pad.handLift = new GameObject[30];
            pad.handDim = new GameObject[30];
            var dimMat = HoloMat("PadDim", new Color(0.1f, 0.15f, 0.2f, 0.55f), 2999);
            for (int i = 0; i < 30; i++)
            {
                var lift = Prim(PrimitiveType.Quad, game, "lift_" + i.ToString("00"), matPadLift);
                lift.transform.localScale = new Vector3(0.025f, 0.034f, 1);
                lift.SetActive(false);
                pad.handLift[i] = lift;
                var t = Prim(PrimitiveType.Quad, game, "tile_" + i.ToString("00"), matHoloFaces[0]);
                t.transform.localPosition = new Vector3(0, 0, -0.0005f);
                t.transform.localScale = new Vector3(0.022f, 0.03f, 1);
                var box = t.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = new Vector3(0, 0, -0.003f);
                box.size = new Vector3(1, 1, 0.008f);
                PadKey(t, pad, i);
                t.SetActive(false);
                var dim = Prim(PrimitiveType.Quad, game, "dim_" + i.ToString("00"), dimMat);
                dim.transform.localScale = new Vector3(0.022f, 0.03f, 1);
                dim.SetActive(false);
                pad.handDim[i] = dim;
                pad.handTiles[i] = t.GetComponent<MeshRenderer>();
            }
            pad.meldTiles = new MeshRenderer[24];
            for (int i = 0; i < 24; i++)
            {
                var t = Prim(PrimitiveType.Quad, game, "meld_" + i.ToString("00"), matHoloFaces[0]);
                t.transform.localPosition = new Vector3(-0.185f + i * 0.0125f, 0.04f, -0.0005f);
                t.transform.localScale = new Vector3(0.012f, 0.016f, 1);
                t.SetActive(false);
                pad.meldTiles[i] = t.GetComponent<MeshRenderer>();
            }
            pad.btnRoots = new GameObject[6];
            pad.btnBacks = new MeshRenderer[6];
            pad.btnLabels = new TextMeshPro[6];
            for (int i = 0; i < 6; i++)
            {
                var g = PadBtn(game, "btn_" + i, "", matPadBtn[0], new Vector2(-0.165f + i * 0.066f, -0.064f), new Vector2(0.06f, 0.024f), 0.075f, out pad.btnLabels[i], out pad.btnBacks[i]);
                g.SetActive(false);
                PadKey(g, pad, 200 + i);
                pad.btnRoots[i] = g;
            }
            var confirm = PadBtn(game, "confirm", "確認", matPadBtn[0], new Vector2(0, -0.064f), new Vector2(0.2f, 0.028f), 0.11f, out _, out _);
            PadKey(confirm, pad, 210);
            confirm.SetActive(false);
            pad.confirmRoot = confirm;

            // ---- 点数 ----
            var score = Group(content, "Score");
            pad.scoreRoot = score;
            var boxes = Group(score, "Boxes");
            pad.scoreBoxes = boxes;
            int[] col = { 0, 1, 1, 1, 0, -1, -1, -1 };
            int[] row = { -1, -1, 0, 1, 1, 1, 0, -1 };
            pad.boxFills = new MeshRenderer[8];
            pad.boxTexts = new TextMeshPro[8];
            for (int p = 0; p < 8; p++)
            {
                var at = new Vector2(col[p] * 0.127f, 0.002f + row[p] * 0.056f);
                var frame = Prim(PrimitiveType.Quad, boxes, "frame_" + p, matPadLine);
                frame.transform.localPosition = new Vector3(at.x, at.y, 0.0006f);
                frame.transform.localScale = new Vector3(0.12f, 0.05f, 1);
                var fill = Prim(PrimitiveType.Quad, boxes, "fill_" + p, matPadBg);
                fill.transform.localPosition = new Vector3(at.x, at.y, 0.0003f);
                fill.transform.localScale = new Vector3(0.117f, 0.047f, 1);
                pad.boxFills[p] = fill.GetComponent<MeshRenderer>();
                pad.boxTexts[p] = PText(boxes, "text_" + p, at, 0.055f, new Vector2(0.11f, 0.044f), TextAlignmentOptions.TopLeft);
            }
            pad.scoreCenter = PText(boxes, "Center", new Vector2(0, 0.002f), 0.065f, new Vector2(0.12f, 0.05f), TextAlignmentOptions.Center);
            pad.resultTable = PText(score, "Result", new Vector2(0, 0.002f), 0.07f, new Vector2(0.37f, 0.165f), TextAlignmentOptions.TopLeft);
            pad.resultTable.gameObject.SetActive(false);

            // ---- ルール・設定(上下に送るリスト) ----
            var list = Group(content, "List");
            pad.listRoot = list;
            pad.listTitle = PText(list, "Title", new Vector2(0, 0.072f), 0.07f, new Vector2(0.38f, 0.024f), TextAlignmentOptions.Center);
            pad.rowLabels = new TextMeshPro[SqmPad.ROWS];
            pad.rowValues = new TextMeshPro[SqmPad.ROWS];
            pad.rowLeft = new GameObject[SqmPad.ROWS];
            pad.rowRight = new GameObject[SqmPad.ROWS];
            for (int r = 0; r < SqmPad.ROWS; r++)
            {
                float y = 0.048f - r * 0.024f;
                pad.rowLabels[r] = PText(list, "label_" + r, new Vector2(-0.085f, y), 0.068f, new Vector2(0.2f, 0.022f), TextAlignmentOptions.Left);
                pad.rowLabels[r].enableWordWrapping = false;
                pad.rowValues[r] = PText(list, "value_" + r, new Vector2(0.085f, y), 0.062f, new Vector2(0.095f, 0.022f), TextAlignmentOptions.Center);
                pad.rowValues[r].enableWordWrapping = false;
                pad.rowLeft[r] = PadBtn(list, "left_" + r, "◀", matPadBtn[7], new Vector2(0.03f, y), new Vector2(0.018f, 0.018f), 0.06f, out _, out _);
                pad.rowRight[r] = PadBtn(list, "right_" + r, "▶", matPadBtn[7], new Vector2(0.142f, y), new Vector2(0.018f, 0.018f), 0.06f, out _, out _);
                PadKey(pad.rowLeft[r], pad, 300 + r * 2);
                PadKey(pad.rowRight[r], pad, 301 + r * 2);
            }
            PadKey(PadBtn(list, "up", "▲", matPadBtn[0], new Vector2(0.183f, 0.048f), new Vector2(0.022f, 0.02f), 0.06f, out _, out _), pad, 320);
            PadKey(PadBtn(list, "down", "▼", matPadBtn[0], new Vector2(0.183f, -0.072f), new Vector2(0.022f, 0.02f), 0.06f, out _, out _), pad, 321);
            list.SetActive(false);
            score.SetActive(false);
            content.SetActive(false);
        }

        /// <summary>席 s のパッドの本体(板・掴む・位置の同期・待機中の JOIN / NEW GAME)</summary>
        static SqmPadBody PadBody(GameObject parent, SqmTable table, int s)
        {
            Vector3 u = U(s), r = R(s);
            var g = new GameObject("Pad_" + s);
            g.transform.SetParent(parent.transform, false);
            // 手前(座る人)へ向けて 45° 傾け、板の上辺を卓の縁(天板の角)に接するように置く
            Vector3 padUp = (-u + Vector3.up).normalized;
            Vector3 edge = u * 1.18f + Vector3.up * TOP;
            g.transform.localPosition = edge - padUp * (PH / 2) + r * 0.3f;
            g.transform.localRotation = Quaternion.LookRotation((-u + Vector3.down).normalized, padUp);
            // 掴む判定は板の裏側に薄く置く(ボタンの判定より奥にして、ボタンを押すじゃまをしない)
            var col = g.AddComponent<BoxCollider>();
            col.center = new Vector3(0, 0, 0.01f);
            col.size = new Vector3(PW, PH, 0.004f);
            col.isTrigger = true;
            var rb = g.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var pickup = g.AddComponent<VRC.SDK3.Components.VRCPickup>();
            pickup.pickupable = false;
            pickup.AutoHold = VRC.SDKBase.VRC_Pickup.AutoHoldMode.Yes;
            pickup.InteractionText = "パッド";
            g.AddComponent<VRC.SDK3.Components.VRCObjectSync>();
            var body = UdonSharpUndo.AddComponent<SqmPadBody>(g);
            body.table = table;
            body.seat = s;
            body.pickup = pickup;

            var frame = Prim(PrimitiveType.Cube, g, "Frame", matPadFrame);
            frame.transform.localPosition = new Vector3(0, 0, 0.007f);
            frame.transform.localScale = new Vector3(PW, PH, 0.012f);
            var panel = Prim(PrimitiveType.Quad, g, "Panel", matPadPanel);
            panel.transform.localPosition = new Vector3(0, 0, -0.0002f);
            panel.transform.localScale = new Vector3(PW - 0.02f, PH - 0.02f, 1);
            var anchor = Group(g, "Anchor");
            anchor.transform.localPosition = new Vector3(0, 0, -0.0012f);
            body.contentAnchor = anchor.transform;

            // 待機中: 平方麻雀・JOIN・NEW GAME
            var idle = Group(g, "Idle");
            body.idleRoot = idle;
            PText(idle, "Title", new Vector2(0, 0.075f), 0.2f, new Vector2(0.38f, 0.05f), TextAlignmentOptions.Center).text = "平方麻雀";
            body.joinButton = PadBtn(idle, "join", "JOIN", matPadBtn[0], new Vector2(0, 0.0f), new Vector2(0.16f, 0.045f), 0.16f, out _, out _);
            TableKey(body.joinButton, table, "Join" + s);
            body.newButton = PadBtn(idle, "new_game", "NEW GAME", matPadBtn[0], new Vector2(0, -0.07f), new Vector2(0.16f, 0.045f), 0.14f, out _, out _);
            TableKey(body.newButton, table, "NewMatch");
            body.nameText = PText(g, "Name", Vector2.zero, 0.14f, new Vector2(0.38f, 0.05f), TextAlignmentOptions.Center);
            body.visuals = new[] { frame, panel, anchor, idle, body.nameText.gameObject };
            body.body = col;
            return body;
        }

        static TextMeshPro PText(GameObject parent, string name, Vector2 at, float size, Vector2 rect, TextAlignmentOptions align)
        {
            var t = Text(parent, name, new Vector3(at.x, at.y, -0.0006f), Quaternion.identity, size, rect);
            t.color = PAD_TEXT;
            t.alignment = align;
            return t;
        }

        static void PadLine(GameObject parent, Vector2 at, Vector2 size)
        {
            var l = Prim(PrimitiveType.Quad, parent, "Line", matPadLine);
            l.transform.localPosition = new Vector3(at.x, at.y, -0.0003f);
            l.transform.localScale = new Vector3(size.x, size.y, 1);
        }

        /// <summary>パッドのボタン(色の板 + 文字 + 押せる所)。押したときの動きは PadKey / TableKey で付ける</summary>
        static GameObject PadBtn(GameObject parent, string name, string label, Material mat, Vector2 at, Vector2 size, float fontSize, out TextMeshPro text, out MeshRenderer back)
        {
            var g = Group(parent, name);
            g.transform.localPosition = new Vector3(at.x, at.y, 0);
            var b = Prim(PrimitiveType.Quad, g, "Back", mat);
            b.transform.localPosition = new Vector3(0, 0, -0.0003f);
            b.transform.localScale = new Vector3(size.x, size.y, 1);
            back = b.GetComponent<MeshRenderer>();
            var edge = Prim(PrimitiveType.Quad, g, "Edge", matPadLine);
            edge.transform.localScale = new Vector3(size.x + 0.0015f, size.y + 0.0015f, 1);
            text = PText(g, "Label", Vector2.zero, fontSize, size, TextAlignmentOptions.Center);
            text.transform.localPosition = new Vector3(0, 0, -0.0008f);
            text.enableWordWrapping = false;
            text.text = label;
            // 押せる所は四角全体(文字より手前に、少し厚めに)
            var box = g.AddComponent<BoxCollider>();
            box.center = new Vector3(0, 0, -0.003f);
            box.size = new Vector3(size.x, size.y, 0.008f);
            box.isTrigger = true;
            return g;
        }

        static void PadKey(GameObject g, SqmPad pad, int code)
        {
            var k = UdonSharpUndo.AddComponent<SqmPadKey>(g);
            k.pad = pad;
            k.code = code;
        }

        static void TableKey(GameObject g, SqmTable table, string eventName)
        {
            var b = UdonSharpUndo.AddComponent<SqmButton>(g);
            b.table = table;
            b.eventName = eventName;
        }

        // ================================================================ 上空のホロ
        // 半透明の青白の板に、太めの明朝。数字は赤い7セグ。見る人の方へ向く(SqmTable.BillboardHolo)。
        // 板の中の座標(m): 原点が板の中央、x が右、y が上。読む人は -Z 側から見る
        const float HW = 2.3f, HH = 0.86f;
        static readonly Color HOLO_TEXT = new Color(0.88f, 0.96f, 1f);

        static void BuildHolo(GameObject root, SqmTable table)
        {
            var holo = Group(root, "Hologram");
            holo.transform.localPosition = new Vector3(0, TOP + 1.05f, 0);
            table.holoRoot = holo.transform;
            table.holoMatLine = matHoloLine; table.holoMatRed = matHoloRed; table.holoMatYellow = matHoloYellow;
            table.holoMatFill = matHoloFill; table.holoMatTurn = matHoloTurn;
            table.holoFaceMats = matHoloFaces;

            Panel(holo, "Panel", Vector2.zero, new Vector2(HW, HH));
            table.holoTitle = HText(holo, "Title", new Vector2(0, 0.02f), 0.7f, new Vector2(HW - 0.2f, HH - 0.2f), TextAlignmentOptions.Center);
            table.holoTitle.text = "平方麻雀²";
            // お知らせは板の上に出す
            table.holo = HText(holo, "Message", new Vector2(0, HH / 2 + 0.05f), 0.32f, new Vector2(HW - 0.1f, 0.08f), TextAlignmentOptions.Center);

            var play = Group(holo, "Play");
            table.holoPlay = play;

            // ---- 左: 局・残り枚数(円のゲージ)・点棒 ----
            var c = new Vector2(-0.8f, 0f);
            var ring = Prim(PrimitiveType.Quad, play, "Ring", matHoloLine);
            ring.GetComponent<MeshFilter>().sharedMesh = RingMesh();
            ring.transform.localPosition = new Vector3(c.x, c.y, 0);
            const int GAUGE = 72;
            table.holoGauge = new GameObject[GAUGE];
            var gauge = Group(play, "Gauge");
            for (int i = 0; i < GAUGE; i++)
            {
                float a = (90f - i * 360f / GAUGE) * Mathf.Deg2Rad;
                var seg = Prim(PrimitiveType.Quad, gauge, "seg_" + i.ToString("00"), matHoloRed);
                seg.transform.localPosition = new Vector3(c.x + Mathf.Cos(a) * 0.3f, c.y + Mathf.Sin(a) * 0.3f, -0.001f);
                seg.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg - 90f);
                seg.transform.localScale = new Vector3(0.018f, 0.016f, 1);
                table.holoGauge[i] = seg;
            }
            table.holoRound = HText(play, "Round", c + new Vector2(0, 0.135f), 1.0f, new Vector2(0.5f, 0.13f), TextAlignmentOptions.Center);
            HText(play, "LeftLabel", c + new Vector2(-0.15f, 0.015f), 0.62f, new Vector2(0.1f, 0.1f), TextAlignmentOptions.Center).text = "残";
            table.holoLeft = SegText(play, "Left", c + new Vector2(0.065f, 0.015f), 0.66f, new Vector2(0.3f, 0.1f), false);
            // 点棒(上: 供託の千点棒、下: 本場の百点棒)と本数
            string[] stickTex = { "stick_1000", "stick_100" };
            for (int i = 0; i < 2; i++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PKG + "/Runtime/Textures/Sticks/" + stickTex[i] + ".png");
                var st = Prim(PrimitiveType.Quad, play, "Stick_" + stickTex[i], ShaderMat("Holo_" + stickTex[i], "Unlit/Transparent", Color.white, tex, Q_PART));
                st.transform.localPosition = new Vector3(c.x - 0.055f, c.y - 0.1f - i * 0.07f, 0);
                st.transform.localScale = new Vector3(0.18f, 0.049f, 1);
            }
            table.holoSticks = HText(play, "Sticks", c + new Vector2(0.115f, -0.135f), 0.48f, new Vector2(0.13f, 0.15f), TextAlignmentOptions.Left);
            table.holoSticks.lineSpacing = -10f;

            // ---- 中: ドラ表示牌・ロン牌・時間 ----
            HText(play, "DoraLabel", new Vector2(-0.31f, 0.3f), 0.46f, new Vector2(0.3f, 0.08f), TextAlignmentOptions.Left).text = "ドラ表示牌";
            HText(play, "RonLabel", new Vector2(-0.31f, 0.05f), 0.46f, new Vector2(0.3f, 0.08f), TextAlignmentOptions.Left).text = "ロン牌";
            table.holoDora = HoloTiles(play, "Dora", SqmTable.N_DORA, 0.3f);
            table.holoRon = HoloTiles(play, "Ron", SqmTable.N_RON, 0.05f);
            HText(play, "SetLabel", new Vector2(-0.3f, -0.21f), 0.44f, new Vector2(0.2f, 0.08f), TextAlignmentOptions.Right).text = "セット";
            HText(play, "KyokuLabel", new Vector2(-0.3f, -0.31f), 0.44f, new Vector2(0.2f, 0.08f), TextAlignmentOptions.Right).text = "現局";
            table.holoSetTime = SegText(play, "SetTime", new Vector2(0.0f, -0.21f), 0.44f, new Vector2(0.34f, 0.075f), true);
            table.holoKyokuTime = SegText(play, "KyokuTime", new Vector2(0.0f, -0.31f), 0.44f, new Vector2(0.34f, 0.075f), true);

            // ---- 右: 8席の見取り図 ----
            var g = new Vector2(0.72f, -0.01f);
            const float CELL = 0.125f, PITCH = 0.14f;
            int[] col = { 0, 1, 1, 1, 0, -1, -1, -1 };
            int[] row = { -1, -1, 0, 1, 1, 1, 0, -1 };
            table.holoCellFrame = new MeshRenderer[8];
            table.holoCellFill = new MeshRenderer[8];
            table.holoCellWind = new TextMeshPro[8];
            table.holoCellName = new TextMeshPro[8];
            table.holoCellScore = new TextMeshPro[8];
            var map = Group(play, "Map");
            table.holoCenter = HText(map, "Center", g, 0.44f, new Vector2(0.125f, 0.08f), TextAlignmentOptions.Center);
            table.holoCenter.color = new Color(1f, 0.82f, 0.44f);
            for (int p = 0; p < 8; p++)
            {
                var at = g + new Vector2(col[p] * PITCH, row[p] * PITCH);
                var cell = Group(map, "cell_" + p);
                var frame = Prim(PrimitiveType.Quad, cell, "Frame", matHoloLine);
                frame.transform.localPosition = new Vector3(at.x, at.y, 0.002f);
                frame.transform.localScale = new Vector3(CELL, CELL, 1);
                var fill = Prim(PrimitiveType.Quad, cell, "Fill", matHoloFill);
                fill.transform.localPosition = new Vector3(at.x, at.y, 0.001f);
                fill.transform.localScale = new Vector3(CELL - 0.008f, CELL - 0.008f, 1);
                table.holoCellFrame[p] = frame.GetComponent<MeshRenderer>();
                table.holoCellFill[p] = fill.GetComponent<MeshRenderer>();
                table.holoCellWind[p] = HText(cell, "Wind", at, 0.8f, new Vector2(CELL, CELL), TextAlignmentOptions.Center);
                // 名前と点数は2行。対面(上)は 名前→点数、自分(下)は 点数→名前、左右の列は 名前→点数
                Vector2 nPos, sPos;
                // マスの外側(マスの端 = 中心から PITCH + CELL/2)に、点数の箱が重ならないように
                if (col[p] == 0 && row[p] > 0) { nPos = new Vector2(g.x, g.y + 0.3f); sPos = new Vector2(g.x, g.y + 0.24f); }
                else if (col[p] == 0) { sPos = new Vector2(g.x, g.y - 0.24f); nPos = new Vector2(g.x, g.y - 0.3f); }
                else
                {
                    float x = g.x + col[p] * 0.305f;
                    nPos = new Vector2(x, at.y + 0.03f); sPos = new Vector2(x, at.y - 0.028f);
                }
                table.holoCellName[p] = HText(cell, "Name", nPos, 0.3f, new Vector2(0.22f, 0.055f), TextAlignmentOptions.Center);
                table.holoCellName[p].enableWordWrapping = false;
                table.holoCellName[p].overflowMode = TextOverflowModes.Ellipsis;
                table.holoCellScore[p] = SegText(cell, "Score", sPos, 0.36f, new Vector2(0.2f, 0.055f), true);
            }

            BuildResult(holo, table);
        }

        /// <summary>結果の画面(和了・流局・終局)。ホロの板の中に重ねる</summary>
        static void BuildResult(GameObject holo, SqmTable table)
        {
            var g = Group(holo, "Result");
            table.resultRoot = g;
            var yellow = new Color(1f, 0.82f, 0.44f);
            table.resTitle = HText(g, "Title", new Vector2(0, 0.355f), 0.6f, new Vector2(HW - 0.2f, 0.11f), TextAlignmentOptions.Center);
            table.resTitle.color = yellow;
            var line = Prim(PrimitiveType.Quad, g, "Divider", matHoloLine);
            line.transform.localPosition = new Vector3(0, 0.29f, 0);
            line.transform.localScale = new Vector3(HW - 0.12f, 0.003f, 1);
            table.resHead = HText(g, "Head", new Vector2(0, 0.225f), 0.5f, new Vector2(HW - 0.14f, 0.1f), TextAlignmentOptions.Left);
            table.resHead.enableWordWrapping = false;

            // 手牌(36枚。和了牌には赤枠)
            const int HAND = 36;
            table.resHand = new MeshRenderer[HAND];
            table.resHandMark = new GameObject[HAND];
            var hand = Group(g, "Hand");
            const float TWH = 0.055f, THH = 0.073f, STEP = 0.059f;
            for (int i = 0; i < HAND; i++)
            {
                float x = -(HAND - 1) * STEP / 2 + i * STEP;
                var q = Prim(PrimitiveType.Quad, hand, "tile_" + i.ToString("00"), matHoloFaces[0]);
                q.transform.localPosition = new Vector3(x, 0.105f, 0);
                q.transform.localScale = new Vector3(TWH, THH, 1);
                q.SetActive(false);
                table.resHand[i] = q.GetComponent<MeshRenderer>();
                var m = Prim(PrimitiveType.Quad, hand, "mark_" + i.ToString("00"), matHoloRed);
                m.transform.localPosition = new Vector3(x, 0.105f, 0.002f);
                m.transform.localScale = new Vector3(TWH + 0.01f, THH + 0.01f, 1);
                m.SetActive(false);
                table.resHandMark[i] = m;
            }

            // ドラ・裏ドラ(小さめ。18枚ずつ)
            table.resDoraLabel = HText(g, "DoraLabel", new Vector2(-0.98f, 0.0f), 0.36f, new Vector2(0.14f, 0.07f), TextAlignmentOptions.Right);
            table.resUraLabel = HText(g, "UraLabel", new Vector2(-0.1f, 0.0f), 0.36f, new Vector2(0.18f, 0.07f), TextAlignmentOptions.Right);
            table.resDora = SmallTiles(g, "Dora", -0.88f, 0.0f);
            table.resUra = SmallTiles(g, "Ura", 0.02f, 0.0f);

            // 役(3列)
            table.resYaku0 = YakuCol(g, "Yaku0", -0.82f);
            table.resYaku1 = YakuCol(g, "Yaku1", -0.3f);
            table.resYaku2 = YakuCol(g, "Yaku2", 0.22f);

            // 翻・点数(右)
            table.resHan = HText(g, "Han", new Vector2(0.84f, -0.1f), 0.62f, new Vector2(0.56f, 0.1f), TextAlignmentOptions.Right);
            table.resHan.color = yellow;
            table.resCalc = HText(g, "Calc", new Vector2(0.84f, -0.18f), 0.28f, new Vector2(0.56f, 0.05f), TextAlignmentOptions.Right);
            table.resCalc.color = yellow;
            table.resPoints = HText(g, "Points", new Vector2(0.84f, -0.275f), 0.72f, new Vector2(0.56f, 0.11f), TextAlignmentOptions.Right);
            table.resPoints.color = yellow;

            // 流局の説明・終局の順位
            table.resBody = HText(g, "Body", new Vector2(0, -0.04f), 0.44f, new Vector2(HW - 0.18f, 0.5f), TextAlignmentOptions.Left);
            table.resRankL = HText(g, "RankL", new Vector2(-0.56f, -0.04f), 0.56f, new Vector2(1.08f, 0.56f), TextAlignmentOptions.Left);
            table.resRankR = HText(g, "RankR", new Vector2(0.58f, -0.04f), 0.4f, new Vector2(1.06f, 0.56f), TextAlignmentOptions.Left);
            table.resFoot = HText(g, "Foot", new Vector2(0, -0.385f), 0.3f, new Vector2(HW - 0.14f, 0.06f), TextAlignmentOptions.Center);
            g.SetActive(false);
        }

        static MeshRenderer[] SmallTiles(GameObject parent, string name, float x0, float y)
        {
            var g = Group(parent, name);
            var rs = new MeshRenderer[SqmTable.N_DORA];
            for (int i = 0; i < rs.Length; i++)
            {
                var q = Prim(PrimitiveType.Quad, g, name + "_" + i.ToString("00"), matHoloFaces[0]);
                q.transform.localPosition = new Vector3(x0 + i * 0.043f, y, 0);
                q.transform.localScale = new Vector3(0.04f, 0.053f, 1);
                q.SetActive(false);
                rs[i] = q.GetComponent<MeshRenderer>();
            }
            return rs;
        }

        static TextMeshPro YakuCol(GameObject parent, string name, float x)
        {
            var t = HText(parent, name, new Vector2(x, -0.21f), 0.32f, new Vector2(0.5f, 0.25f), TextAlignmentOptions.TopLeft);
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.12f;
            t.fontSizeMax = 0.32f;
            return t;
        }

        /// <summary>効果音を鳴らす所(卓の上。近くの人にだけ聞こえる)</summary>
        static void BuildAudio(GameObject root, SqmTable table)
        {
            var g = Group(root, "Sfx");
            g.transform.localPosition = new Vector3(0, TOP + 0.4f, 0);
            var a = g.AddComponent<AudioSource>();
            a.playOnAwake = false;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 2f;
            a.maxDistance = 9f;
            a.dopplerLevel = 0f;
            table.sfx = a;
            string[] names = { "discard", "call", "riichi", "win", "end", "turn", "click" };
            table.sfxClips = new AudioClip[names.Length];
            for (int i = 0; i < names.Length; i++)
                table.sfxClips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(PKG + "/Runtime/Audio/sfx_" + names[i] + ".wav");

            // 宣言の声: 席ごとに、その席の上から鳴らす(SqmTable.VOICE_* の順)
            string[] voices = { "riichi", "pon", "chi", "kan", "ron", "tsumo", "kyushu", "ron_kata", "ron_nimai", "double_riichi" };
            table.voiceClips = new AudioClip[voices.Length];
            for (int i = 0; i < voices.Length; i++)
                table.voiceClips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(PKG + "/Runtime/Audio/Voice/voice_" + voices[i] + ".wav");
            table.voiceSources = new AudioSource[8];
            for (int s = 0; s < 8; s++)
            {
                var v = Group(g, "Voice_" + s);
                v.transform.position = root.transform.TransformPoint(U(s) * 1.1f + Vector3.up * (TOP + 0.35f));
                var va = v.AddComponent<AudioSource>();
                va.playOnAwake = false;
                va.spatialBlend = 1f;
                va.rolloffMode = AudioRolloffMode.Linear;
                va.minDistance = 2.5f;
                va.maxDistance = 10f;
                va.dopplerLevel = 0f;
                table.voiceSources[s] = va;
            }
        }

        /// <summary>半透明の板と、青白い縁</summary>
        static void Panel(GameObject parent, string name, Vector2 at, Vector2 size)
        {
            var g = Group(parent, name);
            var bg = Prim(PrimitiveType.Quad, g, "Back", matHoloPanel);
            bg.transform.localPosition = new Vector3(at.x, at.y, 0.01f);
            bg.transform.localScale = new Vector3(size.x, size.y, 1);
            const float t = 0.006f;
            Vector3[] pos = { new Vector3(at.x, at.y + size.y / 2), new Vector3(at.x, at.y - size.y / 2), new Vector3(at.x - size.x / 2, at.y), new Vector3(at.x + size.x / 2, at.y) };
            Vector3[] sc = { new Vector3(size.x, t, 1), new Vector3(size.x, t, 1), new Vector3(t, size.y, 1), new Vector3(t, size.y, 1) };
            for (int i = 0; i < 4; i++)
            {
                var e = Prim(PrimitiveType.Quad, g, "Edge_" + i, matHoloLine);
                e.transform.localPosition = pos[i] + Vector3.forward * 0.009f;
                e.transform.localScale = sc[i];
            }
        }

        /// <summary>ホロの文字(太めの明朝・青白)</summary>
        static TextMeshPro HText(GameObject parent, string name, Vector2 at, float size, Vector2 rect, TextAlignmentOptions align)
        {
            var t = Text(parent, name, new Vector3(at.x, at.y, -0.002f), Quaternion.identity, size, rect);
            if (holoFont != null) t.font = holoFont;
            t.color = HOLO_TEXT;
            t.alignment = align;
            return t;
        }

        /// <summary>赤い7セグの数字(box = 黒い下地を敷く)</summary>
        static TextMeshPro SegText(GameObject parent, string name, Vector2 at, float size, Vector2 rect, bool box)
        {
            if (box)
            {
                var b = Prim(PrimitiveType.Quad, parent, name + "Box", matHoloSegBox);
                b.transform.localPosition = new Vector3(at.x, at.y, 0.001f);
                b.transform.localScale = new Vector3(rect.x, rect.y, 1);
            }
            var t = Text(parent, name, new Vector3(at.x, at.y, -0.002f), Quaternion.identity, size, rect - new Vector2(0.02f, 0));
            if (segFont != null) t.font = segFont;
            t.color = new Color(1f, 0.3f, 0.22f);
            t.alignment = TextAlignmentOptions.Center;
            t.enableWordWrapping = false;
            return t;
        }

        /// <summary>ホロの牌(9枚で折り返す)</summary>
        static MeshRenderer[] HoloTiles(GameObject parent, string name, int n, float y)
        {
            var g = Group(parent, name);
            var rs = new MeshRenderer[n];
            for (int i = 0; i < n; i++)
            {
                var q = Prim(PrimitiveType.Quad, g, name + "_" + i.ToString("00"), matHoloFaces[0]);
                q.transform.localPosition = new Vector3(-0.14f + (i % 9) * 0.05f, y - (i / 9) * 0.07f, 0);
                q.transform.localScale = new Vector3(0.045f, 0.06f, 1);
                rs[i] = q.GetComponent<MeshRenderer>();
                q.SetActive(false);
            }
            return rs;
        }

        /// <summary>円の縁(輪)。半径 0.318〜0.332</summary>
        static Mesh RingMesh()
        {
            string path = GEN + "/HoloRing2.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "HoloRing2" };
            const int N = 96;
            const float r0 = 0.318f, r1 = 0.332f;
            var v = new Vector3[N * 2];
            var tris = new int[N * 6];
            for (int i = 0; i < N; i++)
            {
                float a = i * Mathf.PI * 2 / N;
                v[i * 2] = new Vector3(Mathf.Cos(a) * r0, Mathf.Sin(a) * r0, 0);
                v[i * 2 + 1] = new Vector3(Mathf.Cos(a) * r1, Mathf.Sin(a) * r1, 0);
                int j = (i + 1) % N;
                tris[i * 6] = i * 2; tris[i * 6 + 1] = j * 2; tris[i * 6 + 2] = i * 2 + 1;
                tris[i * 6 + 3] = j * 2; tris[i * 6 + 4] = j * 2 + 1; tris[i * 6 + 5] = i * 2 + 1;
            }
            mesh.vertices = v;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        // ================================================================ 部品
        static Vector3 U(int s) { float a = s * 45f * Mathf.Deg2Rad; return new Vector3(Mathf.Sin(a), 0, -Mathf.Cos(a)); }
        static Vector3 R(int s) { float a = s * 45f * Mathf.Deg2Rad; return new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)); }

        static GameObject Group(GameObject parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            return g;
        }

        static GameObject Prim(PrimitiveType type, GameObject parent, string name, Material mat)
        {
            var g = GameObject.CreatePrimitive(type);
            g.name = name;
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent.transform, false);
            g.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return g;
        }

        /// <summary>牌1枚: 本体(白)・裏(緑)・表(絵)。表は +Z 側を向く</summary>
        static GameObject MakeTile(GameObject parent, string name, out MeshRenderer face)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent.transform, false);
            var body = Prim(PrimitiveType.Cube, root, "Body", matBody);
            body.transform.localScale = new Vector3(TW, TH, TT);
            var back = Prim(PrimitiveType.Quad, root, "Back", matBack);
            back.transform.localPosition = new Vector3(0, 0, -TT / 2 - 0.0004f);
            back.transform.localScale = new Vector3(TW, TH, 1);
            var f = Prim(PrimitiveType.Quad, root, "Face", matFaces[0]);
            f.transform.localPosition = new Vector3(0, 0, TT / 2 + 0.0004f);
            f.transform.localRotation = Quaternion.Euler(0, 180, 0);
            f.transform.localScale = new Vector3(TW * 0.94f, TH * 0.95f, 1);
            face = f.GetComponent<MeshRenderer>();
            root.SetActive(false);
            return root;
        }

        static Transform[] MakeTiles(GameObject parent, string group, string prefix, int n, out MeshRenderer[] faces)
        {
            var g = Group(parent, group);
            var trs = new Transform[n];
            faces = new MeshRenderer[n];
            for (int i = 0; i < n; i++) trs[i] = MakeTile(g, prefix + i.ToString("000"), out faces[i]).transform;
            return trs;
        }

        /// <summary>壁の幢(伏せた牌2枚重ね): 上が緑の箱</summary>
        static GameObject MakeStack(GameObject parent, string name)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent.transform, false);
            var body = Prim(PrimitiveType.Cube, root, "Body", matBody);
            body.transform.localScale = new Vector3(TW, TT * 2, TH);
            var topQ = Prim(PrimitiveType.Quad, root, "Top", matBack);
            topQ.transform.localPosition = new Vector3(0, TT + 0.0004f, 0);
            topQ.transform.localRotation = Quaternion.Euler(90, 0, 0);
            topQ.transform.localScale = new Vector3(TW, TH, 1);
            return root;
        }

        static TextMeshPro Text(GameObject parent, string name, Vector3 pos, Quaternion rot, float size, Vector2 rect)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;
            var t = g.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            t.fontSize = size;
            t.enableAutoSizing = false;
            t.alignment = TextAlignmentOptions.Center;
            t.rectTransform.sizeDelta = rect;
            t.color = new Color(1f, 0.97f, 0.88f);
            t.text = "";
            return t;
        }

        /// <summary>点数の表示: 黒い枠に赤い7セグ。消えている画も薄く見せる(DSEG7 フォントがあるとき)</summary>
        static TextMeshPro SegDisplay(GameObject parent, Vector3 at, Vector3 u)
        {
            const float w = 0.2f, h = 0.05f;
            var g = Group(parent, "score");
            g.transform.localPosition = at;
            var box = Prim(PrimitiveType.Cube, g, "Box", matSegBox);
            box.transform.localRotation = Quaternion.LookRotation(-u, Vector3.up);
            box.transform.localScale = new Vector3(w, 0.004f, h);
            Quaternion flat = Quaternion.LookRotation(Vector3.down, -u);
            Vector3 lift = Vector3.up * 0.0025f;
            TextMeshPro Seg(string name, Color c)
            {
                var t = Text(g, name, lift, flat, 0.3f, new Vector2(w - 0.016f, h));
                if (segFont != null) t.font = segFont;
                t.alignment = TextAlignmentOptions.Right;
                t.enableWordWrapping = false;
                t.color = c;
                return t;
            }
            if (segFont != null) Seg("ghost", new Color(0.32f, 0.04f, 0.03f)).text = "8888888";
            var lit = Seg("value", new Color(1f, 0.16f, 0.1f));
            lit.transform.localPosition = lift + Vector3.up * 0.0004f;
            lit.text = "0";
            return lit;
        }

        static GameObject Button(GameObject parent, string name, string label, SqmTable table, string eventName, Vector3 pos, Quaternion rot, Vector3 size, out TextMeshPro text)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            g.transform.localPosition = pos;
            g.transform.localRotation = rot;
            var body = Prim(PrimitiveType.Cube, g, "Body", matButton);
            body.transform.localScale = size;
            var box = g.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = true;   // 人はすり抜ける(Interact はできる)
            var btn = UdonSharpUndo.AddComponent<SqmButton>(g);
            btn.table = table;
            btn.eventName = eventName;
            // 文字は箱の手前(-Z 側)に。読む人は -Z 側から見る
            text = Text(g, "Label", new Vector3(0, 0, -size.z / 2 - 0.002f), Quaternion.identity, Mathf.Min(0.32f, size.y * 3.2f), new Vector2(size.x * 0.95f, size.y));
            text.text = label;
            if (size.x < 0.06f) { text.enableAutoSizing = true; text.fontSizeMin = 0.02f; text.fontSizeMax = size.y * 3.2f; }
            return g;
        }

        // ================================================================ 素材
        static void MakeAssets()
        {
            if (!AssetDatabase.IsValidFolder(GEN)) AssetDatabase.CreateFolder(PKG + "/Runtime", "Generated");
            matBody = Mat("TileBody", new Color(0.97f, 0.94f, 0.86f), null);
            matBack = Mat("TileBack", new Color(0.13f, 0.50f, 0.32f), null);
            matFelt = Mat("TableFelt", new Color(0.10f, 0.36f, 0.23f), null);
            matRim = Mat("TableRim", new Color(0.35f, 0.22f, 0.13f), null);
            matButton = Mat("Button", new Color(0.18f, 0.16f, 0.14f), null);
            matButtonHot = Mat("ButtonHot", new Color(0.75f, 0.55f, 0.18f), null);
            matSegBox = Mat("SegBox", new Color(0.03f, 0.03f, 0.03f), null);
            // ホロ: 光の影響を受けない半透明
            matHoloPanel = HoloMat("HoloPanel", new Color(0.08f, 0.22f, 0.36f, 0.42f), Q_BACK);
            matHoloLine = HoloMat("HoloLine", new Color(0.45f, 0.85f, 1f, 0.9f));
            matHoloRed = HoloMat("HoloRed", new Color(1f, 0.32f, 0.26f, 0.95f));
            matHoloYellow = HoloMat("HoloYellow", new Color(1f, 0.82f, 0.4f, 0.95f));
            matHoloFill = HoloMat("HoloFill", new Color(0.05f, 0.12f, 0.18f, 0.6f), Q_FILL);
            matHoloTurn = HoloMat("HoloTurn", new Color(0.45f, 0.85f, 1f, 0.95f), Q_FILL);
            matHoloSegBox = HoloMat("HoloSegBox", new Color(0.04f, 0.01f, 0.01f, 0.85f));
            matFaces = new Material[47];
            for (int t = 0; t < 47; t++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PKG + "/Runtime/Textures/Tiles/tile_" + t.ToString("00") + ".png");
                matFaces[t] = Mat("Face_" + t.ToString("00"), Color.white, tex);
            }
            matHoloFaces = new Material[47];
            for (int t = 0; t < 47; t++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PKG + "/Runtime/Textures/Tiles/tile_" + t.ToString("00") + ".png");
                matHoloFaces[t] = ShaderMat("HoloFace_" + t.ToString("00"), "Unlit/Texture", Color.white, tex);
            }
            AssetDatabase.SaveAssets();
        }

        static Material Mat(string name, Color color, Texture2D tex)
        {
            string path = GEN + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(m, path);
            }
            m.color = color;
            m.mainTexture = tex;
            m.SetFloat("_Glossiness", tex != null ? 0.35f : 0.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // 半透明の描く順: 背景の板(Q_BACK) → 枠・線・下地・点棒(Q_PART) → 枠の上の塗り(Q_FILL) → 文字(TextMeshPro は 3000)。
        // 同じ順番どうしだと距離で前後が入れ替わり、背景が文字の上に描かれて薄く見えることがあるので、順番を決めておく
        const int Q_BACK = 2980, Q_PART = 2990, Q_FILL = 2995;

        static Material HoloMat(string name, Color color, int queue = Q_PART) { return ShaderMat(name, "Sprites/Default", color, null, queue); }

        static Material ShaderMat(string name, string shader, Color color, Texture2D tex, int queue = -1)
        {
            string path = GEN + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find(shader));
                AssetDatabase.CreateAsset(m, path);
            }
            m.shader = Shader.Find(shader);
            m.color = color;
            if (tex != null) m.mainTexture = tex;
            m.renderQueue = queue;
            EditorUtility.SetDirty(m);
            return m;
        }

        static void FindFont()
        {
            font = null;
            segFont = null;
            holoFont = null;
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (lower.Contains("dseg")) { if (segFont == null) segFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path); continue; }
                // ホロは太めの明朝(源ノ明朝の Bold / Heavy / SemiBold があれば)
                if (holoFont == null && (lower.Contains("sourcehan") || lower.Contains("mincho") || lower.Contains("serif")) && (lower.Contains("bold") || lower.Contains("heavy") || lower.Contains("black")))
                    holoFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                if (font != null) continue;
                if (lower.Contains("bold") || lower.Contains("heavy") || lower.Contains("black")) continue;   // ふつうの文字は太字でないもの
                if (lower.Contains("jp") || lower.Contains("japan") || lower.Contains("noto") || lower.Contains("gothic") || lower.Contains("mplus") || lower.Contains("ja") || lower.Contains("sourcehan") || lower.Contains("mincho"))
                {
                    font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                }
            }
        }

        /// <summary>八角形の卓(天板 + 縁)。サブメッシュ0 = 天板、1 = 側面</summary>
        static Mesh OctagonMesh()
        {
            string path = GEN + "/TableOctagon.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "TableOctagon" };
            const float apothem = 1.17f, thick = 0.06f;
            float rad = apothem / Mathf.Cos(22.5f * Mathf.Deg2Rad);
            var v = new System.Collections.Generic.List<Vector3>();
            var topTris = new System.Collections.Generic.List<int>();
            var sideTris = new System.Collections.Generic.List<int>();
            v.Add(new Vector3(0, TOP, 0));
            for (int k = 0; k < 8; k++)
            {
                float a = (k * 45f + 22.5f) * Mathf.Deg2Rad;
                v.Add(new Vector3(Mathf.Sin(a) * rad, TOP, -Mathf.Cos(a) * rad));
            }
            for (int k = 0; k < 8; k++) { topTris.Add(0); topTris.Add(1 + (k + 1) % 8); topTris.Add(1 + k); }
            int baseIdx = v.Count;
            for (int k = 0; k < 8; k++)
            {
                Vector3 a = v[1 + k], b = v[1 + (k + 1) % 8];
                int i0 = v.Count;
                v.Add(a); v.Add(b); v.Add(b + Vector3.down * thick); v.Add(a + Vector3.down * thick);
                sideTris.Add(i0); sideTris.Add(i0 + 1); sideTris.Add(i0 + 2);
                sideTris.Add(i0); sideTris.Add(i0 + 2); sideTris.Add(i0 + 3);
            }
            // 脚の代わりの柱
            int p0 = v.Count;
            float pw = 0.2f;
            Vector3[] pc = { new Vector3(-pw, 0, -pw), new Vector3(pw, 0, -pw), new Vector3(pw, 0, pw), new Vector3(-pw, 0, pw) };
            for (int k = 0; k < 4; k++)
            {
                Vector3 a = pc[k], b = pc[(k + 1) % 4];
                int i0 = v.Count;
                v.Add(a + Vector3.up * (TOP - thick)); v.Add(b + Vector3.up * (TOP - thick)); v.Add(b); v.Add(a);
                sideTris.Add(i0); sideTris.Add(i0 + 1); sideTris.Add(i0 + 2);
                sideTris.Add(i0); sideTris.Add(i0 + 2); sideTris.Add(i0 + 3);
            }
            mesh.SetVertices(v);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(topTris, 0);
            mesh.SetTriangles(sideTris, 1);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }
    }
}
