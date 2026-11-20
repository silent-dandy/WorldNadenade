#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// World Nadenade（Silent Comfort）の音色（NadeSoundPreset）を追加・点検する。
    ///
    /// Tools > Silent Comfort > World Nadenade > Add Sound Preset
    ///   - WorldNadenade の直下に Presets が無ければ作り、NadeManager の Presets Root に設定する
    ///   - Project ウィンドウで AudioClip を選んでから実行すると、1クリップにつき1音色を作る
    ///     （手のひらの音・表示名・Preset Id をクリップ名から設定）。何も選んでいなければ空の音色を1つ作る
    ///   - Preset Id は重複しないよう自動で決める（あとから変えてよいが、公開後は変えない）
    ///
    /// Tools > Silent Comfort > World Nadenade > Check Presets
    ///   - Preset Id の空欄・重複、手のひらの音の未設定、NadeSoundPreset の付け忘れを一覧で表示する
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    public static class NadePresetSetup
    {
        private const string AddMenuPath = "Tools/Silent Comfort/World Nadenade/Add Sound Preset";
        private const string CheckMenuPath = "Tools/Silent Comfort/World Nadenade/Check Presets";

        [MenuItem(AddMenuPath)]
        private static void Add()
        {
            AudioClip[] clips = Selection.GetFiltered<AudioClip>(SelectionMode.Assets);

            NadeManager manager = NadeEditorUtil.FindManager();
            if (manager == null) return;
            Transform presetsRoot = EnsurePresetsRoot(manager);

            HashSet<string> usedIds = CollectIds(presetsRoot);
            GameObject last = null;

            if (clips.Length == 0)
            {
                last = CreatePreset(presetsRoot, "NewPreset", null, usedIds);
            }
            else
            {
                for (int i = 0; i < clips.Length; i++) last = CreatePreset(presetsRoot, clips[i].name, clips[i], usedIds);
            }

            EditorSceneManager.MarkSceneDirty(manager.gameObject.scene);
            Selection.activeGameObject = last;
            Debug.Log("[World Nadenade] 音色を " + Mathf.Max(1, clips.Length).ToString() + " 個追加しました: " + presetsRoot.name
                + (PrefabUtility.IsPartOfPrefabInstance(manager.gameObject) ? "（Prefab に反映する場合は Overrides > Apply All）" : ""));
        }

        [MenuItem(CheckMenuPath)]
        private static void Check()
        {
            NadeManager manager = NadeEditorUtil.FindManager();
            if (manager == null) return;

            Transform presetsRoot = GetPresetsRoot(manager);
            if (presetsRoot == null)
            {
                EditorUtility.DisplayDialog("World Nadenade", "Presets が見つかりません。Add Sound Preset で作成してください。", "OK");
                return;
            }

            StringBuilder sb = new StringBuilder();
            Dictionary<string, string> seen = new Dictionary<string, string>();
            int usable = 0;
            int problems = 0;

            for (int c = 0; c < presetsRoot.childCount; c++)
            {
                Transform child = presetsRoot.GetChild(c);
                if (!child.gameObject.activeSelf)
                {
                    sb.AppendLine("・（無効なので使わない）" + child.name);
                    continue;
                }
                NadeSoundPreset pr = child.GetComponent<NadeSoundPreset>();
                if (pr == null)
                {
                    sb.AppendLine("× NadeSoundPreset がありません: " + child.name);
                    problems++;
                    continue;
                }

                string id = string.IsNullOrEmpty(pr.presetId) ? child.name : pr.presetId;
                if (string.IsNullOrEmpty(pr.presetId))
                {
                    sb.AppendLine("△ Preset Id が空欄（オブジェクト名 '" + child.name + "' を使います）: " + child.name);
                    problems++;
                }
                if (seen.ContainsKey(id))
                {
                    sb.AppendLine("× Preset Id '" + id + "' が重複しています: " + seen[id] + " / " + child.name);
                    problems++;
                }
                else seen.Add(id, child.name);

                if (pr.palmClip == null)
                {
                    sb.AppendLine("× 手のひらの音（Palm Clip）がありません（この音色は使われません）: " + child.name);
                    problems++;
                    continue;
                }
                usable++;
            }

            string head = "使える音色 " + usable.ToString() + " 個 / 問題 " + problems.ToString() + " 件\n"
                + "（壁のパネルもタブレットも、ボタンは 6 個まで表示できます）\n\n";
            if (usable > 6) head += "△ パネルとタブレットには先頭の 6 個だけが表示され、7 個目以降は選べません。使わない音色は無効にしてください\n\n";
            string body = sb.Length > 0 ? sb.ToString() : "問題はありません。";
            Debug.Log("[World Nadenade] 音色の点検\n" + head + body);
            EditorUtility.DisplayDialog("World Nadenade：音色の点検", head + body, "OK");
        }

        private static Transform GetPresetsRoot(NadeManager manager)
        {
            SerializedObject so = new SerializedObject(manager);
            SerializedProperty prop = so.FindProperty("presetsRoot");
            Transform root = prop != null ? prop.objectReferenceValue as Transform : null;
            if (root == null) root = manager.transform.Find("Presets");
            return root;
        }

        private static Transform EnsurePresetsRoot(NadeManager manager)
        {
            Transform root = GetPresetsRoot(manager);
            if (root == null) root = NadeEditorUtil.EnsureChild(manager.transform, "Presets");

            SerializedObject so = new SerializedObject(manager);
            SerializedProperty prop = so.FindProperty("presetsRoot");
            if (prop != null && prop.objectReferenceValue != root)
            {
                Undo.RecordObject(manager, "Set Nade Presets Root");
                prop.objectReferenceValue = root;
                so.ApplyModifiedProperties();
                UdonSharpEditorUtility.CopyProxyToUdon(manager);
            }
            else if (prop == null)
            {
                Debug.LogWarning("[World Nadenade] 欄が見つかりません（NadeManager.cs を最新に差し替えてください）: presetsRoot");
            }
            return root;
        }

        private static HashSet<string> CollectIds(Transform presetsRoot)
        {
            HashSet<string> ids = new HashSet<string>();
            for (int c = 0; c < presetsRoot.childCount; c++)
            {
                Transform child = presetsRoot.GetChild(c);
                NadeSoundPreset pr = child.GetComponent<NadeSoundPreset>();
                if (pr == null) continue;
                ids.Add(string.IsNullOrEmpty(pr.presetId) ? child.name : pr.presetId);
            }
            return ids;
        }

        private static GameObject CreatePreset(Transform parent, string baseName, AudioClip clip, HashSet<string> usedIds)
        {
            GameObject go = new GameObject(baseName);
            Undo.RegisterCreatedObjectUndo(go, "Create Nade Sound Preset");
            go.transform.SetParent(parent, false);
            go.transform.SetAsLastSibling(); // 末尾に追加（Preset Id で復元するので、あとから並べ替えてもよい）

            NadeSoundPreset pr = UdonSharpUndo.AddComponent<NadeSoundPreset>(go);
            string id = MakeUniqueId(MakeId(baseName), usedIds);
            usedIds.Add(id);

            Undo.RecordObject(pr, "Setup Nade Sound Preset");
            pr.presetId = id;
            pr.displayName = clip != null ? clip.name : "";
            pr.palmClip = clip;
            EditorUtility.SetDirty(pr);
            UdonSharpEditorUtility.CopyProxyToUdon(pr);
            return go;
        }

        /// 英数字・'_'・'-' だけを残した Id を作る（日本語名のクリップは "preset" になる）
        private static string MakeId(string source)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char ch in source)
            {
                if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_' || ch == '-') sb.Append(ch);
                else if (ch >= 'A' && ch <= 'Z') sb.Append(char.ToLowerInvariant(ch));
                else if (ch == ' ') sb.Append('_');
            }
            return sb.Length > 0 ? sb.ToString() : "preset";
        }

        private static string MakeUniqueId(string id, HashSet<string> used)
        {
            if (!used.Contains(id)) return id;
            int n = 2;
            while (used.Contains(id + "_" + n.ToString())) n++;
            return id + "_" + n.ToString();
        }
    }
}
#endif
