using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace MonakaLabo.SquareMahjong.EditorTools
{
    /// <summary>
    /// メニュー「Tools/平方麻雀/TMP フォントを作る」: Project で選んだフォント(.ttf / .otf)から、卓で使う TextMeshPro のフォントを作る。
    /// 名前に DSEG を含むフォントは点数(7セグ)用に数字だけ、ほかは Runtime/Fonts/SqmCharacters.txt の文字を入れる。
    /// できたものはフォントと同じフォルダに「元の名前_SDF.asset」で保存する(すでにあれば作らない)。静的(Static)なので、元のフォントはビルドに入らない。
    /// 何も選んでいなければ、Assets の中の DSEG フォントのうち、まだ作っていないものを作る。
    /// </summary>
    public static class SqmFontMaker
    {
        const string CHARS = "Packages/jp.monakalabo.square-mahjong/Runtime/Fonts/SqmCharacters.txt";
        const string SEG_CHARS = "0123456789-:!. ";

        /// <summary>
        /// TMP Essential Resources(TextMeshPro のシェーダー)が読み込まれているか。無いとフォントを作れないので、案内して読み込みの画面を開く
        /// </summary>
        static bool HasTmpEssentials()
        {
            if (Shader.Find("TextMeshPro/Mobile/Distance Field") != null && Shader.Find("TextMeshPro/Distance Field") != null) return true;
            if (EditorUtility.DisplayDialog("平方麻雀", "TextMeshPro の「TMP Essential Resources」がまだ読み込まれていません。\n読み込みの画面を開くので、Import を押してから、もう一度このメニューを実行してください。", "開く", "やめる"))
                EditorApplication.ExecuteMenuItem("Window/TextMeshPro/Import TMP Essential Resources");
            return false;
        }

        [MenuItem("Tools/平方麻雀/TMP フォントを作る")]
        public static void Make()
        {
            if (!HasTmpEssentials()) return;
            var fonts = new List<Font>();
            foreach (var o in Selection.objects) if (o is Font f) fonts.Add(f);
            if (fonts.Count == 0)
            {
                foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { "Assets" }))
                {
                    var f = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                    if (f != null && f.name.ToLowerInvariant().Contains("dseg") && !File.Exists(OutPath(f))) fonts.Add(f);
                }
            }
            if (fonts.Count == 0)
            {
                EditorUtility.DisplayDialog("平方麻雀", "フォント(.ttf / .otf)を Project で選んでから、もう一度実行してください。", "OK");
                return;
            }
            var made = new List<string>();
            var skipped = new List<string>();
            foreach (var f in fonts)
            {
                string outPath = OutPath(f);
                if (File.Exists(outPath)) { skipped.Add(Path.GetFileName(outPath) + "(すでにある)"); continue; }
                string result = MakeOne(f, outPath);
                if (result == null) made.Add(Path.GetFileName(outPath));
                else skipped.Add(f.name + "(" + result + ")");
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string msg = "";
            if (made.Count > 0) msg += "作りました:\n  " + string.Join("\n  ", made) + "\n";
            if (skipped.Count > 0) msg += "作りませんでした:\n  " + string.Join("\n  ", skipped) + "\n";
            msg += "\nこのあと「卓をシーンに置く」をやり直すと、卓で使われます。";
            EditorUtility.DisplayDialog("平方麻雀", msg, "OK");
        }

        /// <summary>
        /// メニュー「Tools/平方麻雀/動的フォールバックを作る」: ユーザー名の漢字など、静的なフォントに無い文字を、その場で作って出すためのフォント。
        /// Project で選んだフォント(選んでいなければ Assets の、名前に Fallback を含むフォント → Medium を含むフォント)から「元の名前_Dynamic.asset」を作り、
        /// Assets にある卓用の TMP フォント(DSEG を除く)すべての予備(Fallback)に入れる。
        /// 元のフォントがビルドに入るので、その分ワールドは重くなる(源ノ明朝そのままだと約 24MB)。
        /// Tools~/make-fallback-font.py で間引いたフォント(SqmFallback-Medium.otf、約 3MB)を使うと軽い。
        /// ほかの「〇〇_Dynamic」は予備から外す(前に作った重いものが残らないように)。ビルドの前に、作った文字は消す(Clear Dynamic Data on Build)。
        /// </summary>
        [MenuItem("Tools/平方麻雀/動的フォールバックを作る")]
        public static void MakeFallback()
        {
            if (!HasTmpEssentials()) return;
            Font src = null;
            foreach (var o in Selection.objects) if (o is Font f && !f.name.ToLowerInvariant().Contains("dseg")) { src = f; break; }
            if (src == null)
            {
                // パッケージに同梱した SqmFallback-Medium(約 3MB)があれば、それを使う
                foreach (var guid in AssetDatabase.FindAssets("t:Font", new[] { "Assets", "Packages/jp.monakalabo.square-mahjong/Runtime/Fonts" }))
                {
                    var f = AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
                    if (f == null) continue;
                    string lower = f.name.ToLowerInvariant();
                    if (lower.Contains("dseg")) continue;
                    if (lower.Contains("fallback")) { src = f; break; }
                    if (src == null || lower.Contains("medium")) src = f;
                }
            }
            if (src == null)
            {
                EditorUtility.DisplayDialog("平方麻雀", "フォント(.ttf / .otf)を Project で選んでから、もう一度実行してください。", "OK");
                return;
            }
            string srcPath = AssetDatabase.GetAssetPath(src);
            // 作ったフォントは Assets に置く(VCC で入れたパッケージの中には書き込めないことがあるため)
            string outDir = srcPath.StartsWith("Assets/") ? Path.GetDirectoryName(srcPath).Replace('\\', '/') : "Assets/Fonts";
            if (!AssetDatabase.IsValidFolder(outDir)) { Directory.CreateDirectory(outDir); AssetDatabase.Refresh(); }
            string outPath = outDir + "/" + src.name + "_Dynamic.asset";
            var dyn = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(outPath);
            if (dyn == null)
            {
                dyn = TMP_FontAsset.CreateFontAsset(src, 64, 7, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
                if (dyn == null) { EditorUtility.DisplayDialog("平方麻雀", src.name + " から作れませんでした。", "OK"); return; }
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
            // ビルドの前に、エディタで作った文字を消す(ワールドが無駄に大きくならないように)
            var so = new SerializedObject(dyn);
            var clear = so.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null) { clear.boolValue = true; so.ApplyModifiedPropertiesWithoutUndo(); }
            EditorUtility.SetDirty(dyn);

            // 卓用の TMP フォントの予備に入れる
            var added = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:TMP_FontAsset", new[] { "Assets" }))
            {
                var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (fa == null || fa == dyn || fa.name.ToLowerInvariant().Contains("dseg")) continue;
                if (fa.fallbackFontAssetTable == null) fa.fallbackFontAssetTable = new List<TMP_FontAsset>();
                // ほかの動的フォールバック(前に作ったもの)と、消えたものは外す
                int removed = fa.fallbackFontAssetTable.RemoveAll(x => x == null || (x != dyn && x.name.EndsWith("_Dynamic")));
                if (removed > 0) EditorUtility.SetDirty(fa);
                if (fa.fallbackFontAssetTable.Contains(dyn)) continue;
                fa.fallbackFontAssetTable.Add(dyn);
                EditorUtility.SetDirty(fa);
                added.Add(fa.name);
            }
            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("平方麻雀", "動的フォールバック: " + dyn.name + "\n\n予備に入れたフォント:\n  " + (added.Count > 0 ? string.Join("\n  ", added) : "(すでに入っていました)")
                + "\n\nあとで TMP フォントを作り直したら、もう一度このメニューを実行してください。", "OK");
        }

        static string OutPath(Font f)
        {
            string src = AssetDatabase.GetAssetPath(f);
            return Path.Combine(Path.GetDirectoryName(src), f.name + "_SDF.asset").Replace('\\', '/');
        }

        /// <summary>1つ作る。うまくいけば null、だめなら理由</summary>
        static string MakeOne(Font font, string outPath)
        {
            bool seg = font.name.ToLowerInvariant().Contains("dseg");
            string chars;
            if (seg) chars = SEG_CHARS;
            else
            {
                var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(CHARS);
                if (ta == null) return "文字の一覧 SqmCharacters.txt が見つからない";
                chars = ta.text;
            }
            int size = seg ? 512 : 2048;
            var fa = TMP_FontAsset.CreateFontAsset(font, seg ? 90 : 64, seg ? 9 : 7, GlyphRenderMode.SDFAA, size, size, AtlasPopulationMode.Dynamic, false);
            if (fa == null) return "作れなかった";
            fa.name = Path.GetFileNameWithoutExtension(outPath);
            fa.TryAddCharacters(chars, out string missing);
            // 静的にする(元のフォントをビルドに入れない)
            fa.atlasPopulationMode = AtlasPopulationMode.Static;
            AssetDatabase.CreateAsset(fa, outPath);
            foreach (var tex in fa.atlasTextures)
            {
                if (tex == null) continue;
                tex.name = fa.name + " Atlas";
                AssetDatabase.AddObjectToAsset(tex, fa);
            }
            fa.material.name = fa.name + " Material";
            AssetDatabase.AddObjectToAsset(fa.material, fa);
            EditorUtility.SetDirty(fa);
            if (!string.IsNullOrEmpty(missing)) Debug.LogWarning("[平方麻雀] " + fa.name + " に入らなかった文字: " + missing);
            Debug.Log("[平方麻雀] TMP フォントを作りました: " + outPath + "(" + fa.characterTable.Count + " 文字)");
            return null;
        }
    }
}
