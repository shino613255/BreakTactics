using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PlayerManager : UnitBase
{
    [Header("Skill Panel")]
    [SerializeField] private SkillSlotUI[] skillSlots;
    [SerializeField] private UnityEngine.UI.Image portraitView;
    [SerializeField] private SkillPanelPager skillPager;

    public PlayerData Data { get; private set; }

    public PlayerRole Role => Data != null ? Data.role : PlayerRole.Mage;

    public PlayerBattleState BattleEffects { get; } = new PlayerBattleState();

    public IReadOnlyList<SkillData> Skills => currentSkills;

    public bool HasSkill(SkillData skill) => skill != null && currentSkills.Contains(skill);
    private IReadOnlyList<SkillData> currentSkills = Array.Empty<SkillData>();

    [Header("Poison")]
    private bool isPoisoned = false;
    private StatusEffectData poisonEffect;
    private int remainingPoisonTurns;

    public bool IsPoisoned => isPoisoned;

    internal void Setup(PlayerData data, IReadOnlyList<SkillData> equippedSkills)
    {
        ResetBattleState();

        var cooldowns = GetComponent<SkillCooldowns>();
        if (cooldowns == null)
        {
            cooldowns = gameObject.AddComponent<SkillCooldowns>();
        }
        cooldowns.Clear();

        if (data == null)
        {
            return;
        }

        Data = data;
        RemovePoison();
        if (portraitView != null)
        {
            portraitView.sprite = data.portrait;
            portraitView.enabled = data.portrait != null;
        }

        name = data.playerName;
        this.maxHp = data.startMaxHp;
        this.maxMp = data.startMaxMp;
        RestoreHpAndMp();
        this.at = data.startAt;
        this.mag = data.startMag;
        this.def = data.startDef;
        this.mdef = data.startMdef;
        // 追加
        currentSkills = equippedSkills;

        UpdateSkillPanel();
    }

    public void RestoreHpAndMp()
    {
        hp = maxHp;
        mp = maxMp;
    }

    public bool ApplyPoison(StatusEffectData effect)
    {
        if (effect == null)
        {
            Debug.LogWarning("PoisonのStatusEffectDataがnullです");
            return false;
        }

        if (effect.type != StatusEffectType.Poison)
        {
            return false;
        }

        if (isPoisoned)
        {
            return false;
        }

        isPoisoned = true;
        poisonEffect = effect;
        remainingPoisonTurns = Mathf.Max(1, effect.durationTurns);
        return true;
    }

    public int TickPoisonDamage()
    {
        if (!isPoisoned)
        {
            return 0;
        }

        if (poisonEffect == null)
        {
            return 0;
        }

        int damage = poisonEffect.CalcTickDamage(maxHp);
        hp = Mathf.Clamp(hp - damage, 0, maxHp);
        remainingPoisonTurns--;
        if (remainingPoisonTurns <= 0)
        {
            RemovePoison();
        }

        return damage;
    }

    public void RemovePoison()
    {
        isPoisoned = false;
        poisonEffect = null;
        remainingPoisonTurns = 0;
    }

    public void ResetBattleState()
    {
        BattleEffects.Reset();
    }

    public int GetSkillMpCost(SkillData skill) => BattleEffects.GetMpCost(skill != null ? skill.mpCost : 0);

    public int TakeDirectDamage(int damage, bool isMagic = false)
    {
        return base.TakeDamageRaw(BattleEffects.ReduceDirectDamage(damage), isMagic);
    }

    private void UpdateSkillPanel()
    {
        if (skillPager != null)
        {
            skillPager.Bind(this);
            return;
        }

        if (skillSlots == null)
        {
            return;
        }

        for (int i = 0; i < skillSlots.Length; i++)
        {
            if (i < currentSkills.Count)
            {
                skillSlots[i].SetSkill(currentSkills[i], this);
            }
            else
            {
                skillSlots[i].SetSkill(null, this);
            }
        }
    }
}
