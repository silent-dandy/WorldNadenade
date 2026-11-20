using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// プレイヤーごとの「なでる側の設定」（音色・手のひら・手の甲の設定・手のひら / 手の甲の入れ替え）を同期する PlayerObject。
    ///
    /// - 同じ GameObject に VRC Player Object を付ける。VRChat が入室したプレイヤーごとに複製し、
    ///   その複製の所有者（オーナー）を本人に固定する
    /// - 本人だけが値を書き換え、全員に同期される（後から入室した人にも届く）
    /// - 音色は番号（preset）と保存用の名前（presetId）の両方を持つ。NadeManager は presetId を優先して音色を探すので、
    ///   VRC Enable Persistence を付けて前回の選択を復元するとき、音色の並べ替え・追加・削除をしてもずれない
    /// - VRC Enable Persistence は任意（付けると次回入室時にも前回の選択が復元される。ワールドごとの保存）
    /// - NadeManager は、なでた人の設定をここから読み取って再生する
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class NadePlayerPreset : UdonSharpBehaviour
    {
        [Tooltip("WorldNadenade（同じ Prefab 内なので Prefab 側で設定済み）")]
        [SerializeField] private NadeManager manager;

        [UdonSynced] private int preset = 0;
        [UdonSynced] private string presetId = "";   // 音色の保存用の名前（NadeSoundPreset の Preset Id）。空 = まだ選んでいない
        [UdonSynced] private int handMode = 0;      // 0 = 向きで切替、1 = 手のひらで統一、2 = 手の甲で統一
        [UdonSynced] private bool handSwap = false; // 手のひらと手の甲を入れ替える

        private bool registered;

        /// 本人のデータの読み込みが終わり、この複製が使える状態になったら登録する
        public override void OnPlayerRestored(VRCPlayerApi player)
        {
            if (registered) return;
            if (player == null || !player.IsValid()) return;
            if (!Networking.IsOwner(player, gameObject)) return; // 自分の持ち主のデータ以外は無視
            if (manager == null) return;

            registered = true;
            manager._RegisterPlayerPreset(this);
        }

        /// 他の人の設定が届いたとき。鳴っている音にも手のひら・手の甲の設定の変更をすぐ反映させる
        public override void OnDeserialization()
        {
            if (!registered || manager == null) return;
            manager._OnRemoteHandSideChanged(this);
        }

        public bool _IsReady()
        {
            return registered;
        }

        public int _GetPreset()
        {
            return preset;
        }

        public string _GetPresetId()
        {
            return presetId == null ? "" : presetId;
        }

        public int _GetHandMode()
        {
            return handMode;
        }

        public bool _GetHandSwap()
        {
            return handSwap;
        }

        public VRCPlayerApi _GetOwnerPlayer()
        {
            return Networking.GetOwner(gameObject);
        }

        public bool _IsLocal()
        {
            return Networking.IsOwner(gameObject);
        }

        /// 本人の端末からだけ呼ばれる想定。値が変わったときだけ送信する
        public void _SetPreset(int value, string id)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (id == null) id = "";
            if (preset == value && presetId == id) return;
            preset = value;
            presetId = id;
            RequestSerialization();
        }

        /// 本人の端末からだけ呼ばれる想定。値が変わったときだけ送信する
        public void _SetHandSide(int mode, bool swap)
        {
            if (!Networking.IsOwner(gameObject)) return;
            if (handMode == mode && handSwap == swap) return;
            handMode = mode;
            handSwap = swap;
            RequestSerialization();
        }
    }
}
