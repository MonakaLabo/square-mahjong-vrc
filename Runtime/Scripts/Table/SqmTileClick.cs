using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>手牌の牌に付ける。クリック(Interact)すると、その牌を選ぶ</summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmTileClick : UdonSharpBehaviour
    {
        public SqmTable table;
        /// <summary>手牌の枠の番号(席 * 28 + 何枚目)</summary>
        public int index;

        public override void Interact()
        {
            if (table != null) table.ClickTile(index);
        }
    }
}
