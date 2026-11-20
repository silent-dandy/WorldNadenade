using UdonSharp;
using UnityEngine;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// 音色の試し聴き（自分にだけ 2D で約2秒鳴らす）。WorldNadenade の子 'Preview' に付ける。
    ///
    /// - 手のひらと手の甲で別の音なら、左から手のひら・右から手の甲を同時に鳴らす。同じ音なら中央から1つだけ
    /// - 音量は「聞こえる音」の音量 × Preview Volume × 音色の Gain。なで音が OFF でも鳴る
    /// - 自分では Update を持たない。NadeManager が PostLateUpdate から _Tick を呼ぶ（毎フレーム処理を Manager に集約）
    /// - 子の 'Palm'（左）/ 'Back'（右）の AudioSource を使う。生成ツール（Add Preview Sources）で作れる。
    ///   立体音響を切るのは、生成ツールが付ける VRC Spatial Audio Source（Enable Spatialization オフ）の役目
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadePreview : UdonSharpBehaviour
    {
        [Tooltip("OFF にすると、音色ボタンを押しても試し聴きを鳴らさない")]
        [SerializeField] private bool previewOnSelect = true;
        [Tooltip("手のひらの音（左）の AudioSource。空欄なら子の 'Palm' を探す")]
        [SerializeField] private AudioSource palmSource;
        [Tooltip("手の甲の音（右）の AudioSource。空欄なら子の 'Back' を探す")]
        [SerializeField] private AudioSource backSource;
        [Tooltip("試し聴きの長さ（秒）。フェードアウトを含む")]
        [SerializeField, Range(0.5f, 5f)] private float duration = 2f;
        [Tooltip("試し聴きの音量（「聞こえる音」の音量に掛ける）")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;
        [Tooltip("手のひらと手の甲で別の音があるときの左右の振り分け（0 = 中央、1 = 完全に左右）")]
        [SerializeField, Range(0f, 1f)] private float pan = 0.8f;
        [SerializeField, Range(0f, 0.5f)] private float fadeIn = 0.05f;
        [SerializeField, Range(0.05f, 1.5f)] private float fadeOut = 0.4f;

        private bool initialized;
        private bool playing;
        private float time;        // 鳴らし始めてからの秒数
        private int playingIndex = -1;
        private float playingGain = 1f;

        /// NadeManager が起動時に呼ぶ
        public void _Init()
        {
            if (initialized) return;
            initialized = true;

            if (palmSource == null)
            {
                Transform t = transform.Find("Palm");
                if (t != null) palmSource = t.GetComponent<AudioSource>();
            }
            if (backSource == null)
            {
                Transform t = transform.Find("Back");
                if (t != null) backSource = t.GetComponent<AudioSource>();
            }
            if (palmSource != null && palmSource == backSource) backSource = null; // 同じ AudioSource を二重に使わない

            if (previewOnSelect && palmSource == null)
            {
                Debug.LogWarning("[World Nadenade] 試し聴き用の AudioSource（Preview/Palm）が無いので、試し聴きは鳴りません");
            }

            _PrepareSource(palmSource);
            _PrepareSource(backSource);
        }

        private void _PrepareSource(AudioSource src)
        {
            if (src == null) return;
            src.playOnAwake = false;
            src.loop = true;          // 試し聴きより短いループ素材でも途切れないように
            src.spatialBlend = 0f;    // 2D（頭の向きや位置に関係なく左右に聞こえる）
            src.panStereo = 0f;
            src.volume = 0f;
            src.Stop();
        }

        /// 鳴らせる状態か（デバッグ表示・判定用）
        public bool _IsAvailable()
        {
            return previewOnSelect && palmSource != null;
        }

        /// 試し聴きを始める。鳴っている試し聴きは止めて鳴らし直す。
        /// back が null なら中央から palm だけ、あれば左右に分けて同時に鳴らす
        public void _Play(int index, AudioClip palm, AudioClip back, float gain)
        {
            _Init();
            if (!_IsAvailable()) return;
            _Stop();

            bool dual = back != null && back != palm && backSource != null;
            if (palm == null && !dual) return;
            float p = dual ? pan : 0f;

            palmSource.clip = palm;
            palmSource.panStereo = -p;
            palmSource.volume = 0f;
            if (palm != null) palmSource.Play();

            if (dual)
            {
                backSource.clip = back;
                backSource.panStereo = p;
                backSource.volume = 0f;
                backSource.Play();
            }

            playing = true;
            time = 0f;
            playingIndex = index;
            playingGain = Mathf.Clamp01(gain);
        }

        public void _Stop()
        {
            playing = false;
            playingIndex = -1;
            if (palmSource != null) { palmSource.volume = 0f; palmSource.Stop(); }
            if (backSource != null) { backSource.volume = 0f; backSource.Stop(); }
        }

        /// NadeManager の PostLateUpdate から毎フレーム呼ばれる。
        /// 音量（フェードイン → 一定 → フェードアウト）を進め、時間が来たら止める
        public void _Tick(float dt, float masterVolume)
        {
            if (!playing) return;
            time += dt;

            float d = Mathf.Max(duration, 0.1f);
            if (time >= d)
            {
                _Stop();
                return;
            }

            float fIn = fadeIn > 0f ? Mathf.Clamp01(time / fadeIn) : 1f;
            float fOut = Mathf.Clamp01((d - time) / Mathf.Max(fadeOut, 0.01f));
            float env = Mathf.Min(fIn, fOut);
            env = env * env * (3f - 2f * env); // なめらかに
            float v = masterVolume * volume * playingGain * env;

            if (palmSource != null) palmSource.volume = v;
            if (backSource != null && backSource.isPlaying) backSource.volume = v;
        }

        /// デバッグ表示用の状態（なし / OFF / 待機 / 再生中）
        public string _GetStatusText()
        {
            if (palmSource == null) return "なし";
            if (!previewOnSelect) return "OFF";
            if (playing) return playingIndex.ToString() + " 再生中 " + time.ToString("F1") + "s";
            return "待機";
        }
    }
}
