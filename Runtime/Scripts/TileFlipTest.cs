using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// 同期の動作確認用。クリック(Interact)すると、全員の画面で牌が裏返る。
    /// 後からインスタンスに入ってきた人にも、同じ向きで見える。
    /// 置き方: Collider の付いた物にこのスクリプトを付け、裏返したい物を tile に入れる(空なら自分自身が回る)。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class TileFlipTest : UdonSharpBehaviour
    {
        [SerializeField] private Transform tile;

        [UdonSynced] private bool faceUp = true;

        private void Start()
        {
            Apply();
        }

        public override void Interact()
        {
            // 同期する値を書き換えられるのはオーナーだけなので、押した人がオーナーになる
            if (!Networking.IsOwner(gameObject)) Networking.SetOwner(Networking.LocalPlayer, gameObject);
            faceUp = !faceUp;
            Apply();
            RequestSerialization();
        }

        public override void OnDeserialization()
        {
            Apply();
        }

        private void Apply()
        {
            Transform t = tile != null ? tile : transform;
            t.localRotation = faceUp ? Quaternion.identity : Quaternion.Euler(180f, 0f, 0f);
        }
    }
}
