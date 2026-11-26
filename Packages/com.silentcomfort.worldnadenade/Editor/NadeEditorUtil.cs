#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// World Nadenade（Silent Comfort）のエディタ拡張で共通に使う小さな処理。
    ///
    /// - WorldNadenade（NadeManager）を探す・数える。WorldNadenade は1つのシーンに1つだけ置く決まりなので、
    ///   表示中のものが2つ以上あるときは「接続先を決められない」として扱う。非表示のものは接続先にも数にも入れない
    /// - パネル・タブレットなど、Manager 欄を持つ部品（LinkableTypes）を WorldNadenade に接続する。
    ///   接続するのは Manager 欄が空のものだけ（入っている参照は書き換えない）。Prefab のアセットとプレハブ編集モードは対象外
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    public static class NadeEditorUtil
    {
        public const string DialogTitle = "World Nadenade";

        /// WorldNadenade に接続する部品（どれも Manager 欄の名前は ManagerField）。
        /// NadePlayerPreset は WorldNadenade の Prefab の中で設定済みなので入れない
        public static readonly System.Type[] LinkableTypes =
        {
            typeof(NadeControlPanel),
            typeof(NadeTabletPanel),
            typeof(NadeDebugPanel),
            typeof(NadeSwitch),
        };

        public const string ManagerField = "manager";

        /// 選択中のオブジェクトの親、なければシーンから WorldNadenade（NadeManager）を探す。
        /// 見つからないとき・表示中のものが2つ以上あるときはダイアログを出して null
        public static NadeManager FindManager()
        {
            NadeManager manager = null;
            if (Selection.activeGameObject != null) manager = Selection.activeGameObject.GetComponentInParent<NadeManager>();
            if (manager != null) return manager;

            Scene scene = Selection.activeGameObject != null && IsEditableSceneObject(Selection.activeGameObject)
                ? Selection.activeGameObject.scene
                : SceneManager.GetActiveScene();
            NadeManager[] list = GetActiveManagers(scene);
            if (list.Length == 1) return list[0];

            if (list.Length == 0)
            {
                EditorUtility.DisplayDialog(DialogTitle, "シーンに WorldNadenade（NadeManager）が見つかりません。"
                    + (CountInactiveManagers(scene) > 0 ? "\n（非表示の WorldNadenade は使いません）" : ""), "OK");
            }
            else
            {
                EditorUtility.DisplayDialog(DialogTitle, DuplicateMessage(list), "OK");
                SelectManagers(list);
            }
            return null;
        }

        /// ダイアログを出さずに、シーンの WorldNadenade を1つ返す。表示中のものがちょうど1つのときだけ。0個・2個以上なら null
        public static NadeManager FindSingleManager(Scene scene)
        {
            NadeManager[] list = GetActiveManagers(scene);
            return list.Length == 1 ? list[0] : null;
        }

        // ───────────────────────── 探す・数える ─────────────────────────

        /// シーンにあって編集できるオブジェクトか（Prefab のアセット・プレハブ編集モード・読み込まれていないシーン・
        /// Hierarchy に表示されていないオブジェクトは false）
        public static bool IsEditableSceneObject(GameObject go)
        {
            if (go == null) return false;
            if (EditorUtility.IsPersistent(go)) return false;
            Scene s = go.scene;
            if (!s.IsValid() || !s.isLoaded) return false;
            if (EditorSceneManager.IsPreviewScene(s)) return false;
            if (IsHiddenInHierarchy(go)) return false;
            return true;
        }

        /// Hierarchy に表示されていないか（自分か親のどれかに HideInHierarchy）。
        /// Scene ビューへ Prefab をドラッグしている間のプレビューはこの状態。まだ置かれていないので扱わない（ドラッグ中にダイアログを出さない）
        public static bool IsHiddenInHierarchy(GameObject go)
        {
            Transform t = go.transform;
            while (t != null)
            {
                if ((t.gameObject.hideFlags & HideFlags.HideInHierarchy) != 0) return true;
                t = t.parent;
            }
            return false;
        }

        /// シーンにある表示中の WorldNadenade
        public static NadeManager[] GetActiveManagers(Scene scene)
        {
            NadeManager[] all = Object.FindObjectsByType<NadeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            List<NadeManager> list = new List<NadeManager>();
            for (int i = 0; i < all.Length; i++)
            {
                NadeManager m = all[i];
                if (m == null || m.gameObject.scene != scene || !IsEditableSceneObject(m.gameObject)) continue;
                if (!m.gameObject.activeInHierarchy) continue;
                list.Add(m);
            }
            return list.ToArray();
        }

        /// シーンにある非表示の WorldNadenade の数（接続先にはしない）
        public static int CountInactiveManagers(Scene scene)
        {
            NadeManager[] all = Object.FindObjectsByType<NadeManager>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int n = 0;
            for (int i = 0; i < all.Length; i++)
            {
                NadeManager m = all[i];
                if (m == null || m.gameObject.scene != scene || !IsEditableSceneObject(m.gameObject)) continue;
                if (!m.gameObject.activeInHierarchy) n++;
            }
            return n;
        }

        /// シーンにある Manager 欄を持つ部品（非表示のものも含む）
        public static Component[] GetLinkables(Scene scene)
        {
            List<Component> list = new List<Component>();
            for (int t = 0; t < LinkableTypes.Length; t++)
            {
                Object[] found = Object.FindObjectsByType(LinkableTypes[t], FindObjectsInactive.Include, FindObjectsSortMode.None);
                for (int i = 0; i < found.Length; i++)
                {
                    Component c = found[i] as Component;
                    if (c == null || c.gameObject.scene != scene || !IsEditableSceneObject(c.gameObject)) continue;
                    list.Add(c);
                }
            }
            return list.ToArray();
        }

        /// root とその子にある Manager 欄を持つ部品（非表示のものも含む）
        public static Component[] GetLinkablesIn(GameObject root)
        {
            List<Component> list = new List<Component>();
            if (root == null) return list.ToArray();
            for (int t = 0; t < LinkableTypes.Length; t++)
            {
                Component[] found = root.GetComponentsInChildren(LinkableTypes[t], true);
                for (int i = 0; i < found.Length; i++)
                {
                    if (found[i] != null) list.Add(found[i]);
                }
            }
            return list.ToArray();
        }

        public static bool IsLinkable(Object obj)
        {
            if (obj == null) return false;
            System.Type type = obj.GetType();
            for (int t = 0; t < LinkableTypes.Length; t++)
            {
                if (LinkableTypes[t] == type) return true;
            }
            return false;
        }

        /// Manager 欄に入っている WorldNadenade（空・欄が無いときは null）
        public static NadeManager GetLinkedManager(Component c)
        {
            if (c == null) return null;
            SerializedObject so = new SerializedObject(c);
            SerializedProperty prop = so.FindProperty(ManagerField);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference) return null;
            return prop.objectReferenceValue as NadeManager;
        }

        // ───────────────────────── 接続する ─────────────────────────

        /// Manager 欄が空なら manager を入れる（Undo できる）。入れたときだけ true。
        /// 入っている参照は書き換えない。別のシーンの WorldNadenade は参照できないので入れない
        public static bool TryLink(Component c, NadeManager manager)
        {
            if (c == null || manager == null) return false;
            if (!IsEditableSceneObject(c.gameObject) || !IsEditableSceneObject(manager.gameObject)) return false;
            if (c.gameObject.scene != manager.gameObject.scene) return false;

            SerializedObject so = new SerializedObject(c);
            SerializedProperty prop = so.FindProperty(ManagerField);
            if (prop == null || prop.propertyType != SerializedPropertyType.ObjectReference)
            {
                Debug.LogWarning("[World Nadenade] Manager の欄が見つかりません（" + c.GetType().Name + " を最新に差し替えてください）: " + c.gameObject.name, c);
                return false;
            }
            if (prop.objectReferenceValue != null) return false;

            prop.objectReferenceValue = manager;
            so.ApplyModifiedProperties(); // Undo に記録され、Prefab のインスタンスでは上書きとして残る
            UdonSharpBehaviour usb = c as UdonSharpBehaviour;
            if (usb != null) UdonSharpEditorUtility.CopyProxyToUdon(usb);
            EditorSceneManager.MarkSceneDirty(c.gameObject.scene);
            return true;
        }

        /// シーンの Manager 欄が空の部品を、すべて manager に接続する。接続した数を返す
        public static int LinkAll(Scene scene, NadeManager manager)
        {
            if (manager == null) return 0;
            Component[] list = GetLinkables(scene);
            int n = 0;
            for (int i = 0; i < list.Length; i++)
            {
                if (TryLink(list[i], manager)) n++;
            }
            return n;
        }

        // ───────────────────────── 表示 ─────────────────────────

        /// WorldNadenade が2つ以上あるときの説明（ダイアログ・インスペクター用）
        public static string DuplicateMessage(NadeManager[] managers)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("このシーンに WorldNadenade が ").Append(managers.Length).Append(" 個あります。\n");
            sb.Append("WorldNadenade は1つのシーンに1つだけ置きます（2つ以上あると、なで音が二重に鳴ります）。");
            sb.Append("パネルとタブレットは、1つの WorldNadenade に何個でも接続できます。\n");
            AppendNames(sb, managers);
            return sb.ToString();
        }

        /// 「- 名前（Hierarchy 上の場所）」を最大 10 行並べる
        public static void AppendNames(StringBuilder sb, Object[] objects)
        {
            const int max = 10;
            for (int i = 0; i < objects.Length && i < max; i++)
            {
                Component c = objects[i] as Component;
                GameObject go = c != null ? c.gameObject : objects[i] as GameObject;
                sb.Append("\n- ").Append(go != null ? GetPath(go.transform) : (objects[i] != null ? objects[i].name : "(なし)"));
            }
            if (objects.Length > max) sb.Append("\n…ほか ").Append(objects.Length - max).Append(" 個");
        }

        /// Hierarchy 上の場所（Room/Wall/NadeControlPanel のような形）
        public static string GetPath(Transform t)
        {
            if (t == null) return "";
            string path = t.name;
            Transform p = t.parent;
            while (p != null)
            {
                path = p.name + "/" + path;
                p = p.parent;
            }
            return path;
        }

        public static void SelectManagers(NadeManager[] managers)
        {
            GameObject[] gos = new GameObject[managers.Length];
            for (int i = 0; i < managers.Length; i++) gos[i] = managers[i] != null ? managers[i].gameObject : null;
            Selection.objects = gos;
            if (gos.Length > 0 && gos[0] != null) EditorGUIUtility.PingObject(gos[0]);
        }

        /// 読み込まれていて編集できるシーン（プレハブ編集モードは含まない）
        public static Scene[] GetLoadedScenes()
        {
            List<Scene> list = new List<Scene>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (s.IsValid() && s.isLoaded && !EditorSceneManager.IsPreviewScene(s)) list.Add(s);
            }
            return list.ToArray();
        }

        // ───────────────────────── 以前からの処理 ─────────────────────────

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
