#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// パネル・タブレットなど、Manager 欄を持つ部品（NadeEditorUtil.LinkableTypes）のインスペクター。
    /// いつもの項目の上に、WorldNadenade との接続の状態を出す。
    ///
    /// - Manager 欄が空で、シーンに WorldNadenade が1つ：警告と「シーンの WorldNadenade に接続」ボタン
    /// - Manager 欄が空で、シーンに WorldNadenade が無い：警告（WorldNadenade を置くと自動で接続される）
    /// - シーンに WorldNadenade が2つ以上：エラー（接続のボタンは出さない）
    /// - 項目そのものは標準の表示のまま（スクリプトに項目を足しても、ここを直す必要はない）
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    public abstract class NadePanelLinkEditor : Editor
    {
        [CustomEditor(typeof(NadeControlPanel))]
        [CanEditMultipleObjects]
        public sealed class ForControlPanel : NadePanelLinkEditor { }

        [CustomEditor(typeof(NadeTabletPanel))]
        [CanEditMultipleObjects]
        public sealed class ForTabletPanel : NadePanelLinkEditor { }

        [CustomEditor(typeof(NadeDebugPanel))]
        [CanEditMultipleObjects]
        public sealed class ForDebugPanel : NadePanelLinkEditor { }

        [CustomEditor(typeof(NadeSwitch))]
        [CanEditMultipleObjects]
        public sealed class ForSwitch : NadePanelLinkEditor { }

        private const double RefreshInterval = 0.5; // シーンを探し直す間隔（秒）。毎回の再描画で探さない

        private double nextRefresh;
        private bool editable;
        private NadeManager[] activeManagers = new NadeManager[0];
        private int inactiveCount;
        private int emptyCount;

        public override void OnInspectorGUI()
        {
            // UdonSharp の標準のヘッダー（スクリプト欄など）。true が返ったら、この先は描かない
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            DrawLinkStatus();

            // 標準の項目（スクリプト欄はヘッダーに出ているので除く）
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }

        private void Refresh(bool force)
        {
            if (!force && EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;

            Component first = target as Component;
            editable = first != null && NadeEditorUtil.IsEditableSceneObject(first.gameObject);
            if (!editable) return;

            Scene scene = first.gameObject.scene;
            activeManagers = NadeEditorUtil.GetActiveManagers(scene);
            inactiveCount = NadeEditorUtil.CountInactiveManagers(scene);
            emptyCount = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                Component c = targets[i] as Component;
                if (c != null && NadeEditorUtil.IsEditableSceneObject(c.gameObject) && NadeEditorUtil.GetLinkedManager(c) == null) emptyCount++;
            }
        }

        private void DrawLinkStatus()
        {
            if (Event.current.type == EventType.Layout) Refresh(false);
            if (!editable) return; // Prefab のアセット・プレハブ編集モードでは出さない

            if (activeManagers.Length > 1)
            {
                EditorGUILayout.HelpBox("このシーンに WorldNadenade が " + activeManagers.Length.ToString() + " 個あるため、接続先を決められません。"
                    + "WorldNadenade は1つのシーンに1つだけ置きます（2つ以上あると、なで音が二重に鳴ります）。", MessageType.Error);
                if (GUILayout.Button("WorldNadenade をすべて選択")) NadeEditorUtil.SelectManagers(activeManagers);
                EditorGUILayout.Space();
                return;
            }

            if (emptyCount == 0) return; // 接続済み

            if (activeManagers.Length == 1)
            {
                EditorGUILayout.HelpBox("Manager が空です。このままでは、ワールドで操作できません。", MessageType.Warning);
                string label = targets.Length > 1 ? "選択中の " + emptyCount.ToString() + " 個をシーンの WorldNadenade に接続" : "シーンの WorldNadenade に接続";
                if (GUILayout.Button(label, GUILayout.Height(26f)))
                {
                    int n = 0;
                    for (int i = 0; i < targets.Length; i++)
                    {
                        if (NadeEditorUtil.TryLink(targets[i] as Component, activeManagers[0])) n++;
                    }
                    if (n > 0) Debug.Log("[World Nadenade] " + n.ToString() + " 個のパネルを「" + activeManagers[0].gameObject.name + "」に接続しました", activeManagers[0]);
                    Refresh(true);
                    GUIUtility.ExitGUI(); // 項目の表示を新しい値で描き直す
                }
            }
            else
            {
                EditorGUILayout.HelpBox("シーンに WorldNadenade がありません。WorldNadenade を置くと、Manager に自動で入ります。"
                    + (inactiveCount > 0 ? "（非表示の WorldNadenade には接続しません）" : ""), MessageType.Warning);
            }
            EditorGUILayout.Space();
        }
    }
}
#endif
