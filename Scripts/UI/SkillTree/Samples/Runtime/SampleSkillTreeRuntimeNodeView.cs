using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using SkillTree.Authoring.Runtime;

namespace SkillTree.Authoring.Samples
{
    public sealed class SampleSkillTreeRuntimeNodeView : SkillTreeRuntimeNodeView
    {
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text costText;
        [SerializeField] private Graphic selectedHighlight;
        [SerializeField] private Color normalBackgroundColor = new(0.16f, 0.18f, 0.22f, 0.96f);
        [SerializeField] private Color selectedBackgroundColor = new(0.2f, 0.31f, 0.42f, 0.98f);
        [SerializeField] private Color lockedBackgroundColor = new(0.11f, 0.11f, 0.13f, 0.96f);
        [SerializeField] private Color availableBackgroundColor = new(0.16f, 0.25f, 0.2f, 0.96f);
        [SerializeField] private Color maxedBackgroundColor = new(0.2f, 0.24f, 0.13f, 0.96f);

        private bool _isLockedVisual;
        private bool _isMaxedVisual;
        private bool _isAffordableVisual = true;
        private bool _hasAppliedBackgroundColor;
        private Color _lastAppliedBackgroundColor;

        public string DisplayName => nameText == null ? string.Empty : nameText.text;
        public string CostLabel => costText == null ? string.Empty : costText.text;
        public Sprite IconSprite => iconImage == null ? null : iconImage.sprite;

        /// <summary>
        /// 정적 스킬 정의를 샘플 UGUI 텍스트와 아이콘에 반영한다.
        /// </summary>
        protected override void OnDefinitionBound(string nodeId, SkillDefinition definition)
        {
            // 정적 정의를 샘플 UGUI 구성 요소에 반영한다.
            if (nameText != null)
            {
                nameText.text = string.IsNullOrWhiteSpace(definition?.displayName)
                    ? nodeId
                    : definition.displayName;
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

            _isLockedVisual = false;
            _isMaxedVisual = false;
            _isAffordableVisual = true;
            UpdatePresentationColors();
        }

        /// <summary>
        /// 유저 진행 상태를 샘플 비용/레벨 라벨과 배경 상태로 변환한다.
        /// </summary>
        protected override void OnStatusBound(UserSkillData userSkill, SkillStatusData status)
        {
            // 계산된 진행 상태를 샘플 레이블과 배경 상태로 변환한다.
            if (costText != null)
            {
                costText.text = BuildStatusLabel(userSkill, status);
            }

            _isLockedVisual = status?.isLocked ?? false;
            _isMaxedVisual = status?.isMaxed ?? false;
            _isAffordableVisual = status?.isAffordable ?? true;
            UpdatePresentationColors();
        }

        /// <summary>
        /// 선택 여부를 샘플 하이라이트와 배경색에 반영한다.
        /// </summary>
        protected override void OnSelectionChanged(bool isSelected)
        {
            // 선택 표시는 하이라이트와 배경색을 함께 갱신한다.
            if (selectedHighlight != null)
            {
                selectedHighlight.enabled = isSelected;
            }

            UpdatePresentationColors();
        }

        /// <summary>
        /// 활성 노드로 복구될 때 샘플 상태 색상 플래그를 기본값으로 되돌린다.
        /// </summary>
        protected override void OnMarkedActive(string nodeId)
        {
            // 활성 복구 시 샘플 상태 색상을 기본값으로 되돌린다.
            _isLockedVisual = false;
            _isMaxedVisual = false;
            _isAffordableVisual = true;
            UpdatePresentationColors();
        }

        /// <summary>
        /// 삭제된 노드임을 샘플 이름 라벨에 표시한다.
        /// </summary>
        protected override void OnMarkedDeleted(string nodeId)
        {
            // 삭제된 노드는 기존 라벨을 유지하면서 편집기에서 식별 가능하게 prefix를 붙인다.
            if (nameText != null &&
                !string.IsNullOrWhiteSpace(nameText.text) &&
                !nameText.text.StartsWith(DeletedPrefix, StringComparison.Ordinal))
            {
                nameText.text = $"{DeletedPrefix}{nameText.text}";
            }
            else if (nameText != null && string.IsNullOrWhiteSpace(nameText.text))
            {
                nameText.text = $"{DeletedPrefix}{nodeId}";
            }
        }

        /// <summary>
        /// 삭제 상태에서 복구될 때 샘플 이름 라벨의 삭제 prefix를 제거한다.
        /// </summary>
        protected override void OnRestoredFromDeleted(string nodeId)
        {
            // 삭제 표시에서 되살아난 노드는 샘플 라벨 prefix를 제거한다.
            if (nameText != null && nameText.text.StartsWith(DeletedPrefix, StringComparison.Ordinal))
            {
                nameText.text = nameText.text.Substring(DeletedPrefix.Length);
            }
        }

        private void UpdatePresentationColors()
        {
            // 상태 조합에 따라 최소한의 배경 피드백을 제공한다.
            if (backgroundImage == null)
            {
                return;
            }

            if (HasExternalBackgroundCustomization())
            {
                return;
            }

            if (IsSelected)
            {
                ApplyBackgroundColor(selectedBackgroundColor);
                return;
            }

            if (_isLockedVisual)
            {
                ApplyBackgroundColor(lockedBackgroundColor);
                return;
            }

            if (_isMaxedVisual)
            {
                ApplyBackgroundColor(maxedBackgroundColor);
                return;
            }

            ApplyBackgroundColor(_isAffordableVisual ? availableBackgroundColor : normalBackgroundColor);
        }

        private static string BuildStatusLabel(UserSkillData userSkill, SkillStatusData status)
        {
            // 현재 요구사항에 맞는 최소 상태 텍스트를 조합한다.
            if (status == null)
            {
                return $"{Mathf.Max(0, userSkill?.definition?.cost ?? 0)}";
            }

            if (status.progressState == SkillNodeProgressState.Locked)
            {
                return string.IsNullOrWhiteSpace(status.prerequisiteSummary)
                    ? "Locked"
                    : status.prerequisiteSummary;
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

        private void ApplyBackgroundColor(Color color)
        {
            // 샘플이 적용한 색을 기억해 이후 사용자 커스터마이징과 구분한다.
            backgroundImage.color = color;
            _lastAppliedBackgroundColor = color;
            _hasAppliedBackgroundColor = true;
        }

        private bool HasExternalBackgroundCustomization()
        {
            // 사용자가 프리팹에서 직접 바꾼 배경색은 리빌드/리프레시 때 덮어쓰지 않는다.
            if (!_hasAppliedBackgroundColor)
            {
                return !IsSamplePaletteColor(backgroundImage.color);
            }

            return !ApproximatelySameColor(backgroundImage.color, _lastAppliedBackgroundColor) &&
                   !IsSamplePaletteColor(backgroundImage.color);
        }

        private bool IsSamplePaletteColor(Color color)
        {
            return ApproximatelySameColor(color, normalBackgroundColor) ||
                   ApproximatelySameColor(color, selectedBackgroundColor) ||
                   ApproximatelySameColor(color, lockedBackgroundColor) ||
                   ApproximatelySameColor(color, availableBackgroundColor) ||
                   ApproximatelySameColor(color, maxedBackgroundColor);
        }

        private static bool ApproximatelySameColor(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) <= 0.001f &&
                   Mathf.Abs(a.g - b.g) <= 0.001f &&
                   Mathf.Abs(a.b - b.b) <= 0.001f &&
                   Mathf.Abs(a.a - b.a) <= 0.001f;
        }
    }
}
