// Unity / UdonSharp の代わり(このテストで SqmEngine をコンパイルするためだけの最小限)
namespace UnityEngine
{
    public class MonoBehaviour { }
}

namespace UdonSharp
{
    public class UdonSharpBehaviour : UnityEngine.MonoBehaviour { }
    public enum BehaviourSyncMode { Any, None, Continuous, Manual, NoVariableSync }
    [System.AttributeUsage(System.AttributeTargets.Class)]
    public class UdonBehaviourSyncModeAttribute : System.Attribute { public UdonBehaviourSyncModeAttribute(BehaviourSyncMode m) { } }
    [System.AttributeUsage(System.AttributeTargets.Method)]
    public class RecursiveMethodAttribute : System.Attribute { }
}
