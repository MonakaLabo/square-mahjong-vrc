using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MonakaLabo.SquareMahjong.EditorTools
{
    /// <summary>
    /// メニュー「Tools/平方麻雀/フォントを作る」: 平方麻雀で使う TMP フォントとダイナミックフォントを、ウィンドウからまとめて作る。
    ///   通常テキスト用・太字テキスト用・点数表示用(7セグ)の3つのフォント(.ttf / .otf)を入れて「作成」を押す。
    ///   - 決まった字(スクリプトに書いた文言・牌の名前・かな・全角英数など)は、静的な TMP フォント「元の名前_SDF」に入れる(元のフォントはビルドに入らない)
    ///   - プレイヤー名など何が来るか分からない字は、同梱の SqmFallback-Medium(約 3MB)から作るダイナミックフォント「SqmFallback-Medium_Dynamic」を予備にして出す
    ///   - 作ったフォントの場所を覚えておき、「卓をシーンに置く」はそれを最優先で使う(フォントの名前で探すのは、覚えていないときだけ)
    /// </summary>
    public class SqmFontMaker : EditorWindow
    {
        const string PKG = "Packages/jp.monakalabo.square-mahjong";
        const string CHARS = PKG + "/Runtime/Fonts/SqmCharacters.txt";
        const string SEG_CHARS = "0123456789-:!. ";

        // 作ったフォントの場所(プロジェクトごとに覚える)
        static string PrefKey(string kind) { return "SqmFont." + kind + "." + Application.dataPath.GetHashCode(); }
        public static string SavedPath(string kind) { return EditorPrefs.GetString(PrefKey(kind), ""); }
        public const string KIND_TEXT = "Text", KIND_BOLD = "Bold", KIND_SEG = "Seg";

        Font textFont, boldFont, segFont;
        bool rebuild = true;
        Vector2 scroll;

        [MenuItem("Tools/平方麻雀/フォントを作る")]
        public static void Open()
        {
            var w = GetWindow<SqmFontMaker>(false, "SQM Font Maker", true);
            w.minSize = new Vector2(460, 420);
            w.Guess();
            w.Show();
        }

        /// <summary>前に作ったときのフォント、無ければ Assets の中から名前で見当をつけて、欄に入れておく</summary>
        void Guess()
        {
            textFont = SourceOf(KIND_TEXT); boldFont = SourceOf(KIND_BOLD); segFont = SourceOf(KIND_SEG);
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { "Assets" }))
            {
                var f = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (f == null) continue;
                string n = f.name.ToLowerInvariant();
                if (n.Contains("dseg")) { if (segFont == null) segFont = f; continue; }
                bool bold = n.Contains("bold") || n.Contains("heavy") || n.Contains("black");
                if (bold) { if (boldFont == null) boldFont = f; }
                else if (textFont == null && (n.Contains("medium") || n.Contains("regular") || n.Contains("sourcehan") || n.Contains("mincho") || n.Contains("jp"))) textFont = f;
            }
        }

        /// <summary>前に作った TMP フォントの元のフォント(同じフォルダの、_SDF を除いた名前)</summary>
        static Font SourceOf(string kind)
        {
            string p = SavedPath(kind);
            if (string.IsNullOrEmpty(p) || !File.Exists(p)) return null;
            string dir = Path.GetDirectoryName(p).Replace('\\', '/');
            string name = Path.GetFileNameWithoutExtension(p);
            if (name.EndsWith("_SDF")) name = name.Substring(0, name.Length - 4);
            foreach (var ext in new[] { ".otf", ".ttf", ".OTF", ".TTF" })
            {
                var f = AssetDatabase.LoadAssetAtPath<Font>(dir + "/" + name + ext);
                if (f != null) return f;
            }
            return null;
        }

        void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            var wrap = new GUIStyle(EditorStyles.label) { wordWrap = true };
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("平方麻雀で使用する TMP フォント・ダイナミックフォントを一括で作成します。フォント(ttf / otf)を入れて、「作成」を実行してください。", wrap);
            EditorGUILayout.Space(8);
            textFont = (Font)EditorGUILayout.ObjectField("通常テキスト用", textFont, typeof(Font), false);
            boldFont = (Font)EditorGUILayout.ObjectField("太字テキスト用", boldFont, typeof(Font), false);
            segFont = (Font)EditorGUILayout.ObjectField("点数表示用(7セグ)", segFont, typeof(Font), false);
            EditorGUILayout.Space(4);
            rebuild = EditorGUILayout.ToggleLeft("すでにある TMP フォントも作り直す(文言が増えたときなど)", rebuild);
            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "■ ライセンス: ワールドに入れて配ってよいフォントを使ってください(SIL Open Font License など)。\n" +
                "  おすすめ: 源ノ明朝(Source Han Serif)の Medium・Bold、DSEG7 Classic の Bold(どれも OFL で無料)。\n" +
                "  DSEG を使うときは、同梱の DSEG-LICENSE.txt もフォントと一緒に置いてください。\n" +
                "  源ノ明朝は、可変フォント版(名前に VF が付くもの)ではなく、ふつうの版を使ってください。\n" +
                "■ 決まった字(卓・パッドの文言、牌の名前、かな、全角英数など)は、作る TMP フォントに入れます。\n" +
                "  プレイヤー名の漢字など、それ以外の字は、同梱の SqmFallback-Medium(約 3MB)から作るダイナミックフォントで出します(ワールドが約 3MB 増えます)。\n" +
                "■ 作ったら「卓をシーンに置く」をやり直すと、卓で使われます。",
                MessageType.Info);
            EditorGUILayout.Space(8);
            using (new EditorGUI.DisabledScope(textFont == null && boldFont == null && segFont == null))
            {
                if (GUILayout.Button("作成", GUILayout.Height(32))) Run();
            }
            EditorGUILayout.EndScrollView();
        }

        // ================================================================ 作る
        void Run()
        {
            if (!HasTmpEssentials()) return;
            var made = new List<string>();
            var skipped = new List<string>();
            var targets = new List<TMP_FontAsset>();
            string chars = GatherChars();
            MakeKind(textFont, KIND_TEXT, chars, made, skipped, targets);
            MakeKind(boldFont, KIND_BOLD, chars, made, skipped, targets);
            MakeKind(segFont, KIND_SEG, SEG_CHARS, made, skipped, null);
            string fb = targets.Count > 0 ? MakeFallback(targets) : "";
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string msg = "";
            if (made.Count > 0) msg += "作りました:\n  " + string.Join("\n  ", made) + "\n";
            if (skipped.Count > 0) msg += "作りませんでした:\n  " + string.Join("\n  ", skipped) + "\n";
            if (fb.Length > 0) msg += "\n" + fb + "\n";
            msg += "\nこのあと「卓をシーンに置く」をやり直すと、卓で使われます。";
            EditorUtility.DisplayDialog("平方麻雀", msg, "OK");
        }

        void MakeKind(Font f, string kind, string chars, List<string> made, List<string> skipped, List<TMP_FontAsset> targets)
        {
            if (f == null) return;
            string outPath = OutPath(f);
            if (File.Exists(outPath) && !rebuild)
            {
                skipped.Add(Path.GetFileName(outPath) + "(すでにある)");
            }
            else
            {
                if (File.Exists(outPath)) AssetDatabase.DeleteAsset(outPath);
                string result = MakeOne(f, outPath, chars, kind == KIND_SEG);
                if (result != null) { skipped.Add(f.name + "(" + result + ")"); return; }
                made.Add(Path.GetFileName(outPath));
            }
            EditorPrefs.SetString(PrefKey(kind), outPath);
            if (targets != null)
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
                if (fa != null) targets.Add(fa);
            }
        }

        static bool HasTmpEssentials()
        {
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") != null && Shader.Find("TextMeshPro/Distance Field") != null) return true;
            if (EditorUtility.DisplayDialog("平方麻雀", "TextMeshPro の「TMP Essential Resources」がまだ読み込まれていません。\n読み込みの画面を開くので、Import を押してから、もう一度「作成」を押してください。", "開く", "やめる"))
                EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
            return false;
        }

        static string OutPath(Font f)
        {
            string src = AssetDatabase.GetAssetPath(f);
            // パッケージの中(VCC で入れたもの)には書き込めないことがあるので、Assets の外のフォントは Assets/Fonts に作る
            string dir = src.StartsWith("Assets/") ? Path.GetDirectoryName(src).Replace('\\', '/') : "Assets/Fonts";
            if (!AssetDatabase.IsValidFolder(dir)) { Directory.CreateDirectory(dir); AssetDatabase.Refresh(); }
            return dir + "/" + f.name + "_SDF.asset";
        }

        /// <summary>
        /// TMP フォントに入れる決まった字: スクリプト(パッケージの Runtime・Editor の .cs)の文字列に出てくる字、SqmCharacters.txt、
        /// ASCII・かな・全角英数・よく使う記号・牌の字。プレイヤー名の漢字などは、ダイナミックフォントで出す
        /// </summary>
        static string GatherChars()
        {
            var set = new HashSet<char>();
            for (int c = 0x20; c < 0x7f; c++) set.Add((char)c);
            for (int c = 0x3041; c < 0x3097; c++) set.Add((char)c);
            for (int c = 0x30A1; c < 0x30FB; c++) set.Add((char)c);
            for (int c = 0xFF01; c < 0xFF5F; c++) set.Add((char)c);
            foreach (char c in "ーゝゞヽヾ〇々、。・「」『』()（）〜…！？：%²³▶◀□■●○×＋−━東南西北白發中風林火山萬筒索算一二三四五六七八九") set.Add(c);
            var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(CHARS);
            if (ta != null) foreach (char c in ta.text) set.Add(c);
            var str = new Regex("\"((?:[^\"\\\\]|\\\\.)*)\"");
            foreach (var dir in new[] { PKG + "/Runtime/Scripts", PKG + "/Editor" })
            {
                string full = Path.GetFullPath(dir);
                if (!Directory.Exists(full)) continue;
                foreach (var file in Directory.GetFiles(full, "*.cs", SearchOption.AllDirectories))
                    foreach (Match m in str.Matches(File.ReadAllText(file)))
                        foreach (char c in m.Groups[1].Value) set.Add(c);
            }
            set.Remove('\r'); set.Remove('\n'); set.Remove('\t');
            var list = new List<char>(set);
            list.Sort();
            return new string(list.ToArray());
        }

        /// <summary>1つ作る。うまくいけば null、だめなら理由。字が多くても入るよう、文字の画像は何枚にも分けられるようにする</summary>
        static string MakeOne(Font font, string outPath, string chars, bool seg)
        {
            int size = seg ? 512 : 2048;
            var fa = TMP_FontAsset.CreateFontAsset(font, seg ? 90 : 64, seg ? 9 : 7, GlyphRenderMode.SDFAA, size, size, AtlasPopulationMode.Dynamic, !seg);
            if (fa == null) return "作れなかった";
            fa.name = Path.GetFileNameWithoutExtension(outPath);
            fa.TryAddCharacters(chars, out string missing);
            // 静的にする(元のフォントをビルドに入れない)
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(fa, outPath);
            int i = 0;
            foreach (var tex in fa.atlasTextures)
            {
                if (tex == null) continue;
                tex.name = fa.name + " Atlas" + (i++ > 0 ? " " + i : "");
                AssetDatabase.AddObjectToAsset(tex, fa);
            }
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[平方麻雀] " + fa.name + " に入らなかった文字(フォントに無い字): " + missing);
            Debug.Log("[平方麻雀] TMP フォントを作りました: " + outPath + "(" + fa.characterTable.Count + " 文字、画像 " + i + " 枚)");
            return null;
        }

        /// <summary>
        /// 予備のダイナミックフォント: 同梱の SqmFallback-Medium から「SqmFallback-Medium_Dynamic」を作り、通常・太字の TMP フォントの予備(Fallback)に入れる。
        /// ほかの「〇〇_Dynamic」は予備から外す(前に作った重いものが残らないように)。ビルドの前に、作った文字は消す(Clear Dynamic Data on Build)
        /// </summary>
        static string MakeFallback(List<TMP_FontAsset> targets)
        {
            Font src = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { PKG + "/Runtime/Fonts" }))
            {
                var f = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                if (f != null && f.name.ToLowerInvariant().Contains("fallback")) { src = f; break; }
            }
            if (src == null) return "予備のフォント SqmFallback-Medium が見つからないので、ダイナミックフォントは作りませんでした。";
            string outDir = "Assets/Fonts";
            if (!AssetDatabase.IsValidFolder(outDir)) { Directory.CreateDirectory(outDir); AssetDatabase.Refresh(); }
            string outPath = outDir + "/" + src.name + "_Dynamic.asset";
            var dyn = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
            if (dyn == null)
            {
                dyn = TMP_FontAsset.CreateFontAsset(src, 64, 7, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (dyn == null) return src.name + " からダイナミックフォントを作れませんでした。";
                dyn.name = Path.GetFileNameWithoutExtension(outPath);
                AssetDatabase.CreateAsset(dyn, outPath);
                foreach (var tex in dyn.atlasTextures)
                {
                    if (tex == null) continue;
                    tex.name = dyn.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(tex, dyn);
                }
                dyn.material.name = dyn.name + " Material";
                AssetDatabase.AddObjectToAsset(dyn.material, dyn);
            }
            var so = new SerializedObject(dyn);
            var clear = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null) { clear.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorUtility.SetDirty(dyn);
            foreach (var fa in targets)
            {
                if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                fa.fallbackFontAssetTable.RemoveAll(x => x == null || (x != dyn && x.name.EndsWith("_Dynamic")));
                if (!fa.fallbackFontAssetTable.Contains(dyn)) fa.fallbackFontAssetTable.Add(dyn);
                EditorUtility.SetDirty(fa);
            }
            return "ダイナミックフォント: " + dyn.name + "(通常・太字の予備に入れました)";
        }
    }
}
