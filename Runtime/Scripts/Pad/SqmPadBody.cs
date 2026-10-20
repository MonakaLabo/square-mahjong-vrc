using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 席ごとのパッドの本体(板)。卓の右寄りに、少し傾けて付いている。
    /// 設定で「パッドを掴めるようにする」を ON にした本人だけが、VR の手のグリップで左右の端を掴める。
    /// 掴む処理は自前(VRCPickup は使わない。VRCPickup は掴んだ手に板を寄せてしまい、掴んだ所を保てない)。
    /// 掴んだ瞬間の手と板の位置関係のまま動かし、離したらその場に置く。元の場所の近くで離すとくっつく。
    /// 位置の同期は同じ GameObject の VRCObjectSync が受け持つ(掴んだ人が持ち主になる)。
    /// 中身(タブ・手牌など)は SqmPad の1組を、座った人の手元でここに付ける。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmPadBody : UdonSharpBehaviour
    {
        public SqmTable table;
        public int seat;
        public Transform contentAnchor;     // SqmPad の中身を付ける所
        public GameObject idleRoot;         // 待機中の表示(平方麻雀・JOIN・NEW GAME)
        public GameObject joinButton, newButton;
        public TextMeshPro nameText;        // ほかの人の席のパッドに出す名前
        public GameObject[] visuals;        // 板の見た目(隠すときに消す)

        public const float W = 0.42f, H = 0.27f;   // 板の大きさ(m)
        public const float GRIP_IN = 0.198f, GRIP_OUT = 0.235f;   // 掴める所: 板の中心から左右へこの範囲(m)。真ん中はボタンを押す所なので掴まない
        public const float GRIP_REACH = 0.05f;   // 掴める所から、これだけ(m)離れていても掴める
        public const float SNAP = 0.3f;     // 元の場所からこの距離(m)以内で離すと、くっつく
        private bool visible = true, grab;
        private Vector3 homePos;
        private Quaternion homeRot;
        private bool homeSet;

        private bool holding;
        private int holdHand;               // 0 = 右手, 1 = 左手
        private Vector3 holdPos;            // 手から見た板の位置と向き
        private Quaternion holdRot;

        private void Start() { SetHome(); }

        private void SetHome()
        {
            if (homeSet) return;
            homeSet = true;
            homePos = transform.position;
            homeRot = transform.rotation;
        }

        /// <summary>グリップを握った/離した。握った手が板の端の近くにあれば掴む</summary>
        public override void InputGrab(bool value, UdonInputEventArgs args)
        {
            int h = args.handType == HandType.LEFT ? 1 : 0;
            if (!value)
            {
                if (holding && h == holdHand) Release();
                return;
            }
            if (holding || !grab || !visible) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp) || !lp.IsUserInVR()) return;
            VRCPlayerApi.TrackingData td = HandData(lp, h);
            if (!InGrip(td.position)) return;
            SetHome();
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(lp, gameObject);
            holdHand = h;
            Quaternion inv = Quaternion.Inverse(td.rotation);
            holdPos = inv * (transform.position - td.position);
            holdRot = inv * transform.rotation;
            holding = true;
        }

        /// <summary>手の位置が、板の左右の端(掴める所)の近くか</summary>
        private bool InGrip(Vector3 p)
        {
            Vector3 q = transform.InverseTransformPoint(p);
            float ax = Mathf.Abs(q.x);
            return ax >= GRIP_IN - GRIP_REACH && ax <= GRIP_OUT + GRIP_REACH && Mathf.Abs(q.y) <= H / 2 + GRIP_REACH && Mathf.Abs(q.z) <= GRIP_REACH * 2;
        }

        public override void PostLateUpdate()
        {
            if (!holding) return;
            VRCPlayerApi lp = Networking.LocalPlayer;
            if (!Utilities.IsValid(lp)) { holding = false; return; }
            VRCPlayerApi.TrackingData td = HandData(lp, holdHand);
            transform.SetPositionAndRotation(td.position + td.rotation * holdPos, td.rotation * holdRot);
        }

        private VRCPlayerApi.TrackingData HandData(VRCPlayerApi lp, int h)
        {
            return lp.GetTrackingData(h == 1 ? VRCPlayerApi.TrackingDataType.LeftHand : VRCPlayerApi.TrackingDataType.RightHand);
        }

        /// <summary>離した: その場に置く。元の場所の近くなら、くっつける</summary>
        private void Release()
        {
            holding = false;
            SetHome();
            if (Vector3.Distance(transform.position, homePos) <= SNAP) transform.SetPositionAndRotation(homePos, homeRot);
        }

        /// <summary>自分が持ち主なら、元の場所に戻す(Reset のとき、全員でこれを呼ぶ)</summary>
        public void ReturnHomeIfOwner()
        {
            SetHome();
            if (!Networking.IsOwner(gameObject)) return;
            holding = false;
            transform.SetPositionAndRotation(homePos, homeRot);
        }

        /// <summary>自分で元の場所に戻す(持ち主になってから動かす)</summary>
        public void ReturnHome()
        {
            SetHome();
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            holding = false;
            transform.SetPositionAndRotation(homePos, homeRot);
        }

        public void SetGrabbable(bool on)
        {
            grab = on;
            if (!on && holding) Release();
        }

        /// <summary>板ごと出す/隠す(手元だけ。位置の同期には関わらない)</summary>
        public void SetVisible(bool on)
        {
            if (visuals != null)
                for (int i = 0; i < visuals.Length; i++) if (visuals[i] != null && visuals[i].activeSelf != on) visuals[i].SetActive(on);
            visible = on;
            if (!on && holding) Release();
        }

        /// <summary>中身の無いときの表示(待機中・ほかの人の席)</summary>
        public void ShowIdle(bool idle, bool join, bool newGame, string name)
        {
            if (idleRoot != null && idleRoot.activeSelf != idle) idleRoot.SetActive(idle);
            if (joinButton != null && joinButton.activeSelf != join) joinButton.SetActive(join);
            if (newButton != null && newButton.activeSelf != newGame) newButton.SetActive(newGame);
            if (nameText != null) nameText.text = name;
        }
    }
}
