#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// WorldNadenade（NadeManager）のインスペクター。いつもの項目の上に、シーンの設置状況を出す。
    ///
    /// - シーンに WorldNadenade が2つ以上：エラーと、それぞれを選ぶボタン
    /// - この WorldNadenade が非表示：お知らせ（非表示のものには自動で接続しない）
    /// - パネル・タブレットの接続状況（接続済み / 未接続 / 別の WorldNadenade に接続）と「未接続のパネルを接続」ボタン
    /// - 項目そのものは標準の表示のまま（NadeManager.cs に項目を足しても、ここを直す必要はない）
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    [CustomEditor(typeof(NadeManager))]
    public class NadeManagerEditor : Editor
    {
        private const double RefreshInterval = 0.5; // シーンを探し直す間隔（秒）

        private double nextRefresh;
        private bool editable;
        private NadeManager[] activeManagers = new NadeManager[0];
        private int linkedHere;
        private int linkedOther;
        private int empty;

        public override void OnInspectorGUI()
        {
            // UdonSharp の標準のヘッダー（スクリプト欄など）。true が返ったら、この先は描かない
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            DrawSceneStatus();

            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }

        private void Refresh(bool force)
        {
            if (!force && EditorApplication.timeSinceStartup < nextRefresh) return;
            nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;

            NadeManager self = target as NadeManager;
            editable = self != null && NadeEditorUtil.IsEditableSceneObject(self.gameObject);
            if (!editable) return;

            Scene scene = self.gameObject.scene;
            activeManagers = NadeEditorUtil.GetActiveManagers(scene);
            linkedHere = 0;
            linkedOther = 0;
            empty = 0;
            Component[] list = NadeEditorUtil.GetLinkables(scene);
            for (int i = 0; i < list.Length; i++)
            {
                NadeManager m = NadeEditorUtil.GetLinkedManager(list[i]);
                if (m == null) empty++;
                else if (m == self) linkedHere++;
                else linkedOther++;
            }
        }

        private void DrawSceneStatus()
        {
            if (Event.current.type == EventType.Layout) Refresh(false);
            if (!editable) return; // Prefab のアセット・プレハブ編集モードでは出さない

            NadeManager self = (NadeManager)target;
            bool selfActive = self.gameObject.activeInHierarchy;

            if (activeManagers.Length > 1)
            {
                EditorGUILayout.HelpBox(NadeEditorUtil.DuplicateMessage(activeManagers), MessageType.Error);
                for (int i = 0; i < activeManagers.Length; i++)
                {
                    NadeManager m = activeManagers[i];
                    if (m == null) continue;
                    string label = NadeEditorUtil.GetPath(m.transform) + (m == self ? "（これ）" : "");
                    if (GUILayout.Button(label, EditorStyles.miniButton))
                    {
                        Selection.activeGameObject = m.gameObject;
                        EditorGUIUtility.PingObject(m.gameObject);
                    }
                }
                EditorGUILayout.Space();
            }

            if (!selfActive)
            {
                EditorGUILayout.HelpBox("この WorldNadenade は非表示です。非表示の WorldNadenade には、パネルを自動で接続しません。"
                    + "不要なら削除してください（WorldNadenade を非表示にしても、なで音のオン・オフにはなりません）。", MessageType.Info);
            }

            string status = "パネル・タブレット：接続済み " + linkedHere.ToString() + " / 未接続 " + empty.ToString();
            if (linkedOther > 0) status += " / 別の WorldNadenade に接続 " + linkedOther.ToString();
            EditorGUILayout.HelpBox(status, empty > 0 ? MessageType.Warning : MessageType.None);

            bool canLink = empty > 0 && selfActive && activeManagers.Length == 1;
            if (empty > 0)
            {
                using (new EditorGUI.DisabledScope(!canLink))
                {
                    if (GUILayout.Button("未接続のパネルをこの WorldNadenade に接続", GUILayout.Height(26f)))
                    {
                        int n = NadeEditorUtil.LinkAll(self.gameObject.scene, self);
                        if (n > 0) Debug.Log("[World Nadenade] " + n.ToString() + " 個のパネルを「" + self.gameObject.name + "」に接続しました", self);
                        Refresh(true);
                    }
                }
            }
            EditorGUILayout.Space();
        }
    }
}
#endif
