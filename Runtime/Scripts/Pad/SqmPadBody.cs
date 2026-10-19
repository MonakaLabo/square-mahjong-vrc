using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 席ごとのパッドの本体(板)。卓の右寄りに、少し傾けて付いている。
    /// 設定で「パッドを掴めるようにする」を ON にした本人だけが掴める。離した所に留まり、元の場所の近くで離すとくっつく。
    /// 位置の同期は同じ GameObject の VRCObjectSync が受け持つ。中身(タブ・手牌など)は SqmPad の1組を、座った人の手元でここに付ける。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmPadBody : UdonSharpBehaviour
    {
        public SqmTable table;
        public int seat;
        public Transform contentAnchor;     // SqmPad の中身を付ける所
        public VRC_Pickup pickup;
        public GameObject idleRoot;         // 待機中の表示(平方麻雀・JOIN・NEW GAME)
        public GameObject joinButton, newButton;
        public TextMeshPro nameText;        // ほかの人の席のパッドに出す名前
        public GameObject[] visuals;        // 板の見た目(隠すときに消す)
        public Collider[] grips;            // 掴む所(板の左右の端だけ。真ん中はボタンを押すじゃまになるので付けない)

        public const float SNAP = 0.3f;     // 元の場所からこの距離(m)以内で離すと、くっつく
        private bool visible = true, grab;
        private Vector3 homePos;
        private Quaternion homeRot;
        private bool homeSet;

        private void Start() { SetHome(); }

        private void SetHome()
        {
            if (homeSet) return;
            homeSet = true;
            homePos = transform.position;
            homeRot = transform.rotation;
        }

        public override void OnDrop()
        {
            SetHome();
            if (Vector3.Distance(transform.position, homePos) <= SNAP) transform.SetPositionAndRotation(homePos, homeRot);
        }

        /// <summary>自分が持ち主なら、元の場所に戻す(Reset のとき、全員でこれを呼ぶ)</summary>
        public void ReturnHomeIfOwner()
        {
            SetHome();
            if (!Networking.IsOwner(gameObject)) return;
            if (pickup != null && pickup.IsHeld) pickup.Drop();
            transform.SetPositionAndRotation(homePos, homeRot);
        }

        /// <summary>自分で元の場所に戻す(持ち主になってから動かす)</summary>
        public void ReturnHome()
        {
            SetHome();
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            if (pickup != null && pickup.IsHeld) pickup.Drop();
            transform.SetPositionAndRotation(homePos, homeRot);
        }

        public void SetGrabbable(bool on)
        {
            grab = on;
            UpdateCollider();
            if (pickup == null || pickup.pickupable == on) return;
            pickup.pickupable = on;
            if (!on && pickup.IsHeld) pickup.Drop();
        }

        /// <summary>板ごと出す/隠す(手元だけ。位置の同期には関わらない)</summary>
        public void SetVisible(bool on)
        {
            if (visuals != null)
                for (int i = 0; i < visuals.Length; i++) if (visuals[i] != null && visuals[i].activeSelf != on) visuals[i].SetActive(on);
            visible = on;
            UpdateCollider();
        }

        /// <summary>掴む判定は、見えていて掴める設定のときだけ(それ以外はボタンを押すじゃまをしないよう消す)</summary>
        private void UpdateCollider()
        {
            bool on = visible && grab;
            if (grips == null) return;
            for (int i = 0; i < grips.Length; i++) if (grips[i] != null && grips[i].enabled != on) grips[i].enabled = on;
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
