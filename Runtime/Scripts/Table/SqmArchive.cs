using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 試合の操作の記録すべて。途中から来た人や、同期がずれた人のために、ホストが頼まれたときだけ送る。
    /// ふだんの同期は SqmTable の直近の記録(リング)だけで足りる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SqmArchive : UdonSharpBehaviour
    {
        public SqmTable table;

        [UdonSynced] public int aMatch;
        [UdonSynced] public int aCount;
        [UdonSynced] public int[] aLog = new int[0];

        public override void OnDeserialization()
        {
            if (table != null) table.OnArchive();
        }
    }
}
