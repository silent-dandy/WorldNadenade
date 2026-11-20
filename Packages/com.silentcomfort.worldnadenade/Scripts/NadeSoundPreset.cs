using UdonSharp;
using UnityEngine;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// 音色（プリセット）1つぶん。名前・音源・音の出方をひとまとめにする。
    ///
    /// - WorldNadenade の Presets の直下に置くと、上から順に音色ボタンへ並ぶ（無効にした子は使わない）
    /// - 値を持つだけのコンポーネント（Start / Update なし）。NadeManager が起動時に一度だけ読み取り、
    ///   以降は Manager 側の配列で処理する（再生中にここを変えても反映されない）
    /// - Preset Id は保存用の名前。一度公開したら変えない（表示名は自由に変えてよい）。
    ///   PlayerObject に Persistence を付けたとき、並び順が変わっても前回の音色を正しく復元するために使う
    /// - Name Languages / Localized Names で、VRChat の表示言語ごとに音色名を変えられる（無ければ Display Name）
    /// - 推奨：NadeSoundPreset_Base.prefab を1つ作り、各音色をその Prefab Variant にする
    ///   （上書きしていない値は Base を直すだけで全音色に反映される）
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadeSoundPreset : UdonSharpBehaviour
    {
        [Header("Identity")]
        [Tooltip("保存用の名前（英数字推奨・ワールド内で重複不可）。一度公開したら変えない。空欄ならオブジェクト名を使う")]
        public string presetId = "";
        [Tooltip("音色ボタンに表示する名前。空欄ならオブジェクト名を使う")]
        public string displayName = "";
        [Tooltip("ほかの言語の表示名の言語コード（en / ko / zh-CN など。VRChat の表示言語と同じ形）。Localized Names と同じ並び（任意）")]
        public string[] nameLanguages = new string[0];
        [Tooltip("ほかの言語の表示名（Name Languages と同じ並び）。表示言語に合うものが無ければ Display Name を使う（任意）")]
        public string[] localizedNames = new string[0];

        [Header("Clips")]
        [Tooltip("手のひらの音（必須）。0.5〜2秒程度の継ぎ目のないループ")]
        public AudioClip palmClip;
        [Tooltip("手の甲の音（任意）。空欄、または手のひらと同じクリップなら、手の甲でも手のひらの音")]
        public AudioClip backClip;

        [Header("Level")]
        [Tooltip("この音色の音量の補正。音源ごとの大きさの差をそろえる（試し聴きにも掛かる）")]
        [Range(0f, 1f)] public float gain = 1f;

        [Header("Volume by hand speed（目の高さ 1.6m 換算の m/s）")]
        [Tooltip("これ以下の速度は無音")]
        public float minSpeed = 0.05f;
        [Tooltip("この速度で最大音量")]
        public float maxSpeed = 0.8f;
        [Tooltip("音量が上がる速さ")]
        public float volumeAttack = 20f;
        [Tooltip("音量が下がる速さ（小さいほど余韻が長い）")]
        public float volumeRelease = 8f;

        [Header("Pitch by hand speed（手の速さでピッチを変える）")]
        [Tooltip("ON にすると、手が速いほどピッチが上がる。OFF はピッチ一定（これまでと同じ）")]
        public bool speedPitch = false;
        [Tooltip("ピッチが元のまま（1.0）になる速度（m/s）。音源を録ったときの手の速さに合わせる")]
        public float pitchReferenceSpeed = 0.4f;
        [Tooltip("速度に対するピッチの上がり方（ピッチ = (速度 ÷ 基準速度) の Exponent 乗）。"
            + "1 = 速度に正比例（表面の凹凸をこする成分だけを見た理論値）、"
            + "0.5 = 現実寄り（髪や手の響きは速度で変わらないので、聞こえる高さは正比例より緩やかに上がる）")]
        [Range(0.1f, 1f)] public float pitchSpeedExponent = 0.5f;
        [Tooltip("ピッチの下限。ゆっくりなでたときに、音が不自然に低くならないようにする")]
        [Range(0.25f, 1f)] public float pitchMin = 0.7f;
        [Tooltip("ピッチの上限。速くなでたときに、音が不自然に高くならないようにする")]
        [Range(1f, 3f)] public float pitchMax = 1.6f;

        [Header("Reversal pitch wobble（折り返しのピッチのゆらぎ）")]
        [Tooltip("ゆらぎの最大幅（0.03 = ±3%）。毎回ランダムな向きと大きさでずれる。0 で無効")]
        [Range(0f, 0.15f)] public float pitchWobbleDepth = 0.03f;
        [Tooltip("ずれたピッチが元へ戻るまでの時間の目安（秒）")]
        [Range(0.05f, 1f)] public float pitchWobbleTime = 0.25f;

        [Header("Reversal attack（折り返しで音量を短く持ち上げる）")]
        [Tooltip("上乗せする音量。直前のストロークの音量に対する割合。0 で無効")]
        [Range(0f, 1f)] public float attackAmount = 0.5f;
        [Tooltip("立ち上がり（秒）。0 に近いほど鋭いが、プチッと鳴りやすい")]
        [Range(0.002f, 0.05f)] public float attackRise = 0.012f;
        [Tooltip("消えるまでの時間の目安（秒）")]
        [Range(0.02f, 0.4f)] public float attackDecay = 0.09f;
    }
}
