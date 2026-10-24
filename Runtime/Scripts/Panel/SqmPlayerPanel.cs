using TMPro;
using UdonSharp;
using UnityEngine;

namespace MonakaLabo.SquareMahjong
{
    /// <summary>
    /// プレイヤーパネル(縦型・横型)。ワールドに置くかどうかは作者の自由(メニュー「平方麻雀 → プレイヤーパネルを置く」)。
    /// SqmPlayerCam の絵(正方形)を、自分の形に切り抜いた板に映し、斜めの半透明の帯の上に 名前・席風 と セットの情報 を重ねる。
    /// NPC の番は、映像の代わりに「[NPC PLAYING]」と出す。
    /// 自分の画面だけで切れる(PanelOn / PanelOff / PanelToggle。LuraSwitch などの Udon イベントから呼べる)。
    /// 全部のパネルが切られていれば、カメラも止まる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class SqmPlayerPanel : UdonSharpBehaviour
    {
        public SqmTable table;
        public SqmPlayerCam cam;
        public GameObject view;              // 中身の全体(自分の画面で切るときに隠す)
        public GameObject video;             // 映像の板
        public GameObject cover;             // 映像の代わりの暗い板(NPC の番・試合が無いとき)
        public TextMeshPro coverText;        // 「[NPC PLAYING]」
        public TextMeshPro nameText, windText, line1Text, line2Text;

        [System.NonSerialized] public bool on = true;
        private float wait;

        public void PanelOn() { SetOn(true); }
        public void PanelOff() { SetOn(false); }
        public void PanelToggle() { SetOn(!on); }

        private void SetOn(bool v)
        {
            on = v;
            if (view != null && view.activeSelf != v) view.SetActive(v);
        }

        /// <summary>自分の画面で出ているか(切られていない・ワールドで非表示にされていない)</summary>
        public bool IsShowing() { return on && gameObject.activeInHierarchy; }

        private void Update()
        {
            wait -= Time.deltaTime;
            if (wait > 0 || !on) return;
            wait = 0.2f;
            if (table == null) return;
            int seat = table.SpotlightSeat();
            bool human = cam != null && cam.human && cam.seat == seat && seat >= 0;
            Show(video, human);
            Show(cover, !human);
            if (seat < 0)
            {
                Set(coverText, "平方麻雀");
                Set(nameText, ""); Set(windText, ""); Set(line1Text, ""); Set(line2Text, "");
                return;
            }
            Set(coverText, human ? "" : "[NPC PLAYING]");
            Set(nameText, table.SeatDisplayName(seat));
            Set(windText, table.PanelWind(seat));
            Set(line1Text, table.PanelLine1());
            Set(line2Text, table.PanelLine2());
        }

        private void Show(GameObject g, bool v) { if (g != null && g.activeSelf != v) g.SetActive(v); }

        /// <summary>文字が変わったときだけ書き直す(TextMeshPro は同じ文字でも作り直すので)</summary>
        private void Set(TextMeshPro t, string s) { if (t != null && t.text != s) t.text = s; }
    }
}
