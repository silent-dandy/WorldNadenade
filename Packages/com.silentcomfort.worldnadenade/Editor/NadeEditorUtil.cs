#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// World Nadenade（Silent Comfort）のエディタ拡張で共通に使う小さな処理。
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    public static class NadeEditorUtil
    {
        /// 選択中のオブジェクトの親、なければシーンから WorldNadenade（NadeManager）を探す。見つからなければダイアログを出して null
        public static NadeManager FindManager()
        {
            NadeManager manager = null;
            if (Selection.activeGameObject != null) manager = Selection.activeGameObject.GetComponentInParent<NadeManager>();
            if (manager == null) manager = Object.FindFirstObjectByType<NadeManager>();
            if (manager == null)
            {
                EditorUtility.DisplayDialog("World Nadenade", "シーンに WorldNadenade（NadeManager）が見つかりません。", "OK");
            }
            return manager;
        }

        public static Transform EnsureChild(Transform parent, string name)
        {
            Transform t = parent.Find(name);
            if (t != null) return t;
            GameObject go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        public static void SetObj(SerializedObject so, string field, Object value, string scriptName)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[World Nadenade] 欄が見つかりません（" + scriptName + " を最新に差し替えてください）: " + field);
                return;
            }
            prop.objectReferenceValue = value;
        }

        public static void SetString(SerializedObject so, string field, string value, string scriptName)
        {
            SerializedProperty prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning("[World Nadenade] 欄が見つかりません（" + scriptName + " を最新に差し替えてください）: " + field);
                return;
            }
            prop.stringValue = value;
        }
    }
}
#endif
