using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 途中から来た人や、同期がずれた人のために、ホストが頼まれたときだけ送る記録。
    /// 今の局の始めの状態(aSnap。aBase 手目の時点)と、そこから aCount 手目までの操作(aLog)。
    /// 受け取った人は局の始めから復元して、その局の手だけを再生する。ふだんの同期は SqmTable の直近の記録(リング)だけで足りる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SqmArchive : UdonSharpBehaviour
    {
        public SqmTable table;

        [UdonSynced] public int aMatch;
        [UdonSynced] public int aCount;
        [UdonSynced] public int aBase;                 // aLog[0] は、試合の最初から数えて何手目か
        [UdonSynced] public int[] aSnap = new int[0];  // aBase 手目の時点の、局の始めの状態(SqmEngine.ksBuf)
        [UdonSynced] public int[] aLog = new int[0];

        public override void OnDeserialization()
        {
            if (table != null) table.OnArchive();
        }
    }
}
