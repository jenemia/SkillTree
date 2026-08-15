using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SkillTree.Authoring.Runtime
{
    /// <summary>패시브 스킬 노드의 상태를 전용 GameObject 하나로만 표시합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class UISkillTreeRuntimeNodeView : SkillTreeRuntimeNodeView
    {
        private enum VisualState
        {
            Activated,
            Selected,
            Locked,
            Available,
            Unavailable,
            Deleted
        }

        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text costText;
        [Header("State Objects")]
        [SerializeField] private GameObject activatedState;
        [SerializeField] private GameObject selectedState;
        [SerializeField] private GameObject lockedState;
        [SerializeField] private GameObject availableState;
        [SerializeField] private GameObject unavailableState;
        [SerializeField] private GameObject deletedState;

        private VisualState _baseVisualState = VisualState.Available;

        public string DisplayName => nameText == null ? string.Empty : nameText.text;
        public string CostLabel => costText == null ? string.Empty : costText.text;
        public Sprite IconSprite => iconImage == null ? null : iconImage.sprite;

        /// <summary>정적 스킬 정보를 텍스트와 아이콘에 반영합니다.</summary>
        protected override void OnDefinitionBound(string nodeId, SkillDefinition definition)
        {
            // 정의 바인딩 직후에는 아직 진행도 정보가 없으므로 사용 가능 상태로 표시한다.
            if (nameText != null)
            {
                nameText.text = string.IsNullOrWhiteSpace(definition?.displayName) ? nodeId : definition.displayName;
            }

            if (costText != null)
            {
                costText.text = $"{Mathf.Max(0, definition?.cost ?? 0)}";
            }

            if (iconImage != null)
            {
                iconImage.sprite = definition?.icon;
                iconImage.enabled = definition?.icon != null;
            }

            _baseVisualState = VisualState.Available;
            RefreshVisualState();
        }

        /// <summary>유저 진행도에 맞는 단일 상태 오브젝트를 선택합니다.</summary>
        protected override void OnStatusBound(UserSkillData userSkill, SkillStatusData status)
        {
            // 진행 상태는 색상 변경 대신 전용 상태 오브젝트의 활성화로 표시한다.
            if (costText != null)
            {
                costText.text = BuildStatusLabel(userSkill, status);
            }

            _baseVisualState = ResolveBaseVisualState(status);
            RefreshVisualState();
        }

        /// <summary>선택 상태를 최우선 시각 상태로 반영합니다.</summary>
        protected override void OnSelectionChanged(bool isSelected)
        {
            // 선택 상태가 해제되면 마지막 진행 상태 표현으로 즉시 되돌린다.
            RefreshVisualState();
        }

        /// <summary>삭제 레이어에서 복구된 노드를 기본 상태로 되돌립니다.</summary>
        protected override void OnMarkedActive(string nodeId)
        {
            // 복구 시 상태 데이터가 다시 바인딩되기 전까지 사용 가능 상태를 표시한다.
            _baseVisualState = VisualState.Available;
            RefreshVisualState();
        }

        /// <summary>그래프에서 삭제된 노드를 삭제 전용 오브젝트로 표시합니다.</summary>
        protected override void OnMarkedDeleted(string nodeId)
        {
            // 삭제된 노드도 다른 상태와 중첩되지 않도록 삭제 오브젝트만 켠다.
            SetVisualState(VisualState.Deleted);
        }

        private static VisualState ResolveBaseVisualState(SkillStatusData status)
        {
            if (status == null)
            {
                return VisualState.Available;
            }

            if (status.isLocked || status.progressState == SkillNodeProgressState.Locked)
            {
                return VisualState.Locked;
            }

            if (status.isMaxed || status.progressState == SkillNodeProgressState.Purchased)
            {
                return VisualState.Activated;
            }

            return status.isAffordable ? VisualState.Available : VisualState.Unavailable;
        }

        private void RefreshVisualState()
        {
            // 선택 표현은 진행 상태보다 우선하지만, 항상 단일 오브젝트만 활성화한다.
            SetVisualState(IsSelected ? VisualState.Selected : _baseVisualState);
        }

        private void SetVisualState(VisualState visualState)
        {
            // 모든 상태 오브젝트를 먼저 끈 뒤 선택된 상태 하나만 켠다.
            SetActive(activatedState, visualState == VisualState.Activated);
            SetActive(selectedState, visualState == VisualState.Selected);
            SetActive(lockedState, visualState == VisualState.Locked);
            SetActive(availableState, visualState == VisualState.Available);
            SetActive(unavailableState, visualState == VisualState.Unavailable);
            SetActive(deletedState, visualState == VisualState.Deleted);
        }

        private static void SetActive(GameObject target, bool isActive)
        {
            if (target != null)
            {
                target.SetActive(isActive);
            }
        }

        private static string BuildStatusLabel(UserSkillData userSkill, SkillStatusData status)
        {
            if (status == null)
            {
                return $"{Mathf.Max(0, userSkill?.definition?.cost ?? 0)}";
            }

            if (status.progressState == SkillNodeProgressState.Locked)
            {
                return string.IsNullOrWhiteSpace(status.prerequisiteSummary) ? "Locked" : status.prerequisiteSummary;
            }

            if (status.progressState == SkillNodeProgressState.Maxed)
            {
                return $"Lv {status.currentLevel}/{status.maxLevel} · Max";
            }

            if (!status.isAffordable)
            {
                return $"Lv {status.currentLevel}/{status.maxLevel} · {status.affordabilitySummary}";
            }

            var stateLabel = status.progressState == SkillNodeProgressState.Purchased ? "Purchased" : "Open";
            return $"{stateLabel} · Lv {status.currentLevel}/{status.maxLevel} · Cost {status.cost}";
        }
    }
}
