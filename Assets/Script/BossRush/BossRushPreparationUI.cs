
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class BossRushPreparationUI : MonoBehaviour
{
    [Serializable]
    private sealed class RoleCard
    {
        public Button button;
        public Image portrait;
        public TMP_Text title;
        public TMP_Text stats;
    }

    [Serializable]
    private sealed class SkillCard
    {
        public Button button;
        public TMP_Text title;
        public TMP_Text description;
        public TMP_Text cost;
    }

    [SerializeField] private BossRushBattleController controller;

    [SerializeField] private RoleCard[] roleCards;
    [SerializeField] private SkillCard[] classCards;
    [SerializeField] private SkillCard[] commonCards;

    // Inspectorには固有枠、自由枠の順で表示欄を登録する
    [SerializeField] private TMP_Text[] selectedSlots;
    [SerializeField] private TMP_Text selectedRole;
    [SerializeField] private TMP_Text remainingChanges;
    [SerializeField] private TMP_Text message;

    [SerializeField] private Button confirm;

    [SerializeField] private Color selectedColor;
    [SerializeField] private Color cardColor;


    private readonly List<SkillData> classSelection = new List<SkillData>();
    private readonly List<SkillData> commonSelection = new List<SkillData>();

    private PlayerData draftPlayer;


    private void Awake()
    {
        for (int i = 0; i < roleCards.Length; i++)
        {
            int index = i;

            roleCards[i].button.onClick.AddListener(
                () => ChooseRole(index)
            );
        }

        BindSkillButtons(classCards, true);
        BindSkillButtons(commonCards, false);

        confirm.onClick.AddListener(Confirm);
    }

    internal void Show()
    {
        draftPlayer = controller.CurrentPlayer;

        classSelection.Clear();
        commonSelection.Clear();

        foreach (SkillData skill in controller.SelectedSkills)
        {
            if (SkillUseRules.IsCommonSkill(skill))
            {
                commonSelection.Add(skill);
            }
            else
            {
                classSelection.Add(skill);
            }
        }

        gameObject.SetActive(true);

        message.text = string.Empty;

        RefreshUI();
    }
    internal void Hide()
    {
        gameObject.SetActive(false);
    }
    private void BindSkillButtons(
        SkillCard[] cards,
        bool isClassSkill)
    {
        for (int i = 0; i < cards.Length; i++)
        {
            int index = i;

            cards[i].button.onClick.AddListener(
                () => ToggleSkill(index, isClassSkill)
            );
        }
    }
    private void ChooseRole(int index)
    {
        PlayerData player =
            controller.ClassOptions[index]?.Player;

        if (!controller.CanChooseClass(player))
        {
            message.text = "このプレイでは、すでに職業を1回変更しています。";
            return;
        }

        if (draftPlayer != player)
        {
            draftPlayer = player;

            // 自由枠は職業に依存しないので、職業を変えても残す
            classSelection.Clear();
        }

        message.text = string.Empty;

        RefreshUI();
    }
    private void ToggleSkill(
        int index,
        bool isClassSkill)
    {
        if (draftPlayer == null)
        {
            message.text = "先に職業を選んでください。";
            return;
        }

        IReadOnlyList<SkillData> candidates = isClassSkill
            ? controller.GetClassCandidates(draftPlayer)
            : controller.CommonSkills;

        List<SkillData> selection = isClassSkill
            ? classSelection
            : commonSelection;

        SkillData skill = candidates[index];

        if (!selection.Remove(skill))
        {
            int maxCount = isClassSkill
                ? BossRushRunState.ClassSkillCount
                : BossRushRunState.CommonSkillCount;

            if (selection.Count >= maxCount)
            {
                message.text = $"この枠は{maxCount}つまでです。選択済みのスキルを押すと外せます。";
                return;
            }

            selection.Add(skill);
        }

        message.text = string.Empty;

        RefreshUI();
    }
    private void Confirm()
    {
        if (!controller.TryConfirmAndStart(
            draftPlayer,
            classSelection,
            commonSelection,
            out string error))
        {
            message.text = error;
        }
    }
    private void RefreshUI()
    {
        selectedRole.text = draftPlayer != null
            ? draftPlayer.playerName
            : "職業未選択";

        remainingChanges.text =
            $"職業変更  残り{controller.RemainingClassChanges}回";


        for (int i = 0; i < roleCards.Length; i++)
        {
            RoleCard card = roleCards[i];

            PlayerData player = i < controller.ClassOptions.Count
                ? controller.ClassOptions[i]?.Player
                : null;

            card.button.gameObject.SetActive(player != null);

            if (player == null)
            {
                continue;
            }

            card.title.text = player.playerName;

            card.stats.text =
                $"HP {player.startMaxHp}   MP {player.startMaxMp}";

            card.portrait.sprite = player.portrait;

            card.portrait.enabled = player.portrait != null;

            card.button.interactable =
                controller.CanChooseClass(player);

            SetSelected(card.button, player == draftPlayer);
        }


        RefreshSkillCards(
            classCards,
            controller.GetClassCandidates(draftPlayer),
            classSelection
        );

        RefreshSkillCards(
            commonCards,
            controller.CommonSkills,
            commonSelection
        );


        for (int i = 0; i < BossRushRunState.ClassSkillCount; i++)
        {
            selectedSlots[i].text = i < classSelection.Count
                ? classSelection[i].skillName
                : "未選択";           
        }

        for (int i = 0; i < BossRushRunState.CommonSkillCount; i++)
        {
            selectedSlots[i + BossRushRunState.ClassSkillCount].text =
                i < commonSelection.Count
                    ? commonSelection[i].skillName
                    : "未選択";
        }        
    }
    private void RefreshSkillCards(
        SkillCard[] cards,
        IReadOnlyList<SkillData> candidates,
        List<SkillData> selection)
    {
        for (int i = 0; i < cards.Length; i++)
        {
            SkillCard card = cards[i];

            SkillData skill = i < candidates.Count
                ? candidates[i]
                : null;

            card.button.gameObject.SetActive(skill != null);

            if (skill == null)
            {
                continue;
            }

            card.button.interactable = draftPlayer != null;

            card.title.text = skill.skillName;
            card.description.text = skill.description;

            card.cost.text =
                $"MP {skill.mpCost}   CD {skill.cooldown}";

            SetSelected(card.button, selection.Contains(skill));
        }
    }
    private void SetSelected(
        Button button,
        bool selected)
    {
        ColorBlock colors = button.colors;

        Color normal = selected
            ? selectedColor
            : cardColor;

        colors.normalColor = normal;

        colors.highlightedColor =
            Color.Lerp(normal, Color.white, 0.14f);

        colors.pressedColor =
            Color.Lerp(normal, Color.black, 0.13f);

        colors.selectedColor = normal;

        button.colors = colors;
    }
}