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
    ///   向き   = 頭の真ん中が、正方形の絵の左上(左から 32%・上から 42%)に来るように向く(縦型・横型どちらの切り抜きでも左上)
    ///   画角   = 0.55h 四方が写るように、カメラと頭の距離から決める(頭が絵の縦の 1/4 くらい)
    /// なめらかにするのは、高さ(カメラと見る点)と画角だけ。前後左右はその場で合わせる(ならすと頭が絵の外へ出やすい)。
    /// 映す人が変わったときは、ならさずに切り替える。
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
        public const float HEAD_UP = 0.05f;              // 頭の骨から、頭の真ん中まで
        public const float VIEW = 0.55f;                 // 写す範囲(正方形の絵の縦横。頭が絵の縦の 1/4 くらい)
        // 頭の真ん中を置く所(正方形の絵の左から・上からの割合)。縦型(真ん中の 3:4)でも横型(真ん中の 16:9)でも左上に来る所
        public const float HEAD_U = 0.32f, HEAD_V = 0.42f;
        public const float SMOOTH = 4f;                  // なめらかさ(大きいほど早く追いつく)

        [System.NonSerialized] public int seat = -1;   // 映している卓の席(-1 = いない)
        [System.NonSerialized] public bool human;      // 映しているのが人か(NPC なら映像を出さない)
        private int shownPid;
        private bool placed;
        private float camY, aimY;   // なめらかにしている、カメラと見る点の高さ
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
            Vector3 bone = p.GetBonePosition(HumanBodyBones.Head);
            float h = p.GetAvatarEyeHeightAsMeters();
            if (bone == Vector3.zero) bone = feet + Vector3.up * ((h > 0.1f ? h : 1.5f) - HEAD_UP * 1.5f);   // 人型でないアバター
            if (h < 0.1f) h = Mathf.Max(0.3f, bone.y - feet.y);
            Vector3 head = bone + Vector3.up * (HEAD_UP * h);   // 頭の骨は首の付け根寄りなので、少し上を頭の真ん中とみなす
            // 体の正面から右へ YAW 度の向きに、頭から水平に DIST * h。高さは頭の高さを Y_MIN〜Y_MAX に収める
            Vector3 dir = Quaternion.AngleAxis(YAW, Vector3.up) * (p.GetRotation() * Vector3.forward);
            dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f) dir = Vector3.forward;
            dir = dir.normalized;   // U# は構造体を書き換えるメソッドが効かないことがあるので、代入にする
            float y = Mathf.Clamp(head.y, feet.y + Y_MIN * h, feet.y + Y_MAX * h);
            Vector3 pos = new Vector3(head.x, y, head.z) + dir * (DIST * h);
            // 写す範囲(正方形の絵の縦横 = VIEW * h)と、頭を絵の左上(HEAD_U, HEAD_V)に置くための、見る点のずらし
            Vector3 toHead = head - pos;
            if (toHead.sqrMagnitude < 0.0001f) toHead = -dir;
            Quaternion r0 = Quaternion.LookRotation(toHead, Vector3.up);
            float view = VIEW * h;
            Vector3 aim = head + (r0 * Vector3.right) * ((0.5f - HEAD_U) * view) - (r0 * Vector3.up) * ((0.5f - HEAD_V) * view);
            float fov = Mathf.Clamp(2f * Mathf.Atan(view * 0.5f / toHead.magnitude) * Mathf.Rad2Deg, 15f, 100f);
            // なめらかにするのは、高さ(カメラ・見る点)と画角だけ。前後左右はその場で合わせる(ならすと頭が絵の外へ出やすい)
            if (!placed)
            {
                camY = pos.y; aimY = aim.y;
                cam.fieldOfView = fov;
                placed = true;
            }
            else
            {
                float k = 1f - Mathf.Exp(-SMOOTH * Time.deltaTime);
                camY = Mathf.Lerp(camY, pos.y, k);
                aimY = Mathf.Lerp(aimY, aim.y, k);
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, k);
            }
            Vector3 cp = new Vector3(pos.x, camY, pos.z);
            Vector3 ap = new Vector3(aim.x, aimY, aim.z);
            Vector3 look = ap - cp;
            if (look.sqrMagnitude < 0.0001f) look = -dir;
            cam.gameObject.transform.SetPositionAndRotation(cp, Quaternion.LookRotation(look, Vector3.up));   // U# で使えるか確かな GameObject を通す
        }
    }
}
