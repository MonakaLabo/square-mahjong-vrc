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
            foreach (var type in new[] { typeof(SqmEngine), typeof(SqmNpc), typeof(SqmTable), typeof(SqmArchive), typeof(SqmTileClick), typeof(SqmButton) })
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

            // ---- 席を選ぶボタン ----
            table.joinButtons = new GameObject[8];
            var joins = Group(root, "JoinButtons");
            for (int s = 0; s < 8; s++)
            {
                Vector3 u = U(s);
                table.joinButtons[s] = Button(joins, "join_" + s, "ここで打つ", table, "Join" + s,
                    u * 1.28f + Vector3.up * (TOP + 0.12f), Quaternion.LookRotation(-u, Vector3.up), new Vector3(0.24f, 0.07f, 0.02f), out _);
            }
            {
                Vector3 u = U(SqmTable.WALLLESS_SIDE);
                Vector3 r = R(SqmTable.WALLLESS_SIDE);
                table.leaveButton = Button(root, "leave", "席を立つ", table, "Leave", u * 1.28f + r * 0.3f + Vector3.up * (TOP + 0.24f), Quaternion.LookRotation(-u, Vector3.up), new Vector3(0.26f, 0.06f, 0.02f), out _);
                table.newMatchButton = Button(root, "new_match", "新しい試合", table, "NewMatch", u * 1.28f - r * 0.3f + Vector3.up * (TOP + 0.24f), Quaternion.LookRotation(-u, Vector3.up), new Vector3(0.26f, 0.06f, 0.02f), out _);
            }

            // ---- 自分の前に出る操作ボタン ----
            var controls = new GameObject("Controls");
            controls.transform.SetParent(root.transform, false);
            controls.transform.localPosition = U(0) * 1.12f + Vector3.up * (TOP + 0.25f);
            table.controls = controls.transform;
            table.discardButton = Button(controls, "discard", "切る", table, "Discard", new Vector3(0.36f, -0.08f, 0), Quaternion.identity, new Vector3(0.12f, 0.06f, 0.02f), out _);
            table.riichiButton = Button(controls, "riichi", "リーチ", table, "ToggleRiichi", new Vector3(0.36f, 0, 0), Quaternion.identity, new Vector3(0.12f, 0.06f, 0.02f), out table.riichiLabel);
            table.confirmButton = Button(controls, "confirm", "確認", table, "Confirm", new Vector3(0, 0.09f, 0), Quaternion.identity, new Vector3(0.2f, 0.07f, 0.02f), out _);
            table.confirmButton.SetActive(false);
            table.optionButtons = new GameObject[SqmTable.N_OPT];
            table.optionLabels = new TextMeshPro[SqmTable.N_OPT];
            for (int i = 0; i < SqmTable.N_OPT; i++)
            {
                int col = i % 6, row = i / 6;
                table.optionButtons[i] = Button(controls, "option_" + i, "選択肢", table, "Opt" + i,
                    new Vector3(-0.33f + col * 0.115f, 0.0f - row * 0.075f, 0), Quaternion.identity, new Vector3(0.11f, 0.065f, 0.02f), out table.optionLabels[i]);
            }

            // U# の部品に入れた値を、裏の UdonBehaviour に写す
            foreach (var b in root.GetComponentsInChildren<UdonSharp.UdonSharpBehaviour>(true)) { UdonSharpEditorUtility.CopyProxyToUdon(b); EditorUtility.SetDirty(b); }
            Directory.CreateDirectory(Path.GetDirectoryName(PREFAB));
            PrefabUtility.SaveAsPrefabAssetAndConnect(root, PREFAB, InteractionMode.UserAction);
            Selection.activeGameObject = root;
            Debug.Log("[平方麻雀] 卓を置きました: " + PREFAB + (font == null ? "(日本語のフォントが見つからないので、文字が □ になるかもしれません)" : "(フォント: " + font.name + ")")
                + (segFont == null ? "(7セグのフォント DSEG7 が見つからないので、点数はふつうの文字です)" : "(7セグ: " + segFont.name + ")"));
        }

        // ================================================================ 上空のホロ
        // 半透明の青白の板に、太めの明朝。数字は赤い7セグ。見る人の方へ向く(SqmTable.BillboardHolo)。
        // 板の中の座標(m): 原点が板の中央、x が右、y が上。読む人は -Z 側から見る
        const float HW = 2.6f, HH = 1.0f;
        static readonly Color HOLO_TEXT = new Color(0.88f, 0.96f, 1f);

        static void BuildHolo(GameObject root, SqmTable table)
        {
            var holo = Group(root, "Hologram");
            holo.transform.localPosition = new Vector3(0, TOP + 1.1f, 0);
            table.holoRoot = holo.transform;
            table.holoMatLine = matHoloLine; table.holoMatRed = matHoloRed; table.holoMatYellow = matHoloYellow;
            table.holoMatFill = matHoloFill; table.holoMatTurn = matHoloTurn;
            table.holoFaceMats = matHoloFaces;

            Panel(holo, "Panel", Vector2.zero, new Vector2(HW, HH));
            table.holoTitle = HText(holo, "Title", new Vector2(0, 0.02f), 0.7f, new Vector2(HW - 0.2f, HH - 0.2f), TextAlignmentOptions.Center);
            table.holoTitle.text = "平方麻雀²";
            table.holo = HText(holo, "Message", new Vector2(0, -HH / 2 + 0.06f), 0.28f, new Vector2(HW - 0.2f, 0.08f), TextAlignmentOptions.Center);

            var play = Group(holo, "Play");
            table.holoPlay = play;

            // ---- 左: 局・残り枚数(円のゲージ)・点棒 ----
            var c = new Vector2(-0.95f, -0.02f);
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
                seg.transform.localPosition = new Vector3(c.x + Mathf.Cos(a) * 0.322f, c.y + Mathf.Sin(a) * 0.322f, -0.001f);
                seg.transform.localRotation = Quaternion.Euler(0, 0, a * Mathf.Rad2Deg - 90f);
                seg.transform.localScale = new Vector3(0.019f, 0.016f, 1);
                table.holoGauge[i] = seg;
            }
            table.holoRound = HText(play, "Round", c + new Vector2(0, 0.12f), 1.0f, new Vector2(0.56f, 0.14f), TextAlignmentOptions.Center);
            HText(play, "LeftLabel", c + new Vector2(-0.16f, -0.02f), 0.6f, new Vector2(0.12f, 0.1f), TextAlignmentOptions.Center).text = "残";
            table.holoLeft = SegText(play, "Left", c + new Vector2(0.07f, -0.02f), 0.62f, new Vector2(0.3f, 0.1f), false);
            // 点棒(上: 供託の千点棒、下: 本場の百点棒)と本数
            string[] stickTex = { "stick_1000", "stick_100" };
            for (int i = 0; i < 2; i++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PKG + "/Runtime/Textures/Sticks/" + stickTex[i] + ".png");
                var st = Prim(PrimitiveType.Quad, play, "Stick_" + stickTex[i], ShaderMat("Holo_" + stickTex[i], "Unlit/Transparent", Color.white, tex));
                st.transform.localPosition = new Vector3(c.x - 0.06f, c.y - 0.135f - i * 0.065f, 0);
                st.transform.localScale = new Vector3(0.17f, 0.046f, 1);
            }
            table.holoSticks = HText(play, "Sticks", c + new Vector2(0.11f, -0.1675f), 0.36f, new Vector2(0.14f, 0.13f), TextAlignmentOptions.Left);
            table.holoSticks.lineSpacing = 0f;

            // ---- 中: ドラ表示牌・ロン牌・時間 ----
            HText(play, "DoraLabel", new Vector2(-0.34f, 0.3f), 0.38f, new Vector2(0.34f, 0.08f), TextAlignmentOptions.Left).text = "ドラ表示牌";
            HText(play, "RonLabel", new Vector2(-0.34f, 0.08f), 0.38f, new Vector2(0.34f, 0.08f), TextAlignmentOptions.Left).text = "ロン牌";
            table.holoDora = HoloTiles(play, "Dora", SqmTable.N_DORA, 0.3f);
            table.holoRon = HoloTiles(play, "Ron", SqmTable.N_RON, 0.08f);
            HText(play, "SetLabel", new Vector2(-0.3f, -0.2f), 0.36f, new Vector2(0.2f, 0.08f), TextAlignmentOptions.Right).text = "セット";
            HText(play, "KyokuLabel", new Vector2(-0.3f, -0.31f), 0.36f, new Vector2(0.2f, 0.08f), TextAlignmentOptions.Right).text = "現局";
            table.holoSetTime = SegText(play, "SetTime", new Vector2(0.0f, -0.2f), 0.4f, new Vector2(0.34f, 0.07f), true);
            table.holoKyokuTime = SegText(play, "KyokuTime", new Vector2(0.0f, -0.31f), 0.4f, new Vector2(0.34f, 0.07f), true);

            // ---- 右: 8席の見取り図 ----
            var g = new Vector2(0.8f, 0.0f);
            const float CELL = 0.13f, PITCH = 0.15f;
            int[] col = { 0, 1, 1, 1, 0, -1, -1, -1 };
            int[] row = { -1, -1, 0, 1, 1, 1, 0, -1 };
            table.holoCellFrame = new MeshRenderer[8];
            table.holoCellFill = new MeshRenderer[8];
            table.holoCellWind = new TextMeshPro[8];
            table.holoCellName = new TextMeshPro[8];
            table.holoCellScore = new TextMeshPro[8];
            var map = Group(play, "Map");
            table.holoCenter = HText(map, "Center", g, 0.4f, new Vector2(0.13f, 0.08f), TextAlignmentOptions.Center);
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
                table.holoCellWind[p] = HText(cell, "Wind", at, 0.72f, new Vector2(CELL, CELL), TextAlignmentOptions.Center);
                // 名前と点数: 上下の席は1行(名前・点数)、左右の列の席は2行(名前の下に点数)
                Vector2 nPos, sPos; Vector2 nRect = new Vector2(0.26f, 0.06f);
                TextAlignmentOptions nAlign = TextAlignmentOptions.Center;
                if (col[p] == 0)
                {
                    float y = g.y + row[p] * 0.235f;
                    nPos = new Vector2(g.x - 0.13f, y); sPos = new Vector2(g.x + 0.1f, y);
                    nAlign = TextAlignmentOptions.Right;
                }
                else
                {
                    float x = g.x + col[p] * 0.34f;
                    nPos = new Vector2(x, at.y + 0.032f); sPos = new Vector2(x, at.y - 0.03f);
                }
                table.holoCellName[p] = HText(cell, "Name", nPos, 0.26f, nRect, nAlign);
                table.holoCellName[p].enableWordWrapping = false;
                table.holoCellName[p].overflowMode = TextOverflowModes.Ellipsis;
                table.holoCellScore[p] = SegText(cell, "Score", sPos, 0.3f, new Vector2(0.18f, 0.05f), true);
            }

            BuildResult(holo, table);
        }

        /// <summary>結果の画面(和了・流局・終局)。ホロの板の中に重ねる</summary>
        static void BuildResult(GameObject holo, SqmTable table)
        {
            var g = Group(holo, "Result");
            table.resultRoot = g;
            var yellow = new Color(1f, 0.82f, 0.44f);
            table.resTitle = HText(g, "Title", new Vector2(0, 0.41f), 0.62f, new Vector2(HW - 0.2f, 0.12f), TextAlignmentOptions.Center);
            table.resTitle.color = yellow;
            var line = Prim(PrimitiveType.Quad, g, "Divider", matHoloLine);
            line.transform.localPosition = new Vector3(0, 0.335f, 0);
            line.transform.localScale = new Vector3(HW - 0.16f, 0.003f, 1);
            table.resHead = HText(g, "Head", new Vector2(0, 0.26f), 0.5f, new Vector2(HW - 0.2f, 0.1f), TextAlignmentOptions.Left);
            table.resHead.enableWordWrapping = false;

            // 手牌(36枚。和了牌には赤枠)
            const int HAND = 36;
            table.resHand = new MeshRenderer[HAND];
            table.resHandMark = new GameObject[HAND];
            var hand = Group(g, "Hand");
            const float TWH = 0.06f, THH = 0.08f, STEP = 0.064f;
            for (int i = 0; i < HAND; i++)
            {
                float x = -(HAND - 1) * STEP / 2 + i * STEP;
                var q = Prim(PrimitiveType.Quad, hand, "tile_" + i.ToString("00"), matHoloFaces[0]);
                q.transform.localPosition = new Vector3(x, 0.12f, 0);
                q.transform.localScale = new Vector3(TWH, THH, 1);
                q.SetActive(false);
                table.resHand[i] = q.GetComponent<MeshRenderer>();
                var m = Prim(PrimitiveType.Quad, hand, "mark_" + i.ToString("00"), matHoloRed);
                m.transform.localPosition = new Vector3(x, 0.12f, 0.002f);
                m.transform.localScale = new Vector3(TWH + 0.01f, THH + 0.01f, 1);
                m.SetActive(false);
                table.resHandMark[i] = m;
            }

            // ドラ・裏ドラ(小さめ。18枚ずつ)
            table.resDoraLabel = HText(g, "DoraLabel", new Vector2(-1.13f, -0.01f), 0.32f, new Vector2(0.16f, 0.07f), TextAlignmentOptions.Right);
            table.resUraLabel = HText(g, "UraLabel", new Vector2(-0.16f, -0.01f), 0.32f, new Vector2(0.2f, 0.07f), TextAlignmentOptions.Right);
            table.resDora = SmallTiles(g, "Dora", -1.02f, -0.01f);
            table.resUra = SmallTiles(g, "Ura", -0.02f, -0.01f);

            // 役(3列)
            table.resYaku0 = YakuCol(g, "Yaku0", -0.95f);
            table.resYaku1 = YakuCol(g, "Yaku1", -0.35f);
            table.resYaku2 = YakuCol(g, "Yaku2", 0.25f);

            // 翻・点数(右)
            table.resHan = HText(g, "Han", new Vector2(0.98f, -0.12f), 0.62f, new Vector2(0.6f, 0.1f), TextAlignmentOptions.Right);
            table.resHan.color = yellow;
            table.resCalc = HText(g, "Calc", new Vector2(0.98f, -0.2f), 0.26f, new Vector2(0.6f, 0.05f), TextAlignmentOptions.Right);
            table.resCalc.color = yellow;
            table.resPoints = HText(g, "Points", new Vector2(0.98f, -0.3f), 0.7f, new Vector2(0.6f, 0.11f), TextAlignmentOptions.Right);
            table.resPoints.color = yellow;

            // 流局の説明・終局の順位
            table.resBody = HText(g, "Body", new Vector2(0, 0.0f), 0.42f, new Vector2(HW - 0.24f, 0.55f), TextAlignmentOptions.Left);
            table.resRankL = HText(g, "RankL", new Vector2(-0.62f, -0.02f), 0.56f, new Vector2(1.16f, 0.62f), TextAlignmentOptions.Left);
            table.resRankR = HText(g, "RankR", new Vector2(0.64f, -0.02f), 0.38f, new Vector2(1.1f, 0.62f), TextAlignmentOptions.Left);
            table.resFoot = HText(g, "Foot", new Vector2(0, -0.43f), 0.28f, new Vector2(HW - 0.2f, 0.06f), TextAlignmentOptions.Center);
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
            var t = HText(parent, name, new Vector2(x, -0.25f), 0.3f, new Vector2(0.56f, 0.26f), TextAlignmentOptions.TopLeft);
            t.enableAutoSizing = true;
            t.fontSizeMin = 0.12f;
            t.fontSizeMax = 0.3f;
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

        /// <summary>円の縁(輪)。半径 0.345〜0.36</summary>
        static Mesh RingMesh()
        {
            string path = GEN + "/HoloRing.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh != null) return mesh;
            mesh = new Mesh { name = "HoloRing" };
            const int N = 96;
            const float r0 = 0.345f, r1 = 0.36f;
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
            var btn = UdonSharpUndo.AddComponent<SqmButton>(g);
            btn.table = table;
            btn.eventName = eventName;
            // 文字は箱の手前(-Z 側)に。読む人は -Z 側から見る
            text = Text(g, "Label", new Vector3(0, 0, -size.z / 2 - 0.002f), Quaternion.identity, Mathf.Min(0.32f, size.y * 3.2f), new Vector2(size.x * 0.95f, size.y));
            text.text = label;
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
            matHoloPanel = HoloMat("HoloPanel", new Color(0.55f, 0.85f, 1f, 0.10f));
            matHoloLine = HoloMat("HoloLine", new Color(0.45f, 0.85f, 1f, 0.9f));
            matHoloRed = HoloMat("HoloRed", new Color(1f, 0.32f, 0.26f, 0.95f));
            matHoloYellow = HoloMat("HoloYellow", new Color(1f, 0.82f, 0.4f, 0.95f));
            matHoloFill = HoloMat("HoloFill", new Color(0.05f, 0.12f, 0.18f, 0.6f));
            matHoloTurn = HoloMat("HoloTurn", new Color(0.45f, 0.85f, 1f, 0.95f));
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

        static Material HoloMat(string name, Color color) { return ShaderMat(name, "Sprites/Default", color, null); }

        static Material ShaderMat(string name, string shader, Color color, Texture2D tex)
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
