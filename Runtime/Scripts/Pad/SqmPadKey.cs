using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>パッドの押せる所。クリック(Interact)すると、SqmPad.OnKey(code) を呼ぶ</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmPadKey : UdonSharpBehaviour
    {
        public SqmPad pad;
        public int code;

        public override void Interact() { Press(); }

        /// <summary>押す(手で触ったときと、ビームで押したとき)</summary>
        public void Press()
        {
            if (pad != null) pad.OnKey(code);
        }
    }
}
