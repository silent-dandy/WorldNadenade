using TMPro;
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// なで音タブレットの壁掛けの置き台（クレードル）。
    ///
    /// - 背板の前面に「タブレットを戻す」の UI ボタン（World Space Canvas + VRC Ui Shape）を置く。VR・デスクトップともレーザー（クリック）で押す
    /// - ボタンは、タブレットを持ち出している間だけ表示する。切り替えはタブレット（NadeTabletPanel）から呼ばれる
    ///   持ち上げたとき（OnPickup）→ 表示 / 置き台に戻ったとき → 非表示。毎フレームの処理は無い
    /// - 置き台1つにタブレット1台。同期なし（戻るのは押した人の画面のタブレットだけ）
    /// - 文字はタブレットから頼まれて、VRChat の表示言語に合わせて差し替える（翻訳表にある文字だけ）
    ///
    /// UI からの呼び出し（生成ツールが配線する）
    ///   → このスクリプトの UdonBehaviour.SendCustomEvent で _OnResetPressed
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadeTabletCradle : UdonSharpBehaviour
    {
        [Header("Connection（この置き台に戻すタブレット）")]
        [SerializeField] private NadeTabletPanel tablet;

        [Header("Reset button（タブレットを持ち出している間だけ表示）")]
        [Tooltip("「タブレットを戻す」の UI（Canvas ごと）。非表示の間はレーザーも当たらない")]
        [SerializeField] private GameObject resetPanel;

        [Header("Feedback")]
        [Tooltip("押したときの音（任意。生成ツールがタブレットと同じクリック音を設定する）。表示を消しても鳴り切るよう、Reset Panel の外に置く")]
        [SerializeField] private AudioSource clickSource;
        [SerializeField] private AudioClip clickClip;
        [SerializeField, Range(0f, 1f)] private float clickVolume = 0.6f;

        private bool docked = true;

        // 言語で差し替える文字（最初に頼まれたときの文字が翻訳表にある TMP だけ）と、その原文
        private TextMeshProUGUI[] localizedTexts;
        private string[] localizedSources;

        private void Start()
        {
            if (tablet == null) Debug.LogWarning("[World Nadenade] 置き台の Tablet が未設定です: " + gameObject.name);
            _ApplyDocked(); // 入室時はタブレットが載っているので隠す
        }

        /// NadeTabletPanel から呼ばれる。true = 置き台に載っている（ボタンを隠す）、false = 持ち出し中（ボタンを出す）
        public void _SetDocked(bool value)
        {
            if (value == docked) return;
            docked = value;
            _ApplyDocked();
        }

        private void _ApplyDocked()
        {
            if (resetPanel != null && resetPanel.activeSelf == docked) resetPanel.SetActive(!docked);
        }

        /// NadeTabletPanel から呼ばれる（起動時と、表示の言語が変わったとき）
        public void _ApplyLanguage(NadeManager manager)
        {
            if (manager == null) return;
            if (localizedTexts == null)
            {
                // 最初の1回だけ、子の TMP（非表示の Reset Panel も含む）から翻訳表に原文がある文字を集める
                TextMeshProUGUI[] all = GetComponentsInChildren<TextMeshProUGUI>(true);
                TextMeshProUGUI[] texts = new TextMeshProUGUI[all.Length];
                string[] sources = new string[all.Length];
                int n = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (all[i] == null) continue;
                    string source = all[i].text;
                    if (!manager._HasTranslation(source)) continue;
                    texts[n] = all[i];
                    sources[n] = source;
                    n++;
                }
                localizedTexts = new TextMeshProUGUI[n];
                localizedSources = new string[n];
                for (int i = 0; i < n; i++)
                {
                    localizedTexts[i] = texts[i];
                    localizedSources[i] = sources[i];
                }
            }
            for (int i = 0; i < localizedTexts.Length; i++)
            {
                if (localizedTexts[i] != null) localizedTexts[i].text = manager._Tr(localizedSources[i]);
            }
        }

        /// 「タブレットを戻す」ボタン（UI の onClick → SendCustomEvent）
        public void _OnResetPressed()
        {
            if (docked) return;
            if (clickSource != null && clickClip != null) clickSource.PlayOneShot(clickClip, clickVolume);
            if (tablet == null)
            {
                Debug.LogWarning("[World Nadenade] 置き台の Tablet が未設定のため、戻せません: " + gameObject.name);
                return;
            }
            tablet._ReturnHome(); // 戻ると _SetDocked(true) が呼ばれてボタンが消える
        }
    }
}
