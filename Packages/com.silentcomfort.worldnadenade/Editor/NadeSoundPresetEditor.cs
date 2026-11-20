#if UNITY_EDITOR
using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// NadeSoundPreset のインスペクター。いつもの項目の上に、調整ツール（Sound Tuner）を開くボタンを足す。
    /// 項目そのものは標準の表示のまま（NadeSoundPreset.cs に項目を足しても、ここを直す必要はない）。
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    [CustomEditor(typeof(NadeSoundPreset))]
    [CanEditMultipleObjects]
    public class NadeSoundPresetEditor : Editor
    {
        public override void OnInspectorGUI()
        {
            // UdonSharp の標準のヘッダー（スクリプト欄など）。true が返ったら、この先は描かない
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            EditorGUILayout.Space();
            // 複数の音色を選んでいるときは、どれを開くか決められないので押せない
            using (new EditorGUI.DisabledScope(targets.Length != 1))
            {
                GUIContent label = new GUIContent("♪ 音を鳴らして調整する（Sound Tuner）",
                    "この音色を調整ツールで開く。エディタ上で音を鳴らしながら値を変え、「適用」でこの音色に書き込む");
                if (GUILayout.Button(label, GUILayout.Height(28f)))
                {
                    NadeSoundTuner.OpenFor((NadeSoundPreset)target);
                }
            }
            EditorGUILayout.Space();

            // 標準の項目（スクリプト欄はヘッダーに出ているので除く）
            serializedObject.Update();
            DrawPropertiesExcluding(serializedObject, "m_Script");
            serializedObject.ApplyModifiedProperties();
        }
    }
}
#endif
