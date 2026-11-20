using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// なで音ギミックの操作パネル（壁・手持ちメニューなど、何枚でも置ける）。
    ///
    /// - 状態は NadeManager だけが持ち、このパネルは表示と入力の窓口になる
    /// - どのパネルで操作しても、すべてのパネル・タブレットの表示がそろう
    /// - 左「聞こえる音」（なで音を聞く・自分なで・音量。自分の端末だけ）と、
    ///   右「なでる音」（音色・手のひら・手の甲の設定・入れ替え。なでた相手にも同じ音が聞こえる）に分かれる
    /// - 「使い方」ボタンで、パネルの中身が使い方の説明に切り替わる（各自の端末だけ。他の人の表示は変わらない）
    /// - Manager 欄が未設定のときは「未接続」の表示を出し、操作を受け付けない
    /// - 文字は VRChat の表示言語に合わせて差し替える。起動時に子の TMP の文字を原文として覚え、
    ///   NadeStrings の翻訳表にある文字だけを差し替える（書き換えた文言・表に無い文字はそのまま）
    ///
    /// UI からの呼び出し（Button の On Click / Slider の On Value Changed）
    ///   → このパネル自身の UdonBehaviour.SendCustomEvent で次のイベント名を指定する
    ///   _OnPowerPressed / _OnSelfPatPressed / _OnVolumeChanged / _OnPresetPressed（次の音色へ）
    ///   _OnHandMode0 / _OnHandMode1 / _OnHandMode2 / _OnSwapPressed / _OnHelpPressed / _OnHelpClosed
    ///   音色を直接選ぶボタンは NadePresetButton 側の _OnClick を呼ぶ
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadeControlPanel : UdonSharpBehaviour
    {
        [Header("Connection（シーンに置いたら WorldNadenade をドラッグ）")]
        [SerializeField] private NadeManager manager;

        [Header("Power")]
        [Tooltip("ON のときだけ表示（スイッチの On など）")]
        [SerializeField] private GameObject[] showWhenOn;
        [Tooltip("OFF のときだけ表示（スイッチの Off など）")]
        [SerializeField] private GameObject[] showWhenOff;
        [Tooltip("ON/OFF の文字表示（任意）")]
        [SerializeField] private TextMeshProUGUI switchLabel;
        [SerializeField] private string onText = "なで音：ON";
        [SerializeField] private string offText = "なで音：OFF";

        [Header("Self pat（自分で自分をなでても鳴らす。各自の端末だけの設定）")]
        [SerializeField] private GameObject[] showWhenSelfPatOn;
        [SerializeField] private GameObject[] showWhenSelfPatOff;
        [Tooltip("ON/OFF の文字表示（任意）")]
        [SerializeField] private TextMeshProUGUI selfPatLabel;
        [SerializeField] private string selfPatOnText = "自分なで：ON";
        [SerializeField] private string selfPatOffText = "自分なで：OFF";

        [Header("Volume")]
        [Tooltip("Min Value 0 / Max Value 1 の UI Slider")]
        [SerializeField] private Slider volumeSlider;
        [Tooltip("音量の % 表示（任意）")]
        [SerializeField] private TextMeshProUGUI volumeLabel;
        [SerializeField] private string volumePrefix = "音量 ";

        [Header("Preset（なでる側の音色。全員に同期される）")]
        [Tooltip("直下の子に NadePresetButton を置く。上から順にプリセット 0, 1, 2 … が割り当てられる")]
        [SerializeField] private Transform presetButtonsRoot;
        [Tooltip("選択中の音色名の表示（任意）")]
        [SerializeField] private TextMeshProUGUI presetLabel;
        [SerializeField] private string presetPrefix = "音色：";

        private NadePresetButton[] presetButtons;

        [Header("Hand side（手のひら・手の甲の設定。なでる側の設定で、全員に同期される）")]
        [Tooltip("手のひら・手の甲の設定の見出し・ボタン・入れ替えをまとめた親。WorldNadenade の Use Hand Side が OFF なら隠れる（任意）")]
        [SerializeField] private GameObject handSection;
        [Tooltip("選択中だけ表示する印。0 = 向きで切替、1 = 手のひらで統一、2 = 手の甲で統一")]
        [SerializeField] private GameObject[] handModeSelected;
        [Tooltip("入れ替え ON のときだけ表示（スイッチの On など）")]
        [SerializeField] private GameObject[] showWhenSwapOn;
        [Tooltip("入れ替え OFF のときだけ表示（スイッチの Off など）")]
        [SerializeField] private GameObject[] showWhenSwapOff;

        [Header("Hand mode availability（手のひらと手の甲で別の音が入った音色のときだけ押せる）")]
        [Tooltip("向きで切替 / 手のひらで統一 / 手の甲で統一 のボタン（0〜2）")]
        [SerializeField] private Button[] handModeButtons;
        [Tooltip("上のボタンをまとめた親の CanvasGroup（薄くするのに使う）")]
        [SerializeField] private CanvasGroup handModeGroup;

        [Header("Swap availability（統一中・手の甲の音が無い音色のときは、入れ替えを薄くして押せなくする）")]
        [Tooltip("入れ替えの行のボタン")]
        [SerializeField] private Button swapButton;
        [Tooltip("入れ替えの行の CanvasGroup（薄くするのに使う）")]
        [SerializeField] private CanvasGroup swapGroup;
        [Tooltip("入れ替えの行の説明文（押せない理由に切り替わる。任意）")]
        [SerializeField] private TextMeshProUGUI swapCaption;
        [SerializeField] private string swapCaptionNormal = "音が逆に聞こえるときに";
        [SerializeField] private string swapCaptionUnified = "統一しているときは使えません";
        [SerializeField] private string swapCaptionSingle = "この音色は手のひらと手の甲が同じ音です";
        [SerializeField, Range(0f, 1f)] private float disabledAlpha = 0.35f;
        [Tooltip("「手のひらと手の甲で音が変わる」印の説明（どのプリセットにも手の甲の音が無ければ隠れる。任意）")]
        [SerializeField] private GameObject dualLegend;

        private int currentPreset;
        private int currentHandMode;
        private bool swapEnabled = true;
        private bool handModeEnabled = true;

        [Header("Help（使い方。各自の端末だけで切り替わる）")]
        [Tooltip("通常の操作画面（使い方を開いている間は隠れる）")]
        [SerializeField] private GameObject mainPage;
        [Tooltip("使い方の画面（初期は非表示）")]
        [SerializeField] private GameObject helpPage;

        [Header("Disconnected（Manager 未設定時の表示）")]
        [Tooltip("未接続のときだけ表示する警告")]
        [SerializeField] private GameObject showWhenDisconnected;
        [Tooltip("未接続のときに隠す操作部品（ボタン・スライダーをまとめた親など。任意）")]
        [SerializeField] private GameObject hideWhenDisconnected;
        [SerializeField] private string disconnectedText = "未接続";

        private bool applying; // スクリプトからスライダーを動かしている間は入力として扱わない

        // 言語で差し替える文字（起動時の文字が翻訳表にある TMP だけ）と、その原文
        private TextMeshProUGUI[] localizedTexts;
        private string[] localizedSources;

        private void Start()
        {
            _ShowHelp(false);

            if (manager == null)
            {
                _ShowDisconnected();
                return;
            }

            if (showWhenDisconnected != null) showWhenDisconnected.SetActive(false);
            if (hideWhenDisconnected != null) hideWhenDisconnected.SetActive(true);
            if (handSection != null) handSection.SetActive(manager._IsHandSideEnabled());
            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f;
                volumeSlider.maxValue = 1f;
            }
            _CollectLocalizedTexts();
            _ApplyStaticTexts();
            _SetupPresetButtons();
            manager._RegisterPanel(this); // 登録すると現在の状態が _ApplyState / _ApplyHandSide で届く
        }

        /// presetButtonsRoot の直下から NadePresetButton を集め、プリセット名を割り当てる
        private void _SetupPresetButtons()
        {
            if (presetButtonsRoot == null)
            {
                presetButtons = new NadePresetButton[0];
                return;
            }

            int childCount = presetButtonsRoot.childCount;
            NadePresetButton[] found = new NadePresetButton[childCount];
            int n = 0;
            for (int c = 0; c < childCount; c++)
            {
                NadePresetButton b = presetButtonsRoot.GetChild(c).GetComponent<NadePresetButton>();
                if (b == null) continue;
                found[n] = b;
                n++;
            }
            presetButtons = new NadePresetButton[n];
            for (int i = 0; i < n; i++) presetButtons[i] = found[i];

            int presetCount = manager._GetPresetCount();
            bool anyDual = false;
            for (int i = 0; i < n; i++)
            {
                if (i < presetCount)
                {
                    presetButtons[i]._Setup(this, i, manager._GetPresetName(i));
                    bool dual = manager._PresetHasBackSound(i);
                    presetButtons[i]._SetDual(dual);
                    if (dual) anyDual = true;
                }
                else presetButtons[i]._Hide(); // プリセット数より多いボタンは隠す
            }
            if (dualLegend != null) dualLegend.SetActive(anyDual);
            if (presetCount > n)
            {
                Debug.LogWarning("[World Nadenade] 音色ボタンが足りません（プリセット " + presetCount.ToString()
                    + " 個 / ボタン " + n.ToString() + " 個）: " + gameObject.name);
            }
        }

        /// 子の TMP（非表示の使い方の画面も含む）から、翻訳表に原文がある文字を集める。
        /// 文字を書き換える前（起動直後）に呼ぶ
        private void _CollectLocalizedTexts()
        {
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

        private void _ApplyStaticTexts()
        {
            if (localizedTexts == null) return;
            for (int i = 0; i < localizedTexts.Length; i++)
            {
                if (localizedTexts[i] != null) localizedTexts[i].text = manager._Tr(localizedSources[i]);
            }
        }

        /// NadeManager から呼ばれる（表示の言語が変わったとき）。このあと _ApplyState / _ApplyHandSide も届く
        public void _ApplyLanguage()
        {
            if (manager == null) return;
            _ApplyStaticTexts();
            if (presetButtons == null) return;
            int presetCount = manager._GetPresetCount();
            for (int i = 0; i < presetButtons.Length && i < presetCount; i++)
            {
                if (presetButtons[i] != null) presetButtons[i]._SetLabel(manager._GetPresetName(i));
            }
        }

        private void _ShowDisconnected()
        {
            Debug.LogWarning("[World Nadenade] 操作パネルの Manager が未設定です: " + gameObject.name);
            if (showWhenDisconnected != null) showWhenDisconnected.SetActive(true);
            if (hideWhenDisconnected != null) hideWhenDisconnected.SetActive(false);
            if (switchLabel != null) switchLabel.text = disconnectedText;
            if (selfPatLabel != null) selfPatLabel.text = disconnectedText;
            _SetObjectsActive(showWhenSelfPatOn, false);
            _SetObjectsActive(showWhenSelfPatOff, false);
            if (volumeLabel != null) volumeLabel.text = disconnectedText;
            if (presetLabel != null) presetLabel.text = disconnectedText;
            if (presetButtonsRoot != null) presetButtonsRoot.gameObject.SetActive(false);
            if (handSection != null) handSection.SetActive(false);
            _SetObjectsActive(showWhenOn, false);
            _SetObjectsActive(showWhenOff, false);
            if (volumeSlider != null) volumeSlider.interactable = false;
        }

        // ───────────────────────── UI から呼ばれる ─────────────────────────

        public void _OnPowerPressed()
        {
            if (manager == null) return;
            manager._ToggleSwitch();
        }

        public void _OnSelfPatPressed()
        {
            if (manager == null) return;
            manager._ToggleSelfPat();
        }

        public void _OnVolumeChanged()
        {
            if (applying) return;
            if (manager == null || volumeSlider == null) return;
            manager._SetVolume(volumeSlider.value);
        }

        public void _OnPresetPressed()
        {
            if (manager == null) return;
            manager._NextPreset();
        }

        /// NadePresetButton から呼ばれる
        public void _OnPresetSelected(int index)
        {
            if (manager == null) return;
            manager._SelectPreset(index);
        }

        public void _OnHandMode0() { _OnHandMode(0); }
        public void _OnHandMode1() { _OnHandMode(1); }
        public void _OnHandMode2() { _OnHandMode(2); }

        private void _OnHandMode(int mode)
        {
            if (manager == null || !handModeEnabled) return;
            manager._SetHandMode(mode);
        }

        public void _OnSwapPressed()
        {
            if (manager == null || !swapEnabled) return;
            manager._ToggleHandSwap();
        }

        public void _OnHelpPressed()
        {
            _ShowHelp(helpPage == null || !helpPage.activeSelf);
        }

        public void _OnHelpClosed()
        {
            _ShowHelp(false);
        }

        private void _ShowHelp(bool show)
        {
            if (helpPage == null) return; // 使い方の画面が無いパネルでは何もしない
            helpPage.SetActive(show);
            if (mainPage != null) mainPage.SetActive(!show);
        }

        // ───────────────────────── NadeManager から呼ばれる ─────────────────────────

        public void _ApplyState(bool on, float volume, int preset, bool selfPat)
        {
            applying = true;

            _SetObjectsActive(showWhenOn, on);
            _SetObjectsActive(showWhenOff, !on);
            if (switchLabel != null) switchLabel.text = manager._Tr(on ? onText : offText);

            _SetObjectsActive(showWhenSelfPatOn, selfPat);
            _SetObjectsActive(showWhenSelfPatOff, !selfPat);
            if (selfPatLabel != null) selfPatLabel.text = manager._Tr(selfPat ? selfPatOnText : selfPatOffText);

            if (volumeSlider != null && Mathf.Abs(volumeSlider.value - volume) > 0.0001f)
            {
                volumeSlider.value = volume; // applying 中なので _OnVolumeChanged は無視される
            }
            if (volumeLabel != null)
            {
                volumeLabel.text = manager._Tr(volumePrefix) + Mathf.RoundToInt(volume * 100f).ToString() + "%";
            }

            if (presetButtons != null)
            {
                for (int i = 0; i < presetButtons.Length; i++)
                {
                    if (presetButtons[i] != null) presetButtons[i]._SetSelected(i == preset);
                }
            }
            if (presetLabel != null && manager != null)
            {
                presetLabel.text = manager._Tr(presetPrefix) + manager._GetPresetName(preset);
            }

            currentPreset = preset;
            _UpdateSwapAvailability();

            applying = false;
        }

        /// 手のひら・手の甲の設定（0 = 向きで切替、1 = 手のひらで統一、2 = 手の甲で統一）と入れ替えの表示を合わせる
        public void _ApplyHandSide(int mode, bool swap)
        {
            if (handModeSelected != null)
            {
                for (int i = 0; i < handModeSelected.Length; i++)
                {
                    if (handModeSelected[i] != null) handModeSelected[i].SetActive(i == mode);
                }
            }
            _SetObjectsActive(showWhenSwapOn, swap);
            _SetObjectsActive(showWhenSwapOff, !swap);

            currentHandMode = mode;
            _UpdateSwapAvailability();
        }

        /// 手のひら・手の甲のボタンは、手のひらと手の甲で別の音が入った音色のときだけ押せる。
        /// 入れ替えはさらに「向きで切替」のときだけ効く。押せないものは薄くする
        private void _UpdateSwapAvailability()
        {
            if (manager == null) return;
            bool single = !manager._PresetHasBackSound(currentPreset);
            bool unified = currentHandMode != 0;
            swapEnabled = !single && !unified;
            handModeEnabled = !single;

            if (handModeGroup != null) handModeGroup.alpha = handModeEnabled ? 1f : disabledAlpha;
            if (handModeButtons != null)
            {
                for (int i = 0; i < handModeButtons.Length; i++)
                {
                    if (handModeButtons[i] != null) handModeButtons[i].interactable = handModeEnabled;
                }
            }

            if (swapGroup != null) swapGroup.alpha = swapEnabled ? 1f : disabledAlpha;
            if (swapButton != null) swapButton.interactable = swapEnabled;
            if (swapCaption != null) swapCaption.text = manager._Tr(single ? swapCaptionSingle : (unified ? swapCaptionUnified : swapCaptionNormal));
        }

        private void _SetObjectsActive(GameObject[] objs, bool active)
        {
            if (objs == null) return;
            for (int i = 0; i < objs.Length; i++)
            {
                if (objs[i] != null) objs[i].SetActive(active);
            }
        }
    }
}
