
using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class BossRushBattleController : MonoBehaviour
{
    [Serializable]
    internal sealed class ClassOption
    {
        [SerializeField] private PlayerData player;

        [SerializeField]
        private SkillData[] skills =
            Array.Empty<SkillData>();

        public PlayerData Player => player;

        public IReadOnlyList<SkillData> Skills => skills;
    }


    [SerializeField] private BattleManager battleManager;
    [SerializeField] private BossRushPreparationUI preparationUI;

    [SerializeField]
    private ClassOption[] classOptions =
        Array.Empty<ClassOption>();

    [SerializeField]
    private SkillData[] commonSkills =
        Array.Empty<SkillData>();


    private readonly BossRushRunState run =
        new BossRushRunState();

    private EnemyData pendingEnemy;
    public PlayerData CurrentPlayer => run.CurrentPlayer;
    public IReadOnlyList<SkillData> SelectedSkills => run.SelectedSkills;
    public int ClassChangesUsed => run.ClassChangesUsed;
    public int RemainingClassChanges => run.RemainingClassChanges;
    public bool IsInBattle => run.IsInBattle;
    internal IReadOnlyList<ClassOption> ClassOptions => classOptions;
    internal IReadOnlyList<SkillData> CommonSkills => commonSkills;

    public event Action BattleStarted;

    public event Action<BattleResult> BattleResolved;
    private void OnEnable()
    {
        if (battleManager != null)
        {
            battleManager.BattleFinished += OnBattleFinished;
        }
    }
    private void OnDisable()
    {
        if (battleManager != null)
        {
            battleManager.BattleFinished -= OnBattleFinished;
        }
    }
    public bool OpenPreparation(EnemyData encounter)
    {
        if (run.IsInBattle)
        {
            return false;
        }

        if (battleManager == null ||
            battleManager.player == null ||
            preparationUI == null ||
            encounter == null)
        {
            Debug.LogError(
                "ボスラッシュの準備画面を開けません。Battle Manager、その Player、Preparation UI、および敵データを設定してください。",
                this
            );

            return false;
        }

        pendingEnemy = encounter;

        // 前の戦闘のメッセージと入力待ちを準備画面に持ち越さない
        if (DialogTextManager.instance != null)
        {
            DialogTextManager.instance.Clear();
        }

        preparationUI.Show();

        return true;
    }
    internal bool CanChooseClass(PlayerData player)
    {
        return run.CanChooseClass(player);
    }
    internal IReadOnlyList<SkillData> GetClassCandidates(
        PlayerData player)
    {
        foreach (ClassOption option in classOptions)
        {
            if (option != null &&
                option.Player != null &&
                option.Player == player)
            {
                return option.Skills;
            }
        }

        return Array.Empty<SkillData>();
    }
    public bool TryConfirmAndStart(
        PlayerData player,
        IReadOnlyList<SkillData> classSkills,
        IReadOnlyList<SkillData> freeSkills,
        out string error)
    {
        if (run.IsInBattle)
        {
            error = "戦闘中は職業と編成を変更できません。";

            return false;
        }

        if (pendingEnemy == null)
        {
            error = "準備画面から戦闘を開始してください。";

            return false;
        }

        // 敵のPrefabに設定漏れがあるときは職業変更回数を消費しない
        if (pendingEnemy.prefab == null ||
            !pendingEnemy.prefab.TryGetComponent(
                out EnemyManager enemyPrefab))
        {
            error =
                $"敵データ「{pendingEnemy.name}」のPrefabに EnemyManager が設定されていません。";

            Debug.LogError(error, this);

            return false;
        }

        if (!run.ValidateLoadout(
            player,
            classSkills,
            freeSkills,
            GetClassCandidates(player),
            commonSkills,
            out error))
        {
            return false;
        }

        EnemyData encounter = pendingEnemy;

        pendingEnemy = null;

        preparationUI.Hide();


        battleManager.player.Setup(
            run.CurrentPlayer,
            run.SelectedSkills
        );

        EnemyManager enemy = Instantiate(enemyPrefab);

        enemy.Setup(encounter);

        battleManager.Setup(enemy);


        Debug.Log(
            $"職業「{CurrentPlayer.playerName}」で戦闘を開始しました。職業変更の残り回数は{RemainingClassChanges}回です。",
            this
        );

        BattleStarted?.Invoke();

        return true;
    }
    private void OnBattleFinished(BattleResult result)
    {
        if (!run.IsInBattle)
        {
            return;
        }

        // 終了通知から次の準備画面を開けるよう、先に戦闘中の状態を解除する
        run.FinishBattle();

        SoundManager.instance.StopBGM();

        Debug.Log(
            $"ボスラッシュの戦闘結果：{(result == BattleResult.Victory ? "勝利" : "敗北")}",
            this
        );

        BattleResolved?.Invoke(result);
    }
}
