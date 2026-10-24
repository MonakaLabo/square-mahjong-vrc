using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// プレイヤーパネル(SqmPlayerPanel)のカメラ。シーンに1台だけ置き、縦型・横型のパネルが同じ絵を自分の形に切り抜いて使う。
    /// 映すのは卓が決めた人(SqmTable.SpotlightSeat: ふだんは手番の人、和了の画面は和了った人、途中流局はその原因の人)。
    /// その人の頭に追従して、正面から少し右・少し下から見上げる位置に置く(手や頭の揺れでガタつかないよう、なめらかにならす)。
    /// 映すレイヤーは鏡と同じ(MirrorReflection・Player など)。自分の画面では自分のアバターの頭が消されている(PlayerLocal)ので、
    /// MirrorReflection の方を映す。そうしないと、自分の番に首なしで映る。
    /// 自分の画面でパネルが全部切られている(PanelOff・非表示)か、映す人が NPC・いないときは、カメラを止める。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmPlayerCam : UdonSharpBehaviour
    {
        public SqmTable table;
        public Camera cam;
        public SqmPlayerPanel[] panels;

        public const float DIST = 0.85f;     // 顔からの距離(m)
        public const float SIDE = 0.28f;     // 右へずらす
        public const float BELOW = 0.2f;     // 頭より下から
        public const float AIM_DOWN = 0.12f; // 見る所(頭から少し下 = 顔と胸のあいだ)
        public const float SMOOTH = 4f;      // なめらかさ(大きいほど早く追いつく)

        [System.NonSerialized] public int seat = -1;   // 映している卓の席(-1 = いない)
        [System.NonSerialized] public bool human;      // 映しているのが人か(NPC なら映像を出さない)
        private int shownPid;
        private bool placed;
        private float checkWait;

        private void Start()
        {
            if (cam != null) cam.enabled = false;
        }

        /// <summary>自分の画面で、どれかのパネルが出ているか</summary>
        private bool AnyPanelOn()
        {
            if (panels == null) return false;
            for (int i = 0; i < panels.Length; i++) if (panels[i] != null && panels[i].IsShowing()) return true;
            return false;
        }

        private void Update()
        {
            checkWait -= Time.deltaTime;
            if (checkWait > 0) return;
            checkWait = 0.1f;
            seat = table != null ? table.SpotlightSeat() : -1;
            int pid = seat >= 0 ? table.SeatPlayer(seat) : 0;
            human = pid != 0 && Utilities.IsValid(VRCPlayerApi.GetPlayerById(pid));
            if (pid != shownPid) { shownPid = pid; placed = false; }   // 映す人が変わったら、なめらかにせずに切り替える
            bool run = human && AnyPanelOn();
            if (cam != null && cam.enabled != run) cam.enabled = run;
        }

        public override void PostLateUpdate()
        {
            if (cam == null || !cam.enabled) return;
            VRCPlayerApi p = VRCPlayerApi.GetPlayerById(shownPid);
            if (!Utilities.IsValid(p)) return;
            Vector3 head = p.GetBonePosition(HumanBodyBones.Head);
            if (head == Vector3.zero) head = p.GetPosition() + Vector3.up * 1.5f;   // 人型でないアバター
            Quaternion yaw = p.GetRotation();
            Vector3 fwd = yaw * Vector3.forward, right = yaw * Vector3.right;
            Vector3 pos = head + fwd * DIST + right * SIDE - Vector3.up * BELOW;
            Vector3 aim = head - Vector3.up * AIM_DOWN;
            Quaternion rot = Quaternion.LookRotation(aim - pos, Vector3.up);
            Transform tr = cam.gameObject.transform;   // U# で使えるか確かな GameObject を通す
            if (!placed) { tr.SetPositionAndRotation(pos, rot); placed = true; return; }
            float k = 1f - Mathf.Exp(-SMOOTH * Time.deltaTime);
            tr.SetPositionAndRotation(Vector3.Lerp(tr.position, pos, k), Quaternion.Slerp(tr.rotation, rot, k));
        }
    }
}
