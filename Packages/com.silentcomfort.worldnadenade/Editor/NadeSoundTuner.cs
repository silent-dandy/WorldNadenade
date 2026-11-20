#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// World Nadenade（Silent Comfort）の音色（NadeSoundPreset）を、エディタ上で鳴らしながら調整するツール。
    ///
    /// Tools > Silent Comfort > World Nadenade > Sound Tuner（NadeSoundPreset のインスペクターのボタンからも開ける）
    ///   - 音色を選ぶと、その音源と設定値が読み込まれる（シーン上の音色でも、Prefab / Prefab Variant でもよい）
    ///   - 再生すると、手が頭の上を往復する動きを計算で作り、NadeManager と同じ式で音量・ピッチを変えて鳴らす
    ///     （再生モードに入らなくてよい。ビルドもアップロードも不要）
    ///   - 「手の速さ」「往復の間隔」と音色の設定値は、鳴らしたまま変えられる
    ///   - 設定値はこのウィンドウの中だけで変わる。「適用」を押すと音色へ書き込む（Undo 可）
    ///
    /// 音の計算は NadeManager._UpdateSlots / _DetectReversal / _OnReversal / _AttackEnvelope と同じ内容。
    /// NadeManager の計算を変えたら、このファイルの Step も合わせて直すこと。
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    public class NadeSoundTuner : EditorWindow
    {
        private const string MenuPath = "Tools/Silent Comfort/World Nadenade/Sound Tuner";

        // 計算の刻み。VR の一般的なフレームレート（90fps）に合わせる
        private const float SimStep = 1f / 90f;
        private const int MaxStepsPerUpdate = 30;

        // メーターを描き直す間隔（秒）。音量とピッチの更新はこれとは別に、エディタの更新ごとに行う
        private const double RepaintInterval = 1.0 / 30.0;

        // NadeManager の内部定数と同じ値
        private const float DirectionFollow = 8f;
        private const float StillResetTime = 0.4f;
        private const float StrokePeakHold = 0.6f;
        private const float SpeedPitchFollow = 14f;
        private const float SpeedPitchSnapVolume = 0.01f;

        /// 音色の設定値の写し（このウィンドウの中だけで変える）
        [System.Serializable]
        private class Values
        {
            public AudioClip palmClip;
            public AudioClip backClip;
            public float gain = 1f;
            public float minSpeed = 0.05f;
            public float maxSpeed = 0.8f;
            public float volumeAttack = 20f;
            public float volumeRelease = 8f;
            public bool speedPitch = false;
            public float pitchReferenceSpeed = 0.4f;
            public float pitchSpeedExponent = 0.5f;
            public float pitchMin = 0.7f;
            public float pitchMax = 1.6f;
            public float pitchWobbleDepth = 0.03f;
            public float pitchWobbleTime = 0.25f;
            public float attackAmount = 0.5f;
            public float attackRise = 0.012f;
            public float attackDecay = 0.09f;

            public void Read(NadeSoundPreset p)
            {
                palmClip = p.palmClip;
                backClip = p.backClip;
                gain = p.gain;
                minSpeed = p.minSpeed;
                maxSpeed = p.maxSpeed;
                volumeAttack = p.volumeAttack;
                volumeRelease = p.volumeRelease;
                speedPitch = p.speedPitch;
                pitchReferenceSpeed = p.pitchReferenceSpeed;
                pitchSpeedExponent = p.pitchSpeedExponent;
                pitchMin = p.pitchMin;
                pitchMax = p.pitchMax;
                pitchWobbleDepth = p.pitchWobbleDepth;
                pitchWobbleTime = p.pitchWobbleTime;
                attackAmount = p.attackAmount;
                attackRise = p.attackRise;
                attackDecay = p.attackDecay;
            }

            public bool Same(NadeSoundPreset p)
            {
                return palmClip == p.palmClip
                    && backClip == p.backClip
                    && speedPitch == p.speedPitch
                    && Eq(gain, p.gain)
                    && Eq(minSpeed, p.minSpeed)
                    && Eq(maxSpeed, p.maxSpeed)
                    && Eq(volumeAttack, p.volumeAttack)
                    && Eq(volumeRelease, p.volumeRelease)
                    && Eq(pitchReferenceSpeed, p.pitchReferenceSpeed)
                    && Eq(pitchSpeedExponent, p.pitchSpeedExponent)
                    && Eq(pitchMin, p.pitchMin)
                    && Eq(pitchMax, p.pitchMax)
                    && Eq(pitchWobbleDepth, p.pitchWobbleDepth)
                    && Eq(pitchWobbleTime, p.pitchWobbleTime)
                    && Eq(attackAmount, p.attackAmount)
                    && Eq(attackRise, p.attackRise)
                    && Eq(attackDecay, p.attackDecay);
            }

            private static bool Eq(float a, float b)
            {
                return Mathf.Approximately(a, b);
            }
        }

        // 調整する音色と、その設定値の写し（スクリプトの再コンパイルをまたいで残す）
        [SerializeField] private NadeSoundPreset target;
        [SerializeField] private Values work = new Values();

        // 手の動き（試聴用。音色には保存しない）
        [SerializeField] private float handSpeed = 0.5f;     // 往復の中央での速さ（m/s）
        [SerializeField] private float strokePeriod = 0.8f;  // 1往復の秒数
        [SerializeField] private bool reciprocate = true;    // OFF なら一定の速さで動かし続ける
        [SerializeField] private bool listenBack = false;    // 手の甲の音で聴く
        [SerializeField] private float monitorVolume = 1f;   // 試聴の音量（パネルの音量に当たる）

        // 全体の設定（NadeManager の値。試聴用で、ここで変えても NadeManager には書き込まない）
        [SerializeField] private float speedSmoothing = 12f;
        [SerializeField] private bool useReversal = true;
        [SerializeField] private float reversalDot = -0.3f;
        [SerializeField] private float reversalMinSpeed = 0.12f;
        [SerializeField] private float reversalSmoothing = 18f;
        [SerializeField] private float reversalCooldown = 0.12f;
        [SerializeField] private float sideCrossfade = 10f;
        [SerializeField] private bool showManagerSettings = false;
        [SerializeField] private Vector2 scroll;

        private const int Stopped = 0;
        private const int Playing = 1;
        private const int Paused = 2;
        private int state = Stopped;

        // 試聴用の AudioSource（保存されない隠しオブジェクト）
        private GameObject audioRoot;
        private AudioSource palmSource;
        private AudioSource backSource;

        private double lastTime;
        private double lastRepaint;
        private float accumulator;

        // 計算の状態（NadeManager の発音スロット1つぶんと同じ）
        private float phase;         // 往復の位相（0〜1 で1往復）
        private float simSpeed;      // なめらかにした手の速さ
        private float simVolume;
        private float simSpeedPitch = 1f;
        private float simBack;
        private float simVel;        // なめらかにした手の速度（向きつき）
        private float simDir;        // それまで動いていた向き
        private bool simHasDir;
        private float simStillTime;
        private float simRevCooldown;
        private int simReversals;
        private float simPitchOffset;
        private float simAttackTime = -1f;
        private float simAttackLevel;
        private float simPeak;
        private float outVolume;     // 実際に鳴らしている音量（アタック込み）
        private float outPitch = 1f;

        // ───────────────────────── 起動・終了 ─────────────────────────

        [MenuItem(MenuPath)]
        private static void Open()
        {
            // 選択中の音色を開く（選んでいなければ OpenFor が決める）
            NadeSoundPreset preset = null;
            if (Selection.activeGameObject != null) preset = Selection.activeGameObject.GetComponent<NadeSoundPreset>();
            OpenFor(preset);
        }

        /// 指定した音色でツールを開く。NadeSoundPreset のインスペクターのボタン（NadeSoundPresetEditor）からも呼ばれる。
        /// preset が null なら、調整中の音色をそのまま、それも無ければシーンの先頭の音色を開く
        public static void OpenFor(NadeSoundPreset preset)
        {
            NadeSoundTuner window = GetWindow<NadeSoundTuner>("Nade Sound Tuner");
            window.minSize = new Vector2(380f, 520f);

            if (preset == null && window.target == null)
            {
                NadeManager manager = Object.FindFirstObjectByType<NadeManager>();
                if (manager != null) preset = manager.GetComponentInChildren<NadeSoundPreset>();
            }
            if (preset != null && preset != window.target) window.TrySetTarget(preset);
            window.Show();
            window.Focus();
        }

        private void OnEnable()
        {
            EditorApplication.update += Tick;
            if (target == null) LoadManagerSettings();
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
            StopPlayback();
            if (audioRoot != null) DestroyImmediate(audioRoot);
            audioRoot = null;
            palmSource = null;
            backSource = null;
        }

        /// 調整中の値が未適用なら確認してから、音色を切り替える
        private void TrySetTarget(NadeSoundPreset preset)
        {
            if (target != null && preset != target && !work.Same(target))
            {
                bool ok = EditorUtility.DisplayDialog("Nade Sound Tuner",
                    "「" + target.name + "」に適用していない変更があります。破棄して音色を切り替えますか？", "破棄して切り替える", "キャンセル");
                if (!ok) return;
            }
            StopPlayback();
            target = preset;
            if (target != null) work.Read(target);
        }

        /// シーンの NadeManager から、全体の設定（速度のなめらかさ・折り返しの検出など）を読み込む
        private void LoadManagerSettings()
        {
            NadeManager manager = Object.FindFirstObjectByType<NadeManager>();
            if (manager == null) return;
            SerializedObject so = new SerializedObject(manager);
            speedSmoothing = GetFloat(so, "speedSmoothing", speedSmoothing);
            reversalDot = GetFloat(so, "reversalDot", reversalDot);
            reversalMinSpeed = GetFloat(so, "reversalMinSpeed", reversalMinSpeed);
            reversalSmoothing = GetFloat(so, "reversalSmoothing", reversalSmoothing);
            reversalCooldown = GetFloat(so, "reversalCooldown", reversalCooldown);
            sideCrossfade = GetFloat(so, "sideCrossfade", sideCrossfade);
            SerializedProperty rev = so.FindProperty("useReversal");
            if (rev != null) useReversal = rev.boolValue;
        }

        private static float GetFloat(SerializedObject so, string field, float fallback)
        {
            SerializedProperty prop = so.FindProperty(field);
            return prop != null ? prop.floatValue : fallback;
        }

        // ───────────────────────── 画面 ─────────────────────────

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.Space();
            EditorGUI.BeginChangeCheck();
            NadeSoundPreset picked = (NadeSoundPreset)EditorGUILayout.ObjectField(
                new GUIContent("音色（Preset）", "調整する NadeSoundPreset。シーン上の音色でも、Prefab / Prefab Variant でもよい"),
                target, typeof(NadeSoundPreset), true);
            if (EditorGUI.EndChangeCheck() && picked != target) TrySetTarget(picked);

            if (target == null)
            {
                EditorGUILayout.HelpBox("調整する音色（NadeSoundPreset）を選んでください。", MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            DrawTransport();
            DrawHandMotion();
            DrawMeters();
            DrawValues();
            DrawApply();
            DrawManagerSettings();

            EditorGUILayout.EndScrollView();
        }

        private void DrawTransport()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(state == Playing || work.palmClip == null))
                {
                    if (GUILayout.Button(state == Paused ? "▶ 再開" : "▶ 再生", GUILayout.Height(28f))) StartPlayback();
                }
                using (new EditorGUI.DisabledScope(state != Playing))
                {
                    if (GUILayout.Button("Ⅱ 一時停止", GUILayout.Height(28f))) PausePlayback();
                }
                using (new EditorGUI.DisabledScope(state == Stopped))
                {
                    if (GUILayout.Button("■ 停止", GUILayout.Height(28f))) StopPlayback();
                }
            }
            if (work.palmClip == null)
            {
                EditorGUILayout.HelpBox("手のひらの音（Palm Clip）がありません。", MessageType.Warning);
            }
        }

        private void DrawHandMotion()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("手の動き（試聴用・音色には保存されません）", EditorStyles.boldLabel);

            reciprocate = EditorGUILayout.Toggle(
                new GUIContent("往復する", "OFF にすると、折り返さずに一定の速さで動かし続ける（速さと音量・ピッチの対応だけを確かめたいとき）"), reciprocate);
            handSpeed = EditorGUILayout.Slider(
                new GUIContent("手の速さ (m/s)", "往復の中央（いちばん速いところ）での速さ。目の高さ 1.6m 換算"), handSpeed, 0.02f, 2f);
            using (new EditorGUI.DisabledScope(!reciprocate))
            {
                strokePeriod = EditorGUILayout.Slider(
                    new GUIContent("往復の間隔 (秒)", "行って戻る1往復にかかる秒数"), strokePeriod, 0.15f, 4f);
            }
            if (reciprocate)
            {
                // 速さが正弦波で変わる往復：片道の距離 = 速さ × 間隔 ÷ π
                float distanceCm = handSpeed * strokePeriod / Mathf.PI * 100f;
                EditorGUILayout.LabelField(" ", "片道の距離 約 " + distanceCm.ToString("F0") + " cm（頭の幅は 15〜20 cm ほど）", EditorStyles.miniLabel);
            }

            bool hasBack = work.backClip != null && work.backClip != work.palmClip;
            using (new EditorGUI.DisabledScope(!hasBack))
            {
                listenBack = EditorGUILayout.Toggle(
                    new GUIContent("手の甲の音で聴く", "手の甲の音（Back Clip）があるときだけ選べる"), listenBack && hasBack);
            }
            monitorVolume = EditorGUILayout.Slider(
                new GUIContent("試聴の音量", "パネルの音量に当たる値。音色の Gain はこれとは別に掛かる"), monitorVolume, 0f, 1f);
        }

        private void DrawMeters()
        {
            EditorGUILayout.Space();
            float speedMax = Mathf.Max(work.maxSpeed, 0.01f) * 1.5f;
            Meter(simSpeed / speedMax, "速さ " + simSpeed.ToString("F2") + " m/s");
            Meter(outVolume, "音量 " + outVolume.ToString("F2"));
            Meter(outPitch / 2f, "ピッチ ×" + outPitch.ToString("F2") + "　折り返し " + simReversals.ToString() + " 回");
        }

        private static void Meter(float value01, string label)
        {
            Rect r = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.ProgressBar(r, Mathf.Clamp01(value01), label);
        }

        private void DrawValues()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
            work.palmClip = (AudioClip)EditorGUILayout.ObjectField(
                new GUIContent("Palm Clip", "手のひらの音（必須）"), work.palmClip, typeof(AudioClip), false);
            work.backClip = (AudioClip)EditorGUILayout.ObjectField(
                new GUIContent("Back Clip", "手の甲の音（任意）"), work.backClip, typeof(AudioClip), false);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Level", EditorStyles.boldLabel);
            work.gain = EditorGUILayout.Slider(new GUIContent("Gain", "この音色の音量の補正"), work.gain, 0f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Volume by hand speed", EditorStyles.boldLabel);
            // インスペクターでは数値欄だが、ここではマウスだけでも動かせるようスライダーにしている（右の欄に数値も打てる）。
            // スライダーの範囲より外の値を使いたいときは、インスペクターで直接入れる
            work.minSpeed = EditorGUILayout.Slider(new GUIContent("Min Speed", "これ以下の速度は無音"), work.minSpeed, 0f, 1f);
            work.maxSpeed = EditorGUILayout.Slider(new GUIContent("Max Speed", "この速度で最大音量"), work.maxSpeed, 0.05f, 3f);
            work.volumeAttack = EditorGUILayout.Slider(new GUIContent("Volume Attack", "音量が上がる速さ"), work.volumeAttack, 1f, 60f);
            work.volumeRelease = EditorGUILayout.Slider(new GUIContent("Volume Release", "音量が下がる速さ（小さいほど余韻が長い）"), work.volumeRelease, 1f, 60f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pitch by hand speed", EditorStyles.boldLabel);
            work.speedPitch = EditorGUILayout.Toggle(new GUIContent("Speed Pitch", "ON にすると、手が速いほどピッチが上がる"), work.speedPitch);
            using (new EditorGUI.DisabledScope(!work.speedPitch))
            {
                work.pitchReferenceSpeed = EditorGUILayout.Slider(
                    new GUIContent("Pitch Reference Speed", "ピッチが元のまま（1.0）になる速度"), work.pitchReferenceSpeed, 0.05f, 2f);
                work.pitchSpeedExponent = EditorGUILayout.Slider(
                    new GUIContent("Pitch Speed Exponent", "1 = 速度に正比例、0.5 = 現実寄り"), work.pitchSpeedExponent, 0.1f, 1f);
                work.pitchMin = EditorGUILayout.Slider(new GUIContent("Pitch Min", "ピッチの下限"), work.pitchMin, 0.25f, 1f);
                work.pitchMax = EditorGUILayout.Slider(new GUIContent("Pitch Max", "ピッチの上限"), work.pitchMax, 1f, 3f);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Reversal pitch wobble", EditorStyles.boldLabel);
            work.pitchWobbleDepth = EditorGUILayout.Slider(
                new GUIContent("Pitch Wobble Depth", "折り返しでのピッチのゆらぎの最大幅（0.03 = ±3%）。0 で無効"), work.pitchWobbleDepth, 0f, 0.15f);
            work.pitchWobbleTime = EditorGUILayout.Slider(
                new GUIContent("Pitch Wobble Time", "ずれたピッチが元へ戻るまでの時間の目安（秒）"), work.pitchWobbleTime, 0.05f, 1f);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Reversal attack", EditorStyles.boldLabel);
            work.attackAmount = EditorGUILayout.Slider(
                new GUIContent("Attack Amount", "折り返しで上乗せする音量（直前のストロークの音量に対する割合）。0 で無効"), work.attackAmount, 0f, 1f);
            work.attackRise = EditorGUILayout.Slider(new GUIContent("Attack Rise", "立ち上がり（秒）"), work.attackRise, 0.002f, 0.05f);
            work.attackDecay = EditorGUILayout.Slider(new GUIContent("Attack Decay", "消えるまでの時間の目安（秒）"), work.attackDecay, 0.02f, 0.4f);
        }

        private void DrawApply()
        {
            EditorGUILayout.Space();
            bool dirty = !work.Same(target);
            if (dirty)
            {
                EditorGUILayout.HelpBox("「" + target.name + "」に適用していない変更があります。", MessageType.Info);
            }
            if (Application.isPlaying && !EditorUtility.IsPersistent(target))
            {
                EditorGUILayout.HelpBox("再生モード中にシーン上の音色へ適用した値は、再生モードを抜けると元に戻ります。", MessageType.Warning);
            }

            using (new EditorGUI.DisabledScope(!dirty))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("適用", "今の値を音色（Preset）に書き込む。Undo で戻せる"), GUILayout.Height(28f))) Apply();
                if (GUILayout.Button(new GUIContent("Preset の値に戻す", "このウィンドウでの変更を捨て、音色の今の値を読み込み直す"), GUILayout.Height(28f)))
                {
                    work.Read(target);
                    GUI.FocusControl(null);
                }
            }
        }

        private void DrawManagerSettings()
        {
            EditorGUILayout.Space();
            showManagerSettings = EditorGUILayout.Foldout(showManagerSettings, "全体の設定（NadeManager の値・試聴用）", true);
            if (!showManagerSettings) return;

            EditorGUILayout.HelpBox("手の速度のなめらかさと折り返しの検出は、音色ではなく NadeManager の設定です。"
                + "ここで変えても NadeManager には書き込まれません（試聴にだけ使います）。", MessageType.None);
            speedSmoothing = Mathf.Max(0.1f, EditorGUILayout.FloatField("Speed Smoothing", speedSmoothing));
            useReversal = EditorGUILayout.Toggle("Use Reversal", useReversal);
            using (new EditorGUI.DisabledScope(!useReversal))
            {
                reversalDot = EditorGUILayout.Slider("Reversal Dot", reversalDot, -1f, 0f);
                reversalMinSpeed = Mathf.Max(0f, EditorGUILayout.FloatField("Reversal Min Speed", reversalMinSpeed));
                reversalSmoothing = Mathf.Max(0.1f, EditorGUILayout.FloatField("Reversal Smoothing", reversalSmoothing));
                reversalCooldown = EditorGUILayout.Slider("Reversal Cooldown", reversalCooldown, 0f, 0.5f);
            }
            sideCrossfade = Mathf.Max(0.1f, EditorGUILayout.FloatField("Side Crossfade", sideCrossfade));
            if (GUILayout.Button("シーンの NadeManager から読み込み直す")) LoadManagerSettings();
        }

        // ───────────────────────── 適用 ─────────────────────────

        /// 今の値を音色に書き込む。SerializedObject 経由なので Undo でき、Prefab Variant では変えた値だけが上書きになる
        private void Apply()
        {
            if (target == null) return;

            SerializedObject so = new SerializedObject(target);
            so.Update();
            SetObj(so, "palmClip", work.palmClip);
            SetObj(so, "backClip", work.backClip);
            SetFloat(so, "gain", work.gain);
            SetFloat(so, "minSpeed", work.minSpeed);
            SetFloat(so, "maxSpeed", work.maxSpeed);
            SetFloat(so, "volumeAttack", work.volumeAttack);
            SetFloat(so, "volumeRelease", work.volumeRelease);
            SerializedProperty sp = so.FindProperty("speedPitch");
            if (sp != null) sp.boolValue = work.speedPitch;
            else Missing("speedPitch");
            SetFloat(so, "pitchReferenceSpeed", work.pitchReferenceSpeed);
            SetFloat(so, "pitchSpeedExponent", work.pitchSpeedExponent);
            SetFloat(so, "pitchMin", work.pitchMin);
            SetFloat(so, "pitchMax", work.pitchMax);
            SetFloat(so, "pitchWobbleDepth", work.pitchWobbleDepth);
            SetFloat(so, "pitchWobbleTime", work.pitchWobbleTime);
            SetFloat(so, "attackAmount", work.attackAmount);
            SetFloat(so, "attackRise", work.attackRise);
            SetFloat(so, "attackDecay", work.attackDecay);
            so.ApplyModifiedProperties();

            UdonSharpEditorUtility.CopyProxyToUdon(target);

            // Project ウィンドウの Prefab を直接調整したときは、ファイルへ保存する
            if (PrefabUtility.IsPartOfPrefabAsset(target))
            {
                PrefabUtility.SavePrefabAsset(target.transform.root.gameObject);
            }

            work.Read(target); // 書き込めなかった欄があれば、表示を実際の値に合わせる
            Debug.Log("[World Nadenade] 音色に設定値を適用しました: " + target.name);
        }

        private static void SetFloat(SerializedObject so, string field, float value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null) { Missing(field); return; }
            prop.floatValue = value;
        }

        private static void SetObj(SerializedObject so, string field, Object value)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null) { Missing(field); return; }
            prop.objectReferenceValue = value;
        }

        private static void Missing(string field)
        {
            Debug.LogWarning("[World Nadenade] 欄が見つかりません（NadeSoundPreset.cs を最新に差し替えてください）: " + field);
        }

        // ───────────────────────── 再生 ─────────────────────────

        private void EnsureAudio()
        {
            if (audioRoot != null && palmSource != null && backSource != null) return;
            if (audioRoot != null) DestroyImmediate(audioRoot);
            audioRoot = EditorUtility.CreateGameObjectWithHideFlags("NadeSoundTunerAudio", HideFlags.HideAndDontSave);
            palmSource = AddSource();
            backSource = AddSource();
        }

        private AudioSource AddSource()
        {
            AudioSource src = audioRoot.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            src.spatialBlend = 0f; // 2D（Scene ビューのカメラの位置に関係なく聞こえる）
            src.dopplerLevel = 0f;
            src.volume = 0f;
            src.pitch = 1f;
            return src;
        }

        private void StartPlayback()
        {
            if (work.palmClip == null) return;
            EnsureAudio();

            if (state == Paused)
            {
                palmSource.UnPause();
                backSource.UnPause();
            }
            else
            {
                ResetSim();
                AssignClips(true);
            }
            state = Playing;
            lastTime = EditorApplication.timeSinceStartup;
            accumulator = 0f;
        }

        private void PausePlayback()
        {
            if (state != Playing) return;
            if (palmSource != null) palmSource.Pause();
            if (backSource != null) backSource.Pause();
            state = Paused;
        }

        private void StopPlayback()
        {
            if (palmSource != null) { palmSource.Stop(); palmSource.volume = 0f; }
            if (backSource != null) { backSource.Stop(); backSource.volume = 0f; }
            state = Stopped;
            ResetSim();
            Repaint();
        }

        /// 音源を AudioSource へセットして鳴らす。再生中に音源を差し替えたときも呼ぶ。
        /// 手の甲の音が無い（空欄・手のひらと同じ）ときは、NadeManager と同じく手のひらの音だけ鳴らす
        private void AssignClips(bool force)
        {
            AudioClip palm = work.palmClip;
            AudioClip back = work.backClip != work.palmClip ? work.backClip : null;

            if (force || palmSource.clip != palm)
            {
                palmSource.Stop();
                palmSource.clip = palm;
                if (palm != null) palmSource.Play();
            }
            if (force || backSource.clip != back)
            {
                backSource.Stop();
                backSource.clip = back;
                if (back != null) backSource.Play();
            }
        }

        private void ResetSim()
        {
            phase = 0f;
            simSpeed = 0f;
            simVolume = 0f;
            simSpeedPitch = 1f;
            simBack = listenBack ? 1f : 0f;
            simVel = 0f;
            simDir = 0f;
            simHasDir = false;
            simStillTime = 0f;
            simRevCooldown = 0f;
            simReversals = 0;
            simPitchOffset = 0f;
            simAttackTime = -1f;
            simAttackLevel = 0f;
            simPeak = 0f;
            outVolume = 0f;
            outPitch = 1f;
        }

        /// エディタの更新ごとに呼ばれる。実時間ぶんだけ計算を進め、AudioSource の音量とピッチへ反映する
        private void Tick()
        {
            if (state != Playing) return;
            if (palmSource == null || backSource == null || target == null) { StopPlayback(); return; }

            double now = EditorApplication.timeSinceStartup;
            float dt = (float)(now - lastTime);
            lastTime = now;
            if (dt <= 0f) return;

            AssignClips(false);

            accumulator += Mathf.Min(dt, 0.25f);
            int steps = 0;
            while (accumulator >= SimStep && steps < MaxStepsPerUpdate)
            {
                Step(SimStep);
                accumulator -= SimStep;
                steps++;
            }
            if (steps >= MaxStepsPerUpdate) accumulator = 0f;
            if (steps == 0) return;

            palmSource.volume = outVolume * (1f - simBack);
            palmSource.pitch = outPitch;
            backSource.volume = outVolume * simBack;
            backSource.pitch = outPitch;

            // メーターの描き直し。このウィンドウで数値を入力している間は描き直さない
            // （入力中に描き直しを続けると、キー入力が数値欄に届かず、エディタのショートカットとして処理されることがあるため）。
            // 入力中もメーターが止まるだけで、音は変わり続ける
            bool typing = EditorGUIUtility.editingTextField && focusedWindow == this;
            if (!typing && now - lastRepaint >= RepaintInterval)
            {
                lastRepaint = now;
                Repaint();
            }
        }

        // ───────────────────────── 音の計算（NadeManager と同じ） ─────────────────────────

        private void Step(float dt)
        {
            // NadeManager._CollectPresets と同じ範囲に収めた値
            float gain = Mathf.Clamp01(work.gain);
            float minSpeed = Mathf.Max(0f, work.minSpeed);
            float maxSpeed = Mathf.Max(work.maxSpeed, minSpeed + 0.01f);
            float volAttack = Mathf.Max(0.1f, work.volumeAttack);
            float volRelease = Mathf.Max(0.1f, work.volumeRelease);
            float wobbleTime = Mathf.Max(0.01f, work.pitchWobbleTime);

            // 手の動き：位置が正弦波で往復する（速さは中央で最大、両端で 0）。往復しないときは一定の速さ
            float vel = handSpeed;
            if (reciprocate)
            {
                phase += dt / Mathf.Max(strokePeriod, 0.05f);
                if (phase >= 1f) phase -= 1f;
                vel = handSpeed * Mathf.Cos(phase * 2f * Mathf.PI);
            }

            float raw = Mathf.Abs(vel);
            float rawLimit = maxSpeed * 3f;
            if (raw > rawLimit)
            {
                vel = vel * (rawLimit / raw);
                raw = rawLimit;
            }
            simSpeed = Mathf.Lerp(simSpeed, raw, 1f - Mathf.Exp(-speedSmoothing * dt));
            float targetVolume = monitorVolume * gain * Mathf.InverseLerp(minSpeed, maxSpeed, simSpeed);

            if (work.speedPitch && simSpeed > minSpeed)
            {
                float targetPitch = Mathf.Clamp(
                    Mathf.Pow(simSpeed / Mathf.Max(0.01f, work.pitchReferenceSpeed), Mathf.Clamp(work.pitchSpeedExponent, 0f, 1f)),
                    Mathf.Clamp(work.pitchMin, 0.1f, 1f), Mathf.Clamp(work.pitchMax, 1f, 3f));
                simSpeedPitch = simVolume < SpeedPitchSnapVolume
                    ? targetPitch
                    : Mathf.Lerp(simSpeedPitch, targetPitch, 1f - Mathf.Exp(-SpeedPitchFollow * dt));
            }
            else if (!work.speedPitch)
            {
                simSpeedPitch = 1f; // 鳴らしたまま OFF にしたら、元のピッチへ戻す
            }

            if (useReversal) DetectReversal(vel, dt);

            float rate = targetVolume > simVolume ? volAttack : volRelease;
            simVolume = Mathf.Lerp(simVolume, targetVolume, 1f - Mathf.Exp(-rate * dt));
            simPeak = Mathf.Max(simVolume, simPeak * Mathf.Exp(-dt / StrokePeakHold));

            bool hasBack = work.backClip != null && work.backClip != work.palmClip;
            simBack = Mathf.Lerp(simBack, (listenBack && hasBack) ? 1f : 0f, 1f - Mathf.Exp(-sideCrossfade * dt));

            float attack = AttackEnvelope(dt);
            simPitchOffset *= Mathf.Exp(-dt * 3f / wobbleTime);
            outPitch = simSpeedPitch * (1f + simPitchOffset);
            outVolume = Mathf.Min(1f, simVolume + simAttackLevel * attack);
        }

        /// NadeManager._DetectReversal の1次元版（向きは +1 / -1）
        private void DetectReversal(float vel, float dt)
        {
            simVel = Mathf.Lerp(simVel, vel, 1f - Mathf.Exp(-reversalSmoothing * dt));
            if (simRevCooldown > 0f) simRevCooldown -= dt;

            float speed = Mathf.Abs(simVel);
            if (speed < reversalMinSpeed || speed < 0.0001f)
            {
                simStillTime += dt;
                if (simStillTime > StillResetTime) simHasDir = false;
                return;
            }
            simStillTime = 0f;

            float dir = simVel > 0f ? 1f : -1f;
            if (!simHasDir)
            {
                simDir = dir;
                simHasDir = true;
                return;
            }

            if (simRevCooldown <= 0f && dir * simDir < reversalDot)
            {
                OnReversal();
                simDir = dir;
                return;
            }

            float follow = Mathf.Lerp(simDir, dir, 1f - Mathf.Exp(-DirectionFollow * dt));
            float len = Mathf.Abs(follow);
            simDir = len > 0.0001f ? follow / len : dir;
        }

        private void OnReversal()
        {
            simReversals++;
            simRevCooldown = reversalCooldown;

            float depth = Mathf.Max(0f, work.pitchWobbleDepth);
            if (depth > 0f)
            {
                float sign = Random.value < 0.5f ? -1f : 1f;
                simPitchOffset = sign * depth * Random.Range(0.5f, 1f);
            }

            float amount = Mathf.Max(0f, work.attackAmount);
            if (amount > 0f)
            {
                simAttackTime = 0f;
                simAttackLevel = amount * simPeak;
            }
        }

        private float AttackEnvelope(float dt)
        {
            float t = simAttackTime;
            if (t < 0f) return 0f;

            t += dt;
            float rise = Mathf.Max(0.001f, work.attackRise);
            float decay = Mathf.Max(0.01f, work.attackDecay);
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
                    t = -1f;
                    simAttackLevel = 0f;
                }
            }
            simAttackTime = t;
            return env;
        }
    }
}
#endif
