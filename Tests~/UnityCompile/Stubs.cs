// コンパイル確認用の最小限の代わり(UdonSharp・TextMeshPro はこの環境に DLL がないため)
namespace UdonSharp
{
    public class UdonSharpBehaviour : UnityEngine.MonoBehaviour
    {
        public virtual void Interact() { }
        public virtual void OnDeserialization() { }
        public void RequestSerialization() { }
        public void SendCustomEvent(string eventName) { }
        public void SendCustomEventDelayedSeconds(string eventName, float delaySeconds) { }
        public void SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, string eventName, params object[] args) { }
        public virtual void OnOwnershipTransferred(VRC.SDKBase.VRCPlayerApi player) { }
        public virtual void OnPlayerLeft(VRC.SDKBase.VRCPlayerApi player) { }
        public virtual void OnDrop() { }
        public bool DisableInteractive { get; set; }
    }
    public enum BehaviourSyncMode { Any, None, Continuous, Manual, NoVariableSync }
    [System.AttributeUsage(System.AttributeTargets.Class)] public class UdonBehaviourSyncModeAttribute : System.Attribute { public UdonBehaviourSyncModeAttribute(BehaviourSyncMode m) { } }
    [System.AttributeUsage(System.AttributeTargets.Method)] public class RecursiveMethodAttribute : System.Attribute { }
    [System.AttributeUsage(System.AttributeTargets.Field)] public class UdonSyncedAttribute : System.Attribute { }
}
namespace UdonSharpEditor
{
    public static class UdonSharpUndo { public static T AddComponent<T>(UnityEngine.GameObject g) where T : UdonSharp.UdonSharpBehaviour => g.AddComponent<T>(); }
    public static class UdonSharpEditorUtility
    {
        public static void CopyProxyToUdon(UdonSharp.UdonSharpBehaviour b) { }
        public static UnityEngine.ScriptableObject GetUdonSharpProgramAsset(System.Type t) => null;
    }
}
namespace TMPro
{
    public enum AtlasPopulationMode { Static, Dynamic }
    public class TMP_Character { }
    public class TMP_FontAsset : UnityEngine.ScriptableObject
    {
        public static TMP_FontAsset CreateFontAsset(UnityEngine.Font font, int samplingPointSize, int atlasPadding, UnityEngine.TextCore.LowLevel.GlyphRenderMode renderMode, int atlasWidth, int atlasHeight, AtlasPopulationMode atlasPopulationMode = AtlasPopulationMode.Dynamic, bool enableMultiAtlasSupport = true) => null;
        public bool TryAddCharacters(string characters, out string missingCharacters, bool includeFontFeatures = false) { missingCharacters = null; return true; }
        public AtlasPopulationMode atlasPopulationMode { get; set; }
        public UnityEngine.Texture2D[] atlasTextures;
        public UnityEngine.Material material;
        public System.Collections.Generic.List<TMP_Character> characterTable;
        public System.Collections.Generic.List<TMP_FontAsset> fallbackFontAssetTable;
    }
    public enum TextAlignmentOptions { Center, Right, Left, TopLeft }
    public enum TextOverflowModes { Overflow, Ellipsis }
    public class TextMeshPro : UnityEngine.MonoBehaviour
    {
        public string text; public TMP_FontAsset font; public UnityEngine.Material fontSharedMaterial; public float fontSize; public bool enableAutoSizing;
        public TextAlignmentOptions alignment; public UnityEngine.Color color;
        public bool enableWordWrapping; public TextOverflowModes overflowMode; public float fontSizeMin, fontSizeMax, lineSpacing;
        public UnityEngine.RectTransform rectTransform => null;
    }
}
namespace VRC.Udon.Common.Interfaces
{
    public enum NetworkEventTarget { All, Owner, Others, Self }
}
namespace VRC.SDK3.UdonNetworkCalling
{
    [System.AttributeUsage(System.AttributeTargets.Method)] public class NetworkCallableAttribute : System.Attribute { }
    public static class NetworkCalling { public static VRC.SDKBase.VRCPlayerApi CallingPlayer => null; }
}
