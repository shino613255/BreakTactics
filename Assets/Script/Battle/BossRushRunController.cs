using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public sealed class BossRushRunController : MonoBehaviour
{
    private const int BattleCount = 3;

    private enum Phase
    {
        Initializing,
        Preparing,
        Fighting,
        Result,
        Reloading
    }

    [SerializeField] private BossRushBattleController battleController;
    [SerializeField] private EnemyData[] enemyCandidates;
    [SerializeField] private TMP_Text encounterPreview;
    [SerializeField] private GameObject resultPanel;
    [SerializeField] private TMP_Text resultTitle;
    [SerializeField] private TMP_Text resultMessage;
    [SerializeField] private Button retryButton;

    private EnemyData[] opponents;
    private Phase phase;

    public int Victories { get; private set; }
    public int CurrentBattleNumber => Mathf.Min(Victories + 1, BattleCount);

    private void OnEnable()
    {
        if (battleController != null)
        {
            battleController.BattleStarted += OnBattleStarted;
            battleController.BattleResolved += OnBattleResolved;
        }

        if (retryButton != null)
        {
            retryButton.onClick.AddListener(Retry);
        }
    }

    private void OnDisable()
    {
        if (battleController != null)
        {
            battleController.BattleStarted -= OnBattleStarted;
            battleController.BattleResolved -= OnBattleResolved;
        }

        if (retryButton != null)
        {
            retryButton.onClick.RemoveListener(Retry);
        }
    }

    private void Start()
    {
        if (resultPanel == null || resultTitle == null || resultMessage == null || retryButton == null)
        {
            Debug.LogError("結果画面のパネル、見出し、メッセージ、RETRYボタンを設定してください。", this);
            return;
        }

        if (battleController == null || encounterPreview == null)
        {
            ShowConfigurationError("戦闘コントローラーと対戦相手の表示欄を設定してください。");
            return;
        }

        if (!TrySelectOpponents(enemyCandidates, out opponents, out string error))
        {
            ShowConfigurationError(error);
            return;
        }

        ShowPreparation();
    }

    private static bool TrySelectOpponents(EnemyData[] candidates, out EnemyData[] selected, out string error)
    {
        selected = null;
        if (candidates == null || candidates.Length < BattleCount)
        {
            error = $"敵候補を{BattleCount}体以上設定してください。";
            return false;
        }

        var uniqueEnemies = new HashSet<EnemyData>();
        foreach (EnemyData candidate in candidates)
        {
            if (candidate == null)
            {
                error = "敵候補に未設定の欄があります。";
                return false;
            }

            if (!uniqueEnemies.Add(candidate))
            {
                error = $"敵候補「{candidate.enemyName}」が重複しています。";
                return false;
            }

            if (candidate.prefab == null || !candidate.prefab.activeSelf ||
                !candidate.prefab.TryGetComponent(out EnemyManager enemyPrefab) || !enemyPrefab.enabled)
            {
                error = $"敵候補「{candidate.enemyName}」には、有効な EnemyManager をルートに持つPrefabが必要です。";
                return false;
            }
        }

        var available = new List<EnemyData>(candidates);
        selected = new EnemyData[BattleCount];
        for (int i = 0; i < BattleCount; i++)
        {
            int index = Random.Range(0, available.Count);
            selected[i] = available[index];
            available.RemoveAt(index);
        }

        error = string.Empty;
        return true;
    }

    private void ShowPreparation()
    {
        EnemyData opponent = opponents[Victories];
        encounterPreview.text = $"第 {CurrentBattleNumber} / {BattleCount} 戦\n対戦相手  {opponent.enemyName}";
        phase = Phase.Preparing;
        if (!battleController.OpenPreparation(opponent))
        {
            ShowConfigurationError("戦闘準備を開けませんでした。シーンの設定を確認してください。");
        }
    }

    private void OnBattleStarted()
    {
        if (phase == Phase.Preparing)
        {
            phase = Phase.Fighting;
        }
    }

    private void OnBattleResolved(BattleResult result)
    {
        if (phase != Phase.Fighting)
        {
            return;
        }

        if (result == BattleResult.Defeat)
        {
            ShowResult("GAME OVER", $"第{CurrentBattleNumber}戦で敗北しました。");
            return;
        }

        Victories++;
        if (Victories == BattleCount)
        {
            ShowResult("GAME CLEAR", $"{BattleCount}戦すべてを突破しました。");
            return;
        }

        ShowPreparation();
    }

    private void ShowConfigurationError(string error)
    {
        Debug.LogError(error, this);
        ShowResult("設定エラー", error);
    }

    private void ShowResult(string title, string message)
    {
        phase = Phase.Result;
        resultTitle.text = title;
        resultMessage.text = message;
        resultPanel.SetActive(true);
    }

    private void Retry()
    {
        if (phase != Phase.Result)
        {
            return;
        }

        int sceneIndex = gameObject.scene.buildIndex;
        if (sceneIndex < 0)
        {
            Debug.LogError("再挑戦できません。BossRushシーンをビルド設定に登録してください。", this);
            return;
        }

        phase = Phase.Reloading;
        retryButton.interactable = false;
        SceneManager.LoadSceneAsync(sceneIndex, LoadSceneMode.Single);
    }
}
