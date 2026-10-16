using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>卓のボタン。クリック(Interact)すると、SqmTable の eventName のメソッドを呼ぶ</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmButton : UdonSharpBehaviour
    {
        public SqmTable table;
        public string eventName;

        public override void Interact()
        {
            if (table != null && eventName != null && eventName.Length > 0) table.SendCustomEvent(eventName);
        }
    }
}
