#if UNITY_EDITOR
using UnityEditor;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// Prefab が読み込み直されたら、シーンの未接続のパネルを接続し直す（NadeAutoLinker.ScheduleScan）。
    ///
    /// Prefab を同じパスに作り直すと、中のオブジェクトの ID が新しくなり、シーンに置いたインスタンスの
    /// Manager 欄の上書きが外れることがある（パッケージの更新で Prefab が置き換わったときも同じ）。その対策。
    /// - どの Prefab でも、読み込み直しのたびに1回だけ確かめる（同時に複数読み込まれても1回にまとめる）
    /// - 接続するのは Manager 欄が空のものだけ。Auto Connect Panels がオフなら何もしない
    ///
    /// 置き場所：Assets/SilentComfort/WorldNadenade/Editor/
    /// </summary>
    public class NadePrefabImportWatcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
        {
            for (int i = 0; i < importedAssets.Length; i++)
            {
                if (importedAssets[i].EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                {
                    NadeAutoLinker.ScheduleScan("Prefab の読み込み直し");
                    return;
                }
            }
        }
    }
}
#endif
