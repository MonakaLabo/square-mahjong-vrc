using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 点数の推移(パッドの点数タブの折れ線グラフ用)。試合の始めの点数と、局が終わるたび(本場も1局)の各家の点数を並べて持つ。
    /// ホスト(卓の同期の持ち主)が書いて、局が終わったときだけ送る(卓の同期に入れると、毎手いっしょに送ることになるので別にする)。
    /// 途中から来た人にも、VRChat が最新の値を届ける。
    /// 線は 平方麻雀 = 卓の席 0〜7、普通の麻雀 = エンジンの席 0〜(人数-1)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class SqmScoreLog : UdonSharpBehaviour
    {
        public SqmTable table;

        public const int MAXL = 8;            // 線の数の上限
        public const int MAX_POINTS = 96;     // 点の数の上限(超えたら、それ以上は足さない)

        [UdonSynced] public int lgMatch;      // どの試合の記録か(SqmTable の sMatch)
        [UdonSynced] public int lgLines;      // 線の数(平方 8 / 四人 4 / 三人 3)
        [UdonSynced] public int lgN;          // 点の数(0 = まだ無い)
        [UdonSynced] public int[] lgScore = new int[MAXL * MAX_POINTS];   // [点 * MAXL + 線]

        private void Own()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            if (lgScore == null || lgScore.Length != MAXL * MAX_POINTS) lgScore = new int[MAXL * MAX_POINTS];
        }

        /// <summary>ホスト: 試合の始め。最初の点(開始の点数)を入れる</summary>
        public void HostStart(int match, int lines, int[] score)
        {
            Own();
            lgMatch = match;
            lgLines = lines > MAXL ? MAXL : lines;
            lgN = 0;
            Add(score);
        }

        /// <summary>ホスト: 局が終わった。そのときの点数を足す</summary>
        public void HostAppend(int match, int[] score)
        {
            if (match != lgMatch || lgN == 0) return;
            Own();
            Add(score);
        }

        /// <summary>ホスト: 試合が無くなった</summary>
        public void HostClear(int match)
        {
            Own();
            lgMatch = match;
            lgN = 0;
            RequestSerialization();
        }

        private void Add(int[] score)
        {
            if (lgN < MAX_POINTS)
            {
                for (int l = 0; l < lgLines && l < score.Length; l++) lgScore[lgN * MAXL + l] = score[l];
                lgN++;
            }
            RequestSerialization();
            if (table != null) table.OnScoreLog();
        }

        public int At(int point, int line) { return lgScore[point * MAXL + line]; }

        public override void OnDeserialization()
        {
            if (table != null) table.OnScoreLog();
        }
    }
}
