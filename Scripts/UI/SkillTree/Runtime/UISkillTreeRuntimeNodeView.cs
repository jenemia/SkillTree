using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkillTree.Authoring.Runtime
{
    /// <summary>패시브 스킬 노드의 상태와 선택 UI를 별도 오브젝트로 표시합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class UISkillTreeRuntimeNodeView : SkillTreeRuntimeNodeView
    {
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text costText;
        [SerializeField] private TMP_Text levelText;
        [Header("Skill State Objects")]
        [SerializeField] private GameObject lockedState;
        [SerializeField] private GameObject activeState;
        [Header("UI State Objects")]
        [SerializeField] private GameObject selectedState;

        public string DisplayName => nameText == null ? string.Empty : nameText.text;
        public string CostLabel => costText == null ? string.Empty : costText.text;
        public string LevelLabel => levelText == null ? string.Empty : levelText.text;
        public Sprite IconSprite => iconImage == null ? null : iconImage.sprite;

        /// <summary>정적 스킬 정보를 아이콘과 기본 레이블에 반영합니다.</summary>
        protected override void OnDefinitionBound(string nodeId, SkillDefinition definition)
        {
            // 정의 바인딩 시에는 활성 상태와 기본 비용·레벨 정보를 표시한다.
            if (nameText != null)
            {
                nameText.text = string.IsNullOrWhiteSpace(definition?.displayName) ? nodeId : definition.displayName;
            }

            if (costText != null)
            {
                costText.text = $"{Mathf.Max(0, definition?.cost ?? 0)}";
            }

            if (levelText != null)
            {
                levelText.text = $"Level 0/{Mathf.Max(0, definition?.maxLevel ?? 0)}";
            }

            if (iconImage != null)
            {
                iconImage.sprite = definition?.icon;
                iconImage.enabled = definition?.icon != null;
            }

            SetSkillState(false);
        }

        /// <summary>잠김/활성 상태와 비용·레벨 레이블을 갱신합니다.</summary>
        protected override void OnStatusBound(UserSkillData userSkill, SkillStatusData status)
        {
            // 스킬 상태는 잠김 또는 활성 오브젝트 중 하나만 켜서 표시한다.
            var isLocked = status?.isLocked ?? false;
            SetSkillState(isLocked);
            UpdateProgressLabels(status);
        }

        /// <summary>선택 UI는 선택 중일 때만 활성화합니다.</summary>
        protected override void OnSelectionChanged(bool isSelected)
        {
            // 미선택은 기본 상태이므로 선택 오브젝트만 끈다.
            if (selectedState != null)
            {
                selectedState.SetActive(isSelected);
            }
        }

        /// <summary>삭제 레이어에서 복구된 노드를 활성 상태로 표시합니다.</summary>
        protected override void OnMarkedActive(string nodeId)
        {
            // 복구 직후에는 최신 진행도 반영 전까지 활성 상태를 사용한다.
            SetSkillState(false);
        }

        /// <summary>삭제된 노드는 선택을 해제하고 잠김 상태로 표시합니다.</summary>
        protected override void OnMarkedDeleted(string nodeId)
        {
            // 삭제 노드는 상호작용할 수 없으므로 잠김 상태와 미선택 UI를 적용한다.
            SetSkillState(true);
            OnSelectionChanged(false);
        }

        private void SetSkillState(bool isLocked)
        {
            // 스킬 상태 오브젝트는 잠김 또는 활성 중 정확히 하나만 활성화한다.
            if (lockedState != null)
            {
                lockedState.SetActive(isLocked);
            }

            if (activeState != null)
            {
                activeState.SetActive(!isLocked);
            }
        }

        private void UpdateProgressLabels(SkillStatusData status)
        {
            // 다음 레벨이 남아 있을 때만 필요한 재화를 표시한다.
            var hasNextLevel = status != null && status.currentLevel < status.maxLevel;
            if (costText != null)
            {
                costText.gameObject.SetActive(hasNextLevel);
                costText.text = hasNextLevel ? Mathf.Max(0, status.cost).ToString() : string.Empty;
            }

            if (levelText != null)
            {
                levelText.text = status == null ? string.Empty : $"{status.currentLevel}/{status.maxLevel}";
            }
        }
    }
}
