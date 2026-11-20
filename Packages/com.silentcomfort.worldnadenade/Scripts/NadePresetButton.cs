using TMPro;
using UdonSharp;
using UnityEngine;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// 操作パネルの「音色を直接選ぶ」ボタン1つぶん。
    ///
    /// - NadeControlPanel の Preset Buttons Root の直下に置くと、上から順に プリセット 0, 1, 2 … が割り当てられる
    /// - ボタンの文字はプリセット名に自動で書き換わり、プリセット数より多いボタンは自動で隠れる
    /// - 手のひらと手の甲で別の音が入っているプリセットだけ、印（Dual Mark）を表示する
    /// - Button の On Click → このボタン自身の UdonBehaviour.SendCustomEvent("_OnClick")
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadePresetButton : UdonSharpBehaviour
    {
        [Tooltip("プリセット名を表示する TMP（ボタンの Text）")]
        [SerializeField] private TextMeshProUGUI label;
        [Tooltip("選択中のときだけ表示（枠・チェックマークなど。任意）")]
        [SerializeField] private GameObject showWhenSelected;
        [Tooltip("手のひらと手の甲で別の音が入っているプリセットのときだけ表示する印（任意）")]
        [SerializeField] private GameObject dualMark;

        private NadeControlPanel panel;
        private int index = -1;

        /// NadeControlPanel が起動時に呼ぶ
        public void _Setup(NadeControlPanel owner, int presetIndex, string presetName)
        {
            panel = owner;
            index = presetIndex;
            if (label != null) label.text = presetName;
            gameObject.SetActive(true);
        }

        /// 表示の言語が変わったとき、NadeControlPanel から音色名だけを書き換える
        public void _SetLabel(string presetName)
        {
            if (label != null) label.text = presetName;
        }

        /// 手のひらと手の甲で別の音が入っているかを表示に反映する
        public void _SetDual(bool dual)
        {
            if (dualMark != null) dualMark.SetActive(dual);
        }

        /// 対応するプリセットが無いボタンを隠す
        public void _Hide()
        {
            index = -1;
            gameObject.SetActive(false);
        }

        public void _SetSelected(bool selected)
        {
            if (showWhenSelected != null) showWhenSelected.SetActive(selected);
        }

        public void _OnClick()
        {
            if (panel == null || index < 0) return;
            panel._OnPresetSelected(index);
        }
    }
}
