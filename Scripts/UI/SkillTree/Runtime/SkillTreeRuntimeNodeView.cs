using System;
using UnityEngine;
using UnityEngine.UI;
using SkillTree.Authoring;

namespace SkillTree.Authoring.Runtime
{
    public enum SkillTreeRuntimeNodeSyncState
    {
        Active = 0,
        DeletedFromGraph = 1
    }

    public abstract class SkillTreeRuntimeNodeView : MonoBehaviour
    {
        protected const string DeletedPrefix = "[Deleted] ";
        private const string RuntimeNodeSuffix = "_RuntimeNode";

        [SerializeField] private Button clickButton;
        [SerializeField] private string serializedNodeId;
        [SerializeField] private SkillTreeRuntimeNodeSyncState syncState;

        private Action<string> _clickHandler;

        public string NodeId { get; private set; }
        public string SerializedNodeId => serializedNodeId;
        public SkillTreeRuntimeNodeSyncState SyncState => syncState;
        public bool IsDeletedFromGraph => syncState == SkillTreeRuntimeNodeSyncState.DeletedFromGraph;
        public bool IsSelected { get; private set; }
        public Button ClickButton => clickButton;
        public RectTransform RectTransform => transform as RectTransform;

        /// <summary>
        /// 컴포넌트가 활성화될 때 노드 ID를 복원하고 클릭 이벤트를 연결한다.
        /// </summary>
        protected virtual void OnEnable()
        {
            // 활성화 시 현재 직렬화 ID를 런타임 ID로 복원하고 버튼 이벤트를 연결한다.
            NodeId = serializedNodeId ?? string.Empty;
            RegisterClickListener();
        }

        /// <summary>
        /// 컴포넌트가 비활성화될 때 클릭 이벤트 연결을 해제한다.
        /// </summary>
        protected virtual void OnDisable()
        {
            // 비활성화 시 중복 이벤트 등록을 막기 위해 버튼 이벤트를 해제한다.
            UnregisterClickListener();
        }

        public void Bind(SkillTreeNodeRecord node, SkillNodeMetadata metadata, bool isSelected)
        {
            // 기존 메타데이터 바인딩 API도 새 정의 바인딩으로 흡수한다.
            var resolvedNodeId = node?.id ?? serializedNodeId ?? string.Empty;
            BindDefinition(resolvedNodeId, metadata?.ToSkillDefinition());
            ApplySelection(isSelected);
        }

        public void BindDefinition(string skillId, SkillDefinition definition)
        {
            // 정적 정의 데이터만 먼저 묶어 구조 빌드와 상태 리프레시를 분리한다.
            var wasDeleted = syncState == SkillTreeRuntimeNodeSyncState.DeletedFromGraph;
            var resolvedSkillId = string.IsNullOrWhiteSpace(skillId) ? serializedNodeId ?? string.Empty : skillId.Trim();
            SetSerializedNodeId(resolvedSkillId);
            syncState = SkillTreeRuntimeNodeSyncState.Active;
            UpdateRuntimeObjectName();

            if (wasDeleted)
            {
                OnRestoredFromDeleted(resolvedSkillId);
            }

            OnDefinitionBound(resolvedSkillId, definition);
            SetInteractionEnabled(true);
        }

        public void ApplyStatus(UserSkillData userSkill, SkillStatusData status, bool isSelected)
        {
            // 유저 진행 상태를 별도로 반영해 레벨/잠금/선택 표현을 갱신한다.
            BindDefinition(userSkill?.definition?.skillId ?? NodeId, userSkill?.definition);
            OnStatusBound(userSkill, status);
            SetInteractionEnabled(true);
            ApplySelection(isSelected);
        }

        public void MarkAsActive(string nodeId)
        {
            // 삭제 레이어에서 복구된 노드를 다시 활성 상태로 되돌린다.
            var wasDeleted = syncState == SkillTreeRuntimeNodeSyncState.DeletedFromGraph;
            SetSerializedNodeId(nodeId);
            syncState = SkillTreeRuntimeNodeSyncState.Active;
            UpdateRuntimeObjectName();

            if (wasDeleted)
            {
                OnRestoredFromDeleted(NodeId);
            }

            SetInteractionEnabled(true);
            ApplySelection(false);
            OnMarkedActive(NodeId);
        }

        public void MarkAsDeleted()
        {
            // 그래프에서 제거된 노드는 표시만 남기고 상호작용을 막는다.
            var resolvedNodeId = string.IsNullOrWhiteSpace(serializedNodeId) ? NodeId : serializedNodeId;
            SetSerializedNodeId(resolvedNodeId);
            syncState = SkillTreeRuntimeNodeSyncState.DeletedFromGraph;
            UpdateRuntimeObjectName();
            ApplySelection(false);
            SetInteractionEnabled(false);
            OnMarkedDeleted(resolvedNodeId);
        }

