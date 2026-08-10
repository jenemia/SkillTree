using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using SkillTree.Authoring;

namespace SkillTree.Authoring.Runtime
{
    public sealed class SkillTreeRuntimeBuildReport
    {
        public int AddedCount { get; internal set; }
        public int MovedCount { get; internal set; }
        public int DeletedCount { get; internal set; }
        public int RevivedCount { get; internal set; }
        public int UntouchedLegacyObjectCount { get; internal set; }
        public int ActiveNodeCount { get; internal set; }
        public int RemovedNodeCount { get; internal set; }
    }

    [ExecuteAlways]
    public sealed class SkillTreeRuntimeView : MonoBehaviour
    {
        [SerializeField] private SkillTreeRuntimeNodeView nodePrefab;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private RectTransform nodeLayer;
        [SerializeField] private RectTransform removedNodeLayer;
        [SerializeField] private SkillTreeRuntimeConnectionGraphic connectionGraphic;
        [SerializeField] private Vector2 contentPadding = new(200f, 200f);

        private readonly Dictionary<string, SkillTreeRuntimeNodeView> _nodeViews = new(StringComparer.Ordinal);
        private readonly Dictionary<string, SkillTreeRuntimeNodeView> _removedNodeViews = new(StringComparer.Ordinal);
        private SkillTreeGraphData _graphSnapshot;
        private ISkillDefinitionProvider _definitionProvider;
        private ResolvedSkillTreeData _currentResolvedData;
        private string _selectedNodeId;
        private bool _isBuilding;
        private SkillTreeRuntimeBuildReport _lastBuildReport = new();

        public event Action<string> OnNodeSelected;

        public ISkillDefinitionProvider DefinitionProvider => _definitionProvider;
        public SkillTreeRuntimeNodeView NodePrefab => nodePrefab;
        public RectTransform ContentRoot => contentRoot;
        public RectTransform NodeLayer => nodeLayer;
        public RectTransform RemovedNodeLayer => removedNodeLayer;
        public SkillTreeRuntimeConnectionGraphic ConnectionGraphic => connectionGraphic;
        public string SelectedNodeId => _selectedNodeId;
        public int RenderedNodeCount => _nodeViews.Count;
        public int RemovedNodeCount => _removedNodeViews.Count;
        public SkillTreeRuntimeBuildReport LastBuildReport => _lastBuildReport;
        public ResolvedSkillTreeData CurrentResolvedData => _currentResolvedData;

        public void Configure(
            SkillTreeGraphData graph,
            SkillNodeMetadataProviderAsset provider,
            SkillTreeRuntimeNodeView prefab,
            RectTransform content,
            RectTransform nodes,
            SkillTreeRuntimeConnectionGraphic connections,
            RectTransform removedNodes = null)
        {
            // 기존 asset 기반 구성은 새 정의 provider 구성으로 위임한다.
            Configure(graph, provider as ISkillDefinitionProvider, prefab, content, nodes, connections, removedNodes);
        }

        // 런타임 계산과 테스트가 공용 정의 provider만 바라보도록 진입점을 연다.
        public void Configure(
            SkillTreeGraphData graph,
            ISkillDefinitionProvider provider,
            SkillTreeRuntimeNodeView prefab,
            RectTransform content,
            RectTransform nodes,
            SkillTreeRuntimeConnectionGraphic connections,
            RectTransform removedNodes = null)
        {
            _graphSnapshot = SkillTreeJsonService.Clone(graph);
            _definitionProvider = provider;
            nodePrefab = prefab;
            contentRoot = content;
            nodeLayer = nodes;
            connectionGraphic = connections;
            if (removedNodes != null)
            {
                removedNodeLayer = removedNodes;
            }
        }

        public SkillTreeRuntimeBuildReport Build(SkillTreeGraphData graph, SkillNodeMetadataProviderAsset provider)
        {
            // 에셋 provider 기반 외부 입력을 현재 렌더러 구성에 주입한 뒤 빌드한다.
            return Build(graph, provider as ISkillDefinitionProvider);
        }

        public SkillTreeRuntimeBuildReport Build(SkillTreeGraphData graph, ISkillDefinitionProvider provider)
        {
            // RuntimeView는 source 데이터를 소유하지 않고 호출자가 넘긴 graph/provider만 렌더링한다.
            _graphSnapshot = SkillTreeJsonService.Clone(graph);
            _definitionProvider = provider;
            return Build();
        }

        public SkillTreeRuntimeBuildReport Build()
        {
            return Build(null);
        }

