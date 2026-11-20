
using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// 壁スイッチ。Interact でマネージャーのイベントを呼ぶだけ（ローカル専用）。
    /// eventName を "_ToggleSwitch" にすると ON/OFF、"_NextPreset" にするとプリセット切替ボタンになる。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadeSwitch : UdonSharpBehaviour
    {
        [SerializeField] private NadeManager manager;
        [SerializeField] private string eventName = "_ToggleSwitch";

        public override void Interact()
        {
            if (manager == null) return;
            manager.SendCustomEvent(eventName);
        }
    }
}
