#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDKBase.Editor.BuildPipeline;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// ワールドのビルド（Build &amp; Test・アップロード）の前に、WorldNadenade の設置を確かめる。
    ///
    /// - 表示中の WorldNadenade が2つ以上：エラーのダイアログ。「中止する」が既定
    /// - Manager 欄が空のパネル（WorldNadenade が1つ）：「接続して続ける / 中止する / 接続せずに続ける」
    /// - パネルがあるのに WorldNadenade が無い：「中止する / このまま続ける」
    /// - WorldNadenade もパネルも無いシーンでは何もしない
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    public class NadeBuildCheck : IVRCSDKBuildRequestedCallback
    {
        public int callbackOrder { get { return 0; } }

        public bool OnBuildRequested(VRCSDKRequestedBuildType requestedBuildType)
        {
            if (requestedBuildType != VRCSDKRequestedBuildType.Scene) return true;

            Scene[] scenes = NadeEditorUtil.GetLoadedScenes();
            for (int s = 0; s < scenes.Length; s++)
            {
                if (!CheckScene(scenes[s])) return false;
            }
            return true;
        }

        /// false を返すとビルドを中止する
        private static bool CheckScene(Scene scene)
        {
            NadeManager[] active = NadeEditorUtil.GetActiveManagers(scene);

            // ── 2つ以上
            if (active.Length > 1)
            {
                bool stop = EditorUtility.DisplayDialog(NadeEditorUtil.DialogTitle,
                    NadeEditorUtil.DuplicateMessage(active) + "\n\nこのままアップロードすると、なで音が二重に鳴ります。",
                    "中止する", "このまま続ける");
                if (stop)
                {
                    NadeEditorUtil.SelectManagers(active);
                    Debug.LogError("[World Nadenade] WorldNadenade が " + active.Length.ToString() + " 個あるため、ビルドを中止しました。1つにしてください");
                    return false;
                }
                Debug.LogWarning("[World Nadenade] WorldNadenade が " + active.Length.ToString() + " 個あるまま、ビルドを続けました");
                return true;
            }

            // ── Manager 欄が空のパネル
            Component[] all = NadeEditorUtil.GetLinkables(scene);
            List<Component> empty = new List<Component>();
            for (int i = 0; i < all.Length; i++)
            {
                if (NadeEditorUtil.GetLinkedManager(all[i]) == null) empty.Add(all[i]);
            }
            if (empty.Count == 0) return true;

            StringBuilder sb = new StringBuilder();
            sb.Append("Manager が空のパネルが ").Append(empty.Count).Append(" 個あります。");
            NadeEditorUtil.AppendNames(sb, empty.ToArray());
            sb.Append("\n\n空のままだと、ワールドで操作できません。");

            if (active.Length == 0)
            {
                sb.Append("\nシーンに WorldNadenade がありません。WorldNadenade を置いてください。");
                bool stop = EditorUtility.DisplayDialog(NadeEditorUtil.DialogTitle, sb.ToString(), "中止する", "このまま続ける");
                if (stop)
                {
                    Selection.objects = ToGameObjects(empty);
                    return false;
                }
                return true;
            }

            int choice = EditorUtility.DisplayDialogComplex(NadeEditorUtil.DialogTitle, sb.ToString(), "接続して続ける", "中止する", "接続せずに続ける");
            if (choice == 1)
            {
                Selection.objects = ToGameObjects(empty);
                return false;
            }
            if (choice == 0)
            {
                int n = NadeEditorUtil.LinkAll(scene, active[0]);
                Debug.Log("[World Nadenade] ビルドの前に " + n.ToString() + " 個のパネルを「" + active[0].gameObject.name + "」に接続しました", active[0]);
            }
            return true;
        }

        private static Object[] ToGameObjects(List<Component> list)
        {
            Object[] gos = new Object[list.Count];
            for (int i = 0; i < list.Count; i++) gos[i] = list[i] != null ? list[i].gameObject : null;
            return gos;
        }
    }
}
#endif
