using DG.Tweening;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using static EnemyPartsController;

public enum BattleResult
{
    Victory,
    Defeat
}

public class BattleManager : MonoBehaviour
{
    private const int BossHeavyChargeTurns = 2;
    private const int SingleTurnCharge = 1;

    [Header("Battle Presentation")]
    [SerializeField] private float battlePauseDuration = 0.5f;
    [SerializeField] private float dialogPauseDuration = 0.5f;
    [SerializeField] private float screenShakeDuration = 0.3f;
    [SerializeField] private float screenShakeStrength = 0.5f;
    [SerializeField] private int screenShakeVibrato = 20;
    [SerializeField] private Camera mainCamera;
    [SerializeField] private float playerBaseAccuracy = 1f;
    [SerializeField] private float enemyBaseAccuracy = 1f;
    [SerializeField] private SkillData selectedPlayerSkill;
    [SerializeField] private bool useSelectedSkill = false;

    [Header("UI References")]
    [SerializeField] private GameObject skillSelectionPanel;
    [SerializeField] private GameObject playerStatusPanel;
    [SerializeField] private GameObject skillButton;

    public event System.Action<BattleResult> BattleFinished;
    public Transform screenShakeTarget;
    public PlayerUIManager playerUI;
    public EnemyUIManager enemyUI;
    public PlayerManager player;
    private EnemyManager enemy;
    private EnemyAI enemyAI;
    private SkillVfxController skillVfx;

    public SkillVfxController SkillVfx
    {
        get
        {
            if (skillVfx == null)
            {
                skillVfx = GetComponent<SkillVfxController>() 
                    ?? gameObject.AddComponent<SkillVfxController>();
            }

            return skillVfx;
        }
    }

    private bool waitingTap;
    private bool isPlayerTurn;
    private bool isPartViewVisible = false;
    private bool isBattleRunning = false;
    private bool isEndingBattle = false;

    private bool CombatantsAlive => 
        player != null && 
        player.IsAlive && 
        enemy != null && 
        enemy.IsAlive;

    private struct EnemyActionPlan
    {
        public EnemyManager enemy;
        public SkillData skill;
        public bool isReady;
        public bool isInterrupted;
        public bool isTelegraphed;
        public int remainingChargeTurns;
    }

    private EnemyActionPlan enemyActionPlan;
    private bool isShowingEnemyTelegraph;
    private string[] playerActionMessages;
    private int playerInputBlockedThroughFrame = -1;
    public static BattleManager Instance;

    private void Awake()
    {
        enemyAI = GetComponent<EnemyAI>();
        Instance = this;
        HideBattleUI();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Setup(EnemyManager enemymanager)
    {
        if (isBattleRunning || isEndingBattle)
        {
            Debug.LogWarning("進行中の戦闘が終了していないため、戦闘を開始できません。", this);
            return;
        }

        if (enemymanager == null || 
            !enemymanager.IsAlive || 
            player == null || 
            !player.IsAlive)
        {
            Debug.LogError("戦闘の開始には生存しているプレイヤーと敵が必要です。", this);
            return;
        }

        isBattleRunning = true;
        ResetActionState();
        ClearBattleEffects();
        skillButton.SetActive(true);
        playerStatusPanel.SetActive(true);
        playerInputBlockedThroughFrame = Time.frameCount;
        //SoundManager.instance.PlayBattleBGM();
        SoundManager.instance.PlayBattleBGM(enemymanager.data != null ? enemymanager.data.Battlebgm : null);

        enemyUI.gameObject.SetActive(true);
        enemy = enemymanager;
        enemy.ClearCounter();
        enemy.SetPartViewVisible(isPartViewVisible);
        if (enemy.data != null && 
            enemy.data.role == EnemyRole.Tank &&
            (enemy.data.enemyType == EnemyType.Normal || 
            enemy.data.enemyType == EnemyType.Boss) &&
            enemy.GetComponent<SkillCooldowns>() == null)
        {
            enemy.gameObject.AddComponent<SkillCooldowns>();
        }

        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        enemyUI.SetupUI(enemy);
        playerUI.SetupUI(player);
        StartCoroutine(BattleLoop());
    }

    private void HideBattleUI()
    {
        skillButton.SetActive(false);
        skillSelectionPanel.SetActive(false);
        playerStatusPanel.SetActive(false);
        enemyUI.gameObject.SetActive(false);
    }

    public void ToggleSkillPanel()
    {
        if (!CanReceivePlayerInput())
        {
            return;
        }

        playerInputBlockedThroughFrame = Time.frameCount;
        skillSelectionPanel.SetActive(!skillSelectionPanel.activeSelf);
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.V))
        {
            isPartViewVisible = !isPartViewVisible;
            if (enemy != null)
            {
                enemy.SetPartViewVisible(isPartViewVisible);
            }
        }