        public SkillTreeRuntimeBuildReport Build(
            Func<SkillTreeRuntimeNodeView, Transform, SkillTreeRuntimeNodeView> instantiateNode)
        {
            // 그래프 구조와 정적 정의만으로 노드/연결선 구조를 다시 맞춘다.
            if (_isBuilding)
            {
                return _lastBuildReport;
            }

            EnsureRequiredReferences();
            if (_graphSnapshot == null || contentRoot == null || nodeLayer == null || connectionGraphic == null)
            {
                return _lastBuildReport;
            }

            _isBuilding = true;
            try
            {
                _graphSnapshot = SkillTreeJsonService.Normalize(SkillTreeJsonService.Clone(_graphSnapshot));
                _lastBuildReport = new SkillTreeRuntimeBuildReport();
                _nodeViews.Clear();
                _removedNodeViews.Clear();

                var existingViews = CollectManagedNodeViews(_lastBuildReport);
                var maxX = contentPadding.x;
                var maxY = contentPadding.y;

                foreach (var node in _graphSnapshot.nodes)
                {
                    if (string.IsNullOrWhiteSpace(node.id))
                    {
                        continue;
                    }

                    SkillTreeRuntimeNodeView view;
                    if (existingViews.TryGetValue(node.id, out var existingView))
                    {
                        existingViews.Remove(node.id);
                        var wasRemoved = IsRemovedView(existingView);
                        var wasMoved = wasRemoved || !IsPositionSynchronized(existingView.RectTransform, node.position);
                        MoveViewToLayer(existingView, nodeLayer);
                        existingView.MarkAsActive(node.id);
                        existingView.ApplyLayout(node.position);
                        existingView.BindDefinition(node.id, ResolveDefinition(node.id));
                        existingView.SetClickHandler(SelectNode);
                        view = existingView;

                        if (wasRemoved)
                        {
                            _lastBuildReport.RevivedCount += 1;
                        }
                        else if (wasMoved)
                        {
                            _lastBuildReport.MovedCount += 1;
                        }
                    }
                    else
                    {
                        if (nodePrefab == null)
                        {
                            continue;
                        }

                        view = instantiateNode == null
                            ? Instantiate(nodePrefab, nodeLayer)
                            : instantiateNode(nodePrefab, nodeLayer);
                        if (view == null)
                        {
                            continue;
                        }

                        view.ApplyLayout(node.position);
                        view.BindDefinition(node.id, ResolveDefinition(node.id));
                        view.SetClickHandler(SelectNode);
                        _lastBuildReport.AddedCount += 1;
                    }

                    _nodeViews[node.id] = view;
                    ExpandBounds(view.RectTransform, ref maxX, ref maxY);
                }

                foreach (var pair in existingViews)
                {
                    var removedView = pair.Value;
                    var wasAlreadyRemoved = IsRemovedView(removedView);
                    MoveViewToLayer(removedView, removedNodeLayer);
                    removedView.MarkAsDeleted();
                    _removedNodeViews[pair.Key] = removedView;
                    if (!wasAlreadyRemoved)
                    {
                        _lastBuildReport.DeletedCount += 1;
                    }
                }

                _lastBuildReport.ActiveNodeCount = _nodeViews.Count;
                _lastBuildReport.RemovedNodeCount = _removedNodeViews.Count;
                ResizeLayers(maxX, maxY, includeRemovedNodesInEditor: !Application.isPlaying);
                ApplyRemovedNodeVisibility();

                if (string.IsNullOrWhiteSpace(_selectedNodeId) || !_nodeViews.ContainsKey(_selectedNodeId))
                {
                    _selectedNodeId = _nodeViews.Keys.FirstOrDefault();
                }

                if (_currentResolvedData != null)
                {
                    Refresh(_currentResolvedData);
                }
                else
                {
                    ApplySelectionState();
                }

                Canvas.ForceUpdateCanvases();
                connectionGraphic.Bind(
                    _graphSnapshot,
                    _nodeViews.ToDictionary(pair => pair.Key, pair => pair.Value.RectTransform, StringComparer.Ordinal));
                connectionGraphic.RefreshNow();
                return _lastBuildReport;
            }
            finally
            {
                _isBuilding = false;
            }
        }

        public void SelectNode(string nodeId)
        {
            // 선택은 뷰 내부 상태를 갱신하고 컨트롤러로 이벤트를 전달한다.
            if (string.IsNullOrWhiteSpace(nodeId) || !_nodeViews.ContainsKey(nodeId))
            {
                return;
            }

            _selectedNodeId = nodeId;
            ApplySelectionState();
            OnNodeSelected?.Invoke(nodeId);
        }

