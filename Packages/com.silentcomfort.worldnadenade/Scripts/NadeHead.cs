using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.Contact.Components;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// プレイヤー1人ぶんの頭レシーバー。NadeManager が頭の位置へ毎フレーム移動させる。
    /// 同じ GameObject に VRC Contact Receiver（Sphere）が必要。
    /// 「自分が関わる接触」（自分の頭 or 自分の手）だけをマネージャーへ通知する。
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadeHead : UdonSharpBehaviour
    {
        [System.NonSerialized] public VRCPlayerApi owner;

        private NadeManager manager;
        private int index = -1;
        private VRCContactReceiver receiver;

        public void _Init(NadeManager m, int i, string[] tags)
        {
            manager = m;
            index = i;
            receiver = GetComponent<VRCContactReceiver>();
            if (receiver == null) return;
            receiver.UpdateContentTypes(DynamicsUsageFlags.Avatar);
            receiver.UpdateCollisionTags(tags);
        }

        public void _SetRadius(float radius)
        {
            if (receiver == null) return;
            receiver.radius = radius;
            receiver.ApplyConfigurationChanges();
        }

        public override void OnContactEnter(ContactEnterInfo info)
        {
            VRCPlayerApi patter = _GetPatter(info.contactSender);
            if (patter == null) return;
            int side = _GetSide(info.matchingTags);
            if (side < 0) return;
            manager._HandEnter(index, patter, side);
        }

        public override void OnContactExit(ContactExitInfo info)
        {
            VRCPlayerApi patter = _GetPatter(info.contactSender);
            if (patter == null) return;
            int side = _GetSide(info.matchingTags);
            if (side < 0) return;
            manager._HandExit(index, patter, side);
        }

        /// 自分が関わる、他人の手による接触なら手の持ち主を返す。それ以外は null。
        private VRCPlayerApi _GetPatter(ContactSenderProxy sender)
        {
            if (manager == null || index < 0) return null;
            if (!sender.isValid || sender.usage != DynamicsUsage.Avatar) return null;

            VRCPlayerApi patter = sender.player;
            if (patter == null || !patter.IsValid()) return null;
            if (owner == null || !owner.IsValid()) return null;
            if (patter == owner && !manager._IsSelfPatAllowed()) return null; // 自分で自分をなでる接触は、パネルの「自分なで」が ON のときだけ通す
            if (!owner.isLocal && !patter.isLocal) return null;  // 自分が関わらない接触は無視
            return patter;
        }

        /// 0 = 左手、1 = 右手、-1 = 判定不能
        private int _GetSide(string[] tags)
        {
            if (tags == null) return -1;
            for (int t = 0; t < tags.Length; t++)
            {
                string tag = tags[t];
                if (tag == "HandL" || tag == "FingerL") return 0;
                if (tag == "HandR" || tag == "FingerR") return 1;
            }
            return -1;
        }
    }
}
