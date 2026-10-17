// コンパイル確認用の最小限の代わり(UdonSharp・TextMeshPro はこの環境に DLL がないため)
namespace UdonSharp
{
    public class UdonSharpBehaviour : UnityEngine.MonoBehaviour
    {
        public virtual void Interact() { }
        public virtual void OnDeserialization() { }
        public void RequestSerialization() { }
        public void SendCustomEvent(string eventName) { }
        public void SendCustomNetworkEvent(VRC.Udon.Common.Interfaces.NetworkEventTarget target, string eventName, params object[] args) { }
        public virtual void OnOwnershipTransferred(VRC.SDKBase.VRCPlayerApi player) { }
        public virtual void OnPlayerLeft(VRC.SDKBase.VRCPlayerApi player) { }
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
    public class TMP_FontAsset : UnityEngine.ScriptableObject { }
    public enum TextAlignmentOptions { Center, Right }
    public enum TextOverflowModes { Overflow, Ellipsis }
    public class TextMeshPro : UnityEngine.MonoBehaviour
    {
        public string text; public TMP_FontAsset font; public float fontSize; public bool enableAutoSizing;
        public TextAlignmentOptions alignment; public UnityEngine.Color color;
        public bool enableWordWrapping; public TextOverflowModes overflowMode;
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
