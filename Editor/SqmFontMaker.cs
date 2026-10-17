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

        [MenuItem("Tools/平方麻雀/TMP フォントを作る")]
        public static void Make()
        {
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
            var fa = TMP_FontAsset.CreateFontAsset(font, seg ? 90 : 80, seg ? 9 : 8, GlyphRenderMode.SDFAA, size, size, AtlasPopulationMode.Dynamic, false);
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
