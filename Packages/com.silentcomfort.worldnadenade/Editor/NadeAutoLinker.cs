#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// パネル・タブレットを置いたときに、Manager 欄へシーンの WorldNadenade を自動で入れる。
    /// WorldNadenade が2つ以上になったときは、エラーのダイアログを出す。
    ///
    /// 置いたときに働く（Prefab のドロップ・複製・貼り付け。ObjectChangeEvents の CreateGameObjectHierarchy）
    ///   - パネルを置いた：表示中の WorldNadenade がちょうど1つなら、Hierarchy の変化が落ち着いてから（0.3 秒）空の Manager 欄に入れる。
    ///     置いた直後に入れると、UdonSharp の部品が一番上に付いた Prefab（タブレット単体など）では値が空に戻されることがあるため
    ///   - WorldNadenade を置いた：それが1つ目なら、シーンの未接続のパネルをまとめて接続する（パネルを先に置いた場合）
    ///   - WorldNadenade が2つ以上になった：「今置いた方を削除 / そのままにする」のダイアログを出す。接続はしない
    ///   - 入っている参照は書き換えない。プレイ中・Prefab のアセット・プレハブ編集モードでは何もしない
    ///   - Scene ビューへドラッグしている間のプレビュー（Hierarchy に表示されない）は扱わず、ドロップして表示されてから処理する
    ///
    /// 置き直さずに Manager 欄が空になったときも、自動で接続し直す（ScheduleScan）
    ///   - Prefab が読み込み直されたとき（NadePrefabImportWatcher。Prefab の作り直し・パッケージの更新で、
    ///     シーンに置いたインスタンスの上書きが外れた場合）
    ///   - シーンを開いたとき・スクリプトの再読み込みのあと（Unity の起動時を含む）
    ///   - Hierarchy が変わったとき（少し待ってから。置いたときのイベントが届かなかった・入れた値が戻された場合の保険。
    ///     Pickup や Rigidbody が一番上に付いたタブレット単体の Prefab で、置いたときの接続が働かなかったことへの対策）
    ///   - 表示中の WorldNadenade がちょうど1つのシーンだけ。2つ以上のシーンは接続せず、Console に警告を出す（ダイアログは出さない）
    ///
    /// Tools > Silent Comfort > World Nadenade > Connect Panels
    ///   読み込んでいるシーンの未接続のパネルを、まとめて接続する
    /// Tools > Silent Comfort > World Nadenade > Auto Connect Panels
    ///   自動接続（置いたとき・接続し直し）のオン・オフ（初期はオン。この PC の設定として保存）。2つ以上のときのエラーはオフでも出す
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    [InitializeOnLoad]
    public static class NadeAutoLinker
    {
        private const string MenuRoot = "Tools/Silent Comfort/World Nadenade/";
        private const string ConnectMenuPath = MenuRoot + "Connect Panels";
        private const string AutoMenuPath = MenuRoot + "Auto Connect Panels";
        private const string AutoPrefKey = "SilentComfort.WorldNadenade.AutoConnect";

        private static readonly List<int> pendingIds = new List<int>();
        private static readonly List<GameObject> pendingDestroy = new List<GameObject>(); // 「今置いた方を削除」で消すもの
        private const int DestroyWaitFrames = 3;
        private static int destroyWaitFrames;
        private static readonly Dictionary<int, double> hiddenSince = new Dictionary<int, double>(); // 表示されるのを待ち始めた時刻
        private const double HiddenWaitLimit = 30.0; // これより長く表示されないものは待つのをやめる（ずっと隠れているオブジェクト）
        private static bool scheduled;
        private static bool scanScheduled;
        private static string scanReason = "";
        private const double HierarchyScanDelay = 0.3; // Hierarchy が変わってから確かめるまでの時間（秒）。ドラッグ中などの途中の状態を避ける
        private static double hierarchyScanAt;
        private static bool hierarchyScanWaiting;
        private static string settledReason = "";
        private const string PlacedReason = "置いたとき";

        static NadeAutoLinker()
        {
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            EditorSceneManager.sceneOpened += OnSceneOpened;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            ScheduleScan("スクリプトの読み込みのあと"); // Unity の起動時・パッケージの更新のあとも、ここを通る
        }

        public static bool AutoConnectEnabled
        {
            get { return EditorPrefs.GetBool(AutoPrefKey, true); }
            set { EditorPrefs.SetBool(AutoPrefKey, value); }
        }

        // ───────────────────────── 置いたとき ─────────────────────────

        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.CreateGameObjectHierarchy) continue;
                CreateGameObjectHierarchyEventArgs e;
                stream.GetCreateGameObjectHierarchyEvent(i, out e);
                pendingIds.Add(e.instanceId);
            }

            // イベントの途中ではシーンを書き換えず、ダイアログも出さない。まとめて次の更新で処理する
            if (pendingIds.Count > 0 && !scheduled)
            {
                scheduled = true;
                EditorApplication.delayCall += ProcessPending;
            }
        }

        private static void ProcessPending()
        {
            scheduled = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                pendingIds.Clear();
                return;
            }

            List<GameObject> roots = new List<GameObject>();
            List<int> waiting = new List<int>();
            for (int i = 0; i < pendingIds.Count; i++)
            {
                GameObject go = EditorUtility.InstanceIDToObject(pendingIds[i]) as GameObject;
                if (go == null || roots.Contains(go)) continue; // ドラッグを取り消して消えたものも、ここで外れる
                if (NadeEditorUtil.IsHiddenInHierarchy(go))
                {
                    // Scene ビューへドラッグ中のプレビュー。ドロップして表示されるまで待つ（ドラッグ中にダイアログを出さない）
                    int id = pendingIds[i];
                    double since;
                    if (!hiddenSince.TryGetValue(id, out since))
                    {
                        since = EditorApplication.timeSinceStartup;
                        hiddenSince[id] = since;
                    }
                    if (EditorApplication.timeSinceStartup - since < HiddenWaitLimit && !waiting.Contains(id)) waiting.Add(id);
                    else hiddenSince.Remove(id);
                    continue;
                }
                hiddenSince.Remove(pendingIds[i]);
                if (!NadeEditorUtil.IsEditableSceneObject(go)) continue;
                roots.Add(go);
            }
            pendingIds.Clear();
            if (waiting.Count == 0) hiddenSince.Clear();
            if (waiting.Count > 0)
            {
                pendingIds.AddRange(waiting);
                if (!scheduled)
                {
                    scheduled = true;
                    EditorApplication.delayCall += ProcessPending;
                }
            }
            if (roots.Count == 0) return;

            List<Scene> scenes = new List<Scene>();
            for (int i = 0; i < roots.Count; i++)
            {
                if (!scenes.Contains(roots[i].scene)) scenes.Add(roots[i].scene);
            }
            for (int s = 0; s < scenes.Count; s++) ProcessScene(scenes[s], roots);
        }

        private static void ProcessScene(Scene scene, List<GameObject> roots)
        {
            // 置いたものの中の WorldNadenade（表示中のものだけ）と、Manager 欄が空のパネル
            List<NadeManager> placedManagers = new List<NadeManager>();
            List<Component> placedEmpty = new List<Component>();
            for (int i = 0; i < roots.Count; i++)
            {
                if (roots[i] == null || roots[i].scene != scene) continue;
                NadeManager[] ms = roots[i].GetComponentsInChildren<NadeManager>(false);
                for (int k = 0; k < ms.Length; k++)
                {
                    if (!placedManagers.Contains(ms[k])) placedManagers.Add(ms[k]);
                }
                Component[] ls = NadeEditorUtil.GetLinkablesIn(roots[i]);
                for (int k = 0; k < ls.Length; k++)
                {
                    if (!placedEmpty.Contains(ls[k]) && NadeEditorUtil.GetLinkedManager(ls[k]) == null) placedEmpty.Add(ls[k]);
                }
            }
            if (placedManagers.Count == 0 && placedEmpty.Count == 0) return;

            NadeManager[] active = NadeEditorUtil.GetActiveManagers(scene);

            // ── 2つ以上：接続せず、エラーを出す
            if (active.Length > 1)
            {
                if (placedManagers.Count > 0) AskRemovePlaced(placedManagers, active);
                else
                {
                    Debug.LogError("[World Nadenade] WorldNadenade が " + active.Length.ToString()
                        + " 個あるため、置いたパネルを自動で接続しませんでした。WorldNadenade を1つにしてから、Tools > Silent Comfort > World Nadenade > Connect Panels を実行してください",
                        placedEmpty[0]);
                }
                return;
            }

            if (!AutoConnectEnabled) return;

            // ── 0個：案内だけ
            if (active.Length == 0)
            {
                if (placedEmpty.Count > 0)
                {
                    Debug.LogWarning("[World Nadenade] シーンに WorldNadenade がないため、Manager 欄は空のままです。"
                        + "WorldNadenade を置くと、自動で接続されます: " + placedEmpty[0].gameObject.name, placedEmpty[0]);
                }
                return;
            }

            // ── ちょうど1つ：Hierarchy の変化が落ち着いてから、シーンの空の欄をまとめて接続する
            //    （WorldNadenade を後から置いたときは、先に置いてあったパネルも入る）。
            //    置いた直後に入れると、UdonSharp の部品が一番上に付いた Prefab（タブレット単体など）では値が空に戻されることがある
            RequestSettledScan(PlacedReason);
        }

        /// WorldNadenade が2つ以上になったとき、今置いた方を消すか尋ねる
        private static void AskRemovePlaced(List<NadeManager> placed, NadeManager[] active)
        {
            StringBuilder sb = new StringBuilder(NadeEditorUtil.DuplicateMessage(active));
            sb.Append("\n\n今置いた WorldNadenade を削除しますか？（Ctrl+Z で戻せます）");

            bool remove = EditorUtility.DisplayDialog(NadeEditorUtil.DialogTitle, sb.ToString(), "今置いた方を削除", "そのままにする");
            if (!remove)
            {
                Debug.LogError("[World Nadenade] WorldNadenade が " + active.Length.ToString() + " 個あります。1つにしてください（2つ以上あると、なで音が二重に鳴ります）", placed[0]);
                NadeEditorUtil.SelectManagers(active);
                return;
            }

            for (int i = 0; i < placed.Count; i++)
            {
                NadeManager m = placed[i];
                if (m == null) continue;
                GameObject go = m.gameObject;
                // 別の Prefab の中に入っている WorldNadenade は、その Prefab のインスタンスからは消せない
                if (PrefabUtility.IsPartOfPrefabInstance(go) && !PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                {
                    Debug.LogError("[World Nadenade] Prefab の中にある WorldNadenade は削除できません。Prefab を開いて取り除いてください: "
                        + NadeEditorUtil.GetPath(go.transform), go);
                    continue;
                }
                if (!pendingDestroy.Contains(go)) pendingDestroy.Add(go);
            }

            // すぐには消さない。置いた直後の UdonSharp の初期設定（UdonSharpEditorUtility.RunBehaviourSetup。delayCall で動く）が
            // 終わる前に消すと、UdonSharp 側で MissingReferenceException が出るため、数回の更新を待ってから消す
            if (pendingDestroy.Count > 0)
            {
                destroyWaitFrames = DestroyWaitFrames;
                EditorApplication.update -= DestroyPending;
                EditorApplication.update += DestroyPending;
            }
        }

        private static void DestroyPending()
        {
            if (destroyWaitFrames > 0)
            {
                destroyWaitFrames--;
                return;
            }
            EditorApplication.update -= DestroyPending;
            for (int i = 0; i < pendingDestroy.Count; i++)
            {
                if (pendingDestroy[i] != null) Undo.DestroyObjectImmediate(pendingDestroy[i]);
            }
            pendingDestroy.Clear();
        }

        // ───────────────────────── 接続し直し ─────────────────────────

        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            ScheduleScan("シーンを開いたとき");
        }

        /// Hierarchy が変わったら、少し待ってから確かめる（続けて変わったら、最後の変化から待ち直す）。
        /// 接続した値はプロパティの変更なので、ここからの接続で Hierarchy の変化が続くことはない
        private static void OnHierarchyChanged()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            RequestSettledScan("置いたあとの確認");
        }

        /// Hierarchy の変化が HierarchyScanDelay 秒止まってから ScheduleScan する。続けて呼ばれたら待ち直す。
        /// 理由は「置いたとき」を優先する（ログの書き方を変えるため）
        private static void RequestSettledScan(string reason)
        {
            if (string.IsNullOrEmpty(settledReason) || reason == PlacedReason) settledReason = reason;
            hierarchyScanAt = EditorApplication.timeSinceStartup + HierarchyScanDelay;
            if (hierarchyScanWaiting) return;
            hierarchyScanWaiting = true;
            EditorApplication.update += WaitHierarchyScan;
        }

        private static void WaitHierarchyScan()
        {
            if (EditorApplication.timeSinceStartup < hierarchyScanAt) return;
            EditorApplication.update -= WaitHierarchyScan;
            hierarchyScanWaiting = false;
            string reason = settledReason;
            settledReason = "";
            ScheduleScan(reason);
        }

        /// 読み込んでいるシーンの未接続のパネルを、次の更新で接続し直す。何度呼んでも1回にまとめる
        public static void ScheduleScan(string reason)
        {
            if (string.IsNullOrEmpty(scanReason) || reason == PlacedReason) scanReason = reason;
            if (scanScheduled) return;
            scanScheduled = true;
            EditorApplication.delayCall += ScanScenes;
        }

        private static void ScanScenes()
        {
            scanScheduled = false;
            string reason = scanReason;
            scanReason = "";
            if (!AutoConnectEnabled) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                ScheduleScan(reason); // 読み込みやコンパイルが終わってからもう一度
                return;
            }

            Scene[] scenes = NadeEditorUtil.GetLoadedScenes();
            for (int s = 0; s < scenes.Length; s++)
            {
                Component[] all = NadeEditorUtil.GetLinkables(scenes[s]);
                int empty = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    if (NadeEditorUtil.GetLinkedManager(all[i]) == null) empty++;
                }
                if (empty == 0) continue;

                NadeManager[] active = NadeEditorUtil.GetActiveManagers(scenes[s]);
                if (active.Length > 1)
                {
                    Debug.LogWarning("[World Nadenade] シーン「" + scenes[s].name + "」に WorldNadenade が " + active.Length.ToString()
                        + " 個あるため、未接続のパネル " + empty.ToString() + " 個を接続できません。WorldNadenade を1つにしてください", active[0]);
                    continue;
                }
                if (active.Length == 0) continue; // WorldNadenade を置いたときに接続される

                int n = NadeEditorUtil.LinkAll(scenes[s], active[0]);
                if (n > 0 && reason == PlacedReason)
                {
                    Debug.Log("[World Nadenade] " + n.ToString() + " 個のパネルを「" + active[0].gameObject.name + "」に接続しました"
                        + "（自動接続は Tools > Silent Comfort > World Nadenade > Auto Connect Panels で切り替えられます）", active[0]);
                }
                else if (n > 0)
                {
                    Debug.Log("[World Nadenade] " + reason + "：Manager が空だった " + n.ToString() + " 個のパネルを「"
                        + active[0].gameObject.name + "」に接続しました（シーン「" + scenes[s].name + "」。保存すると残ります）", active[0]);
                }
            }
        }

        // ───────────────────────── メニュー ─────────────────────────

        [MenuItem(ConnectMenuPath)]
        private static void ConnectPanels()
        {
            Scene[] scenes = NadeEditorUtil.GetLoadedScenes();
            int linked = 0;
            int notFound = 0;
            StringBuilder errors = new StringBuilder();

            for (int s = 0; s < scenes.Length; s++)
            {
                NadeManager[] active = NadeEditorUtil.GetActiveManagers(scenes[s]);
                if (active.Length > 1)
                {
                    if (errors.Length > 0) errors.Append("\n\n");
                    errors.Append("シーン「").Append(scenes[s].name).Append("」：\n").Append(NadeEditorUtil.DuplicateMessage(active));
                    NadeEditorUtil.SelectManagers(active);
                    continue;
                }
                if (active.Length == 0)
                {
                    if (NadeEditorUtil.GetLinkables(scenes[s]).Length > 0) notFound++;
                    continue;
                }
                linked += NadeEditorUtil.LinkAll(scenes[s], active[0]);
            }

            if (errors.Length > 0)
            {
                EditorUtility.DisplayDialog(NadeEditorUtil.DialogTitle, errors.ToString() + "\n\nこのシーンのパネルは接続していません。", "OK");
                return;
            }

            string message;
            if (linked > 0) message = linked.ToString() + " 個のパネルを WorldNadenade に接続しました。";
            else if (notFound > 0) message = "シーンに WorldNadenade が見つかりません。先に WorldNadenade を置いてください。";
            else message = "未接続のパネルはありません。";
            EditorUtility.DisplayDialog(NadeEditorUtil.DialogTitle, message, "OK");
            if (linked > 0) Debug.Log("[World Nadenade] " + linked.ToString() + " 個のパネルを WorldNadenade に接続しました");
        }

        [MenuItem(AutoMenuPath)]
        private static void ToggleAutoConnect()
        {
            AutoConnectEnabled = !AutoConnectEnabled;
            Debug.Log("[World Nadenade] 置いたときの自動接続を" + (AutoConnectEnabled ? "オン" : "オフ") + "にしました");
        }

        [MenuItem(AutoMenuPath, true)]
        private static bool ToggleAutoConnectValidate()
        {
            Menu.SetChecked(AutoMenuPath, AutoConnectEnabled);
            return true;
        }
    }
}
#endif
