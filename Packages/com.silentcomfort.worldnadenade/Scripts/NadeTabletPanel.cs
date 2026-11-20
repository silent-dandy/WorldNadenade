using TMPro;
using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// なで音ギミックのタブレット版操作パネル（横置きの持ち運べる Pickup）。
    ///
    /// - 同じ GameObject に VRC Pickup（+ Rigidbody：Is Kinematic オン / Use Gravity オフ）を付ける。
    ///   手を離すとその場に浮いて止まるので、自分の手の届く位置に置いておける
    /// - 同期なし（各プレイヤーが自分のタブレットを持つ。他の人には動かした位置は見えない）
    /// - 画面は2つのタブに分かれる
    ///   「聞こえる音」… 音量（自分の端末だけの設定）
    ///   「なでる音」　… 音色・手のひら・手の甲の設定・手のひらと手の甲の入れ替え（なでた相手にも同じ音が聞こえる）
    /// - VR：人差し指の先で画面に触れると反応する（ボーン位置で判定。アバター側の改変不要）
    ///   持っている手の指は無視する
    /// - デスクトップ：置いた状態では通常の UI クリック（Button / Slider のイベント → このスクリプト）。
    ///   持った状態では、画面中央の照準を合わせてクリック（Pickup の Use）で操作する
    /// - 掴める距離は VR とデスクトップで別に設定する（VR は撫でるときに遠くから掴んでしまわないよう短く）
    /// - 状態は NadeManager だけが持つ。なで音が ON の間だけ画面が点灯する
    /// - タップ時のフィードバック：ボタンの沈み込み・点灯、指先のカーソル、コントローラーの振動、クリック音
    /// - 戻す：置き台（NadeTabletCradle）のボタンで Home Point（置き台の Dock）に戻る。
    ///   Home Point が空欄なら入室時の位置に戻る。リスポーン時・離れたまま一定時間たったときにも戻せる。
    ///   置き台の近くで手を離すと、置き台に収まる。持ち上げた・戻ったことを置き台に知らせ、ボタンの表示を切り替える
    /// - 文字は VRChat の表示言語に合わせて差し替える。起動時に子の TMP・Pickup の文字を原文として覚え、
    ///   NadeStrings の翻訳表にある文字だけを差し替える（置き台の文字も、置き台に頼んで差し替える）
    ///
    /// UI からの呼び出し（デスクトップ用。生成ツールが配線する）
    ///   → このスクリプトの UdonBehaviour.SendCustomEvent で次のイベント名を指定する
    ///   _UiTab0 / _UiTab1 / _UiMinus / _UiPlus / _UiVolumeChanged / _UiPreset0 … _UiPreset8
    ///   _UiHandMode0 … _UiHandMode2 / _UiSwap
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadeTabletPanel : UdonSharpBehaviour
    {
        [Header("Connection（シーンに置いたら WorldNadenade をドラッグ）")]
        [SerializeField] private NadeManager manager;

        [Header("Screen")]
        [Tooltip("画面の Canvas（World Space）。中心が Pivot、表示面が -Z 向き")]
        [SerializeField] private RectTransform screen;
        [Tooltip("画面の中身。CanvasGroup の alpha で点灯・消灯する")]
        [SerializeField] private CanvasGroup content;
        [Tooltip("消灯中にタップしたときだけ出す案内（任意）")]
        [SerializeField] private TextMeshProUGUI sleepHint;
        [Tooltip("指先カーソル。0 = 左手、1 = 右手（任意。content の直下・中央アンカー）")]
        [SerializeField] private RectTransform[] cursors;

        [Header("Tabs（0 = 聞こえる音、1 = なでる音。タブのボタンは子に 'Label'（TMP）/ 'Selected' / 'Pressed' を置く）")]
        [SerializeField] private RectTransform[] tabButtons;
        [Tooltip("タブごとのページ（tabButtons と同じ並び）。選んでいないページは非表示になる")]
        [SerializeField] private GameObject[] tabPages;
        [Tooltip("入室時に開くタブ")]
        [SerializeField] private int startTab = 0;
        [Tooltip("選んでいるタブの文字色")]
        [SerializeField] private Color tabTextSelected = new Color(0.11f, 0.114f, 0.125f);
        [Tooltip("選んでいないタブの文字色")]
        [SerializeField] private Color tabTextNormal = new Color(0.663f, 0.659f, 0.639f);

        [Header("Volume")]
        [SerializeField] private Slider volumeSlider;
        [Tooltip("スライダーのタッチ範囲（スライダー本体）")]
        [SerializeField] private RectTransform volumeTouchArea;
        [Tooltip("値 0〜1 を割り当てる横幅（Slider の Handle Slide Area）")]
        [SerializeField] private RectTransform volumeRange;
        [Tooltip("つまみ（ドラッグ中に少し大きくする。任意）")]
        [SerializeField] private RectTransform volumeKnob;
        [SerializeField] private TextMeshProUGUI volumeLabel;
        [SerializeField] private RectTransform minusButton;
        [SerializeField] private RectTransform plusButton;
        [Tooltip("－ / ＋ ボタン 1 回で変わる量")]
        [SerializeField, Range(0.01f, 0.5f)] private float volumeStep = 0.1f;

        [Header("Preset（直下の子1つ＝音色ボタン1つ。子に 'Label'（TMP）/ 'Selected' / 'Pressed' を置く）")]
        [SerializeField] private Transform presetButtonsRoot;

        [Header("Hand side（手のひら・手の甲の設定。WorldNadenade の Use Hand Side が OFF なら Hand Section ごと隠れる）")]
        [Tooltip("手のひら・手の甲の設定の見出し・ボタン・入れ替えをまとめた親")]
        [SerializeField] private GameObject handSection;
        [Tooltip("直下の子3つ＝ 向きで切替 / 手のひらで統一 / 手の甲で統一。子に 'Selected' / 'Pressed' を置く")]
        [SerializeField] private Transform handModeButtonsRoot;
        [Tooltip("「手のひらと手の甲を入れ替える」の行全体（タッチ範囲）。子に 'SwitchOn' / 'SwitchOff' / 'Pressed' を置く")]
        [SerializeField] private RectTransform swapButton;
        [Tooltip("入れ替えの行の説明文（押せないときは理由に切り替わる。任意）")]
        [SerializeField] private TextMeshProUGUI swapCaption;
        [SerializeField] private string swapCaptionNormal = "音が逆に聞こえるときに";
        [SerializeField] private string swapCaptionUnified = "統一しているときは使えません";
        [SerializeField] private string swapCaptionSingle = "この音色は手のひらと手の甲が同じ音です";
        [Tooltip("押せないときの入れ替えの行の濃さ（行の CanvasGroup の alpha）")]
        [SerializeField, Range(0f, 1f)] private float disabledAlpha = 0.35f;
        [Tooltip("「手のひらと手の甲で音が変わる」印の説明（どのプリセットにも手の甲の音が無ければ隠れる。任意）")]
        [SerializeField] private GameObject dualLegend;

        [Header("Finger touch（VR・メートル）")]
        [Tooltip("指先が画面のこの距離まで近づいたら押したことにする")]
        [SerializeField] private float pressDepth = 0.006f;
        [Tooltip("押している指がこの距離まで離れたら離したことにする（pressDepth より大きく）")]
        [SerializeField] private float releaseDepth = 0.015f;
        [Tooltip("この距離以内に指があるとカーソルを出す。押すにはまずこの範囲の手前側から近づく必要がある")]
        [SerializeField] private float hoverDepth = 0.06f;
        [Tooltip("画面の裏側へ突き抜けても押したままとみなす深さ")]
        [SerializeField] private float behindLimit = 0.03f;
        [Tooltip("ボタンの当たり判定を周囲に広げる量")]
        [SerializeField] private float touchPadding = 0.004f;
        [Tooltip("指先の推定。中節→末節ボーンの延長率（1 = 末節ボーン、1.8 ≒ 指先）")]
        [SerializeField] private float tipExtrapolation = 1.8f;
        [Tooltip("頭からタブレットまでがこの距離以内のときだけ指を調べる")]
        [SerializeField] private float activeDistance = 1.5f;
        [Tooltip("－ / ＋ を押し続けたときの連続入力（開始までの秒数 / 間隔）")]
        [SerializeField] private float repeatDelay = 0.45f;
        [SerializeField] private float repeatInterval = 0.12f;
        [Tooltip("VR でもレーザーで操作できるようにする（OFF なら VR では指タッチのみ）")]
        [SerializeField] private bool laserInVR = false;
        [Tooltip("置いてあるタブレットの画面の手前この距離（m）に指があるあいだは、掴めない状態にする。"
            + "手を近づけたときの Pickup の強調表示がタブレットを手より手前に描いてしまうのを防ぐ")]
        [SerializeField] private float grabBlockDepth = 0.15f;

        [Header("Grab distance（掴める距離・メートル。0 以下なら VRC Pickup の Proximity をそのまま使う）")]
        [Tooltip("VR で掴める距離。撫でるときに遠くのタブレットを掴んでしまうのを防ぐ")]
        [SerializeField] private float vrGrabDistance = 0.1f;
        [Tooltip("デスクトップで掴める距離（頭から測るので、小さくすると掴めなくなる）")]
        [SerializeField] private float desktopGrabDistance = 2f;

        [Header("Return（戻す位置。置き台のボタン・リスポーン・置き忘れで戻る）")]
        [Tooltip("戻す位置と向き（置き台の Dock）。空欄なら入室時の位置に戻す")]
        [SerializeField] private Transform homePoint;
        [Tooltip("このタブレットの置き台（任意）。持ち上げたときに「タブレットを戻す」ボタンを出し、戻ったときに消す")]
        [SerializeField] private NadeTabletCradle cradle;
        [Tooltip("Home Point からこの距離（m）以内で手を離すと、置き台に収まる。0 以下で使わない")]
        [SerializeField] private float snapDistance = 0.08f;
        [Tooltip("自分がリスポーンしたら、タブレットを戻す")]
        [SerializeField] private bool returnOnRespawn = true;
        [Tooltip("自分がタブレットからこの距離（m）より離れたまま Auto Return Time 秒たったら戻す。0 以下で使わない")]
        [SerializeField] private float autoReturnDistance = 0f;
        [Tooltip("離れてから戻すまでの秒数")]
        [SerializeField] private float autoReturnTime = 60f;

        [Header("Feedback")]
        [SerializeField] private bool useHaptics = true;
        [SerializeField, Range(0f, 1f)] private float hapticAmplitude = 0.35f;
        [SerializeField] private float hapticDuration = 0.03f;
        [SerializeField] private float hapticFrequency = 250f;
        [Tooltip("タップ音（任意。生成ツールが自作のクリック音を作って設定する）")]
        [SerializeField] private AudioSource clickSource;
        [SerializeField] private AudioClip clickClip;
        [SerializeField, Range(0f, 1f)] private float clickVolume = 0.6f;
        [Tooltip("押している間のボタンの縮み")]
        [SerializeField, Range(0.8f, 1f)] private float pressedScale = 0.95f;

        [Header("Sleep（なで音 OFF の間は画面が消える）")]
        [SerializeField] private float wakeTime = 0.35f;
        [SerializeField] private float sleepTime = 0.6f;
        [Tooltip("消灯中に画面をタップしたら、なで音を ON にする（OFF なら案内を出すだけ）")]
        [SerializeField] private bool tapToWake = false;
        [SerializeField] private float hintTime = 2.5f;
        [SerializeField] private string disconnectedText = "未接続：Manager を設定してください";

        // タッチ対象の番号
        private const int TargetMinus = 0;
        private const int TargetPlus = 1;
        private const int TargetSlider = 2;
        private const int TargetTab0 = 3;       // 3〜4 = タブ
        private const int TabCount = 2;
        private const int TargetHand0 = 5;      // 5〜7 = 手のひら・手の甲の設定
        private const int HandModeCount = 3;
        private const int TargetSwap = 8;
        private const int TargetPreset0 = 9;    // 9〜 = 音色
        private RectTransform[] targetRects;
        private GameObject[] targetPressed;
        private GameObject[] presetSelected;
        private int presetShown;

        private GameObject[] tabSelected;
        private TextMeshProUGUI[] tabLabels;
        private GameObject[] handSelected;
        private GameObject swapOn;
        private GameObject swapOff;
        private CanvasGroup swapGroup;
        private Button swapUiButton;
        private bool swapEnabled = true;
        private bool handModeEnabled = true;
        private CanvasGroup handModeGroup;
        private Button[] handModeUiButtons;
        private int currentPreset;
        private int currentHandMode;
        private int currentTab = -1;

        // 入力ごとの状態（0 = 左手、1 = 右手、2 = デスクトップで持っているときのクリック）
        private const int DesktopInput = 2;
        private bool[] handArmed = new bool[3];
        private bool[] handPressed = new bool[3];
        private int[] handTarget = new int[] { -1, -1, -1 };
        private float[] handRepeat = new float[3];
        private int[] handStep = new int[] { -1, -1, -1 };
        private bool desktopAimValid;
        private bool[] handNearScreen = new bool[2];
        private bool grabBlocked;
        private Image[] cursorImages;

        private VRCPlayerApi localPlayer;
        private VRC_Pickup pickup;
        private int heldHand = -1;
        private bool isVR;
        private bool ready;
        private bool stateKnown;
        private bool applying;
        private bool isOn;
        private float currentVolume = 1f;
        private float fade;        // 0 = 消灯、1 = 点灯
        private float fadeTarget;
        private float hintTimer;
        private int laserFrames;   // VRCUiShape の初期化を待ってからコライダーを切る
        private bool laserDone;

        // 戻す位置（Home Point が空欄のときに使う、入室時の位置）と置き忘れの判定
        private const float AutoReturnCheckInterval = 1f;
        private Vector3 startPosition;
        private Quaternion startRotation;
        private float awayTimer;
        private float autoReturnElapsed;

        // 言語で差し替える文字（起動時の文字が翻訳表にある TMP だけ）と、その原文
        private TextMeshProUGUI[] localizedTexts;
        private string[] localizedSources;
        private string pickupInteractionSource;
        private string pickupUseSource;

        // ───────────────────────── 初期化 ─────────────────────────

        private void Start()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;

            localPlayer = Networking.LocalPlayer;
            isVR = Utilities.IsValid(localPlayer) && localPlayer.IsUserInVR();
            pickup = (VRC_Pickup)GetComponent(typeof(VRC_Pickup));

            // 掴める距離（VR は短く、デスクトップは頭から測るので長いまま）
            if (pickup != null)
            {
                float grab = isVR ? vrGrabDistance : desktopGrabDistance;
                if (grab > 0f) pickup.proximity = grab;
            }

            if (cursors != null)
            {
                cursorImages = new Image[cursors.Length];
                for (int i = 0; i < cursors.Length; i++)
                {
                    if (cursors[i] == null) continue;
                    cursorImages[i] = cursors[i].GetComponent<Image>();
                    cursors[i].gameObject.SetActive(false);
                }
            }
            else
            {
                cursorImages = new Image[0];
            }

            // 状態が届くまでは消灯
            fade = 0f;
            fadeTarget = 0f;
            _ApplyFade();
            if (sleepHint != null) sleepHint.gameObject.SetActive(false);
            if (volumeSlider != null)
            {
                volumeSlider.minValue = 0f;
                volumeSlider.maxValue = 1f;
            }

            if (manager == null)
            {
                Debug.LogWarning("[World Nadenade] タブレットの Manager が未設定です: " + gameObject.name);
                if (sleepHint != null)
                {
                    sleepHint.text = disconnectedText;
                    Color c = sleepHint.color;
                    c.a = 1f;
                    sleepHint.color = c;
                    sleepHint.gameObject.SetActive(true);
                }
                return;
            }

            _CollectLocalizedTexts();
            _CollectTargets();
            _ApplyLanguage();
            _SelectTab(Mathf.Clamp(startTab, 0, TabCount - 1));
            ready = true;
            manager._RegisterTablet(this); // 登録すると現在の状態が _ApplyState / _ApplyHandSide で届く
        }

        /// 子の TMP（非表示のページ・案内も含む）と Pickup の文字から、翻訳表に原文がある文字を集める。
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

            if (pickup != null)
            {
                pickupInteractionSource = pickup.InteractionText;
                pickupUseSource = pickup.UseText;
            }
        }

        /// 起動時と、表示の言語が変わったとき（NadeManager から）に呼ぶ。
        /// 言語が変わったときは、このあと _ApplyState / _ApplyHandSide も届き、状態で変わる文字が作り直される
        public void _ApplyLanguage()
        {
            if (manager == null) return;
            if (localizedTexts != null)
            {
                for (int i = 0; i < localizedTexts.Length; i++)
                {
                    if (localizedTexts[i] != null) localizedTexts[i].text = manager._Tr(localizedSources[i]);
                }
            }

            // 音色名
            if (presetButtonsRoot != null)
            {
                int n = Mathf.Min(presetShown, presetButtonsRoot.childCount);
                for (int c = 0; c < n; c++)
                {
                    Transform labelTf = presetButtonsRoot.GetChild(c).Find("Label");
                    if (labelTf == null) continue;
                    TextMeshProUGUI label = labelTf.GetComponent<TextMeshProUGUI>();
                    if (label != null) label.text = manager._GetPresetName(c);
                }
            }

            if (pickup != null)
            {
                if (manager._HasTranslation(pickupInteractionSource)) pickup.InteractionText = manager._Tr(pickupInteractionSource);
                if (manager._HasTranslation(pickupUseSource)) pickup.UseText = manager._Tr(pickupUseSource);
            }

            if (cradle != null) cradle._ApplyLanguage(manager);
        }

        private void _CollectTargets()
        {
            int childCount = presetButtonsRoot != null ? presetButtonsRoot.childCount : 0;
            int presetCount = manager._GetPresetCount();
            presetShown = Mathf.Min(childCount, presetCount);

            targetRects = new RectTransform[TargetPreset0 + presetShown];
            targetPressed = new GameObject[TargetPreset0 + presetShown];
            presetSelected = new GameObject[presetShown];

            // 音量
            targetRects[TargetMinus] = minusButton;
            targetRects[TargetPlus] = plusButton;
            targetRects[TargetSlider] = volumeTouchArea;
            targetPressed[TargetMinus] = _FindChild(minusButton, "Pressed");
            targetPressed[TargetPlus] = _FindChild(plusButton, "Pressed");
            targetPressed[TargetSlider] = null;

            // タブ
            tabSelected = new GameObject[TabCount];
            tabLabels = new TextMeshProUGUI[TabCount];
            for (int i = 0; i < TabCount; i++)
            {
                RectTransform tb = (tabButtons != null && i < tabButtons.Length) ? tabButtons[i] : null;
                targetRects[TargetTab0 + i] = tb;
                targetPressed[TargetTab0 + i] = _FindChild(tb, "Pressed");
                tabSelected[i] = _FindChild(tb, "Selected");
                if (tb != null)
                {
                    Transform labelTf = tb.Find("Label");
                    if (labelTf != null) tabLabels[i] = labelTf.GetComponent<TextMeshProUGUI>();
                }
            }

            // 手のひら・手の甲の設定
            handSelected = new GameObject[HandModeCount];
            handModeUiButtons = new Button[HandModeCount];
            bool handEnabled = manager._IsHandSideEnabled();
            if (handSection != null) handSection.SetActive(handEnabled);
            if (handModeButtonsRoot != null) handModeGroup = handModeButtonsRoot.GetComponent<CanvasGroup>();
            for (int i = 0; i < HandModeCount; i++)
            {
                Transform child = (handModeButtonsRoot != null && i < handModeButtonsRoot.childCount) ? handModeButtonsRoot.GetChild(i) : null;
                if (child == null) continue;
                targetRects[TargetHand0 + i] = child.GetComponent<RectTransform>();
                targetPressed[TargetHand0 + i] = _FindChild(child, "Pressed");
                handSelected[i] = _FindChild(child, "Selected");
                handModeUiButtons[i] = child.GetComponent<Button>();
            }
            targetRects[TargetSwap] = swapButton;
            targetPressed[TargetSwap] = _FindChild(swapButton, "Pressed");
            if (swapButton != null)
            {
                Transform on = swapButton.Find("SwitchOn");
                Transform off = swapButton.Find("SwitchOff");
                swapOn = on != null ? on.gameObject : null;
                swapOff = off != null ? off.gameObject : null;
                swapGroup = swapButton.GetComponent<CanvasGroup>();
                swapUiButton = swapButton.GetComponent<Button>();
            }

            // 音色（手のひらと手の甲で別の音が入っているプリセットだけ、子の 'DualMark' を表示）
            bool anyDual = false;
            for (int c = 0; c < childCount; c++)
            {
                Transform child = presetButtonsRoot.GetChild(c);
                if (c >= presetShown)
                {
                    child.gameObject.SetActive(false); // プリセット数より多いボタンは隠す
                    continue;
                }
                child.gameObject.SetActive(true);
                Transform labelTf = child.Find("Label");
                if (labelTf != null)
                {
                    TextMeshProUGUI label = labelTf.GetComponent<TextMeshProUGUI>();
                    if (label != null) label.text = manager._GetPresetName(c);
                }
                targetRects[TargetPreset0 + c] = child.GetComponent<RectTransform>();
                targetPressed[TargetPreset0 + c] = _FindChild(child, "Pressed");
                presetSelected[c] = _FindChild(child, "Selected");

                bool dual = manager._PresetHasBackSound(c);
                if (dual) anyDual = true;
                Transform mark = child.Find("DualMark");
                if (mark != null) mark.gameObject.SetActive(dual);
            }
            if (dualLegend != null) dualLegend.SetActive(anyDual);

            if (presetCount > childCount)
            {
                Debug.LogWarning("[World Nadenade] タブレットの音色ボタンが足りません（プリセット " + presetCount.ToString()
                    + " 個 / ボタン " + childCount.ToString() + " 個）: " + gameObject.name);
            }
        }

        private GameObject _FindChild(Transform parent, string childName)
        {
            if (parent == null) return null;
            Transform t = parent.Find(childName);
            if (t == null) return null;
            t.gameObject.SetActive(false);
            return t.gameObject;
        }

        // ───────────────────────── タブ ─────────────────────────

        private void _SelectTab(int tab)
        {
            if (tab == currentTab) return;

            // 切り替わるページで押している最中の入力は離したことにする（見えなくなったボタンが押されたまま残らないように）
            for (int h = 0; h < handTarget.Length; h++)
            {
                int t = handTarget[h];
                if (t >= 0 && t != TargetTab0 && t != TargetTab0 + 1)
                {
                    _SetPressedVisual(t, false);
                    handTarget[h] = -1;
                }
            }

            currentTab = tab;
            for (int i = 0; i < TabCount; i++)
            {
                bool sel = i == tab;
                if (tabPages != null && i < tabPages.Length && tabPages[i] != null) tabPages[i].SetActive(sel);
                if (tabSelected != null && tabSelected[i] != null) tabSelected[i].SetActive(sel);
                if (tabLabels != null && tabLabels[i] != null) tabLabels[i].color = sel ? tabTextSelected : tabTextNormal;
            }
        }

        // ───────────────────────── NadeManager から呼ばれる ─────────────────────────

        public void _ApplyState(bool on, float volume, int preset, bool selfPat)
        {
            applying = true;

            currentVolume = volume;
            if (volumeSlider != null && Mathf.Abs(volumeSlider.value - volume) > 0.0001f)
            {
                volumeSlider.value = volume; // applying 中なので _UiVolumeChanged は無視される
            }
            if (volumeLabel != null) volumeLabel.text = Mathf.RoundToInt(volume * 100f).ToString() + "%";

            if (presetSelected != null)
            {
                for (int i = 0; i < presetSelected.Length; i++)
                {
                    if (presetSelected[i] != null) presetSelected[i].SetActive(i == preset);
                }
            }
            currentPreset = preset;
            _UpdateSwapAvailability();

            if (!stateKnown || on != isOn)
            {
                isOn = on;
                fadeTarget = on ? 1f : 0f;
                if (!stateKnown)
                {
                    fade = fadeTarget; // 入室直後は演出なしで合わせる
                    _ApplyFade();
                }
                stateKnown = true;
                if (!on)
                {
                    _ResetHand(0);
                    _ResetHand(1);
                    _ResetHand(DesktopInput);
                }
                else
                {
                    hintTimer = 0f;
                }
            }

            applying = false;
        }

        /// 手のひら・手の甲の設定（0 = 向きで切替、1 = 手のひらで統一、2 = 手の甲で統一）と入れ替えの表示を合わせる
        public void _ApplyHandSide(int mode, bool swap)
        {
            if (handSelected != null)
            {
                for (int i = 0; i < handSelected.Length; i++)
                {
                    if (handSelected[i] != null) handSelected[i].SetActive(i == mode);
                }
            }
            if (swapOn != null) swapOn.SetActive(swap);
            if (swapOff != null) swapOff.SetActive(!swap);
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
            if (handModeUiButtons != null)
            {
                for (int i = 0; i < handModeUiButtons.Length; i++)
                {
                    if (handModeUiButtons[i] != null) handModeUiButtons[i].interactable = handModeEnabled;
                }
            }

            if (swapGroup != null) swapGroup.alpha = swapEnabled ? 1f : disabledAlpha;
            if (swapUiButton != null) swapUiButton.interactable = swapEnabled;
            if (swapCaption != null) swapCaption.text = manager._Tr(single ? swapCaptionSingle : (unified ? swapCaptionUnified : swapCaptionNormal));
        }

        // ───────────────────────── 戻す（置き台・リスポーン・置き忘れ） ─────────────────────────

        /// 置き台（NadeTabletCradle）のボタンから呼ばれる。持っていれば手を離させてから、戻す位置へ移す。
        /// 同期なしなので、戻るのは自分の画面のタブレットだけ
        public void _ReturnHome()
        {
            if (pickup != null && pickup.IsHeld) pickup.Drop();

            // OnDrop が呼ばれなかった場合にも、入力と掴める状態を元に戻しておく
            heldHand = -1;
            grabBlocked = false;
            if (pickup != null) pickup.pickupable = true;
            _ResetHand(0);
            _ResetHand(1);
            _ResetHand(DesktopInput);
            handNearScreen[0] = false;
            handNearScreen[1] = false;
            if (!isVR) _SetScreenCollider(true);
            awayTimer = 0f;

            _SnapHome();
            if (cradle != null) cradle._SetDocked(true);
            // 手を離した直後に VRChat 側が位置を書き戻す場合に備えて、次のフレームでもう一度合わせる
            SendCustomEventDelayedFrames(nameof(_SnapHome), 1);
        }

        public void _SnapHome()
        {
            if (homePoint != null) transform.SetPositionAndRotation(homePoint.position, homePoint.rotation);
            else transform.SetPositionAndRotation(startPosition, startRotation);
        }

        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            if (!returnOnRespawn || !Utilities.IsValid(player) || !player.isLocal) return;
            _ReturnHome();
        }

        /// 置き忘れ：自分が Auto Return Distance より離れたまま Auto Return Time 秒たったら戻す（1秒ごとに調べる）
        private void _UpdateAutoReturn(float dt)
        {
            if (autoReturnDistance <= 0f) return;
            autoReturnElapsed += dt;
            if (autoReturnElapsed < AutoReturnCheckInterval) return;
            float elapsed = autoReturnElapsed;
            autoReturnElapsed = 0f;

            if (!Utilities.IsValid(localPlayer) || (pickup != null && pickup.IsHeld))
            {
                awayTimer = 0f;
                return;
            }
            Vector3 home = homePoint != null ? homePoint.position : startPosition;
            if ((transform.position - home).sqrMagnitude < 0.0025f) // 戻す位置から 5cm 以内なら置いてある扱い
            {
                awayTimer = 0f;
                return;
            }
            if ((localPlayer.GetPosition() - transform.position).sqrMagnitude <= autoReturnDistance * autoReturnDistance)
            {
                awayTimer = 0f;
                return;
            }
            awayTimer += elapsed;
            if (awayTimer >= autoReturnTime) _ReturnHome();
        }

        // ───────────────────────── Pickup ─────────────────────────

        public override void OnPickup()
        {
            heldHand = -1;
            if (pickup != null)
            {
                VRC_Pickup.PickupHand hand = pickup.currentHand;
                if (hand == VRC_Pickup.PickupHand.Left) heldHand = 0;
                else if (hand == VRC_Pickup.PickupHand.Right) heldHand = 1;
            }
            if (heldHand >= 0) _ResetHand(heldHand); // 持っている手の指では押さない

            // デスクトップで持っている間は、UI のクリックではなく Use（クリック）で操作する（二重入力防止）
            if (!isVR) _SetScreenCollider(false);

            if (cradle != null) cradle._SetDocked(false); // 置き台に「タブレットを戻す」ボタンを出す
        }

        public override void OnDrop()
        {
            heldHand = -1;
            grabBlocked = false;
            if (pickup != null) pickup.pickupable = true;
            _ResetHand(DesktopInput);
            if (!isVR) _SetScreenCollider(true);

            // 置き台の近くで離したら、置き台に収める
            if (homePoint != null && snapDistance > 0f
                && (transform.position - homePoint.position).sqrMagnitude <= snapDistance * snapDistance)
            {
                _SnapHome();
                SendCustomEventDelayedFrames(nameof(_SnapHome), 1);
                if (cradle != null) cradle._SetDocked(true);
            }
        }

        /// デスクトップで持っているときのクリック：照準（画面中央）の先にあるボタンを押す
        public override void OnPickupUseDown()
        {
            if (isVR || !ready || screen == null) return;
            Vector3 pt = _GetDesktopAim();
            if (!desktopAimValid) return;
            handPressed[DesktopInput] = true;
            _OnTouchDown(DesktopInput, pt);
        }

        public override void OnPickupUseUp()
        {
            _ResetHand(DesktopInput);
        }

        /// 頭の向きのレイと画面の交点（画面の外なら desktopAimValid = false）
        private Vector3 _GetDesktopAim()
        {
            desktopAimValid = false;
            if (!Utilities.IsValid(localPlayer)) return Vector3.zero;
            VRCPlayerApi.TrackingData td = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 dir = td.rotation * Vector3.forward;
            Vector3 n = screen.forward;
            float denom = Vector3.Dot(dir, n);
            if (Mathf.Abs(denom) < 0.0001f) return Vector3.zero;
            float t = Vector3.Dot(screen.position - td.position, n) / denom;
            if (t < 0f || t > 3f) return Vector3.zero;
            Vector3 pt = td.position + dir * t;
            Vector3 lp = screen.InverseTransformPoint(pt);
            Rect r = screen.rect;
            if (lp.x < r.xMin || lp.x > r.xMax || lp.y < r.yMin || lp.y > r.yMax) return Vector3.zero;
            desktopAimValid = true;
            return pt;
        }

        private void _SetScreenCollider(bool on)
        {
            if (screen == null) return;
            BoxCollider col = screen.GetComponent<BoxCollider>();
            if (col != null) col.enabled = on;
        }

        // ───────────────────────── 毎フレーム ─────────────────────────

        public override void PostLateUpdate()
        {
            float dt = Time.deltaTime;
            _UpdateFade(dt);
            _UpdateHint(dt);
            _UpdateAutoReturn(dt);

            if (!ready || screen == null) return;
            if (!Utilities.IsValid(localPlayer)) return;

            if (!isVR)
            {
                // デスクトップで持ったままクリックしている間（スライダーのドラッグ・＋－の長押し）
                if (handPressed[DesktopInput])
                {
                    Vector3 aim = _GetDesktopAim();
                    if (desktopAimValid) _OnTouchHold(DesktopInput, aim, dt);
                }
                return;
            }

            if (!laserDone)
            {
                laserFrames++;
                if (laserFrames > 3)
                {
                    laserDone = true;
                    if (!laserInVR)
                    {
                        // VRC Ui Shape のコライダーを切り、VR ではレーザーが反応しないようにする（指タッチのみ）
                        BoxCollider col = screen.GetComponent<BoxCollider>();
                        if (col != null) col.enabled = false;
                    }
                }
            }

            Vector3 head = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            if ((head - screen.position).sqrMagnitude > activeDistance * activeDistance)
            {
                _ResetHand(0);
                _ResetHand(1);
                handNearScreen[0] = false;
                handNearScreen[1] = false;
                _UpdateGrabBlock();
                return;
            }

            _UpdateHand(0, dt);
            _UpdateHand(1, dt);
            _UpdateGrabBlock();
        }

        /// 置いてあるタブレットの画面に指を近づけている間は掴めなくする。
        /// 掴める状態だと、手を近づけた時点で VRChat の Pickup の強調表示が出て、
        /// タブレットが手より手前に重なって描かれる（持っているときに反対の手で押す場合はこれが出ない）。
        /// 縁や裏に手を伸ばしたときは掴める
        private void _UpdateGrabBlock()
        {
            if (pickup == null) return;
            bool block = heldHand < 0 && (handNearScreen[0] || handNearScreen[1]);
            if (block == grabBlocked) return;
            grabBlocked = block;
            pickup.pickupable = !block;
        }

        private void _UpdateHand(int h, float dt)
        {
            handNearScreen[h] = false;
            if (h == heldHand)
            {
                _ResetHand(h);
                return;
            }

            Vector3 tip = _GetTip(h);
            if (tip == Vector3.zero)
            {
                _ResetHand(h);
                return;
            }

            Vector3 lp = screen.InverseTransformPoint(tip);
            float d = -lp.z * screen.lossyScale.z; // 画面の手前側（-Z）までの距離（m）。裏側は負
            Rect r = screen.rect;
            bool inScreen = lp.x >= r.xMin && lp.x <= r.xMax && lp.y >= r.yMin && lp.y <= r.yMax;
            Vector2 p = new Vector2(lp.x, lp.y);
            handNearScreen[h] = handPressed[h] || (inScreen && d <= grabBlockDepth && d >= -behindLimit);

            if (!handPressed[h])
            {
                if (!inScreen || d > hoverDepth || d < -behindLimit)
                {
                    handArmed[h] = false;
                    _HideCursor(h);
                    return;
                }
                if (d > pressDepth)
                {
                    handArmed[h] = true; // 手前から近づいてきた
                    _ShowCursor(h, p, d);
                    return;
                }
                if (!handArmed[h])
                {
                    // 横や裏から画面に入ってきた指は押したことにしない
                    _HideCursor(h);
                    return;
                }
                handArmed[h] = false;
                handPressed[h] = true;
                _ShowCursor(h, p, 0f);
                _OnTouchDown(h, tip);
                return;
            }

            bool release = d > releaseDepth || d < -(behindLimit + 0.02f) || !inScreen;
            if (release)
            {
                _OnTouchUp(h);
                handPressed[h] = false;
                handArmed[h] = inScreen && d > pressDepth && d <= hoverDepth;
                if (handArmed[h]) _ShowCursor(h, p, d);
                else _HideCursor(h);
                return;
            }

            _ShowCursor(h, p, 0f);
            _OnTouchHold(h, tip, dt);
        }

        /// 人差し指の先の推定位置（取れなければ zero）
        private Vector3 _GetTip(int h)
        {
            Vector3 mid = localPlayer.GetBonePosition(h == 0 ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate);
            Vector3 distal = localPlayer.GetBonePosition(h == 0 ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
            if (distal == Vector3.zero) return Vector3.zero;
            if (mid == Vector3.zero) return distal;
            return Vector3.LerpUnclamped(mid, distal, tipExtrapolation);
        }

        // ───────────────────────── タッチ処理 ─────────────────────────

        private void _OnTouchDown(int h, Vector3 tip)
        {
            handTarget[h] = -1;

            if (!isOn)
            {
                _OnSleepTap(h);
                return;
            }

            int t = _HitTest(tip);
            if (t < 0) return;
            bool handDisabled = !handModeEnabled && t >= TargetHand0 && t < TargetHand0 + HandModeCount;
            if ((t == TargetSwap && !swapEnabled) || handDisabled)
            {
                _Haptic(h, 0.15f); // 押せない行：沈み込み・クリック音は出さず、ごく弱い振動だけ
                return;
            }
            for (int o = 0; o < handPressed.Length; o++)
            {
                if (o != h && handPressed[o] && handTarget[o] == t) return; // 別の入力が押している
            }

            handTarget[h] = t;
            _SetPressedVisual(t, true);
            _Feedback(h, tip, 1f);

            if (t == TargetSlider)
            {
                handStep[h] = -1;
                _DragSlider(h, tip);
            }
            else if (t == TargetMinus || t == TargetPlus)
            {
                _StepVolume(t == TargetMinus ? -1 : 1);
                handRepeat[h] = repeatDelay;
            }
            else if (t >= TargetTab0 && t < TargetTab0 + TabCount)
            {
                _SelectTab(t - TargetTab0);
            }
            else if (t >= TargetHand0 && t < TargetHand0 + HandModeCount)
            {
                manager._SetHandMode(t - TargetHand0);
            }
            else if (t == TargetSwap)
            {
                manager._ToggleHandSwap();
            }
            else
            {
                manager._SelectPreset(t - TargetPreset0);
            }
        }

        private void _OnTouchHold(int h, Vector3 tip, float dt)
        {
            int t = handTarget[h];
            if (t < 0) return;

            if (t == TargetSlider)
            {
                _DragSlider(h, tip); // 押したまま指をずらすと音量が変わる（ボタンの外へ出ても追従）
                return;
            }

            if (_HitTest(tip) != t)
            {
                // 指がボタンの外へずれたら押下表示をやめ、連続入力も止める
                _SetPressedVisual(t, false);
                handTarget[h] = -1;
                return;
            }

            if (t == TargetMinus || t == TargetPlus)
            {
                handRepeat[h] -= dt;
                if (handRepeat[h] <= 0f)
                {
                    handRepeat[h] = repeatInterval;
                    float before = currentVolume;
                    _StepVolume(t == TargetMinus ? -1 : 1);
                    if (Mathf.Abs(currentVolume - before) > 0.0001f) _Feedback(h, tip, 0.5f);
                }
            }
        }

        private void _OnTouchUp(int h)
        {
            int t = handTarget[h];
            if (t >= 0) _SetPressedVisual(t, false);
            handTarget[h] = -1;
        }

        private void _ResetHand(int h)
        {
            if (handPressed[h]) _OnTouchUp(h);
            handPressed[h] = false;
            handArmed[h] = false;
            _HideCursor(h);
        }

        private int _HitTest(Vector3 tip)
        {
            if (targetRects == null) return -1;
            for (int t = 0; t < targetRects.Length; t++)
            {
                RectTransform rt = targetRects[t];
                if (rt == null || !rt.gameObject.activeInHierarchy) continue; // 開いていないタブのボタンは対象外
                Vector3 lp = rt.InverseTransformPoint(tip);
                Rect r = rt.rect;
                float pad = touchPadding / Mathf.Max(rt.lossyScale.x, 0.000001f);
                if (lp.x >= r.xMin - pad && lp.x <= r.xMax + pad && lp.y >= r.yMin - pad && lp.y <= r.yMax + pad) return t;
            }
            return -1;
        }

        private void _DragSlider(int h, Vector3 tip)
        {
            if (volumeRange == null) return;
            Vector3 lp = volumeRange.InverseTransformPoint(tip);
            Rect r = volumeRange.rect;
            float v = Mathf.Clamp01(Mathf.InverseLerp(r.xMin, r.xMax, lp.x));

            // 5% ごとに軽く振動（両端は少し強く）
            int step = Mathf.RoundToInt(v * 20f);
            if (step != handStep[h])
            {
                if (handStep[h] >= 0) _Haptic(h, (step == 0 || step == 20) ? 0.8f : 0.25f);
                handStep[h] = step;
            }
            manager._SetVolume(v);
        }

        private void _StepVolume(int dir)
        {
            float s = Mathf.Max(volumeStep, 0.01f);
            float units = currentVolume / s;
            float next = dir < 0 ? (Mathf.Ceil(units - 0.001f) - 1f) * s : (Mathf.Floor(units + 0.001f) + 1f) * s;
            manager._SetVolume(Mathf.Clamp01(next));
        }

        // ───────────────────────── 消灯中のタップ ─────────────────────────

        private void _OnSleepTap(int h)
        {
            _Haptic(h, 0.3f);
            if (tapToWake)
            {
                manager._ToggleSwitch(); // OFF → ON（_ApplyState で点灯する）
                return;
            }
            hintTimer = hintTime;
        }

        private void _UpdateHint(float dt)
        {
            if (sleepHint == null || !ready) return;
            if (hintTimer <= 0f)
            {
                if (sleepHint.gameObject.activeSelf) sleepHint.gameObject.SetActive(false);
                return;
            }
            hintTimer -= dt;
            if (!sleepHint.gameObject.activeSelf) sleepHint.gameObject.SetActive(true);
            float a = Mathf.Clamp01(Mathf.Min((hintTime - hintTimer) / 0.2f, hintTimer / 0.6f));
            Color c = sleepHint.color;
            c.a = a;
            sleepHint.color = c;
        }

        // ───────────────────────── 点灯・消灯 ─────────────────────────

        private void _UpdateFade(float dt)
        {
            if (fade == fadeTarget) return;
            float speed = fadeTarget > fade ? 1f / Mathf.Max(wakeTime, 0.01f) : 1f / Mathf.Max(sleepTime, 0.01f);
            fade = Mathf.MoveTowards(fade, fadeTarget, speed * dt);
            _ApplyFade();
        }

        private void _ApplyFade()
        {
            if (content == null) return;
            bool visible = fade > 0.001f;
            if (content.gameObject.activeSelf != visible) content.gameObject.SetActive(visible); // 消灯中は描画しない
            content.alpha = fade * fade * (3f - 2f * fade);
            bool interactive = isOn && fade > 0.99f;
            content.interactable = interactive;
            content.blocksRaycasts = interactive;
        }

        // ───────────────────────── フィードバック ─────────────────────────

        private void _SetPressedVisual(int t, bool pressed)
        {
            if (targetPressed != null && t < targetPressed.Length && targetPressed[t] != null) targetPressed[t].SetActive(pressed);

            if (t == TargetSlider)
            {
                if (volumeKnob != null) volumeKnob.localScale = Vector3.one * (pressed ? 1.25f : 1f);
                return;
            }
            if (targetRects != null && t < targetRects.Length && targetRects[t] != null)
            {
                // 横長の行（入れ替え）は縮みを控えめにする
                float k = t == TargetSwap ? Mathf.Lerp(1f, pressedScale, 0.4f) : pressedScale;
                targetRects[t].localScale = Vector3.one * (pressed ? k : 1f);
            }
        }

        private void _Feedback(int h, Vector3 at, float strength)
        {
            _Haptic(h, strength);
            if (clickSource != null && clickClip != null)
            {
                clickSource.transform.position = at;
                clickSource.PlayOneShot(clickClip, clickVolume * strength);
            }
        }

        private void _Haptic(int h, float strength)
        {
            if (!useHaptics || !isVR || !Utilities.IsValid(localPlayer)) return;
            VRC_Pickup.PickupHand hand = h == 0 ? VRC_Pickup.PickupHand.Left : VRC_Pickup.PickupHand.Right;
            localPlayer.PlayHapticEventInHand(hand, hapticDuration, hapticAmplitude * strength, hapticFrequency);
        }

        private void _ShowCursor(int h, Vector2 p, float d)
        {
            if (cursors == null || h >= cursors.Length || cursors[h] == null) return;
            RectTransform c = cursors[h];
            if (!c.gameObject.activeSelf) c.gameObject.SetActive(true);
            c.localPosition = new Vector3(p.x, p.y, 0f);
            float k = Mathf.Clamp01(d / Mathf.Max(hoverDepth, 0.001f)); // 0 = 触れている、1 = 遠い
            c.localScale = Vector3.one * Mathf.Lerp(0.6f, 1.6f, k);
            Image img = cursorImages[h];
            if (img != null)
            {
                Color col = img.color;
                col.a = Mathf.Lerp(0.95f, 0.2f, k);
                img.color = col;
            }
        }

        private void _HideCursor(int h)
        {
            if (cursors == null || h >= cursors.Length || cursors[h] == null) return;
            if (cursors[h].gameObject.activeSelf) cursors[h].gameObject.SetActive(false);
        }

        // ───────────────────────── デスクトップ（UI イベント） ─────────────────────────

        public void _UiTab0() { if (ready && isOn) _SelectTab(0); }
        public void _UiTab1() { if (ready && isOn) _SelectTab(1); }

        public void _UiMinus() { if (ready && isOn) _StepVolume(-1); }
        public void _UiPlus() { if (ready && isOn) _StepVolume(1); }

        public void _UiVolumeChanged()
        {
            if (applying || !ready || !isOn || volumeSlider == null) return;
            manager._SetVolume(volumeSlider.value);
        }

        public void _UiPreset0() { _UiPreset(0); }
        public void _UiPreset1() { _UiPreset(1); }
        public void _UiPreset2() { _UiPreset(2); }
        public void _UiPreset3() { _UiPreset(3); }
        public void _UiPreset4() { _UiPreset(4); }
        public void _UiPreset5() { _UiPreset(5); }
        public void _UiPreset6() { _UiPreset(6); }
        public void _UiPreset7() { _UiPreset(7); }
        public void _UiPreset8() { _UiPreset(8); }

        private void _UiPreset(int index)
        {
            if (!ready || !isOn || index >= presetShown) return;
            manager._SelectPreset(index);
        }

        public void _UiHandMode0() { _UiHandMode(0); }
        public void _UiHandMode1() { _UiHandMode(1); }
        public void _UiHandMode2() { _UiHandMode(2); }

        private void _UiHandMode(int mode)
        {
            if (!ready || !isOn || !handModeEnabled) return;
            manager._SetHandMode(mode);
        }

        public void _UiSwap()
        {
            if (!ready || !isOn || !swapEnabled) return;
            manager._ToggleHandSwap();
        }
    }
}
