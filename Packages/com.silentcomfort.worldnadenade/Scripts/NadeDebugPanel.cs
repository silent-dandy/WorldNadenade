using TMPro;
using UdonSharp;
using UnityEngine;

namespace SilentComfort.WorldNadenade
{
    /// <summary>
    /// World Nadenade のデバッグ表示パネル（ワールド作者の確認用）。操作パネルとは別に、好きな場所へ置ける。
    ///
    /// - NadeManager の Debug Mode がオフ（または Manager 未設定）なら、起動時にパネルごと非表示になる
    /// - 表示内容は NadeManager が 0.2 秒ごとに作って配る。「更新を止める」を押すと、その時点の表示で止まり、
    ///   止めている間（とパネルが非表示の間）は文字列を作る処理もしない
    /// - 上段（Summary）に全体の状態、下段（Table）に発音スロットごとの状態を表で出す。
    ///   表の列は TextMeshPro の pos タグで揃えるので、テキストは2つだけ
    ///
    /// UI からの呼び出し（Button の On Click → このパネルの UdonBehaviour.SendCustomEvent）
    ///   _OnPausePressed
    /// </summary>
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class NadeDebugPanel : UdonSharpBehaviour
    {
        [Header("Connection（シーンに置いたら WorldNadenade をドラッグ）")]
        [SerializeField] private NadeManager manager;

        [Header("Text")]
        [Tooltip("全体の状態（ON/OFF・音量・自分の設定・頭と発音の数）")]
        [SerializeField] private TextMeshProUGUI summaryText;
        [Tooltip("発音スロットの表")]
        [SerializeField] private TextMeshProUGUI tableText;

        [Header("Pause")]
        [Tooltip("「更新を止める / 再開」ボタンの文字（任意）")]
        [SerializeField] private TextMeshProUGUI pauseLabel;
        [SerializeField] private string pauseText = "更新を止める";
        [SerializeField] private string resumeText = "更新を再開";

        private bool paused;
        private bool ready;

        private void Start()
        {
            if (manager == null)
            {
                Debug.LogWarning("[World Nadenade] デバッグ表示パネルの Manager が未設定です: " + gameObject.name);
                gameObject.SetActive(false);
                return;
            }
            if (!manager._IsDebugMode())
            {
                gameObject.SetActive(false); // 公開時（Debug Mode オフ）はパネルごと出さない
                return;
            }

            ready = true;
            _ApplyPauseLabel();
            manager._RegisterDebugPanel(this);
        }

        /// NadeManager が問い合わせる。true のときだけ表示内容を作って送る
        public bool _IsUpdating()
        {
            return ready && !paused && gameObject.activeInHierarchy;
        }

        public void _SetDebug(string summary, string table)
        {
            if (summaryText != null) summaryText.text = summary;
            if (tableText != null) tableText.text = table;
        }

        public void _OnPausePressed()
        {
            if (!ready) return;
            paused = !paused;
            _ApplyPauseLabel();
            if (!paused) manager._RequestDebugRefresh(); // 再開したらすぐ最新にする
        }

        private void _ApplyPauseLabel()
        {
            if (pauseLabel != null) pauseLabel.text = paused ? resumeText : pauseText;
        }
    }
}
