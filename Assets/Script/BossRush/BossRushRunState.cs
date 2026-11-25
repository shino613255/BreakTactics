using System;
using System.Collections.Generic;

internal sealed class BossRushRunState
{
    public const int ClassSkillCount = 2;
    public const int CommonSkillCount = 3;
    public const int TotalSkills = ClassSkillCount + CommonSkillCount;
    private const int MaxClassChanges = 1;

    public PlayerData CurrentPlayer { get; private set; }
    public IReadOnlyList<SkillData> SelectedSkills { get; private set; } = Array.Empty<SkillData>();
    public int ClassChangesUsed { get; private set; }
    public int RemainingClassChanges => MaxClassChanges - ClassChangesUsed;
    public bool IsInBattle { get; private set; }

    public bool CanChooseClass(PlayerData player)
    {
        return !IsInBattle && 
            player != null &&
            (CurrentPlayer == null || 
            CurrentPlayer.role == player.role || 
            RemainingClassChanges > 0);
    }

    public bool ValidateLoadout(
        PlayerData player, 
        IReadOnlyList<SkillData> classSkills,
        IReadOnlyList<SkillData> commonSkills, 
        IReadOnlyList<SkillData> classCandidates,
        IReadOnlyList<SkillData> commonCandidates, 
        out string error)
    {
        if (IsInBattle)
        {
            error = "戦闘中は職業と編成を変更できません。";
            return false;
        }

        if (player == null)
        {
            error = "職業を選んでください。";
            return false;
        }

        if (!CanChooseClass(player))
        {
            error = "このプレイでは、すでに職業を1回変更しています。";
            return false;
        }

        if (classSkills == null || 
            commonSkills == null ||
            classSkills.Count != ClassSkillCount || 
            commonSkills.Count != CommonSkillCount)
        {
            error = $"固有スキルを{ClassSkillCount}つ、自由スキルを{CommonSkillCount}つ選んでください。";
            return false;
        }

        var equipped = new SkillData[TotalSkills];
        var skillIds = new HashSet<SkillId>();
        for (int i = 0; i < ClassSkillCount; i++)
        {
            SkillData classSkill = classSkills[i];

            if (!SkillUseRules.IsClassSkill(classSkill, player.role) || 
                !IsSkillRegistered(classSkill, classCandidates))
            {
                error = "固有枠には、選んだ職業に登録されているスキルを指定してください。";
                return false;
            }

            if (!skillIds.Add(classSkill.id))
            {
                error = "同じスキルを複数の枠に編成することはできません。";
                return false;
            }

            equipped[i] = classSkill;
        }

        for (int i = 0; i < CommonSkillCount; i++)
        {
            SkillData commonSkill = commonSkills[i];

            if (!SkillUseRules.IsCommonSkill(commonSkill) ||
                !IsSkillRegistered(commonSkill, commonCandidates))
            {
                error = "自由枠には、登録されている共通スキルを指定してください。";
                return false;
            }

            if (!skillIds.Add(commonSkill.id))
            {
                error = "同じスキルを複数の枠に編成することはできません。";
                return false;
            }

            // 戦闘のスキル欄でも固有枠が先に並ぶよう、自由枠はその後ろに詰める
            equipped[i + ClassSkillCount] = commonSkill;
        }

        // 編成の検証をすべて通過してから、職業変更回数と編成を確定する
        if (CurrentPlayer != null && CurrentPlayer.role != player.role)
        {
            ClassChangesUsed++;
        }

        CurrentPlayer = player;
        SelectedSkills = Array.AsReadOnly(equipped);
        IsInBattle = true;
        error = string.Empty;
        return true;
    }

    public void FinishBattle()
    {
        IsInBattle = false;
    }

    private static bool IsSkillRegistered(
        SkillData skill, 
        IReadOnlyList<SkillData> candidates)
    {
        foreach (SkillData candidate in candidates)
        {
            if (candidate == skill)
            {
                return true;
            }
        }

        return false;
    }
}
