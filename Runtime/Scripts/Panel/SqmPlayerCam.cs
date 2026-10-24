using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// プレイヤーパネル(SqmPlayerPanel)のカメラ。シーンに1台だけ置き、縦型・横型のパネルが同じ絵を自分の形に切り抜いて使う。
    /// 映すのは卓が決めた人(SqmTable.SpotlightSeat: ふだんは手番の人、和了の画面は和了った人、途中流局はその原因の人)。
    ///
    /// 置き方(アバターの大きさ・姿勢によらず、同じように収まるように。長さはどれもアバターの目の高さ h を単位にする):
    ///   高さ   = 頭の高さを、床から 0.5h〜0.8h に収めたもの(座っていれば真横から、立てば下から見上げ、寝転がれば上から見下ろす)
    ///   水平   = 体の正面から、アバターから見て右へ 20° の向きに、頭から水平に 0.3h
    ///   向き   = 頭の少し下(あご〜首)を向く
    ///   画角   = 頭から胸まで(0.4h)がちょうど入るように、カメラと頭の距離から決める
    /// 位置・向き・画角はなめらかにならす(手や頭の揺れでガタつかないように)。映す人が変わったときは、ならさずに切り替える。
    ///
    /// 映すレイヤーは鏡と同じ(MirrorReflection・Player など)。自分の画面では自分のアバターの頭が消されている(PlayerLocal)ので、
    /// MirrorReflection の方を映す。そうしないと、自分の番に首なしで映る。卓・パッド・牌・ホロ・パネル(レイヤー 22)は映さない。
    /// 自分の画面でパネルが全部切られている(PanelOff・非表示)か、映す人が NPC・いないときは、カメラを止める。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmPlayerCam : UdonSharpBehaviour
    {
        public SqmTable table;
        public Camera cam;
        public SqmPlayerPanel[] panels;

        // 長さは、アバターの目の高さ h を単位にする
        public const float Y_MIN = 0.5f, Y_MAX = 0.8f;   // カメラの高さ(床から)の範囲
        public const float DIST = 0.3f;                  // 頭からの水平の距離
        public const float YAW = 20f;                    // 体の正面から、アバターから見て右へ(度)
        public const float AIM_DOWN = 0.06f;             // 見る所(頭から少し下)
        public const float FRAME = 0.4f;                 // 縦に入れる範囲(頭から胸まで)
        public const float SMOOTH = 4f;                  // なめらかさ(大きいほど早く追いつく)

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
            Vector3 feet = p.GetPosition();
            Vector3 head = p.GetBonePosition(HumanBodyBones.Head);
            float h = p.GetAvatarEyeHeightAsMeters();
            if (head == Vector3.zero) head = feet + Vector3.up * (h > 0.1f ? h : 1.5f);   // 人型でないアバター
            if (h < 0.1f) h = Mathf.Max(0.3f, head.y - feet.y);
            // 体の正面から右へ YAW 度の向きに、頭から水平に DIST * h。高さは頭の高さを Y_MIN〜Y_MAX に収める
            Vector3 dir = Quaternion.AngleAxis(YAW, Vector3.up) * (p.GetRotation() * Vector3.forward);
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir = dir.normalized;   // U# は構造体を書き換えるメソッドが効かないことがあるので、代入にする
            float y = Mathf.Clamp(head.y, feet.y + Y_MIN * h, feet.y + Y_MAX * h);
            Vector3 pos = new Vector3(head.x, y, head.z) + dir * (DIST * h);
            Vector3 aim = head - Vector3.up * (AIM_DOWN * h);
            Vector3 look = aim - pos;
            if (look.sqrMagnitude < 0.0001f) look = -dir;
            Quaternion rot = Quaternion.LookRotation(look, Vector3.up);
            // 画角: 頭から胸まで(FRAME * h)が、カメラと頭の距離でちょうど入るように
            float fov = 2f * Mathf.Atan(FRAME * h * 0.5f / look.magnitude) * Mathf.Rad2Deg;
            fov = Mathf.Clamp(fov, 15f, 90f);
            Transform tr = cam.gameObject.transform;   // U# で使えるか確かな GameObject を通す
            if (!placed)
            {
                // 映す人が変わったときは、ならさずに切り替える
                tr.SetPositionAndRotation(pos, rot);
                cam.fieldOfView = fov;
                placed = true;
                return;
            }
            float k = 1f - Mathf.Exp(-SMOOTH * Time.deltaTime);
            tr.SetPositionAndRotation(Vector3.Lerp(tr.position, pos, k), Quaternion.Slerp(tr.rotation, rot, k));
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, k);
        }
    }
}