        public void ApplyLayout(Vector2 graphPosition)
        {
            // 그래프 좌표계를 UGUI 기준 좌상단 anchor 좌표로 변환한다.
            var rect = RectTransform;
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(graphPosition.x, -graphPosition.y);
        }

        public void SetClickHandler(Action<string> clickHandler)
        {
            // 버튼 이벤트는 항상 현재 컨트롤러 핸들러를 바라보게 유지한다.
            _clickHandler = clickHandler;
            RegisterClickListener();
        }

        public void ApplySelection(bool isSelected)
        {
            // 선택 상태는 기본 클래스가 보관하고 표현은 구현 클래스에 위임한다.
            IsSelected = isSelected;
            OnSelectionChanged(isSelected);
        }

        public bool TryRestoreSerializedNodeIdFromName()
        {
            if (!string.IsNullOrWhiteSpace(serializedNodeId))
            {
                NodeId = serializedNodeId;
                return true;
            }

            if (!TryResolveNodeIdFromObjectName(name, out var restoredNodeId))
            {
                return false;
            }

            SetSerializedNodeId(restoredNodeId);
            return true;
        }

        public static bool TryResolveNodeIdFromObjectName(string objectName, out string nodeId)
        {
            nodeId = null;
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return false;
            }

            var candidate = objectName.Trim();
            if (candidate.StartsWith(DeletedPrefix, StringComparison.Ordinal))
            {
                candidate = candidate.Substring(DeletedPrefix.Length);
            }

            if (!candidate.EndsWith(RuntimeNodeSuffix, StringComparison.Ordinal))
            {
                return false;
            }

            var resolvedNodeId = candidate.Substring(0, candidate.Length - RuntimeNodeSuffix.Length).Trim();
            if (string.IsNullOrWhiteSpace(resolvedNodeId))
            {
                return false;
            }

            nodeId = resolvedNodeId;
            return true;
        }

        /// <summary>
        /// 정적 스킬 정의가 바인딩되었을 때 프로젝트별 노드 표현을 갱신한다.
        /// </summary>
        protected abstract void OnDefinitionBound(string nodeId, SkillDefinition definition);

        /// <summary>
        /// 유저 진행 상태가 바인딩되었을 때 프로젝트별 상태 표현을 갱신한다.
        /// </summary>
        protected abstract void OnStatusBound(UserSkillData userSkill, SkillStatusData status);

        /// <summary>
        /// 선택 상태가 변경되었을 때 프로젝트별 선택 표현을 갱신한다.
        /// </summary>
        protected abstract void OnSelectionChanged(bool isSelected);

        /// <summary>
        /// 삭제 상태였던 노드가 다시 활성 노드로 표시될 때 필요한 표현을 갱신한다.
        /// </summary>
        protected virtual void OnMarkedActive(string nodeId)
        {
        }

        /// <summary>
        /// 그래프에서 삭제된 노드로 표시될 때 필요한 표현을 갱신한다.
        /// </summary>
        protected virtual void OnMarkedDeleted(string nodeId)
        {
        }

        /// <summary>
        /// 삭제 표시에서 복구될 때 남아 있는 삭제 전용 표현을 정리한다.
        /// </summary>
        protected virtual void OnRestoredFromDeleted(string nodeId)
        {
        }

        /// <summary>
        /// 버튼 상호작용 가능 여부가 변경될 때 프로젝트별 상호작용 표현을 갱신한다.
        /// </summary>
        protected virtual void OnInteractionEnabledChanged(bool isEnabled)
        {
        }

        private void HandleClicked()
        {
            if (syncState != SkillTreeRuntimeNodeSyncState.Active)
            {
                return;
            }

            _clickHandler?.Invoke(NodeId);
        }

        private void SetSerializedNodeId(string nodeId)
        {
            serializedNodeId = string.IsNullOrWhiteSpace(nodeId) ? string.Empty : nodeId.Trim();
            NodeId = serializedNodeId;
        }

        private void SetInteractionEnabled(bool enabled)
        {
            if (clickButton != null)
            {
                clickButton.interactable = enabled;
            }

            OnInteractionEnabledChanged(enabled);
        }

        private void RegisterClickListener()
        {
            if (clickButton == null)
            {
                return;
            }

            clickButton.onClick.RemoveListener(HandleClicked);
            clickButton.onClick.AddListener(HandleClicked);
        }

        private void UnregisterClickListener()
        {
            if (clickButton != null)
            {
                clickButton.onClick.RemoveListener(HandleClicked);
            }
        }

        private void UpdateRuntimeObjectName()
        {
            name = syncState == SkillTreeRuntimeNodeSyncState.DeletedFromGraph
                ? $"{DeletedPrefix}{serializedNodeId}{RuntimeNodeSuffix}"
                : $"{serializedNodeId}{RuntimeNodeSuffix}";
        }
    }
}
