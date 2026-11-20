using UdonSharp;
using UnityEngine;
using VRC.SDK3.Data;
using VRC.SDKBase;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// ワールド版なで音ギミックの本体（SDK 3.10.6 / ローカル専用・同期なし）。
    /// - ON/OFF・音量（初期値 50%）の状態はここだけが持ち、操作パネル（NadeControlPanel）と
    ///   タブレット（NadeTabletPanel）は何枚置いても同じ状態を表示する
    ///
    /// - 音色（プリセット）は Presets の直下の NadeSoundPreset（名前・音源・音の出方のひとまとまり）から起動時に集める
    /// - 音色・手のひら・手の甲の音の使い方（向きで切替 / どちらかで統一）・入れ替えは「なでる側」が選び、
    ///   PlayerObject（NadePlayerPreset）で全員に同期される。なでる人ごとに、その人の設定で鳴る。
    ///   音色は番号とあわせて Preset Id も同期・保存するので、Persistence を付けても並べ替えで前回の選択がずれない
    /// - ON/OFF と音量は各プレイヤーが自分の端末だけで切り替える
    /// - ON のとき「自分がなでられた」「自分が誰かをなでた」接触だけを発音
    /// - 音はなでている手の Hand ボーン（手首）から鳴る（Sound Palm Blend で手のひら側へ寄せられる）
    /// - 頭に対する手の相対速度で音量が変化（速度と音量の対応は音色ごと）
    /// - 音色の Speed Pitch が ON なら、手の相対速度でピッチも変化（速いほど高い。OFF の音色はピッチ一定）
    /// - ストロークの折り返しで、ピッチを一瞬ゆらし、音量を短く持ち上げてアタックを付け直す（検出は共通、音の変化は音色ごと）
    /// - 手のひら / 手の甲で音源をクロスフェード（useHandSide）
    /// - 音色ボタンを押すと、その音色を自分にだけ試し聴きさせる（NadePreview に任せる）
    /// - 表示の言語は VRChat の表示言語に合わせる（翻訳表は NadeStrings）。言語が変わったら全パネル・タブレットの文字を作り直す
    ///
    /// 負荷対策:
    /// - 頭レシーバーは「自分の頭」と「自分の手が届く距離にいる人の頭」だけ有効化
    /// - 毎フレーム処理はこのマネージャーの PostLateUpdate に集約（頭ごと・音色ごと・試し聴きの Update なし）
    /// - 音色の設定値は起動時に配列へコピーし、毎フレームは他のコンポーネントのフィールドを読まない
    /// - 発音は voiceRoots の数（推奨 6）までに制限し、ボーン取得は発音中の手だけ
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadeManager : UdonSharpBehaviour
    {
        [Header("Pool（ルートを指定すると、直下の子を起動時に自動で集める）")]
        [Tooltip("直下の子に NadeHead を定員ぶん置く")]
        [SerializeField] private Transform headsRoot;
        [Tooltip("直下の子1つ＝発音1つ。各子に AudioSource 付きの子 'Palm'（必須）と 'Back'（任意）を置く")]
        [SerializeField] private Transform voicesRoot;

        [Header("Presets（直下の NadeSoundPreset を上から順に音色ボタンへ並べる。無効にした子は使わない）")]
        [Tooltip("音色の親。空欄なら子の 'Presets' を探す")]
        [SerializeField] private Transform presetsRoot;

        [Header("Preview（音色ボタンを押したとき、自分にだけ試し聴きを鳴らす）")]
        [Tooltip("試し聴き。空欄なら子の 'Preview' の NadePreview を探す。無ければ試し聴きなし")]
        [SerializeField] private NadePreview preview;

        private const string PalmChildName = "Palm";
        private const string BackChildName = "Back";

        // 起動時に headsRoot / voicesRoot から作る配列
        private NadeHead[] heads;
        private Transform[] voiceRoots;
        private AudioSource[] palmSources;
        private AudioSource[] backSources;

        // 起動時に presetsRoot から作る配列（音色の番号と同じ並び）。毎フレームの処理はここだけを読む
        private int presetCount;
        private string[] presetIds;
        private string[] presetNames;
        private NadeSoundPreset[] presetSources; // 言語ごとの表示名を引くときだけ読む（言語が決まったとき・変わったときだけ）
        private AudioClip[] palmClips;
        private AudioClip[] backClips;       // 手の甲の音が無い（空欄・手のひらと同じ）音色は null
        private float[] presetGain;
        private float[] presetMinSpeed;
        private float[] presetMaxSpeed;
        private float[] presetVolAttack;
        private float[] presetVolRelease;
        private bool[] presetSpeedPitch;     // 手の速さでピッチを変えるか
        private float[] presetPitchRefSpeed; // ピッチ 1.0 になる速度
        private float[] presetPitchExponent;
        private float[] presetPitchMin;
        private float[] presetPitchMax;
        private float[] presetWobbleDepth;
        private float[] presetWobbleTime;
        private float[] presetAttackAmount;
        private float[] presetAttackRise;
        private float[] presetAttackDecay;

        // プレイヤーごとの設定（PlayerObject）。各複製が OnPlayerRestored で登録する
        private NadePlayerPreset[] playerPresets;
        private NadePlayerPreset localPlayerPreset;
        private int localPreset;              // 自分が選んでいる音色（パネル表示用）
        private int pendingLocalPreset = -1;  // 自分の PlayerObject が使えるようになる前に選ばれた音色

        // 手のひら・手の甲の音の使い方（なでる側の設定。全員に同期される）。0 = 向きで切替、1 = 手のひらで統一、2 = 手の甲で統一
        public const int HandAuto = 0;
        public const int HandPalm = 1;
        public const int HandBack = 2;
        private int localHandMode = HandAuto;
        private bool localHandSwap;
        private bool pendingLocalHand;        // 自分の PlayerObject が使えるようになる前に変更された

        [Header("Initial state（操作パネルにもこの値が表示される）")]
        [Tooltip("入室時に ON にするか")]
        [SerializeField] private bool startEnabled = false;
        [Tooltip("入室時の音量（0〜1）。ワールド音量を下げても聞こえるよう Voice の Gain を上げ、50% から始める想定")]
        [SerializeField, Range(0f, 1f)] private float defaultVolume = 0.5f;
        [Tooltip("入室時に「自分で自分をなでても鳴らす」を ON にするか")]
        [SerializeField] private bool defaultSelfPat = false;

        [Header("Hand speed（速度と音量・ピッチの対応は音色ごとに NadeSoundPreset で設定）")]
        [Tooltip("手の速度のなめらかさ。大きいほど反応が速い")]
        [SerializeField] private float speedSmoothing = 12f;

        [Header("Stroke reversal（ストロークの折り返しの検出。折り返しでの音の変化は音色ごと）")]
        [Tooltip("OFF にすると、どの音色でも折り返しでのピッチのゆらぎとアタックを付けない")]
        [SerializeField] private bool useReversal = true;
        [Tooltip("折り返しとみなす向きの変化。それまで動いていた向きと新しい向きの内積がこの値より小さいと折り返し（-1 = 真逆だけ、0 = 直角以上の変化）")]
        [SerializeField, Range(-1f, 0f)] private float reversalDot = -0.3f;
        [Tooltip("この速度（m/s）を超えて動いているときだけ向きを判定する。小さいとトラッキングの揺れで誤反応しやすい")]
        [SerializeField] private float reversalMinSpeed = 0.12f;
        [Tooltip("判定に使う手の速度ベクトルのなめらかさ。大きいほど反応が速いが、揺れも拾いやすい")]
        [SerializeField] private float reversalSmoothing = 18f;
        [Tooltip("一度反応してから、次に反応できるまでの秒数")]
        [SerializeField, Range(0f, 0.5f)] private float reversalCooldown = 0.12f;

        [Header("Palm / Back of hand")]
        [Tooltip("OFF にすると手のひら / 手の甲を区別せず、パネルとタブレットの「手のひら・手の甲」の設定も出なくなる")]
        [SerializeField] private bool useHandSide = true;
        [Tooltip("判定のヒステリシス。大きいほど切り替わりにくい")]
        [SerializeField, Range(0f, 0.9f)] private float sideThreshold = 0.15f;
        [SerializeField] private float sideCrossfade = 10f;

        [Header("Sound position")]
        [Tooltip("音の鳴る位置。0 = Hand ボーン（手首）、1 = 手のひら中心")]
        [SerializeField, Range(0f, 1f)] private float soundPalmBlend = 0f;

        [Header("Geometry（目の高さ 1.6m 基準。アバター身長で自動スケール）")]
        [SerializeField] private float headRadius = 0.15f;
        [Tooltip("視点（目）から頭の中心へのオフセット。Z+ が正面")]
        [SerializeField] private Vector3 headCenterOffset = new Vector3(0f, -0.02f, -0.03f);
        [Tooltip("この距離以内の相手だけ頭レシーバーを有効化")]
        [SerializeField] private float nearDistance = 2.5f;
        [SerializeField] private float nearCheckInterval = 0.25f;

        [Header("Language（表示の言語。各自の端末だけ）")]
        [Tooltip("空欄なら VRChat の表示言語に合わせる。ja / en などを入れると、その言語で固定する（確認用）")]
        [SerializeField] private string forceLanguage = "";
        [Tooltip("VRChat の表示言語に翻訳が無いときに使う言語")]
        [SerializeField] private string fallbackLanguage = "en";

        [Header("Debug")]
        [Tooltip("OFF にすると、デバッグ表示パネル（NadeDebugPanel）が非表示になり、表示の処理もしない（公開時は OFF 推奨）")]
        [SerializeField] private bool debugMode = false;

        private bool selfPatEnabled; // 自分で自分をなでても鳴らすか（各自の端末だけの設定）

        private const float ReferenceEyeHeight = 1.6f;
        private const float DebugInterval = 0.2f;
        private float debugTimer;

        // 折り返し判定の内部定数
        private const float DirectionFollow = 8f;   // 「それまで動いていた向き」が今の向きへ追従する速さ。ゆっくり回す動きは折り返しにしない
        private const float StillResetTime = 0.4f;  // これより長く止まっていたら、次の動き出しは折り返しとみなさない
        private const float StrokePeakHold = 0.6f;  // アタックの基準にする「直前のストロークの音量」を覚えておく時間の目安

        // 速度ピッチの内部定数
        private const float SpeedPitchFollow = 14f;     // ピッチが速度へ追従する速さ（速度自体のなめらかさに重ねて掛かる）
        private const float SpeedPitchSnapVolume = 0.01f; // 音量がこれより小さい（聞こえない）間は、追従させず目標のピッチへ直接合わせる

        // 登録された操作パネル（各パネルが Start で _RegisterPanel を呼ぶ）
        private NadeControlPanel[] panels;
        // 登録されたタブレット（各タブレットが Start で _RegisterTablet を呼ぶ）
        private NadeTabletPanel[] tablets;
        // 登録されたデバッグ表示パネル（Debug Mode オンのときだけ、各パネルが Start で _RegisterDebugPanel を呼ぶ）
        private NadeDebugPanel[] debugPanels;
        // デバッグ表の列の位置（テキストの幅に対する割合）。# / 頭 / 手 / 判定 / 接触 / 速度 / 音量 / 音色 / 手の音 / 状態
        private string[] debugColumns = new string[] { "0%", "4.2%", "20.2%", "25.6%", "35.7%", "41.7%", "49.4%", "57.1%", "73.2%", "88.1%" };
        private const int SideLeft = 0;

        // 表示の言語（NadeStrings の翻訳表）
        private string[] langCodes;
        private string[] stringTable;
        private int stringColumns;
        private DataDictionary stringRows; // 原文（日本語） → 翻訳表の行
        private string clientLanguage = "";
        private int langIndex;             // langCodes の番号。0 = 原文（日本語）

        private bool initialized;
        private bool isOn;
        private float masterVolume;
        private VRCPlayerApi localPlayer;
        private float nearTimer;

        // 頭ごとの状態（heads と同じ並び）
        private VRCPlayerApi[] headOwner;
        private float[] headScale;
        private bool[] headNear;

        // 発音スロット（voiceRoots と同じ並び）
        private bool[] slotUsed;
        private bool[] slotReleasing;
        private int[] slotHead;
        private VRCPlayerApi[] slotPatter;
        private int[] slotSide;
        private int[] slotCount;
        private Vector3[] slotPrevLocal;
        private bool[] slotHasPrev;
        private float[] slotSpeed;
        private float[] slotVolume;
        private float[] slotSpeedPitch;     // 手の速さによるピッチ（1 = 元のピッチ。Speed Pitch が OFF の音色は 1 のまま）
        private float[] slotBack;
        private bool[] slotBackState;
        private int[] slotPreset;           // 鳴り始めたときの音色。鳴り終わるまでこの音色の設定で鳴らす
        private int[] slotHandMode;
        private bool[] slotHandSwap;

        // 折り返し（スロットごと）
        private Vector3[] slotVel;          // なめらかにした手の速度ベクトル（頭基準・身長正規化）
        private Vector3[] slotDir;          // それまで動いていた向き（単位ベクトル）
        private bool[] slotHasDir;
        private float[] slotStillTime;      // 判定速度を下回っている時間
        private float[] slotRevCooldown;
        private int[] slotReversals;        // 折り返しの回数（デバッグ表示用）
        private float[] slotPitchOffset;    // ピッチのずれ（0 = 元のピッチ）
        private float[] slotAttackTime;     // アタックが始まってからの秒数（負 = 鳴っていない）
        private float[] slotAttackLevel;    // 今のアタックで上乗せする音量
        private float[] slotPeak;           // 直前のストロークの音量（ゆっくり減衰）

        // ───────────────────────── 初期化 ─────────────────────────

        private void Start()
        {
            _EnsureInit();
        }

        private void _EnsureInit()
        {
            if (initialized) return;
            initialized = true;

            if (panels == null) panels = new NadeControlPanel[0];
            if (tablets == null) tablets = new NadeTabletPanel[0];
            if (debugPanels == null) debugPanels = new NadeDebugPanel[0];
            if (playerPresets == null) playerPresets = new NadePlayerPreset[0];
            localPlayer = Networking.LocalPlayer;

            _SetupLanguage();
            _CollectPresets();
            _CollectHeads();
            _CollectVoices();
            _SetupPreview();
            Debug.Log("[World Nadenade] Presets " + presetCount.ToString() + " / Heads " + heads.Length.ToString()
                + " / Voices " + voiceRoots.Length.ToString()
                + (preview != null ? " / Preview あり" : " / Preview なし"));

            int headCount = heads.Length;
            headOwner = new VRCPlayerApi[headCount];
            headScale = new float[headCount];
            headNear = new bool[headCount];

            string[] tags = new string[] { "HandL", "HandR", "FingerL", "FingerR" };
            for (int i = 0; i < headCount; i++)
            {
                headScale[i] = 1f;
                if (heads[i] == null) continue;
                heads[i]._Init(this, i, tags);
                heads[i]._SetRadius(headRadius);
                heads[i].gameObject.SetActive(false);
            }

            int voiceCount = voiceRoots.Length;
            slotUsed = new bool[voiceCount];
            slotReleasing = new bool[voiceCount];
            slotHead = new int[voiceCount];
            slotPatter = new VRCPlayerApi[voiceCount];
            slotSide = new int[voiceCount];
            slotCount = new int[voiceCount];
            slotPrevLocal = new Vector3[voiceCount];
            slotHasPrev = new bool[voiceCount];
            slotSpeed = new float[voiceCount];
            slotVolume = new float[voiceCount];
            slotSpeedPitch = new float[voiceCount];
            slotBack = new float[voiceCount];
            slotBackState = new bool[voiceCount];
            slotPreset = new int[voiceCount];
            slotHandMode = new int[voiceCount];
            slotHandSwap = new bool[voiceCount];

            slotVel = new Vector3[voiceCount];
            slotDir = new Vector3[voiceCount];
            slotHasDir = new bool[voiceCount];
            slotStillTime = new float[voiceCount];
            slotRevCooldown = new float[voiceCount];
            slotReversals = new int[voiceCount];
            slotPitchOffset = new float[voiceCount];
            slotAttackTime = new float[voiceCount];
            slotAttackLevel = new float[voiceCount];
            slotPeak = new float[voiceCount];

            for (int s = 0; s < voiceCount; s++)
            {
                _PrepareSource(palmSources[s]);
                _PrepareSource(backSources[s]);
                slotAttackTime[s] = -1f;
                slotSpeedPitch[s] = 1f;
            }

            masterVolume = Mathf.Clamp01(defaultVolume);
            selfPatEnabled = defaultSelfPat;
            _SetOn(startEnabled); // 登録済みのパネルにも状態を配る
        }

        // ───────────────────────── プールの自動収集 ─────────────────────────

        /// presetsRoot の直下の有効な子から NadeSoundPreset を集め、値を配列へコピーする。
        /// 以降は NadeSoundPreset のフィールドを読まない（Udon では他のコンポーネントのフィールド読み取りが重いため）
        private void _CollectPresets()
        {
            if (presetsRoot == null) presetsRoot = transform.Find("Presets");

            int childCount = presetsRoot != null ? presetsRoot.childCount : 0;
            NadeSoundPreset[] found = new NadeSoundPreset[childCount];
            int n = 0;
            for (int c = 0; c < childCount; c++)
            {
                Transform child = presetsRoot.GetChild(c);
                if (!child.gameObject.activeSelf) continue; // 無効にした音色は使わない
                NadeSoundPreset pr = child.GetComponent<NadeSoundPreset>();
                if (pr == null)
                {
                    Debug.LogWarning("[World Nadenade] NadeSoundPreset が無いのでスキップ: " + child.name);
                    continue;
                }
                if (pr.palmClip == null)
                {
                    Debug.LogWarning("[World Nadenade] 手のひらの音（Palm Clip）が無いのでスキップ: " + child.name);
                    continue;
                }
                found[n] = pr;
                n++;
            }

            if (presetsRoot == null) Debug.LogError("[World Nadenade] Presets Root が未設定です（子の 'Presets' も見つかりません）");
            else if (n == 0) Debug.LogError("[World Nadenade] 使える音色がありません（Presets の直下に NadeSoundPreset を置いてください）");

            presetCount = n;
            presetIds = new string[n];
            presetNames = new string[n];
            presetSources = new NadeSoundPreset[n];
            palmClips = new AudioClip[n];
            backClips = new AudioClip[n];
            presetGain = new float[n];
            presetMinSpeed = new float[n];
            presetMaxSpeed = new float[n];
            presetVolAttack = new float[n];
            presetVolRelease = new float[n];
            presetSpeedPitch = new bool[n];
            presetPitchRefSpeed = new float[n];
            presetPitchExponent = new float[n];
            presetPitchMin = new float[n];
            presetPitchMax = new float[n];
            presetWobbleDepth = new float[n];
            presetWobbleTime = new float[n];
            presetAttackAmount = new float[n];
            presetAttackRise = new float[n];
            presetAttackDecay = new float[n];

            for (int i = 0; i < n; i++)
            {
                NadeSoundPreset pr = found[i];
                string objName = pr.gameObject.name;

                string id = pr.presetId;
                if (id == null || id.Length == 0)
                {
                    id = objName;
                    Debug.LogWarning("[World Nadenade] Preset Id が空欄なのでオブジェクト名を使います（公開前に設定してください）: " + objName);
                }
                for (int j = 0; j < i; j++)
                {
                    if (presetIds[j] == id)
                    {
                        Debug.LogError("[World Nadenade] Preset Id が重複しています（前回の音色の復元で先の音色が選ばれます）: " + id);
                        break;
                    }
                }
                presetIds[i] = id;

                string label = pr.displayName;
                presetNames[i] = (label != null && label.Length > 0) ? label : objName;
                presetSources[i] = pr;

                palmClips[i] = pr.palmClip;
                backClips[i] = pr.backClip != pr.palmClip ? pr.backClip : null; // 同じクリップなら手の甲の音なし扱い

                presetGain[i] = Mathf.Clamp01(pr.gain);
                float minSpeed = Mathf.Max(0f, pr.minSpeed);
                presetMinSpeed[i] = minSpeed;
                presetMaxSpeed[i] = Mathf.Max(pr.maxSpeed, minSpeed + 0.01f);
                presetVolAttack[i] = Mathf.Max(0.1f, pr.volumeAttack);
                presetVolRelease[i] = Mathf.Max(0.1f, pr.volumeRelease);
                presetSpeedPitch[i] = pr.speedPitch;
                presetPitchRefSpeed[i] = Mathf.Max(0.01f, pr.pitchReferenceSpeed);
                presetPitchExponent[i] = Mathf.Clamp(pr.pitchSpeedExponent, 0f, 1f);
                presetPitchMin[i] = Mathf.Clamp(pr.pitchMin, 0.1f, 1f);
                presetPitchMax[i] = Mathf.Clamp(pr.pitchMax, 1f, 3f); // AudioSource のピッチは 3 まで
                presetWobbleDepth[i] = Mathf.Max(0f, pr.pitchWobbleDepth);
                presetWobbleTime[i] = Mathf.Max(0.01f, pr.pitchWobbleTime);
                presetAttackAmount[i] = Mathf.Max(0f, pr.attackAmount);
                presetAttackRise[i] = Mathf.Max(0.001f, pr.attackRise);
                presetAttackDecay[i] = Mathf.Max(0.01f, pr.attackDecay);
            }
        }

        /// headsRoot の直下の子から NadeHead を集める（有効・無効は問わない）
        private void _CollectHeads()
        {
            if (headsRoot == null)
            {
                Debug.LogError("[World Nadenade] Heads Root が未設定です");
                heads = new NadeHead[0];
                return;
            }

            int childCount = headsRoot.childCount;
            NadeHead[] found = new NadeHead[childCount];
            int n = 0;
            for (int c = 0; c < childCount; c++)
            {
                Transform child = headsRoot.GetChild(c);
                NadeHead head = child.GetComponent<NadeHead>();
                if (head == null)
                {
                    Debug.LogWarning("[World Nadenade] NadeHead が無いのでスキップ: " + child.name);
                    continue;
                }
                found[n] = head;
                n++;
            }

            heads = new NadeHead[n];
            for (int i = 0; i < n; i++) heads[i] = found[i];
        }

        /// voicesRoot の直下の子1つを発音1つとして集める。
        /// 子の下の 'Palm' を手のひら用、'Back' を手の甲用の AudioSource として使う。
        private void _CollectVoices()
        {
            if (voicesRoot == null)
            {
                Debug.LogError("[World Nadenade] Voices Root が未設定です");
                voiceRoots = new Transform[0];
                palmSources = new AudioSource[0];
                backSources = new AudioSource[0];
                return;
            }

            int childCount = voicesRoot.childCount;
            Transform[] roots = new Transform[childCount];
            AudioSource[] palms = new AudioSource[childCount];
            AudioSource[] backs = new AudioSource[childCount];
            int n = 0;
            for (int c = 0; c < childCount; c++)
            {
                Transform voice = voicesRoot.GetChild(c);

                Transform palmTf = voice.Find(PalmChildName);
                AudioSource palm = palmTf != null ? palmTf.GetComponent<AudioSource>() : null;
                if (palm == null)
                {
                    Debug.LogWarning("[World Nadenade] AudioSource 付きの子 '" + PalmChildName + "' が無いのでスキップ: " + voice.name);
                    continue;
                }

                Transform backTf = voice.Find(BackChildName);
                AudioSource back = backTf != null ? backTf.GetComponent<AudioSource>() : null;
                if (backTf != null && back == null)
                {
                    Debug.LogWarning("[World Nadenade] '" + BackChildName + "' に AudioSource が無いので手の甲音なし: " + voice.name);
                }

                roots[n] = voice;
                palms[n] = palm;
                backs[n] = back;
                n++;
            }

            voiceRoots = new Transform[n];
            palmSources = new AudioSource[n];
            backSources = new AudioSource[n];
            for (int i = 0; i < n; i++)
            {
                voiceRoots[i] = roots[i];
                palmSources[i] = palms[i];
                backSources[i] = backs[i];
            }
        }

        // ───────────────────────── 表示の言語 ─────────────────────────

        private void _SetupLanguage()
        {
            langCodes = NadeStrings.Languages();
            stringTable = NadeStrings.Table();
            stringColumns = langCodes.Length;
            stringRows = new DataDictionary();
            int rows = stringColumns > 0 ? stringTable.Length / stringColumns : 0;
            for (int r = 0; r < rows; r++)
            {
                string source = stringTable[r * stringColumns];
                if (source == null || source.Length == 0) continue;
                if (!stringRows.ContainsKey(source)) stringRows.Add(source, r);
            }

            string current = VRCPlayerApi.GetCurrentLanguage();
            clientLanguage = current != null ? current : "";
            langIndex = _ResolveLanguage();
        }

        /// VRChat の表示言語が変わったとき。全パネル・タブレットの文字を作り直す
        /// （翻訳の言語が同じでも、音色の Localized Names は表示言語ごとに違うことがあるので、毎回作り直す。めったに起きないので負荷は気にしない）
        public override void OnLanguageChanged(string language)
        {
            _EnsureInit();
            clientLanguage = language != null ? language : "";
            langIndex = _ResolveLanguage();

            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] != null) panels[i]._ApplyLanguage();
            }
            for (int i = 0; i < tablets.Length; i++)
            {
                if (tablets[i] != null) tablets[i]._ApplyLanguage();
            }
            _BroadcastState(); // 状態で変わる文字（ON/OFF・説明文など）を新しい言語で作り直す
        }

        /// Force Language → VRChat の表示言語 → Fallback Language → 原文（日本語）の順に、翻訳表にある言語を選ぶ
        private int _ResolveLanguage()
        {
            int i = _FindLanguage(_RequestedLanguage());
            if (i < 0) i = _FindLanguage(fallbackLanguage);
            if (i < 0) i = 0;
            return i;
        }

        /// 求められた言語。Force Language があればそれ、無ければ VRChat の表示言語
        private string _RequestedLanguage()
        {
            string forced = forceLanguage != null ? forceLanguage.Trim() : "";
            return forced.Length > 0 ? forced : clientLanguage;
        }

        /// 言語コードを翻訳表の列の番号にする。無ければ -1。
        /// en-US のような地域付きは、地域なし（en）でも探す。中国語は簡体字・繁体字を取り違えないよう、地域まで一致したときだけ
        private int _FindLanguage(string code)
        {
            if (code == null || code.Length == 0) return -1;
            string want = _NormalizeLanguage(code);
            for (int i = 0; i < langCodes.Length; i++)
            {
                if (_NormalizeLanguage(langCodes[i]) == want) return i;
            }
            int dash = want.IndexOf('-');
            if (dash <= 0) return -1;
            string baseCode = want.Substring(0, dash);
            if (baseCode == "zh") return -1;
            for (int i = 0; i < langCodes.Length; i++)
            {
                if (_NormalizeLanguage(langCodes[i]) == baseCode) return i;
            }
            return -1;
        }

        private string _NormalizeLanguage(string code)
        {
            return code.Trim().Replace("_", "-").ToLower();
        }

        /// 原文（日本語）を表示の言語の文にする。翻訳表に無い文字・空欄の翻訳は、そのまま返す
        public string _Tr(string source)
        {
            _EnsureInit();
            if (source == null || source.Length == 0 || langIndex == 0) return source;
            DataToken row;
            if (!stringRows.TryGetValue(source, out row)) return source;
            string text = stringTable[row.Int * stringColumns + langIndex];
            return (text != null && text.Length > 0) ? text : source;
        }

        /// 翻訳表に原文として載っているか。パネルが起動時に、差し替える文字を選ぶのに使う
        public bool _HasTranslation(string source)
        {
            _EnsureInit();
            if (source == null || source.Length == 0) return false;
            return stringRows.ContainsKey(source);
        }

        /// 今の表示の言語のコード（ja / en など）
        public string _GetLanguage()
        {
            _EnsureInit();
            return langCodes[langIndex];
        }

        // ───────────────────────── 操作パネル ─────────────────────────

        /// NadeControlPanel が Start で呼ぶ。登録と同時に現在の状態を表示させる
        public void _RegisterPanel(NadeControlPanel panel)
        {
            _EnsureInit();
            if (panel == null) return;
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == panel)
                {
                    panel._ApplyState(isOn, masterVolume, localPreset, selfPatEnabled);
                    panel._ApplyHandSide(localHandMode, localHandSwap);
                    return;
                }
            }

            NadeControlPanel[] next = new NadeControlPanel[panels.Length + 1];
            for (int i = 0; i < panels.Length; i++) next[i] = panels[i];
            next[panels.Length] = panel;
            panels = next;

            panel._ApplyState(isOn, masterVolume, localPreset, selfPatEnabled);
            panel._ApplyHandSide(localHandMode, localHandSwap);
        }

        /// NadeTabletPanel が Start で呼ぶ。登録と同時に現在の状態を表示させる
        public void _RegisterTablet(NadeTabletPanel tablet)
        {
            _EnsureInit();
            if (tablet == null) return;
            for (int i = 0; i < tablets.Length; i++)
            {
                if (tablets[i] == tablet)
                {
                    tablet._ApplyState(isOn, masterVolume, localPreset, selfPatEnabled);
                    tablet._ApplyHandSide(localHandMode, localHandSwap);
                    return;
                }
            }

            NadeTabletPanel[] next = new NadeTabletPanel[tablets.Length + 1];
            for (int i = 0; i < tablets.Length; i++) next[i] = tablets[i];
            next[tablets.Length] = tablet;
            tablets = next;

            tablet._ApplyState(isOn, masterVolume, localPreset, selfPatEnabled);
            tablet._ApplyHandSide(localHandMode, localHandSwap);
        }

        /// NadeDebugPanel が Start で呼ぶ（Debug Mode オンのときだけ）。次のフレームで表示を作る
        public void _RegisterDebugPanel(NadeDebugPanel panel)
        {
            _EnsureInit();
            if (panel == null) return;
            for (int i = 0; i < debugPanels.Length; i++)
            {
                if (debugPanels[i] == panel) { debugTimer = 0f; return; }
            }

            NadeDebugPanel[] next = new NadeDebugPanel[debugPanels.Length + 1];
            for (int i = 0; i < debugPanels.Length; i++) next[i] = debugPanels[i];
            next[debugPanels.Length] = panel;
            debugPanels = next;
            debugTimer = 0f;
        }

        /// 操作パネルのスライダーから呼ばれる。値が変わったときだけ全パネルへ配る（パネル間のループ防止）
        public void _SetVolume(float volume)
        {
            _EnsureInit();
            volume = Mathf.Clamp01(volume);
            if (Mathf.Abs(volume - masterVolume) < 0.0001f) return;
            masterVolume = volume;
            _BroadcastState();
            // 鳴っている音（試し聴きを含む）は次フレームの音量計算で新しい値に追従する
        }

        private void _BroadcastState()
        {
            for (int i = 0; i < panels.Length; i++)
            {
                if (panels[i] == null) continue;
                panels[i]._ApplyState(isOn, masterVolume, localPreset, selfPatEnabled);
                panels[i]._ApplyHandSide(localHandMode, localHandSwap);
            }
            for (int i = 0; i < tablets.Length; i++)
            {
                if (tablets[i] == null) continue;
                tablets[i]._ApplyState(isOn, masterVolume, localPreset, selfPatEnabled);
                tablets[i]._ApplyHandSide(localHandMode, localHandSwap);
            }
        }

        private void _PrepareSource(AudioSource src)
        {
            if (src == null) return;
            src.playOnAwake = false;
            src.loop = true;
            src.volume = 0f;
            src.pitch = 1f;
            src.Stop();
        }

        // ───────────────────────── プレイヤー管理 ─────────────────────────

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            _EnsureInit();
            if (player == null || !player.IsValid()) return;
            if (_FindHead(player) >= 0) return;

            for (int i = 0; i < heads.Length; i++)
            {
                if (heads[i] == null || headOwner[i] != null) continue;
                headOwner[i] = player;
                heads[i].owner = player;
                headNear[i] = false;
                _UpdateScale(i);
                nearTimer = 0f; // 次フレームで有効化判定
                return;
            }
            // プールが足りない場合、その人の頭は判定対象外（heads をワールド定員ぶん用意する）
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            _EnsureInit();
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (!slotUsed[s]) continue;
                if (slotPatter[s] == player || headOwner[slotHead[s]] == player) _FreeSlot(s);
            }

            int i = _FindHead(player);
            if (i < 0) return;
            headOwner[i] = null;
            heads[i].owner = null;
            headNear[i] = false;
            heads[i].gameObject.SetActive(false);
        }

        public override void OnAvatarEyeHeightChanged(VRCPlayerApi player, float prevEyeHeightAsMeters)
        {
            _EnsureInit();
            int i = _FindHead(player);
            if (i >= 0) _UpdateScale(i);
        }

        public override void OnAvatarChanged(VRCPlayerApi player)
        {
            _EnsureInit();
            // アバター変更でContactが作り直されると Exit が届かないことがあるので、関係スロットを止める
            _ReleaseSlotsForPlayer(player);
            int i = _FindHead(player);
            if (i >= 0) _UpdateScale(i);
        }

        private int _FindHead(VRCPlayerApi player)
        {
            if (player == null) return -1;
            for (int i = 0; i < headOwner.Length; i++)
            {
                if (headOwner[i] == player) return i;
            }
            return -1;
        }

        private void _UpdateScale(int i)
        {
            VRCPlayerApi p = headOwner[i];
            float scale = 1f;
            if (p != null && p.IsValid())
            {
                scale = Mathf.Max(0.1f, p.GetAvatarEyeHeightAsMeters() / ReferenceEyeHeight);
            }
            if (Mathf.Abs(scale - headScale[i]) < 0.001f) return; // 設定変更は重いので変化時だけ
            headScale[i] = scale;
            heads[i]._SetRadius(headRadius * scale);
        }

        // ───────────────────────── スイッチ / プリセット ─────────────────────────

        public void _ToggleSwitch()
        {
            _EnsureInit();
            _SetOn(!isOn);
        }

        /// 「自分で自分をなでても鳴らす」の ON/OFF（パネルから呼ばれる。各自の端末だけの設定）
        public void _ToggleSelfPat()
        {
            _EnsureInit();
            selfPatEnabled = !selfPatEnabled;

            if (!selfPatEnabled)
            {
                // 自分の手で自分の頭を鳴らしている音を止める
                VRCPlayerApi me = Networking.LocalPlayer;
                for (int s = 0; s < slotUsed.Length; s++)
                {
                    if (slotUsed[s] && slotPatter[s] == me && headOwner[slotHead[s]] == me)
                    {
                        slotCount[s] = 0;
                        slotReleasing[s] = true;
                    }
                }
            }
            _BroadcastState();
        }

        /// パネルが起動時に問い合わせる。false ならデバッグ表示と切替ボタンを隠す
        public bool _IsDebugMode()
        {
            return debugMode;
        }

        // ───────────────────────── 音色（プリセット） ─────────────────────────
        // パネル・タブレットの Start から先に呼ばれることがあるので、どれも _EnsureInit を通す

        public int _GetPresetCount()
        {
            _EnsureInit();
            return presetCount;
        }

        /// 表示の言語に合わせた音色名。音色の Localized Names（求められた言語 → 翻訳に使っている言語の順）→ Display Name
        public string _GetPresetName(int index)
        {
            _EnsureInit();
            if (index < 0 || index >= presetCount) return "";
            string baseName = presetNames[index];

            NadeSoundPreset pr = presetSources[index];
            if (pr != null)
            {
                string requested = _RequestedLanguage();
                string named = _FindLocalizedName(pr, requested);
                if (named.Length == 0 && langCodes[langIndex] != requested) named = _FindLocalizedName(pr, langCodes[langIndex]);
                if (named.Length > 0) return named;
            }
            return baseName;
        }

        /// 音色の Name Languages から言語コードの一致する名前を探す。無ければ空文字
        private string _FindLocalizedName(NadeSoundPreset pr, string code)
        {
            if (code == null || code.Length == 0) return "";
            string[] codes = pr.nameLanguages;
            string[] names = pr.localizedNames;
            if (codes == null || names == null) return "";
            string want = _NormalizeLanguage(code);
            int n = Mathf.Min(codes.Length, names.Length);
            for (int i = 0; i < n; i++)
            {
                if (codes[i] == null || names[i] == null || names[i].Length == 0) continue;
                if (_NormalizeLanguage(codes[i]) == want) return names[i];
            }
            return "";
        }

        /// 音色の保存用の名前（Preset Id）。範囲外なら空文字
        public string _GetPresetId(int index)
        {
            _EnsureInit();
            if (index < 0 || index >= presetCount) return "";
            return presetIds[index];
        }

        /// Preset Id から音色の番号を探す。見つからなければ -1（重複していれば先の音色）
        public int _FindPresetIndex(string id)
        {
            _EnsureInit();
            if (id == null || id.Length == 0) return -1;
            for (int i = 0; i < presetCount; i++)
            {
                if (presetIds[i] == id) return i;
            }
            return -1;
        }

        /// プリセットに、手のひらとは別の手の甲用の音があるか。
        /// 手の甲用が空欄、または手のひらと同じクリップなら false（どちらの面で触れても同じ音になる）
        public bool _PresetHasBackSound(int index)
        {
            _EnsureInit();
            if (index < 0 || index >= presetCount) return false;
            return backClips[index] != null;
        }

        /// 自分の「なでる側の音色」を選ぶ（パネルの音色ボタンから呼ばれる）。全員に同期される。
        /// 押すたびに、その音色を自分にだけ試し聴きさせる（選択中の音色をもう一度押しても聴き直せる）
        public void _SelectPreset(int index)
        {
            _EnsureInit();
            if (presetCount == 0) return;
            index = Mathf.Clamp(index, 0, presetCount - 1);

            if (preview != null) preview._Play(index, palmClips[index], backClips[index], presetGain[index]);
            if (index == localPreset) return;

            localPreset = index;
            if (localPlayerPreset != null && Utilities.IsValid(localPlayerPreset))
            {
                localPlayerPreset._SetPreset(index, presetIds[index]);
            }
            else
            {
                pendingLocalPreset = index; // PlayerObject の準備ができたら送る
            }

            // 自分の手で鳴っている音はフェードアウトし、次になでたときから新しい音色
            VRCPlayerApi me = Networking.LocalPlayer;
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (slotUsed[s] && slotPatter[s] == me) { slotCount[s] = 0; slotReleasing[s] = true; }
            }
            _BroadcastState();
        }

        /// 自分の音色を次へ進める（NadeSwitch などから使う）
        public void _NextPreset()
        {
            _EnsureInit();
            if (presetCount == 0) return;
            _SelectPreset((localPreset + 1) % presetCount);
        }

        // ───────────────────────── 試し聴き ─────────────────────────

        /// 試し聴き（NadePreview）を探して初期化する
        private void _SetupPreview()
        {
            if (preview == null)
            {
                Transform t = transform.Find("Preview");
                if (t != null) preview = t.GetComponent<NadePreview>();
            }
            if (preview != null) preview._Init();
        }

        // ───────────────────────── 手のひら・手の甲の音の使い方 ─────────────────────────

        /// タブレットが起動時に問い合わせる。false なら「手のひら・手の甲」の設定を出さない
        public bool _IsHandSideEnabled()
        {
            return useHandSide;
        }

        /// 自分の「手のひら・手の甲」の設定を選ぶ（0 = 向きで切替、1 = 手のひらで統一、2 = 手の甲で統一）。全員に同期される
        public void _SetHandMode(int mode)
        {
            _EnsureInit();
            mode = Mathf.Clamp(mode, HandAuto, HandBack);
            if (mode == localHandMode) return;
            localHandMode = mode;
            _SendLocalHandSide();
        }

        /// 自分の「手のひらと手の甲を入れ替える」を切り替える。全員に同期される
        public void _ToggleHandSwap()
        {
            _EnsureInit();
            localHandSwap = !localHandSwap;
            _SendLocalHandSide();
        }

        private void _SendLocalHandSide()
        {
            if (localPlayerPreset != null && Utilities.IsValid(localPlayerPreset))
            {
                localPlayerPreset._SetHandSide(localHandMode, localHandSwap);
            }
            else
            {
                pendingLocalHand = true; // PlayerObject の準備ができたら送る
            }

            // 自分の手で鳴っている音にもすぐ反映する（音色と違い、切り替えはクロスフェードで滑らかに）
            _ApplyHandSideToSlots(Networking.LocalPlayer, localHandMode, localHandSwap);
            _BroadcastState();
        }

        /// NadePlayerPreset が他の人の設定を受け取ったときに呼ぶ。その人の手で鳴っている音へ反映する
        public void _OnRemoteHandSideChanged(NadePlayerPreset p)
        {
            if (!initialized || p == null) return;
            VRCPlayerApi owner = p._GetOwnerPlayer();
            if (owner == null || !owner.IsValid() || owner.isLocal) return;
            _ApplyHandSideToSlots(owner, Mathf.Clamp(p._GetHandMode(), HandAuto, HandBack), p._GetHandSwap());
        }

        private void _ApplyHandSideToSlots(VRCPlayerApi patter, int mode, bool swap)
        {
            if (patter == null || slotUsed == null) return;
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (!slotUsed[s] || slotPatter[s] != patter) continue;
                slotHandMode[s] = mode;
                slotHandSwap[s] = swap;
            }
        }

        /// なでている人の手のひら・手の甲の設定を返す。mode（0〜2）に、入れ替えありなら 4 を足した値
        private int _GetHandSideForPlayer(VRCPlayerApi player)
        {
            if (player == null || !player.IsValid()) return HandAuto;
            if (player.isLocal) return localHandMode + (localHandSwap ? 4 : 0);

            for (int i = 0; i < playerPresets.Length; i++)
            {
                NadePlayerPreset p = playerPresets[i];
                if (p == null || !Utilities.IsValid(p)) continue;
                if (p._GetOwnerPlayer() == player)
                {
                    return Mathf.Clamp(p._GetHandMode(), HandAuto, HandBack) + (p._GetHandSwap() ? 4 : 0);
                }
            }
            return HandAuto;
        }

        // ───────────────────────── PlayerObject ─────────────────────────

        /// NadePlayerPreset（各プレイヤーの PlayerObject）が使える状態になったら呼ばれる
        public void _RegisterPlayerPreset(NadePlayerPreset p)
        {
            _EnsureInit();
            if (p == null) return;

            // 退室で破棄された古い登録を詰めながら追加する
            int n = 0;
            NadePlayerPreset[] next = new NadePlayerPreset[playerPresets.Length + 1];
            for (int i = 0; i < playerPresets.Length; i++)
            {
                NadePlayerPreset q = playerPresets[i];
                if (q == null || !Utilities.IsValid(q)) continue;
                if (q == p) continue;
                next[n] = q;
                n++;
            }
            next[n] = p;
            n++;
            playerPresets = new NadePlayerPreset[n];
            for (int i = 0; i < n; i++) playerPresets[i] = next[i];

            if (p._IsLocal())
            {
                localPlayerPreset = p;
                if (pendingLocalPreset >= 0)
                {
                    p._SetPreset(pendingLocalPreset, _GetPresetId(pendingLocalPreset));
                    pendingLocalPreset = -1;
                }
                else
                {
                    _RestoreLocalPreset(p);
                }

                if (pendingLocalHand)
                {
                    p._SetHandSide(localHandMode, localHandSwap);
                    pendingLocalHand = false;
                }
                else
                {
                    localHandMode = Mathf.Clamp(p._GetHandMode(), HandAuto, HandBack);
                    localHandSwap = p._GetHandSwap();
                }
                _BroadcastState();
            }
        }

        /// 自分の PlayerObject に入っている音色（Persistence で復元された前回の選択）を反映する。
        /// - Preset Id が入っていれば、それで探す（並べ替え・途中への追加をしても正しい音色になる）
        /// - その Id の音色が削除されていれば先頭の音色にする
        /// - Id が空（Persistence なし・まだ選んでいない）なら番号をそのまま使う
        /// 番号が今の並びと食い違っていたら、正しい番号と Id を送り直す（他の人は番号でも読めるように）
        private void _RestoreLocalPreset(NadePlayerPreset p)
        {
            if (presetCount == 0) { localPreset = 0; return; }

            string savedId = p._GetPresetId();
            bool hasId = savedId != null && savedId.Length > 0;
            int index;
            if (hasId)
            {
                index = _FindPresetIndex(savedId);
                if (index < 0) index = 0;
            }
            else
            {
                index = Mathf.Clamp(p._GetPreset(), 0, presetCount - 1);
            }
            localPreset = index;

            if (hasId && (p._GetPreset() != index || savedId != presetIds[index]))
            {
                p._SetPreset(index, presetIds[index]);
            }
        }

        /// なでている人（patter）の音色を返す。PlayerObject がまだ使えなければ 0、音色が1つも無ければ -1。
        /// 他の人の音色は Preset Id で探す（ワールド更新直後などで並びが違っても、同じ音色で鳴るように）
        private int _GetPresetForPlayer(VRCPlayerApi player)
        {
            if (presetCount == 0) return -1;
            if (player == null || !player.IsValid()) return 0;
            if (player.isLocal) return Mathf.Clamp(localPreset, 0, presetCount - 1);

            for (int i = 0; i < playerPresets.Length; i++)
            {
                NadePlayerPreset p = playerPresets[i];
                if (p == null || !Utilities.IsValid(p)) continue;
                if (p._GetOwnerPlayer() != player) continue;

                string id = p._GetPresetId();
                if (id != null && id.Length > 0)
                {
                    int found = _FindPresetIndex(id);
                    return found >= 0 ? found : 0; // このワールドに無い音色なら先頭
                }
                return Mathf.Clamp(p._GetPreset(), 0, presetCount - 1);
            }
            return 0;
        }

        private void _SetOn(bool on)
        {
            isOn = on;
            _BroadcastState();

            if (!on)
            {
                for (int s = 0; s < slotUsed.Length; s++)
                {
                    if (slotUsed[s]) _FreeSlot(s);
                }
                for (int i = 0; i < heads.Length; i++)
                {
                    headNear[i] = false;
                    if (heads[i] != null) heads[i].gameObject.SetActive(false);
                }
            }
            nearTimer = 0f;
        }

        // ───────────────────────── 毎フレーム ─────────────────────────

        public override void PostLateUpdate()
        {
            if (!initialized) return;

            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            // 試し聴きは、なで音の ON/OFF と関係なく最後まで鳴らす（OFF のまま壁のパネルで音色を選んだときも聞こえる）
            if (preview != null) preview._Tick(dt, masterVolume);

            // デバッグ表示は、どれかのパネルで表示中のときだけ更新する（文字列は1回だけ作って配る）
            debugTimer -= dt;
            if (debugTimer <= 0f)
            {
                debugTimer = DebugInterval;
                if (debugMode && _AnyDebugPanelUpdating()) _UpdateDebugText();
            }

            if (!isOn) return;
            if (localPlayer == null || !localPlayer.IsValid()) return;

            nearTimer -= dt;
            if (nearTimer <= 0f)
            {
                nearTimer = nearCheckInterval;
                _UpdateNear();
            }

            for (int i = 0; i < heads.Length; i++)
            {
                if (headNear[i]) _PlaceHead(i);
            }

            _UpdateSlots(dt);
        }

        private void _UpdateNear()
        {
            Vector3 me = localPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
            int localIndex = _FindHead(localPlayer);
            float myScale = localIndex >= 0 ? headScale[localIndex] : 1f;

            for (int i = 0; i < heads.Length; i++)
            {
                VRCPlayerApi owner = headOwner[i];
                bool near = false;
                if (owner != null && owner.IsValid())
                {
                    if (owner.isLocal)
                    {
                        near = true; // 自分の頭は常に有効（なでられ判定）
                    }
                    else
                    {
                        float reach = nearDistance * Mathf.Max(myScale, headScale[i]);
                        Vector3 other = owner.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                        near = (other - me).sqrMagnitude < reach * reach;
                    }
                }

                if (near == headNear[i]) continue;
                headNear[i] = near;
                heads[i].gameObject.SetActive(near);
                if (!near) _ReleaseSlotsForHead(i);
            }
        }

        private void _PlaceHead(int i)
        {
            VRCPlayerApi p = headOwner[i];
            if (p == null || !p.IsValid()) return;

            VRCPlayerApi.TrackingData td = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            Vector3 pos = td.position;
            Quaternion rot = td.rotation;
            if (pos == Vector3.zero)
            {
                pos = p.GetBonePosition(HumanBodyBones.Head);
                rot = p.GetBoneRotation(HumanBodyBones.Head);
            }
            heads[i].transform.SetPositionAndRotation(pos + rot * (headCenterOffset * headScale[i]), rot);
        }

        private void _UpdateSlots(float dt)
        {
            float speedK = 1f - Mathf.Exp(-speedSmoothing * dt);
            float sideK = 1f - Mathf.Exp(-sideCrossfade * dt);
            float pitchK = 1f - Mathf.Exp(-SpeedPitchFollow * dt);
            float peakDecay = Mathf.Exp(-dt / StrokePeakHold);

            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (!slotUsed[s]) continue;

                int pr = slotPreset[s]; // 鳴り始めたときの音色（_HandEnter で 0 以上を保証）
                int h = slotHead[s];
                VRCPlayerApi patter = slotPatter[s];
                VRCPlayerApi owner = headOwner[h];
                float targetVolume = 0f;

                bool valid = headNear[h]
                    && patter != null && patter.IsValid()
                    && owner != null && owner.IsValid();

                Vector3 hand = Vector3.zero;
                if (valid)
                {
                    hand = patter.GetBonePosition(slotSide[s] == SideLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
                    if (hand == Vector3.zero) valid = false;
                }

                if (valid)
                {
                    // 指の付け根は手のひら中心（速度・距離・裏表判定）の算出に使う
                    bool left = slotSide[s] == SideLeft;
                    Vector3 index = patter.GetBonePosition(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
                    Vector3 little = patter.GetBonePosition(left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
                    bool fingersOk = index != Vector3.zero && little != Vector3.zero;
                    Vector3 palm = fingersOk ? (hand + index + little) / 3f : hand;
                    // 音の位置：0 = Hand ボーン（手首）、1 = 手のひら中心
                    voiceRoots[s].position = Vector3.Lerp(hand, palm, soundPalmBlend);

                    Transform headTf = heads[h].transform;
                    float scale = headScale[h];

                    // Exit を取りこぼした場合の保険：手が頭から十分離れたら終了扱い
                    float limit = (headRadius + 0.35f) * scale;
                    if ((palm - headTf.position).sqrMagnitude > limit * limit)
                    {
                        slotCount[s] = 0;
                        slotReleasing[s] = true;
                    }

                    if (!slotReleasing[s])
                    {
                        // 頭基準・身長正規化した手の速度
                        Vector3 local = headTf.InverseTransformPoint(palm) / scale;
                        Vector3 vel = Vector3.zero;
                        if (slotHasPrev[s]) vel = (local - slotPrevLocal[s]) / dt;
                        slotPrevLocal[s] = local;
                        slotHasPrev[s] = true;

                        float maxSpeed = presetMaxSpeed[pr];
                        float raw = vel.magnitude;
                        float rawLimit = maxSpeed * 3f; // 同期ジャンプ対策
                        if (raw > rawLimit)
                        {
                            vel = vel * (rawLimit / raw);
                            raw = rawLimit;
                        }
                        slotSpeed[s] = Mathf.Lerp(slotSpeed[s], raw, speedK);
                        targetVolume = masterVolume * presetGain[pr] * Mathf.InverseLerp(presetMinSpeed[pr], maxSpeed, slotSpeed[s]);

                        // 速度ピッチ：ピッチ = (速度 ÷ 基準速度)^Exponent。
                        // 無音になる速度以下では更新しない（折り返しで止まるたびに、余韻のピッチが下がり続けるのを防ぐ）。
                        // 終了中（手が離れたあとの余韻）も更新しないので、最後のピッチのまま消える
                        if (presetSpeedPitch[pr] && slotSpeed[s] > presetMinSpeed[pr])
                        {
                            float targetPitch = Mathf.Clamp(
                                Mathf.Pow(slotSpeed[s] / presetPitchRefSpeed[pr], presetPitchExponent[pr]),
                                presetPitchMin[pr], presetPitchMax[pr]);
                            // 聞こえていない間（鳴り始め・止まったあとの動き出し）は、ずり上がらないよう直接合わせる
                            slotSpeedPitch[s] = slotVolume[s] < SpeedPitchSnapVolume
                                ? targetPitch
                                : Mathf.Lerp(slotSpeedPitch[s], targetPitch, pitchK);
                        }

                        if (useReversal) _DetectReversal(s, vel, dt);

                        if (useHandSide && fingersOk)
                        {
                            // 手のひら法線：左手は (人差し指-手首)×(小指-手首)、右手は逆順
                            Vector3 a = index - hand;
                            Vector3 b = little - hand;
                            Vector3 normal = slotSide[s] == SideLeft ? Vector3.Cross(a, b) : Vector3.Cross(b, a);
                            float d = Vector3.Dot(normal.normalized, (headTf.position - palm).normalized);
                            if (slotBackState[s] && d > sideThreshold) slotBackState[s] = false;
                            else if (!slotBackState[s] && d < -sideThreshold) slotBackState[s] = true;
                        }
                    }
                }
                else
                {
                    slotCount[s] = 0;
                    slotReleasing[s] = true;
                }

                // 終了中は折り返しを数えない（再び触れたときに古い向きと比べて誤反応しないよう向きも忘れる）
                if (slotReleasing[s]) slotHasDir[s] = false;

                float rate = targetVolume > slotVolume[s] ? presetVolAttack[pr] : presetVolRelease[pr];
                slotVolume[s] = Mathf.Lerp(slotVolume[s], targetVolume, 1f - Mathf.Exp(-rate * dt));
                slotPeak[s] = Mathf.Max(slotVolume[s], slotPeak[s] * peakDecay);

                slotBack[s] = Mathf.Lerp(slotBack[s], _BackTarget(s), sideK);

                // 折り返しのアタック（音量の上乗せ）とピッチのゆらぎ。ゆらぎは速度ピッチに対する割合で掛ける
                float attack = _AttackEnvelope(s, dt);
                slotPitchOffset[s] *= Mathf.Exp(-dt * 3f / presetWobbleTime[pr]);
                float pitch = slotSpeedPitch[s] * (1f + slotPitchOffset[s]);

                float v = Mathf.Min(1f, slotVolume[s] + slotAttackLevel[s] * attack);
                palmSources[s].volume = v * (1f - slotBack[s]);
                palmSources[s].pitch = pitch;
                if (backSources[s] != null)
                {
                    backSources[s].volume = v * slotBack[s];
                    backSources[s].pitch = pitch;
                }

                if (slotReleasing[s] && slotVolume[s] < 0.002f) _FreeSlot(s);
            }
        }

        // ───────────────────────── 折り返し ─────────────────────────

        /// 手の速度ベクトルから、ストロークの折り返しを見つける。
        /// 折り返しでは手がいったん止まりかけてから逆向きに動き出すので、
        /// 「それまでの向き」をゆっくり追従させておき、動き出した向きがそれと大きく違えば折り返しとみなす。
        /// 円を描くような動きは「それまでの向き」が追いつくので反応しない
        private void _DetectReversal(int s, Vector3 vel, float dt)
        {
            slotVel[s] = Vector3.Lerp(slotVel[s], vel, 1f - Mathf.Exp(-reversalSmoothing * dt));
            if (slotRevCooldown[s] > 0f) slotRevCooldown[s] -= dt;

            float speed = slotVel[s].magnitude;
            if (speed < reversalMinSpeed || speed < 0.0001f)
            {
                // 止まりかけ（折り返しの途中）は向きを更新しない。長く止まっていたら向きを忘れる
                slotStillTime[s] += dt;
                if (slotStillTime[s] > StillResetTime) slotHasDir[s] = false;
                return;
            }
            slotStillTime[s] = 0f;

            Vector3 dir = slotVel[s] / speed;
            if (!slotHasDir[s])
            {
                slotDir[s] = dir;
                slotHasDir[s] = true;
                return;
            }

            if (slotRevCooldown[s] <= 0f && Vector3.Dot(dir, slotDir[s]) < reversalDot)
            {
                _OnReversal(s);
                slotDir[s] = dir;
                return;
            }

            Vector3 follow = Vector3.Lerp(slotDir[s], dir, 1f - Mathf.Exp(-DirectionFollow * dt));
            float len = follow.magnitude;
            slotDir[s] = len > 0.0001f ? follow / len : dir;
        }

        /// 折り返しを見つけたとき：ピッチをランダムにずらし、アタックを鳴らし直す（量は音色ごと）
        private void _OnReversal(int s)
        {
            int pr = slotPreset[s];
            slotReversals[s]++;
            slotRevCooldown[s] = reversalCooldown;

            float depth = presetWobbleDepth[pr];
            if (depth > 0f)
            {
                float sign = Random.value < 0.5f ? -1f : 1f;
                slotPitchOffset[s] = sign * depth * Random.Range(0.5f, 1f);
            }

            float amount = presetAttackAmount[pr];
            if (amount > 0f)
            {
                // 折り返しの瞬間は音量が下がっているので、直前のストロークの音量を基準にする（弱くなでれば弱いアタック）
                slotAttackTime[s] = 0f;
                slotAttackLevel[s] = amount * slotPeak[s];
            }
        }

        /// アタックの形（0〜1）を返して時間を進める。短く立ち上がり、指数的に消える
        private float _AttackEnvelope(int s, float dt)
        {
            float t = slotAttackTime[s];
            if (t < 0f) return 0f;

            int pr = slotPreset[s];
            t += dt;
            float rise = presetAttackRise[pr];
            float decay = presetAttackDecay[pr];
            float env;
            if (t < rise)
            {
                env = t / rise;
            }
            else
            {
                float after = t - rise;
                env = Mathf.Exp(-after * 3f / decay);
                if (after > decay * 2f)
                {
                    env = 0f;
                    t = -1f; // 終わり
                    slotAttackLevel[s] = 0f;
                }
            }
            slotAttackTime[s] = t;
            return env;
        }

        /// 判定した面に「入れ替え」を反映した面（true = 手の甲）。「向きで切替」のときの音の選択に使う
        private bool _IsBackSide(int s)
        {
            if (!useHandSide) return false;
            return slotHandSwap[s] ? !slotBackState[s] : slotBackState[s];
        }

        /// 手の甲用の音源へのクロスフェード量の目標（0 = 手のひらの音、1 = 手の甲の音）。
        /// - 向きで切替：判定した面（入れ替えを反映）の音
        /// - 手のひらで統一 / 手の甲で統一：触れた面にかかわらず、その音だけ
        /// 手の甲用のクリップが無いプリセットは、どの設定でも手のひらの音
        private float _BackTarget(int s)
        {
            if (!useHandSide) return 0f;
            bool hasBack = backSources[s] != null && backSources[s].clip != null;
            if (!hasBack) return 0f;
            int mode = slotHandMode[s];
            if (mode == HandPalm) return 0f;
            if (mode == HandBack) return 1f;
            return _IsBackSide(s) ? 1f : 0f;
        }

        // ───────────────────────── デバッグ ─────────────────────────

        /// デバッグ表示パネルで更新を再開した直後に呼ばれる。次のフレームで最新の状態に更新する
        public void _RequestDebugRefresh()
        {
            debugTimer = 0f;
        }

        private bool _AnyDebugPanelUpdating()
        {
            for (int i = 0; i < debugPanels.Length; i++)
            {
                if (debugPanels[i] != null && Utilities.IsValid(debugPanels[i]) && debugPanels[i]._IsUpdating()) return true;
            }
            return false;
        }

        /// NadeHead から呼ばれる。true なら自分で自分の頭をなでる接触も通す
        public bool _IsSelfPatAllowed()
        {
            return selfPatEnabled;
        }

        private string _HandModeText(int mode, bool swap)
        {
            if (mode == HandPalm) return "手のひら統一";
            if (mode == HandBack) return "手の甲統一";
            return swap ? "向きで切替・入替" : "向きで切替"; // 入れ替えは向きで切替のときだけ意味がある
        }

        private void _UpdateDebugText()
        {
            int activeHeads = 0;
            int assignedHeads = 0;
            for (int i = 0; i < heads.Length; i++)
            {
                if (headOwner[i] != null) assignedHeads++;
                if (headNear[i]) activeHeads++;
            }
            int usedSlots = 0;
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (slotUsed[s]) usedSlots++;
            }

            _UpdateDebugPanels(activeHeads, assignedHeads, usedSlots);
        }

        /// デバッグ表示パネル用：上段（全体の状態）と下段（発音スロットの表）を作って配る
        private void _UpdateDebugPanels(int activeHeads, int assignedHeads, int usedSlots)
        {
            // 見出しは薄く、値は濃く（色はパネル側の文字色のまま、透明度だけ変える）
            string k = "<alpha=#99>";
            string v = "<alpha=#FF>";

            string previewText = preview != null ? preview._GetStatusText() : "なし";

            string summary = k + "なで音 " + v + (isOn ? "ON" : "OFF")
                + "　" + k + "自分なで " + v + (selfPatEnabled ? "ON" : "OFF")
                + "　" + k + "音量 " + v + Mathf.RoundToInt(masterVolume * 100f).ToString() + "%"
                + "\n" + k + "自分の音色 " + v + localPreset.ToString() + "：" + _GetPresetName(localPreset)
                + k + "（" + _GetPresetId(localPreset) + "）" + v
                + "　" + k + "手の音 " + v + _HandModeText(localHandMode, localHandSwap)
                + (localPlayerPreset == null ? k + "（同期準備中）" + v : "")
                + "\n" + k + "有効な頭 " + v + activeHeads.ToString() + " / 割当 " + assignedHeads.ToString()
                + "　" + k + "発音 " + v + usedSlots.ToString() + " / " + slotUsed.Length.ToString()
                + "　" + k + "試し聴き " + v + previewText
                + "　" + k + "言語 " + v + langCodes[langIndex] + k + "（" + clientLanguage + "）" + v;

            string table = "<line-height=2.1em>" + k
                + _DebugRow(new string[] { "#", "頭", "手", "判定", "接触", "速度", "音量", "音色", "手の音", "状態 折返" }) + v;

            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (!slotUsed[s])
                {
                    table += "\n<alpha=#55>" + _DebugRow(new string[] { s.ToString(), "—", "", "", "", "", "", "", "", "空き" }) + v;
                    continue;
                }

                VRCPlayerApi owner = headOwner[slotHead[s]];
                string ownerName = "?";
                if (owner != null && owner.IsValid()) ownerName = owner.isLocal ? "自分" : _Clip(owner.displayName, 8);

                string state = (slotReleasing[s] ? "終了中" : "発音中") + " " + slotReversals[s].ToString();

                table += "\n" + _DebugRow(new string[] {
                    s.ToString(),
                    ownerName,
                    slotSide[s] == SideLeft ? "左" : "右",
                    _IsBackSide(s) ? "手の甲" : "手のひら",
                    slotCount[s].ToString(),
                    slotSpeed[s].ToString("F2"),
                    slotVolume[s].ToString("F2"),
                    slotPreset[s].ToString() + " " + _Clip(_GetPresetName(slotPreset[s]), 6),
                    _HandModeText(slotHandMode[s], slotHandSwap[s]),
                    state
                });
            }

            for (int i = 0; i < debugPanels.Length; i++)
            {
                NadeDebugPanel p = debugPanels[i];
                if (p != null && Utilities.IsValid(p) && p._IsUpdating()) p._SetDebug(summary, table);
            }
        }

        private string _DebugRow(string[] cells)
        {
            string row = "";
            for (int i = 0; i < cells.Length && i < debugColumns.Length; i++)
            {
                row += "<pos=" + debugColumns[i] + ">" + cells[i];
            }
            return row;
        }

        /// 表の列からはみ出さないよう、長い名前を切り詰める
        private string _Clip(string s, int max)
        {
            if (s == null) return "";
            if (s.Length <= max) return s;
            return s.Substring(0, max - 1) + "…";
        }

        // ───────────────────────── Contact から呼ばれる ─────────────────────────

        public void _HandEnter(int head, VRCPlayerApi patter, int side)
        {
            if (!initialized || !isOn) return;

            int preset = _GetPresetForPlayer(patter); // なでている人が選んだ音色
            if (preset < 0) return; // 音色が1つも無い

            int s = _FindSlot(head, patter, side);
            if (s >= 0)
            {
                if (slotReleasing[s] && slotPreset[s] != preset)
                {
                    // フェードアウト中に音色が変わっていたら、古い音色で復活させず新しく鳴らし直す
                    _FreeSlot(s);
                }
                else
                {
                    slotCount[s] = Mathf.Max(slotCount[s], 0) + 1;
                    slotReleasing[s] = false;
                    return;
                }
            }

            s = _AllocateSlot();
            if (s < 0) return; // 同時発音数オーバー

            int handSide = _GetHandSideForPlayer(patter); // なでている人が選んだ手のひら・手の甲の設定

            slotUsed[s] = true;
            slotReleasing[s] = false;
            slotHead[s] = head;
            slotPatter[s] = patter;
            slotSide[s] = side;
            slotCount[s] = 1;
            slotHasPrev[s] = false;
            slotSpeed[s] = 0f;
            slotVolume[s] = 0f;
            slotSpeedPitch[s] = 1f;
            slotBackState[s] = false;
            slotPreset[s] = preset;
            slotHandMode[s] = handSide & 3;
            slotHandSwap[s] = (handSide & 4) != 0;
            _ResetReversal(s);

            // 手の甲の音が無い音色は backClips が null（同じ音を二重に再生しない）
            _StartSource(palmSources[s], palmClips[preset]);
            _StartSource(backSources[s], backClips[preset]);

            // 最初からその設定の音で鳴らす（向きで切替のときは、面の判定が出るまで手のひら側。入れ替えありなら手の甲側）
            slotBack[s] = _BackTarget(s);
        }

        public void _HandExit(int head, VRCPlayerApi patter, int side)
        {
            if (!initialized) return;
            int s = _FindSlot(head, patter, side);
            if (s < 0) return;
            slotCount[s]--;
            if (slotCount[s] <= 0)
            {
                slotCount[s] = 0;
                slotReleasing[s] = true;
            }
        }

        private int _FindSlot(int head, VRCPlayerApi patter, int side)
        {
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (slotUsed[s] && slotHead[s] == head && slotSide[s] == side && slotPatter[s] == patter) return s;
            }
            return -1;
        }

        private int _AllocateSlot()
        {
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (!slotUsed[s]) return s;
            }
            // 空きが無ければ、フェードアウト中で一番小さい音を奪う
            int best = -1;
            float bestVolume = float.MaxValue;
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (slotReleasing[s] && slotVolume[s] < bestVolume)
                {
                    best = s;
                    bestVolume = slotVolume[s];
                }
            }
            if (best >= 0) _FreeSlot(best);
            return best;
        }

        private void _StartSource(AudioSource src, AudioClip clip)
        {
            if (src == null) return;
            src.Stop();
            src.clip = clip;
            src.volume = 0f;
            src.pitch = 1f;
            if (clip != null) src.Play();
        }

        /// 折り返しの状態を初期化する（新しく鳴らし始めるとき・止めるとき）
        private void _ResetReversal(int s)
        {
            slotVel[s] = Vector3.zero;
            slotDir[s] = Vector3.zero;
            slotHasDir[s] = false;
            slotStillTime[s] = 0f;
            slotRevCooldown[s] = 0f;
            slotReversals[s] = 0;
            slotPitchOffset[s] = 0f;
            slotAttackTime[s] = -1f;
            slotAttackLevel[s] = 0f;
            slotPeak[s] = 0f;
        }

        private void _FreeSlot(int s)
        {
            slotUsed[s] = false;
            slotReleasing[s] = false;
            slotPatter[s] = null;
            slotCount[s] = 0;
            slotVolume[s] = 0f;
            slotSpeedPitch[s] = 1f;
            _ResetReversal(s);
            if (palmSources[s] != null) { palmSources[s].volume = 0f; palmSources[s].pitch = 1f; palmSources[s].Stop(); }
            if (backSources[s] != null) { backSources[s].volume = 0f; backSources[s].pitch = 1f; backSources[s].Stop(); }
        }

        private void _ReleaseSlotsForHead(int head)
        {
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (slotUsed[s] && slotHead[s] == head) { slotCount[s] = 0; slotReleasing[s] = true; }
            }
        }

        private void _ReleaseSlotsForPlayer(VRCPlayerApi player)
        {
            if (player == null) return;
            for (int s = 0; s < slotUsed.Length; s++)
            {
                if (!slotUsed[s]) continue;
                if (slotPatter[s] == player || headOwner[slotHead[s]] == player)
                {
                    slotCount[s] = 0;
                    slotReleasing[s] = true;
                }
            }
        }
    }
}
