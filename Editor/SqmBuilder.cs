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

        static Material matBody, matBack, matFelt, matRim, matButton, matButtonHot;
        static Material[] matFaces;
        static TMP_FontAsset font;

        [MenuItem("Tools/平方麻雀/卓をシーンに置く")]
        public static void Build()
        {
            MakeAssets();
            FindFont();

            var root = new GameObject("SquareMahjongTable");
            Undo.RegisterCreatedObjectUndo(root, "平方麻雀の卓");
            var engine = UdonSharpUndo.AddComponent<SqmEngine>(root);
            var npc = UdonSharpUndo.AddComponent<SqmNpc>(root);
            var table = UdonSharpUndo.AddComponent<SqmTable>(root);
            npc.engine = engine;
            table.engine = engine;
            table.npc = npc;
            table.faceMats = matFaces;

            // ---- 卓 ----
            var top = new GameObject("TableTop");
            top.transform.SetParent(root.transform, false);
            top.AddComponent<MeshFilter>().sharedMesh = OctagonMesh();
            top.AddComponent<MeshRenderer>().sharedMaterials = new[] { matFelt, matRim };
            top.AddComponent<MeshCollider>();

            // ---- 壁(幢 525) ----
            var wallRoot = Group(root, "Wall");
            table.wallStacks = new Transform[525];
            for (int i = 0; i < 525; i++) table.wallStacks[i] = MakeStack(wallRoot, "stack_" + i.ToString("000")).transform;

            // ---- 手牌・河・副露・中央 ----
            table.handTiles = new Transform[8 * SqmTable.SLOT_HAND];
            table.handFaces = new MeshRenderer[8 * SqmTable.SLOT_HAND];
            var hands = Group(root, "Hands");
            for (int i = 0; i < table.handTiles.Length; i++)
            {
                var t = MakeTile(hands, "hand_" + (i / SqmTable.SLOT_HAND) + "_" + (i % SqmTable.SLOT_HAND).ToString("00"), out MeshRenderer face);
                var box = t.AddComponent<BoxCollider>();
                box.size = new Vector3(TW, TH, TT * 1.5f);
                var click = UdonSharpUndo.AddComponent<SqmTileClick>(t);
                click.table = table;
                click.index = i;
                table.handTiles[i] = t.transform;
                table.handFaces[i] = face;
            }
            table.riverTiles = MakeTiles(root, "Rivers", "river_", 8 * SqmTable.SLOT_RIVER, out table.riverFaces);
            table.meldTiles = MakeTiles(root, "Melds", "meld_", 8 * SqmTable.SLOT_MELD, out table.meldFaces);
            table.centerTiles = MakeTiles(root, "Center", "center_", SqmTable.N_DORA + SqmTable.N_RON, out table.centerFaces);

            // ---- 文字 ----
            table.holo = Text(root, "Hologram", new Vector3(0, TOP + 0.75f, 0), Quaternion.identity, 0.55f, new Vector2(1.6f, 0.9f));
            table.holo.text = "平方麻雀²";
            table.seatLabels = new TextMeshPro[8];
            var labels = Group(root, "SeatLabels");
            for (int s = 0; s < 8; s++)
            {
                Vector3 u = U(s);
                // 卓の縁に寝かせて、その席から読める向き
                table.seatLabels[s] = Text(labels, "label_" + s, u * 1.13f + Vector3.up * (TOP + 0.002f), Quaternion.LookRotation(Vector3.down, -u), 0.28f, new Vector2(0.6f, 0.12f));
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
                Button(joins, "watch", "NPC だけで観戦", table, "Watch", u * 1.28f + r * 0.3f + Vector3.up * (TOP + 0.24f), Quaternion.LookRotation(-u, Vector3.up), new Vector3(0.26f, 0.06f, 0.02f), out _);
                Button(root, "new_match", "新しい試合", table, "NewMatch", u * 1.28f - r * 0.3f + Vector3.up * (TOP + 0.24f), Quaternion.LookRotation(-u, Vector3.up), new Vector3(0.26f, 0.06f, 0.02f), out _);
            }

            // ---- 自分の前に出る操作ボタン ----
            var controls = new GameObject("Controls");
            controls.transform.SetParent(root.transform, false);
            controls.transform.localPosition = U(0) * 1.12f + Vector3.up * (TOP + 0.25f);
            table.controls = controls.transform;
            table.discardButton = Button(controls, "discard", "切る", table, "Discard", new Vector3(0.36f, -0.08f, 0), Quaternion.identity, new Vector3(0.12f, 0.06f, 0.02f), out _);
            table.riichiButton = Button(controls, "riichi", "リーチ", table, "ToggleRiichi", new Vector3(0.36f, 0, 0), Quaternion.identity, new Vector3(0.12f, 0.06f, 0.02f), out table.riichiLabel);
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
            Debug.Log("[平方麻雀] 卓を置きました: " + PREFAB + (font == null ? "(日本語のフォントが見つからないので、文字が □ になるかもしれません)" : "(フォント: " + font.name + ")"));
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
            matFaces = new Material[47];
            for (int t = 0; t < 47; t++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(PKG + "/Runtime/Textures/Tiles/tile_" + t.ToString("00") + ".png");
                matFaces[t] = Mat("Face_" + t.ToString("00"), Color.white, tex);
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

        static void FindFont()
        {
            font = null;
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                string lower = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                if (lower.Contains("jp") || lower.Contains("japan") || lower.Contains("noto") || lower.Contains("gothic") || lower.Contains("mplus") || lower.Contains("ja"))
                {
                    font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
                    if (font != null) return;
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
