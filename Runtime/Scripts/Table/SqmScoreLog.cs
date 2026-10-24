using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 点数の推移(パッドの点数タブの折れ線グラフ用)。試合の始めの点数と、局が終わるたび(本場も1局)の各家の点数を並べて持つ。
    /// 平方戦は、先発の和了があった時点にも1点足す(先発の点数はその場で払われるので)。
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
        [UdonSynced] public int[] lgKyoku = new int[MAX_POINTS];          // 点ごとの局(場 * 1000 + 局 * 100 + 本場。平方戦は 先発1・後発2 を 100000 の位に。開始の点は -1)
        [UdonSynced] public int[] lgWin = new int[MAXL];                  // 線ごとの和了の回数(終局の表に出す)
        [UdonSynced] public int[] lgDeal = new int[MAXL];                 // 線ごとの放銃の回数

        private void Own()
        {
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            if (lgScore == null || lgScore.Length != MAXL * MAX_POINTS) lgScore = new int[MAXL * MAX_POINTS];
            if (lgKyoku == null || lgKyoku.Length != MAX_POINTS) lgKyoku = new int[MAX_POINTS];
            if (lgWin == null || lgWin.Length != MAXL) lgWin = new int[MAXL];
            if (lgDeal == null || lgDeal.Length != MAXL) lgDeal = new int[MAXL];
        }

        /// <summary>ホスト: 試合の始め。最初の点(開始の点数)を入れる</summary>
        public void HostStart(int match, int lines, int[] score)
        {
            Own();
            lgMatch = match;
            lgLines = lines > MAXL ? MAXL : lines;
            lgN = 0;
            for (int l = 0; l < MAXL; l++) { lgWin[l] = 0; lgDeal[l] = 0; }
            Add(score, -1);
        }

        /// <summary>ホスト: 局が終わった。そのときの点数と、終わった局(場 * 1000 + 局 * 100 + 本場)を足す</summary>
        public void HostAppend(int match, int[] score, int kyoku)
        {
            if (match != lgMatch || lgN == 0) return;
            Own();
            Add(score, kyoku);
        }

        /// <summary>ホスト: 和了が1つあった(win = 和了った線、from = 放銃した線。ツモ・流し満貫は -1)。送るのは局の終わりの HostAppend といっしょ</summary>
        public void HostCount(int match, int win, int from)
        {
            if (match != lgMatch || lgN == 0) return;
            Own();
            if (win >= 0 && win < MAXL) lgWin[win]++;
            if (from >= 0 && from < MAXL) lgDeal[from]++;
            RequestSerialization();
        }

        public int WinsOf(int line) { return lgWin != null && line >= 0 && line < lgWin.Length ? lgWin[line] : 0; }
        public int DealsOf(int line) { return lgDeal != null && line >= 0 && line < lgDeal.Length ? lgDeal[line] : 0; }

        /// <summary>ホスト: 試合が無くなった</summary>
        public void HostClear(int match)
        {
            Own();
            lgMatch = match;
            lgN = 0;
            RequestSerialization();
        }

        private void Add(int[] score, int kyoku)
        {
            if (lgN < MAX_POINTS)
            {
                lgKyoku[lgN] = kyoku;
                for (int l = 0; l < lgLines && l < score.Length; l++) lgScore[lgN * MAXL + l] = score[l];
                lgN++;
            }
            RequestSerialization();
            if (table != null) table.OnScoreLog();
        }

        public int At(int point, int line) { return lgScore[point * MAXL + line]; }
        public int KyokuAt(int point) { return lgKyoku[point]; }

        public override void OnDeserialization()
        {
            if (table != null) table.OnScoreLog();
        }
    }
}