        if (!CanReceivePlayerInput())
        {
            return;
        }

        if (skillSelectionPanel.activeSelf)
        {
            return;
        }

        if (Input.GetMouseButtonDown(0))
        {
            TryPickBodyPart(Input.mousePosition);
        }
    }

    public void OnSkillSelected(SkillData selectedSkill)
    {
        if (!CanReceivePlayerInput())
        {
            return;
        }

        if (enemy == null)
        {
            return;
        }

        if (!enemy.IsAlive)
        {
            return;
        }

        if (selectedSkill == null)
        {
            Debug.LogWarning("選択されたスキルがnullですわ！");
            return;
        }

        UnitBase skillTarget = selectedSkill.targetType == TargetType.Self 
            ? (UnitBase)player 
            : enemy;
        if (!SkillUseRules.CanUse(player, skillTarget, selectedSkill, out string useError))
        {
            ShowPlayerChoiceMessage(useError);
            return;
        }

        if (selectedSkill.targetType != TargetType.Self &&
            (!SkillUseRules.IsDirectAttack(selectedSkill) || !selectedSkill.canTargetPart))
        {
            ShowPlayerChoiceMessage("このスキルでは部位を攻撃できない。");
            return;
        }

        playerInputBlockedThroughFrame = Time.frameCount;
        SkillCooldowns cooldowns = player.GetComponent<SkillCooldowns>();
        if (cooldowns != null && !cooldowns.IsReady(selectedSkill))
        {
            ShowPlayerChoiceMessage($"{selectedSkill.skillName}はクールダウン中！");
            return;
        }

        if (selectedSkill.targetType == TargetType.Self)
        {
            var result = SkillExecutor.Execute(player, player, selectedSkill);
            if (!result.executed)
            {
                ShowPlayerChoiceMessage(result.message);
                return;
            }

            ClearPlayerSkillSelection();
            PlaySkillEffect(selectedSkill, playerUI.GetSkillEffectPosition(mainCamera));
            playerUI.UpdateUI(player);
            skillSelectionPanel.SetActive(false);
            waitingTap = false;
            isPlayerTurn = false;
            playerActionMessages = new[]
            {
                result.message
            };
            return;
        }

        selectedPlayerSkill = selectedSkill;
        useSelectedSkill = true;
        waitingTap = true;
        skillSelectionPanel.SetActive(false);
        ShowPlayerChoiceMessage($"{selectedSkill.skillName}：部位を選択！");
    }

    private void TryPickBodyPart(Vector2 screenPos)
    {
        if (EventSystem.current != null)
        {
            var pointer = new PointerEventData(EventSystem.current)
            {
                position = screenPos
            };
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            if (hits.Exists(result => result.module is UnityEngine.UI.GraphicRaycaster))
            {
                return;
            }
        }

        Vector2 worldPos = mainCamera.ScreenToWorldPoint(screenPos);
        RaycastHit2D hit = Physics2D.Raycast(worldPos, Vector2.zero);
        if (!hit.collider)
        {
            return;
        }

        var part = hit.collider.GetComponentInParent<BodyPart>();
        if (part == null)
        {
            return;
        }

        OnBodyPartTapped(part);
    }

    private int CalculateDamage(SkillData skill)
    {
        if (skill == null)
        {
            return DamageRule.CalcNormalAttack(player.at, enemy.def);
        }

        if (skill.skillType == SkillType.Magic)
        {
            return DamageRule.CalcMagic(player.mag, enemy.mdef, skill.power);
        }

        return DamageRule.CalcPhysical(player.at, enemy.def, skill.power);
    }

    private AttackContext CreateNormalAttackContext()
    {
        int normalDamage = CalculateDamage(null);
        int finalDamage = DamageRule.RollCrit(normalDamage, player.critRate, player.critMultiplier);
        bool isCritical = finalDamage > normalDamage;
        return new AttackContext
        {
            baseDamage = finalDamage,
            mainDamageRate = 1f,
            partDamageRate = 1f,
            canApplyStatus = false,
            sourceSkill = null,
            isCritical = isCritical
        };
    }

    private AttackContext CreateSkillAttackContext(SkillData skill)
    {
        int skillDamage = CalculateDamage(skill);
        int finalDamage = skillDamage;
        bool isCritical = false;
        if (skill.canCrit)
        {
            finalDamage = DamageRule.RollCrit(skillDamage, Mathf.Clamp01(player.critRate + skill.critBonus), player.critMultiplier);
            isCritical = finalDamage > skillDamage;
        }

        return new AttackContext
        {
            baseDamage = finalDamage,
            mainDamageRate = skill.mainDamageRate,
            partDamageRate = skill.partDamageRate,
            canApplyStatus = skill.statusEffect != null,
            sourceSkill = skill,
            isCritical = isCritical
        };
    }

    public void OnBodyPartTapped(BodyPart part)
    {
        if (!CanAttackPart(part))
        {
            return;
        }

        EnemyPartsController enemyParts = part.GetComponentInParent<EnemyPartsController>();
        if (enemyParts == null || part.GetComponentInParent<EnemyManager>() != enemy)
        {
            Debug.LogWarning($"部位「{part.GetPartNameJP()}」の親にEnemyPartsControllerが見つかりませんでしたわ！");
            return;
        }

        enemyParts.SetSelectedPart(part);
        if (!TryCreateAttackContext(out AttackContext ctx))
        {
            return;
        }

        // 狙いなどは攻撃を試みた時点で消費するため、命中判定より先に反映する
        SkillExecutor.PreparePlayerAttack(player, ref ctx);
        waitingTap = false;
        isPlayerTurn = false;
        if (!RollPlayerAttackHit(ctx))
        {
            HandlePlayerAttackMiss();
            return;
        }

        PlayAttackEffect(ctx, part);
        bool wasBroken = part.IsBroken;
        int hpBefore = enemy.hp;
        int defenseBefore = enemy.def;
        int magicDefenseBefore = enemy.mdef;
        float accuracyPenaltyBefore = enemy.GetAccuracyPenalty();
        var sealedBossSkillsBefore = TankBossRoleAction.GetSealedSkills(enemy);

        var result = enemyParts.ApplyAttack(ctx);

        bool justBroken = !wasBroken && part.IsBroken;
        bool techniqueLearned = justBroken && player.BattleEffects.LearnTechnique(player.Role, part);
        bool interrupted = TryInterruptEnemyAction(part, justBroken);

        TryApplyStatusEffect(ctx);
        int reflectedDamage = ApplyCounterDamage(hpBefore);

        if (useSelectedSkill)
        {
            useSelectedSkill = false;
            selectedPlayerSkill = null;
        }

        if (enemy != null)
        {
            enemyUI.UpdateUI(enemy);
        }

        playerUI.UpdateUI(player);
        var messages = new List<string>();
        if (interrupted)
        {
            messages.Add(UsesBossHeavyAttack(enemy)
                ? $"{enemyActionPlan.skill.skillName}を中断した！\n敵はこのターン行動しない。"
                : $"妨害成功！{enemyActionPlan.skill.skillName}を中断！\n敵はこのターン行動しない。");
        }

        messages.Add(CreateAttackMessage(part, ctx, result));
        string breakMessage = CreatePartBreakMessage(
            part,
            wasBroken,
            justBroken,
            Mathf.Max(0, defenseBefore - enemy.def),
            Mathf.Max(0, magicDefenseBefore - enemy.mdef),
            enemy.GetAccuracyPenalty() - accuracyPenaltyBefore);
        if (!string.IsNullOrEmpty(breakMessage))
        {
            messages.Add(breakMessage);
        }

        if (techniqueLearned)
        {
            messages.Add($"戦技習得！ {part.GetPartNameJP()}の戦技を得た！");
        }

        if (justBroken && enemy.IsAlive)
        {
            foreach (SkillData skill in TankBossRoleAction.GetSealedSkills(enemy))
            {
                if (!sealedBossSkillsBefore.Contains(skill))
                {
                    messages.Add($"{skill.skillName}を封印！\n敵は以後このスキルを使えない。");
                }
            }
        }

        if (reflectedDamage > 0)
        {
            messages.Add($"カウンター！\nプレイヤーは{reflectedDamage}ダメージ受けた！");
        }

        playerActionMessages = messages.ToArray();
    }

    private void HandlePlayerAttackMiss()
    {
        if (useSelectedSkill)
        {
            useSelectedSkill = false;
            selectedPlayerSkill = null;
        }

        playerUI.UpdateUI(player);
        playerActionMessages = new[]
        {
            $"{player.name}の攻撃！\nしかし{enemy.name}に当たらなかった！"
        };
    }

    private int ApplyCounterDamage(int hpBefore)
    {
        // 反射は実際に減った本体HPを基準にし、ガードの対象外として扱う
        int reflectedDamage = enemy.GetCounterDamage(Mathf.Max(0, hpBefore - enemy.hp));
        if (reflectedDamage > 0)
        {
            player.TakeDamageRaw(reflectedDamage);
        }

        return reflectedDamage;
    }

    private bool CanAttackPart(BodyPart part)
    {
        if (part == null ||
            !CanReceivePlayerInput() ||
            enemy == null ||
            !enemy.IsAlive)
        {
            return false;
        }

        return true;
    }

    private bool TryCreateAttackContext(out AttackContext ctx)
    {
        if (useSelectedSkill && selectedPlayerSkill != null)
        {
            // スキル選択時は予約だけにして、攻撃する部位が決まってからコストを支払う
            if (!SkillUseRules.TryPay(player, enemy, selectedPlayerSkill, out string useError))
            {
                ShowPlayerChoiceMessage(useError);
                ctx = default;
                return false;
            }

            ctx = CreateSkillAttackContext(selectedPlayerSkill);
            player.GetComponent<SkillCooldowns>()?.StartCooldown(selectedPlayerSkill);
            return true;
        }

        ctx = CreateNormalAttackContext();
        return true;
    }

    private void PlayAttackEffect(AttackContext ctx, BodyPart part)
    {
        if (ctx.sourceSkill == null)
        {
            return;
        }

        Collider2D collider = part.GetComponent<Collider2D>();
        PlaySkillEffect(ctx.sourceSkill, collider != null ? collider.bounds.center : part.transform.position);
    }

    private bool RollPlayerAttackHit(AttackContext ctx)
    {
        float accuracy = ctx.sourceSkill != null ? ctx.sourceSkill.accuracy : playerBaseAccuracy;
        return DamageRule.RollHit(accuracy, enemy.evasionRate, ctx.accuracyBonus);
    }

    private void TryApplyStatusEffect(AttackContext ctx)
    {
        if (ctx.sourceSkill == null)
        {
            return;
        }

        StatusEffectData effect = ctx.sourceSkill.statusEffect;
        if (effect == null)
        {
            return;
        }

        if (enemy == null || !enemy.IsAlive)
        {
            return;
        }

        float chance = ctx.sourceSkill.applyChance;
        if (Random.value > chance)
        {
            return;
        }

        switch (effect.type)
        {
            case StatusEffectType.Burn:
                enemy.ApplyBurn(effect);
                break;
            case StatusEffectType.Frozen:
                enemy.ApplyFrozen(effect);
                break;
        }
    }

    private string CreateAttackMessage(BodyPart part, AttackContext ctx, AttackResult result)
    {
        string message;
        if (ctx.isCritical)
        {
            message = "クリティカル！\n";
        }
        else
        {
            message = string.Empty;
        }

        if (ctx.sourceSkill != null)
        {
            message += $"{ctx.sourceSkill.skillName}で{part.GetPartNameJP()}に攻撃！";
        }
        else
        {
            message += $"{part.GetPartNameJP()}を攻撃！";
        }

        if (result.damageMultiplier > 1f)
        {
            message += "\n弱点！";
        }
        else if (result.damageMultiplier > 0f && result.damageMultiplier < 1f)
        {
            message += "\n耐性あり";
        }

        message += $"\nダメージ：本体{result.mainDamage}／部位{result.partDamage}";
        return message;
    }

    private string CreatePartBreakMessage(
        BodyPart part,
        bool wasBroken,
        bool justBroken,
        int defenseDown,
        int magicDefenseDown,
        float accuracyDown)
    {
        if (justBroken)
        {
            switch (part.partType)
            {
                case PartType.Face:
                    return $"顔を破壊！\n防御-{defenseDown}／魔法防御-{magicDefenseDown}";
                case PartType.Hand:
                    return $"手を破壊！\n通常攻撃の命中率-{Mathf.RoundToInt(accuracyDown * 100f)}ポイント";
                case PartType.Leg:
                    return "脚を破壊！\nこのターンからプレイヤーは2回行動！";
            }
        }

        return wasBroken ? "この部位は破壊済み。\n再攻撃では妨害できない。" : string.Empty;
    }
    public void PlaySkillEffect(SkillData skill, Vector3 worldPos)
    {
        SoundManager.instance.PlaySkillSE(skill);
        SkillVfx.Play(skill, worldPos);
    }

    private bool CanReceivePlayerInput()
    {
        var dialog = DialogTextManager.instance;
        return isBattleRunning && 
            !isEndingBattle && 
            CombatantsAlive && 
            isPlayerTurn && 
            !isShowingEnemyTelegraph &&
            Time.frameCount > playerInputBlockedThroughFrame &&
            (dialog == null || 
            (dialog.IsCompleteDisplayText && !dialog.HasPendingLines && !dialog.ConsumedClickThisFrame));
    }

    private string GetEnemyTelegraphMessage()
    {
        if (!HasPlannedTelegraph())
        {
            return string.Empty;
        }

        string skillName = enemyActionPlan.skill.skillName;
        string announcementName = UsesBossHeavyAttack(enemy) ? skillName + "！" : skillName;
        if (enemyActionPlan.isInterrupted)
        {
            return $"{skillName}は中断済み！\n敵はこのターン行動しない。";
        }

        string announcement = UsesBossHeavyAttack(enemy) && enemyActionPlan.remainingChargeTurns > 1
            ? $"{enemy.name}は{skillName}を構えている！\nあと{enemyActionPlan.remainingChargeTurns - 1}回構えてから発動！"
            : $"次の敵行動：{announcementName}";
        bool hasHands = false;
        foreach (BodyPart part in enemy.GetComponentsInChildren<BodyPart>(true))
        {
            if (part.partType != PartType.Hand || !part.canBreak)
            {
                continue;
            }

            hasHands = true;
            if (!part.IsBroken)
            {
                return $"{announcement}\n壊れていない手を破壊すると中断！";
            }
        }

        return $"{announcement}\n" + (hasHands ? "両手は破壊済み。今回は妨害不可。" : "破壊できる手がなく、今回は妨害不可。");
    }

    private void ShowPlayerChoiceMessage(string message)
    {
        DialogTextManager.instance.SetScenarios(new[] { message });
        playerInputBlockedThroughFrame = Time.frameCount;
    }

    private bool UsesEnemyActionPlan(EnemyManager target)
    {
        return target != null && target.data != null && target.data.enemyType == EnemyType.Normal &&
            target.data.role == EnemyRole.Attacker && target.data.AnnouncementSkillName != null;
    }

    private void PrepareEnemyAction()
    {
        // 構え中の技を保持し、次のプレイヤーターンで同じ技を予告し直す
        if (enemyActionPlan.isReady && enemyActionPlan.enemy == enemy && enemyActionPlan.remainingChargeTurns > 0)
        {
            enemyActionPlan.isTelegraphed = false;
            return;
        }

        ClearEnemyActionPlan();
        if (UsesBossHeavyAttack(enemy))
        {
            SkillData heavyAttack = enemyAI.TakePendingBossHeavyAttack(enemy);
            if (heavyAttack != null)
            {
                StartEnemyCharge(heavyAttack, BossHeavyChargeTurns);
            }

            return;
        }

        if (!UsesEnemyActionPlan(enemy))
        {
            return;
        }

        enemyActionPlan = new EnemyActionPlan
        {
            enemy = enemy,
            // 敵のクールダウンが1ターン進んだ後に使用できる技を予告する
            skill = enemyAI.ChooseSkill(enemy, 1),
            isReady = true
        };
    }

    private bool HasPlannedTelegraph()
    {
        return enemyActionPlan.isReady && 
            enemyActionPlan.enemy == enemy && 
            enemy != null && 
            enemy.data != null &&
            enemyActionPlan.skill != null && 
            enemyActionPlan.skill == enemy.data.AnnouncementSkillName;
    }

    private bool TryInterruptEnemyAction(BodyPart part, bool justBroken)
    {
        // 破壊済みの手を再攻撃しても妨害を繰り返せないようにする
        if (!justBroken || 
            part == null || 
            part.partType != PartType.Hand ||
            !(UsesEnemyActionPlan(enemy) || 
            (UsesChargedAttack(enemy) && 
            enemyActionPlan.isTelegraphed)) ||
            !HasPlannedTelegraph() || 
            !enemy.IsAlive || 
            enemyActionPlan.isInterrupted ||
            part.GetComponentInParent<EnemyManager>() != enemy)
        {
            return false;
        }

        enemyActionPlan.isInterrupted = true;
        return true;
    }

    private bool TryConsumeEnemyAction(out SkillData skill, out bool isInterrupted)
    {
        skill = null;
        isInterrupted = false;
        if (!enemyActionPlan.isReady || enemyActionPlan.enemy != enemy)
        {
            return false;
        }

        skill = enemyActionPlan.skill;
        isInterrupted = enemyActionPlan.isInterrupted;
        ClearEnemyActionPlan();
        return true;
    }

    private void ClearEnemyActionPlan()
    {
        enemyActionPlan = default;
    }

    private bool UsesBossHeavyAttack(EnemyManager target)
    {
        return target != null && 
            target.data != null && 
            target.data.enemyType == EnemyType.Boss &&
            target.data.role == EnemyRole.Tank && 
            target.data.AnnouncementSkillName != null &&
            target.data.AnnouncementSkillName.skillType == SkillType.Physical;
    }

    private bool UsesEliteCharge(EnemyManager target)
    {
        return target != null && 
            target.data != null && 
            target.data.enemyType == EnemyType.Reinforced &&
            target.data.role == EnemyRole.Attacker && 
            target.data.AnnouncementSkillName != null;
    }

    private bool UsesChargedAttack(EnemyManager target)
    {
        return UsesBossHeavyAttack(target) || UsesEliteCharge(target) || ExpansionEnemyAction.UsesCharge(target);
    }

    private void StartEnemyCharge(SkillData skill, int playerTurns)
    {
        enemyActionPlan = new EnemyActionPlan
        {
            enemy = enemy,
            skill = skill,
            isReady = true,
            remainingChargeTurns = playerTurns
        };
    }

    private void ClearPlayerSkillSelection()
    {
        selectedPlayerSkill = null;
        useSelectedSkill = false;
    }

    private void ResetActionState()
    {
        ClearEnemyActionPlan();
        ClearPlayerSkillSelection();
        enemyAI?.ResetBattleState();
        isShowingEnemyTelegraph = false;
        isPlayerTurn = false;
        waitingTap = false;
        playerActionMessages = null;
    }

    private void ResetPlayerBattleState()
    {
        if (player != null)
        {
            player.RemovePoison();
            player.ResetBattleState();
        }
    }

    private void ClearBattleEffects()
    {
        if (skillVfx != null)
        {
            skillVfx.Clear();
        }

        DOTween.Kill(this, true);
    }

    private void RemoveEnemy()
    {
        if (enemy != null)
        {
            enemy.ClearCounter();
            DOTween.Kill(enemy.transform);
            Destroy(enemy.gameObject);
        }

        enemy = null;
    }

    // BattleManagerが無効になったときの後片付け
    private void OnDisable()
    {
        StopAllCoroutines();
        isBattleRunning = false;
        isEndingBattle = false;
        ResetActionState();
        ResetPlayerBattleState();
        ClearBattleEffects();
        RemoveEnemy();
    }

    #if UNITY_EDITOR
    public void DebugKillEnemy()
    {        
        if (!CanReceivePlayerInput() || !waitingTap)
        {
            return;
        }

        enemy.TakeDamageRaw(enemy.hp);
    }
    #endif

    private IEnumerator BattleLoop()
    {

        while (CombatantsAlive)
        {
            player.BattleEffects.BeginPlayerTurn();
            int playerActionCount = enemy.GetPlayerActionCount();
            SkillCooldowns playerCooldowns = player.GetComponent<SkillCooldowns>();
            if (playerCooldowns != null)
            {
                playerCooldowns.Tick();
            }

            PrepareEnemyAction();

            for (int i = 0; i < playerActionCount && CombatantsAlive; i++)
            {
                yield return StartCoroutine(PlayerActByTap(i == 0));

                if (CombatantsAlive)
                {
                    // 最初の行動で脚を破壊した場合は、このターンから追加行動を許可する。
                    playerActionCount = enemy.GetPlayerActionCount();
                }
            }

            if (!CombatantsAlive)
            {
                break;
            }

            player.BattleEffects.EndPlayerTurn();
            enemy.BattleEffects.EndPlayerTurn();
            enemy.ClearCounter();
            yield return new WaitForSeconds(battlePauseDuration);
            yield return StartCoroutine(EnemyActCore());
            if (player != null)
            {
                player.BattleEffects.EndEnemyAction();
            }

            if (CombatantsAlive)
            {
                yield return new WaitForSeconds(battlePauseDuration);
            }
        }

        BattleResult result = player != null && player.IsAlive 
            ? BattleResult.Victory 
            : BattleResult.Defeat;
        yield return FinishBattle(result);
    }

    IEnumerator PlayerActByTap(bool isFirstActionOfTurn = false)
    {
        EnemyManager battleEnemy = enemy;
        isPlayerTurn = false;
        waitingTap = true;
        playerActionMessages = null;
        // 脚破壊で増えた行動は同じターンなので、毒は最初の行動だけで進める
        int poisonDamage = isFirstActionOfTurn ? player.TickPoisonDamage() : 0;
        if (poisonDamage > 0)
        {
            playerUI.UpdateUI(player);
            var poisonMessages = new List<string>
            {
                $"毒ダメージ！\n{poisonDamage}ダメージ受けた！"
            };
            if (player.IsAlive && !player.IsPoisoned)
            {
                poisonMessages.Add("毒が自然に解除された！");
            }

            yield return StartCoroutine(DialogTextManager.instance.ShowAndWait(poisonMessages.ToArray(), dialogPauseDuration));

            if (!CombatantsAlive)
            {
                yield break;
            }
        }

        if (!isBattleRunning || isEndingBattle || enemy != battleEnemy)
        {
            yield break;
        }

        if (isFirstActionOfTurn && HasPlannedTelegraph())
        {
            isShowingEnemyTelegraph = true;
            skillSelectionPanel.SetActive(false);
            yield return StartCoroutine(DialogTextManager.instance.ShowAndWait(GetEnemyTelegraphMessage(), dialogPauseDuration));
            yield return null;
            if (enemy != battleEnemy)
            {
                yield break;
            }

            isShowingEnemyTelegraph = false;
            if (!isBattleRunning || isEndingBattle || player == null || !player.IsAlive || enemy == null || !enemy.IsAlive)
            {
                ClearEnemyActionPlan();
                yield break;
            }

            enemyActionPlan.isTelegraphed = true;
        }
        else
        {
            ShowPlayerChoiceMessage("攻撃する部位を選択！");
        }

        isPlayerTurn = true;

        while (waitingTap && CombatantsAlive)
        {
            yield return null;
        }

        isPlayerTurn = false;
        if (!isBattleRunning || isEndingBattle || enemy != battleEnemy)
        {
            yield break;
        }

        string[] messages = playerActionMessages;
        playerActionMessages = null;
        if (messages != null)
        {
            yield return StartCoroutine(DialogTextManager.instance.ShowAndWait(messages, dialogPauseDuration));
        }

        if (!isBattleRunning || isEndingBattle || enemy != battleEnemy)
        {
            yield break;
        }

        if (player != null && player.IsAlive && enemy != null)
        {
            enemyUI.UpdateUI(enemy);
        }
    }

    IEnumerator EnemyActCore()
    {
        if (!CombatantsAlive)
        {
            yield break;
        }

        EnemyManager actingEnemy = enemy;

        enemy.TickDefenseBuff();
        enemy.TickMagicDefenseBuff();
        enemy.TickFrozen();
        SkillCooldowns cooldowns = enemy.GetComponent<SkillCooldowns>();
        if (cooldowns != null)
        {
            cooldowns.Tick();
        }

        enemyUI.UpdateUI(enemy);
        int burnDmg = enemy.TickBurnDamage();
        if (burnDmg > 0)
        {
            if (!CombatantsAlive)
            {
                yield break;
            }

            enemyUI.UpdateUI(enemy);
            yield return StartCoroutine(DialogTextManager.instance.ShowAndWait($"火傷ダメージ！\n敵は{burnDmg}ダメージ受けた", dialogPauseDuration));
        }

        yield return new WaitForSeconds(battlePauseDuration);
        if (!CombatantsAlive || enemy != actingEnemy || !isBattleRunning || isEndingBattle)
        {
            yield break;
        }

        bool isChargedAction = UsesChargedAttack(enemy) && enemyActionPlan.isReady && enemyActionPlan.enemy == enemy &&
            enemyActionPlan.remainingChargeTurns > 0 && enemyActionPlan.skill == enemy.data.AnnouncementSkillName;
        bool isBossHeavyAction = isChargedAction && UsesBossHeavyAttack(enemy);
        if (isChargedAction && !enemyActionPlan.isTelegraphed)
        {
            ClearEnemyActionPlan();
            Debug.LogWarning("予告されていない構え攻撃は実行しません。", this);
            yield break;
        }

        if (isChargedAction && !enemyActionPlan.isInterrupted &&
            !(isBossHeavyAction && TankBossRoleAction.GetSealedSkills(enemy).Contains(enemyActionPlan.skill)) &&
            enemyActionPlan.remainingChargeTurns > 1)
        {
            enemyActionPlan.remainingChargeTurns--;
            enemyActionPlan.isTelegraphed = false;
            yield return StartCoroutine(DialogTextManager.instance.ShowAndWait($"{enemy.name}は強力な一撃の構えを続けている！", dialogPauseDuration));
            yield break;
        }

        SkillData skill;
        if (!TryConsumeEnemyAction(out skill, out bool isInterrupted))
        {
            if (UsesEnemyActionPlan(enemy))
            {
                Debug.LogWarning("確定済みの敵行動がないため、敵の行動を終了します。", this);
                yield break;
            }

            skill = enemyAI.ChooseSkill(enemy, 0, player, UsesBossHeavyAttack(enemy));
            if ((UsesEliteCharge(enemy) || ExpansionEnemyAction.UsesCharge(enemy)) && skill != null &&
                skill == enemy.data.AnnouncementSkillName)
            {
                StartEnemyCharge(skill, SingleTurnCharge);
            }

            if ((UsesBossHeavyAttack(enemy) && enemyAI.PendingBossHeavyAttack != null) ||
                ((UsesEliteCharge(enemy) || ExpansionEnemyAction.UsesCharge(enemy)) && enemyActionPlan.isReady))
            {
                yield return StartCoroutine(DialogTextManager.instance.ShowAndWait($"{enemy.name}は強力な一撃を構えた！", dialogPauseDuration));
                yield break;
            }
        }

        // 予告後の部位破壊で予約技が封印された場合は、敵の行動ごと中断する
        if (isBossHeavyAction && TankBossRoleAction.GetSealedSkills(enemy).Contains(skill))
        {
            isInterrupted = true;
        }

        if (ExpansionEnemyAction.IsSealed(enemy, skill))
        {
            isInterrupted = true;
        }

        if (isInterrupted)
        {
            yield return StartCoroutine(DialogTextManager.instance.ShowAndWait(
                $"{skill.skillName}は中断された！\n{enemy.name}は行動できない！",
                dialogPauseDuration));
            yield break;
        }

        if (skill == null)
        {
            float physicalMultiplier = enemy.BattleEffects.ConsumePhysicalMultiplier();
            float finalAcc = enemyBaseAccuracy - enemy.GetAccuracyPenalty();
            finalAcc = Mathf.Clamp01(finalAcc);
            bool hit = DamageRule.RollHit(finalAcc, 0f);
            if (!hit)
            {
                yield return StartCoroutine(DialogTextManager.instance.ShowAndWait("敵の攻撃！\nしかし外れた！", dialogPauseDuration));
                yield break;
            }

            SoundManager.instance.PlayButtonSE(1);
            ShakeScreen();
            int normalDamage = DamageRule.CalcNormalAttack(enemy.at, player.def);
            normalDamage = Mathf.RoundToInt(normalDamage * physicalMultiplier);
            int dmg = player.TakeDirectDamage(normalDamage);
            playerUI.UpdateUI(player);
            yield return StartCoroutine(DialogTextManager.instance.ShowAndWait($"敵の攻撃！\nプレイヤーは{dmg}ダメージ受けた", dialogPauseDuration));
            yield break;
        }

        UnitBase target;
        if (skill.targetType == TargetType.Self)
        {
            target = enemy;
        }
        else
        {
            target = player;
        }

        var result = SkillExecutor.Execute(enemy, target, skill, 0f);
        if (result.executed && result.hit)
        {
            PlaySkillEffect(
                skill,
                target == enemy ? enemy.transform.position : playerUI.GetSkillEffectPosition(mainCamera));
        }

        enemyUI.UpdateUI(enemy);
        if (result.hit && skill.targetType != TargetType.Self)
        {
            if (skill.se == null)
            {
                SoundManager.instance.PlayButtonSE(1);
            }

            ShakeScreen();
        }

        playerUI.UpdateUI(player);
        yield return StartCoroutine(DialogTextManager.instance.ShowAndWait(result.message, dialogPauseDuration));
    }

    private void ShakeScreen()
    {
        screenShakeTarget.DOShakePosition(
            screenShakeDuration,
            screenShakeStrength,
            screenShakeVibrato,
            0,
            false,
            true).SetId(this);
    }

    private IEnumerator FinishBattle(BattleResult result)
    {
        if (!isBattleRunning || isEndingBattle)
        {
            yield break;
        }

        isEndingBattle = true;
        isBattleRunning = false;
        string message = result == BattleResult.Victory
            ? $"{(enemy != null ? enemy.name : "敵")}を倒した"
            : "プレイヤーは倒れた。";

        ResetActionState();
        ResetPlayerBattleState();
        ClearBattleEffects();
        HideBattleUI();
        RemoveEnemy();

        if (player != null)
        {
            playerUI.UpdateUI(player);
        }

        DialogTextManager.instance.SetScenarios(new[] { message });

        // 破棄の反映を待ち、購読側が次の戦闘を始められる状態で通知する。
        yield return null;
        isEndingBattle = false;
        BattleFinished?.Invoke(result);
    }
}