        // 계산된 진행 상태를 기존 노드 인스턴스에 덮어써서 화면만 갱신한다.
        public void Refresh(ResolvedSkillTreeData resolved)
        {
            _currentResolvedData = resolved;
            if (resolved == null)
            {
                ApplySelectionState();
                return;
            }

            var userSkillsById = resolved.userSkills
                .Where(item => item?.definition != null && !string.IsNullOrWhiteSpace(item.definition.skillId))
                .ToDictionary(item => item.definition.skillId, StringComparer.Ordinal);
            var statusesById = resolved.skillStatuses
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.skillId))
                .ToDictionary(item => item.skillId, StringComparer.Ordinal);

            if (!string.IsNullOrWhiteSpace(resolved.selectedSkillId) && _nodeViews.ContainsKey(resolved.selectedSkillId))
            {
                _selectedNodeId = resolved.selectedSkillId;
            }
            else if (string.IsNullOrWhiteSpace(_selectedNodeId) || !_nodeViews.ContainsKey(_selectedNodeId))
            {
                _selectedNodeId = _nodeViews.Keys.FirstOrDefault();
            }

            foreach (var pair in _nodeViews)
            {
                var skillId = pair.Key;
                var view = pair.Value;
                userSkillsById.TryGetValue(skillId, out var userSkill);
                statusesById.TryGetValue(skillId, out var status);

                if (userSkill == null)
                {
                    userSkill = new UserSkillData
                    {
                        definition = ResolveDefinition(skillId),
                        state = new UserSkillState
                        {
                            skillId = skillId,
                            level = 0,
                            isUnlocked = false
                        }
                    };
                }

                view.ApplyStatus(
                    userSkill,
                    status,
                    string.Equals(skillId, _selectedNodeId, StringComparison.Ordinal));
            }
        }

        public bool TryGetNodeView(string nodeId, out SkillTreeRuntimeNodeView view)
        {
            return _nodeViews.TryGetValue(nodeId, out view);
        }

        public bool TryGetRemovedNodeView(string nodeId, out SkillTreeRuntimeNodeView view)
        {
            return _removedNodeViews.TryGetValue(nodeId, out view);
        }

        private void EnsureRequiredReferences()
        {
            if (contentRoot == null && nodeLayer != null)
            {
                contentRoot = nodeLayer.parent as RectTransform;
            }

            if (removedNodeLayer == null && contentRoot != null)
            {
                removedNodeLayer = contentRoot.Find("RemovedNodes") as RectTransform;
                if (removedNodeLayer == null)
                {
                    var removedRoot = new GameObject("RemovedNodes", typeof(RectTransform));
                    removedNodeLayer = removedRoot.GetComponent<RectTransform>();
                    removedNodeLayer.SetParent(contentRoot, false);
                }
            }
        }

        private void ApplySelectionState()
        {
            foreach (var pair in _nodeViews)
            {
                pair.Value.ApplySelection(string.Equals(pair.Key, _selectedNodeId, StringComparison.Ordinal));
            }
        }

        // 구조 빌드와 상태 갱신에서 공통으로 쓸 정적 정의를 해석한다.
        private SkillDefinition ResolveDefinition(string nodeId)
        {
            if (DefinitionProvider != null && DefinitionProvider.TryGetDefinition(nodeId, out var definition))
            {
                return definition;
            }

            return new SkillDefinition
            {
                skillId = nodeId,
                displayName = nodeId,
                description = string.Empty,
                effectSummary = string.Empty,
                cost = 0,
                maxLevel = 1,
                icon = null
            };
        }

        private Dictionary<string, SkillTreeRuntimeNodeView> CollectManagedNodeViews(SkillTreeRuntimeBuildReport report)
        {
            var managedViews = new Dictionary<string, SkillTreeRuntimeNodeView>(StringComparer.Ordinal);
            CollectLayerViews(nodeLayer, managedViews, report);
            CollectLayerViews(removedNodeLayer, managedViews, report);
            return managedViews;
        }

        private static void CollectLayerViews(
            RectTransform layer,
            IDictionary<string, SkillTreeRuntimeNodeView> viewsById,
            SkillTreeRuntimeBuildReport report)
        {
            if (layer == null)
            {
                return;
            }

            for (var index = 0; index < layer.childCount; index += 1)
            {
                var child = layer.GetChild(index);
                var nodeView = child.GetComponent<SkillTreeRuntimeNodeView>();
                if (nodeView == null)
                {
                    continue;
                }

                if (!nodeView.TryRestoreSerializedNodeIdFromName() || string.IsNullOrWhiteSpace(nodeView.SerializedNodeId))
                {
                    report.UntouchedLegacyObjectCount += 1;
                    continue;
                }

                if (!viewsById.ContainsKey(nodeView.SerializedNodeId))
                {
                    viewsById[nodeView.SerializedNodeId] = nodeView;
                    continue;
                }

                report.UntouchedLegacyObjectCount += 1;
            }
        }

        private bool IsRemovedView(SkillTreeRuntimeNodeView view)
        {
            return view != null &&
                   (view.SyncState == SkillTreeRuntimeNodeSyncState.DeletedFromGraph ||
                    view.transform.parent == removedNodeLayer);
        }

        private static bool IsPositionSynchronized(RectTransform rect, Vector2 graphPosition)
        {
            if (rect == null)
            {
                return false;
            }

            var expectedPosition = new Vector2(graphPosition.x, -graphPosition.y);
            return Vector2.Distance(rect.anchoredPosition, expectedPosition) <= 0.01f;
        }

        private static void MoveViewToLayer(SkillTreeRuntimeNodeView view, RectTransform targetLayer)
        {
            if (view == null || targetLayer == null || view.transform.parent == targetLayer)
            {
                return;
            }

            var rect = view.RectTransform;
            var anchorMin = rect.anchorMin;
            var anchorMax = rect.anchorMax;
            var pivot = rect.pivot;
            var sizeDelta = rect.sizeDelta;
            var anchoredPosition = rect.anchoredPosition;

            rect.SetParent(targetLayer, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = sizeDelta;
            rect.anchoredPosition = anchoredPosition;
        }

        private void ExpandBounds(RectTransform rect, ref float maxX, ref float maxY)
        {
            var size = ResolveNodeSize(rect);
            var position = rect == null ? Vector2.zero : rect.anchoredPosition;
            maxX = Mathf.Max(maxX, position.x + size.x + contentPadding.x);
            maxY = Mathf.Max(maxY, -position.y + size.y + contentPadding.y);
        }

        private Vector2 ResolveNodeSize(RectTransform rect)
        {
            if (rect == null)
            {
                return ResolveNodePrefabSize();
            }

            var size = rect.sizeDelta;
            if (size.x <= 0f || size.y <= 0f)
            {
                size = rect.rect.size;
            }

            if (size.x <= 0f || size.y <= 0f)
            {
                size = ResolveNodePrefabSize();
            }

            return new Vector2(
                Mathf.Max(220f, size.x),
                Mathf.Max(96f, size.y));
        }

        private Vector2 ResolveNodePrefabSize()
        {
            var prefabRect = nodePrefab == null ? null : nodePrefab.RectTransform;
            if (prefabRect == null)
            {
                return new Vector2(220f, 96f);
            }

            var size = prefabRect.sizeDelta;
            if (size.x <= 0f || size.y <= 0f)
            {
                size = prefabRect.rect.size;
            }

            return new Vector2(
                Mathf.Max(220f, size.x),
                Mathf.Max(96f, size.y));
        }

        private void ResizeLayers(float maxX, float maxY, bool includeRemovedNodesInEditor)
        {
            if (includeRemovedNodesInEditor)
            {
                foreach (var removedView in _removedNodeViews.Values)
                {
                    ExpandBounds(removedView.RectTransform, ref maxX, ref maxY);
                }
            }

            contentRoot.anchorMin = new Vector2(0f, 1f);
            contentRoot.anchorMax = new Vector2(0f, 1f);
            contentRoot.pivot = new Vector2(0f, 1f);
            contentRoot.sizeDelta = new Vector2(maxX, maxY);

            ApplyLayerRect(nodeLayer);
            ApplyLayerRect(removedNodeLayer);
            ApplyLayerRect(connectionGraphic == null ? null : connectionGraphic.rectTransform);
        }

        private void ApplyLayerRect(RectTransform layerRect)
        {
            if (layerRect == null)
            {
                return;
            }

            layerRect.anchorMin = new Vector2(0f, 1f);
            layerRect.anchorMax = new Vector2(0f, 1f);
            layerRect.pivot = new Vector2(0f, 1f);
            layerRect.sizeDelta = contentRoot.sizeDelta;
            layerRect.anchoredPosition = Vector2.zero;
        }

        private void ApplyRemovedNodeVisibility()
        {
            if (removedNodeLayer == null)
            {
                return;
            }

            removedNodeLayer.gameObject.SetActive(!Application.isPlaying && _removedNodeViews.Count > 0);
        }
    }
}
