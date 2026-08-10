using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace SkillTree.Authoring.Editor
{
    [Flags]
    public enum SkillTreeEditorChangeKind
    {
        None = 0,
        Graph = 1 << 0,
        Selection = 1 << 1,
        Validation = 1 << 2,
        Metadata = 1 << 3,
        File = 1 << 4,
        Status = 1 << 5,
        All = Graph | Selection | Validation | Metadata | File | Status
    }

    public enum SkillTreeEditorStatusType
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public sealed class SkillTreeEditorController
    {
        private SkillTreeGraphData _graph = SkillTreeJsonService.CreateDefaultGraph();
        private IReadOnlyList<SkillTreeValidationIssue> _validationIssues = Array.Empty<SkillTreeValidationIssue>();
        private string _selectedNodeId;
        private string _selectedConnectionChildId;
        private string _pendingChildNodeId;

        public event Action<SkillTreeEditorChangeKind> StateChanged;

        public SkillTreeGraphData Graph => _graph;
        public string CurrentFilePath { get; private set; }
        public string SelectedNodeId => _selectedNodeId;
        public string SelectedConnectionChildId => _selectedConnectionChildId;
        public string PendingChildNodeId => _pendingChildNodeId;
        public SkillNodeMetadataProviderAsset MetadataProvider { get; private set; }
        public string MetadataProviderAssetGuid => _graph?.editorBindings?.metadataProviderAssetGuid;
        public IReadOnlyList<SkillTreeValidationIssue> ValidationIssues => _validationIssues;
        public string StatusMessage { get; private set; }
        public SkillTreeEditorStatusType StatusType { get; private set; }
        internal SkillTreeMetadataSyncReport LastMetadataSyncReport { get; private set; }

        public SkillTreeEditorController()
        {
            RefreshValidation(false);
        }

        public void CreateNewGraph(string treeId = "skill_tree")
        {
            _graph = SkillTreeJsonService.CreateDefaultGraph(treeId);
            CurrentFilePath = null;
            _selectedNodeId = null;
            _selectedConnectionChildId = null;
            _pendingChildNodeId = null;
            MetadataProvider = null;
            LastMetadataSyncReport = null;
            SetStatus("새 스킬 트리를 만들었습니다.", SkillTreeEditorStatusType.Info);
            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.All);
        }

        public void SetMetadataProvider(SkillNodeMetadataProviderAsset provider)
        {
            MetadataProvider = provider;
            UpdateMetadataProviderBinding(provider);
            if (TrySyncMetadataProvider(CurrentFilePath))
            {
                RefreshValidation(false);
                NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Metadata | SkillTreeEditorChangeKind.Status);
                return;
            }

            LastMetadataSyncReport = null;
            SetStatus(provider == null ? "메타데이터 공급자가 해제되었습니다." : "메타데이터 공급자를 연결했습니다.", SkillTreeEditorStatusType.Info);
            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Metadata | SkillTreeEditorChangeKind.Status);
        }

        public void ReloadMetadata()
        {
            if (!TrySyncMetadataProvider(CurrentFilePath))
            {
                LastMetadataSyncReport = null;
                SetStatus(MetadataProvider == null
                    ? "메타데이터 공급자가 없어 다시 매칭할 수 없습니다."
                    : "이 공급자는 자동 다시 매칭을 지원하지 않습니다.", SkillTreeEditorStatusType.Warning);
            }

            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Metadata | SkillTreeEditorChangeKind.Status);
        }

        public void LoadFromFile(string path)
        {
            _graph = SkillTreeJsonService.LoadFromFile(path);
            CurrentFilePath = path;
            _selectedNodeId = ResolveSelectionAfterLoad();
            _selectedConnectionChildId = null;
            _pendingChildNodeId = null;
            MetadataProvider = null;
            LastMetadataSyncReport = null;

            var hasStoredMetadataBinding = !string.IsNullOrWhiteSpace(_graph.editorBindings?.metadataProviderAssetPath) ||
                                           !string.IsNullOrWhiteSpace(_graph.editorBindings?.metadataProviderAssetGuid);
            var warnings = new List<string>();
            RestoreEditorBindings(warnings);

            var metadataSynced = false;
            if (MetadataProvider != null)
            {
                metadataSynced = TrySyncMetadataProvider(path);
            }
            else if (!hasStoredMetadataBinding)
            {
                var fallbackProvider = SkillTreeMetadataAssetSyncService.FindExistingProvider(_graph);
                if (fallbackProvider != null)
                {
                    MetadataProvider = fallbackProvider;
                    UpdateMetadataProviderBinding(fallbackProvider);
                    metadataSynced = TrySyncMetadataProvider(path);
                }
            }

            if (warnings.Count > 0)
            {
                SetStatus(string.Join(" ", warnings), SkillTreeEditorStatusType.Warning);
            }
            else if (!metadataSynced)
            {
                SetStatus("JSON을 불러왔습니다.", SkillTreeEditorStatusType.Info);
            }

            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.All);
        }

        public bool CreateAndAttachMetadataAssets()
        {
            var report = SkillTreeMetadataAssetSyncService.CreateOrAttachAssets(_graph, CurrentFilePath);
            MetadataProvider = report.Provider;
            UpdateMetadataProviderBinding(report.Provider);
            LastMetadataSyncReport = report;
            SetStatus(report.Summary, SkillTreeEditorStatusType.Info);
            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Metadata | SkillTreeEditorChangeKind.Status);
            return true;
        }

        public bool SaveToCurrentFile(out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(CurrentFilePath))
            {
                errorMessage = "저장 경로가 없습니다.";
                return false;
            }

            return SaveToPath(CurrentFilePath, out errorMessage);
        }

        public bool SaveToPath(string path, out string errorMessage)
        {
            errorMessage = null;
            RefreshValidation(false);
            if (HasBlockingErrors())
            {
                errorMessage = "저장 전에 오류를 해결해야 합니다.";
                SetStatus(errorMessage, SkillTreeEditorStatusType.Error);
                NotifyStateChanged(SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Status);
                return false;
            }

            SkillTreeJsonService.SaveToFile(path, _graph);
            CurrentFilePath = path;
            SetStatus("JSON 저장을 완료했습니다.", SkillTreeEditorStatusType.Info);
            NotifyStateChanged(SkillTreeEditorChangeKind.File | SkillTreeEditorChangeKind.Status);
            return true;
        }

        public SkillTreeNodeRecord AddNode(Vector2? position = null)
        {
            var resolvedPosition = position ?? new Vector2(
                120f + _graph.nodes.Count * 30f,
                120f + _graph.nodes.Count * 24f);
            var node = SkillTreeGraphMutator.AddNode(_graph, resolvedPosition);
            _selectedNodeId = node.id;
            SetStatus($"노드 {node.id} 를 추가했습니다.", SkillTreeEditorStatusType.Info);
            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Status);
            return node;
        }

        public bool DeleteSelectedNode()
        {
            if (string.IsNullOrWhiteSpace(_selectedNodeId))
            {
                return false;
            }

            var deleted = SkillTreeGraphMutator.DeleteNode(_graph, _selectedNodeId);
            if (!deleted)
            {
                return false;
            }

            _selectedNodeId = ResolveSelectionAfterDelete();
            NormalizeSelectionAfterGraphChange();
            _pendingChildNodeId = null;
            SetStatus("노드를 삭제하고 자식 노드를 루트로 승격했습니다.", SkillTreeEditorStatusType.Info);
            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Status);
            return true;
        }

        public void SelectNode(string nodeId)
        {
            _selectedNodeId = string.IsNullOrWhiteSpace(nodeId) ? null : nodeId;
            _selectedConnectionChildId = null;
            NotifyStateChanged(SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Status);
        }

        public void SelectConnection(string childNodeId)
        {
            if (string.IsNullOrWhiteSpace(childNodeId))
            {
                _selectedConnectionChildId = null;
                NotifyStateChanged(SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Status);
                return;
            }

            var node = SkillTreeGraphMutator.FindNode(_graph, childNodeId);
            _selectedConnectionChildId = node != null && !string.IsNullOrWhiteSpace(node.parentId)
                ? childNodeId
                : null;
            _selectedNodeId = null;
            NotifyStateChanged(SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Status);
        }

        public bool MoveNode(string nodeId, Vector2 position)
        {
            if (!SkillTreeGraphMutator.MoveNode(_graph, nodeId, position))
            {
                return false;
            }

            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Validation);
            return true;
        }

        public bool RenameSelectedNode(string newId)
        {
            var current = GetSelectedNode();
            if (current == null || string.IsNullOrWhiteSpace(newId))
            {
                return false;
            }

            var trimmed = newId.Trim();
            if (!SkillTreeGraphMutator.RenameNode(_graph, current.id, trimmed))
            {
                return false;
            }

            _selectedNodeId = trimmed;
            SetStatus($"노드 ID를 {trimmed} 로 변경했습니다.", SkillTreeEditorStatusType.Info);
            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Status);
            return true;
        }

        public bool SetSelectedParent(string parentId, out string errorMessage)
        {
            errorMessage = null;
            var current = GetSelectedNode();
            if (current == null)
            {
                return false;
            }

            var success = TrySetParent(current.id, parentId, out errorMessage);
            if (!success)
            {
                SetStatus(errorMessage, SkillTreeEditorStatusType.Warning);
                NotifyStateChanged(SkillTreeEditorChangeKind.Status);
                return false;
            }

            SetStatus(string.IsNullOrWhiteSpace(parentId)
                ? $"노드 {current.id} 를 루트로 변경했습니다."
                : $"노드 {current.id} 의 부모를 {parentId} 로 변경했습니다.", SkillTreeEditorStatusType.Info);
            NormalizeSelectionAfterGraphChange();
            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Status);
            return true;
        }

        public bool SetSelectedConnectionLineType(SkillTreeConnectionLineType lineType)
        {
            var connectionNode = GetSelectedConnectionNode();
            if (connectionNode == null)
            {
                return false;
            }

            connectionNode.parentLineType = lineType;
            SetStatus($"연결선 타입을 {lineType} 로 변경했습니다.", SkillTreeEditorStatusType.Info);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Status);
            return true;
        }

        public bool CommitSelectedPosition(Vector2 position)
        {
            var current = GetSelectedNode();
            return current != null && MoveNode(current.id, position);
        }

        public void BeginParentLink(string childNodeId)
        {
            if (string.IsNullOrWhiteSpace(childNodeId))
            {
                return;
            }

            _pendingChildNodeId = childNodeId;
            SetStatus($"Parent Link: {childNodeId} -> ?", SkillTreeEditorStatusType.Info);
            NotifyStateChanged(SkillTreeEditorChangeKind.Status | SkillTreeEditorChangeKind.Graph);
        }

        public bool CompleteParentLink(string parentNodeId, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(_pendingChildNodeId))
            {
                errorMessage = "연결 중인 자식 노드가 없습니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(parentNodeId))
            {
                CancelParentLink("부모 연결을 취소했습니다.");
                return false;
            }

            if (string.Equals(_pendingChildNodeId, parentNodeId, StringComparison.Ordinal))
            {
                errorMessage = "자기 자신을 부모로 연결할 수 없습니다.";
                SetStatus(errorMessage, SkillTreeEditorStatusType.Warning);
                _pendingChildNodeId = null;
                NotifyStateChanged(SkillTreeEditorChangeKind.Status | SkillTreeEditorChangeKind.Graph);
                return false;
            }

            var resolvedChildId = _pendingChildNodeId;
            var success = TrySetParent(resolvedChildId, parentNodeId, out errorMessage);
            _pendingChildNodeId = null;
            if (!success)
            {
                SetStatus(errorMessage, SkillTreeEditorStatusType.Warning);
                NotifyStateChanged(SkillTreeEditorChangeKind.Status | SkillTreeEditorChangeKind.Graph);
                return false;
            }

            _selectedNodeId = resolvedChildId;
            _selectedConnectionChildId = null;
            SetStatus($"노드 {resolvedChildId} 의 부모를 {parentNodeId} 로 연결했습니다.", SkillTreeEditorStatusType.Info);
            RefreshValidation(false);
            NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Selection | SkillTreeEditorChangeKind.Validation | SkillTreeEditorChangeKind.Status);
            return true;
        }

        public void CancelParentLink(string message = "부모 연결을 취소했습니다.")
        {
            if (string.IsNullOrWhiteSpace(_pendingChildNodeId))
            {
                return;
            }

            _pendingChildNodeId = null;
            SetStatus(message, SkillTreeEditorStatusType.Info);
            NotifyStateChanged(SkillTreeEditorChangeKind.Status | SkillTreeEditorChangeKind.Graph);
        }

        public SkillTreeNodeRecord GetSelectedNode()
        {
            return SkillTreeGraphMutator.FindNode(_graph, _selectedNodeId);
        }

        public SkillTreeNodeRecord GetSelectedConnectionNode()
        {
            var node = SkillTreeGraphMutator.FindNode(_graph, _selectedConnectionChildId);
            if (node == null || string.IsNullOrWhiteSpace(node.parentId))
            {
                return null;
            }

            return node;
        }

        public SkillNodeMetadata GetMetadata(string nodeId)
        {
            if (MetadataProvider == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return null;
            }

            return MetadataProvider.TryGetMetadata(nodeId, out var metadata) ? metadata : null;
        }

        public IReadOnlyList<SkillTreeValidationIssue> GetIssuesForNode(string nodeId)
        {
            return _validationIssues
                .Where(issue => string.IsNullOrWhiteSpace(nodeId)
                    ? string.IsNullOrWhiteSpace(issue.nodeId)
                    : string.Equals(issue.nodeId, nodeId, StringComparison.Ordinal))
                .ToList();
        }

        public bool HasBlockingErrors()
        {
            return _validationIssues.Any(issue => issue.severity == SkillTreeValidationSeverity.Error);
        }

        public SkillNodeMetadataCatalog GetMetadataCatalog()
        {
            return (MetadataProvider as ScriptableObjectSkillNodeMetadataProvider)?.Catalog;
        }

        public bool HasCatalogBackedMetadataProvider()
        {
            return MetadataProvider is ScriptableObjectSkillNodeMetadataProvider { Catalog: not null };
        }

        public void ReportStatus(string message, SkillTreeEditorStatusType statusType)
        {
            SetStatus(message, statusType);
            NotifyStateChanged(SkillTreeEditorChangeKind.Status);
        }

        private bool TrySetParent(string childNodeId, string parentId, out string errorMessage)
        {
            errorMessage = null;
            var child = SkillTreeGraphMutator.FindNode(_graph, childNodeId);
            if (child == null)
            {
                errorMessage = "대상 노드를 찾을 수 없습니다.";
                return false;
            }

            if (string.Equals(child.parentId, parentId, StringComparison.Ordinal))
            {
                errorMessage = "이미 같은 부모가 연결되어 있습니다.";
                return false;
            }

            var success = SkillTreeGraphMutator.SetParent(_graph, childNodeId, parentId, out errorMessage);
            if (success)
            {
                NormalizeSelectionAfterGraphChange();
            }

            return success;
        }

        private void RestoreEditorBindings(List<string> warnings)
        {
            EnsureEditorBindings();

            if (!string.IsNullOrWhiteSpace(_graph.editorBindings.metadataProviderAssetPath) ||
                !string.IsNullOrWhiteSpace(_graph.editorBindings.metadataProviderAssetGuid))
            {
                MetadataProvider = LoadBoundAsset<SkillNodeMetadataProviderAsset>(
                    _graph.editorBindings.metadataProviderAssetPath,
                    _graph.editorBindings.metadataProviderAssetGuid,
                    "메타데이터 공급자",
                    warnings);
                if (MetadataProvider != null)
                {
                    UpdateMetadataProviderBinding(MetadataProvider);
                }
            }
        }

        private void RefreshValidation(bool notify = true)
        {
            _graph = SkillTreeJsonService.Normalize(_graph);
            _validationIssues = SkillTreeGraphValidator.Validate(_graph, MetadataProvider);
            if (notify)
            {
                NotifyStateChanged(SkillTreeEditorChangeKind.Graph | SkillTreeEditorChangeKind.Validation);
            }
        }

        private string ResolveSelectionAfterLoad()
        {
            if (!string.IsNullOrWhiteSpace(_selectedNodeId) &&
                _graph.nodes.Any(node => string.Equals(node.id, _selectedNodeId, StringComparison.Ordinal)))
            {
                return _selectedNodeId;
            }

            return _graph.nodes.FirstOrDefault()?.id;
        }

        private string ResolveSelectionAfterDelete()
        {
            return _graph.nodes.FirstOrDefault()?.id;
        }

        private void NormalizeSelectionAfterGraphChange()
        {
            if (!string.IsNullOrWhiteSpace(_selectedNodeId) &&
                SkillTreeGraphMutator.FindNode(_graph, _selectedNodeId) == null)
            {
                _selectedNodeId = null;
            }

            var selectedConnectionNode = GetSelectedConnectionNode();
            if (selectedConnectionNode == null)
            {
                _selectedConnectionChildId = null;
            }
        }

        private void SetStatus(string message, SkillTreeEditorStatusType statusType)
        {
            StatusMessage = message ?? string.Empty;
            StatusType = statusType;
        }

        private void EnsureEditorBindings()
        {
            _graph.editorBindings ??= new SkillTreeEditorBindingsData();
        }

        private void UpdateMetadataProviderBinding(SkillNodeMetadataProviderAsset provider)
        {
            EnsureEditorBindings();
            _graph.editorBindings.metadataProviderAssetPath = ResolveAssetPath(provider);
            _graph.editorBindings.metadataProviderAssetGuid = ResolveAssetGuid(provider);
        }

        private static string ResolveAssetPath(UnityEngine.Object asset)
        {
            var assetPath = asset == null ? null : AssetDatabase.GetAssetPath(asset);
            return string.IsNullOrWhiteSpace(assetPath) ? null : assetPath.Trim();
        }

        private static string ResolveAssetGuid(UnityEngine.Object asset)
        {
            var assetPath = ResolveAssetPath(asset);
            return string.IsNullOrWhiteSpace(assetPath) ? null : AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static TAsset LoadBoundAsset<TAsset>(string assetPath, string label, List<string> warnings)
            where TAsset : UnityEngine.Object
        {
            return LoadBoundAsset<TAsset>(assetPath, null, label, warnings);
        }

        private static TAsset LoadBoundAsset<TAsset>(string assetPath, string assetGuid, string label, List<string> warnings)
            where TAsset : UnityEngine.Object
        {
            var typedAsset = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
            if (typedAsset != null)
            {
                return typedAsset;
            }

            if (!string.IsNullOrWhiteSpace(assetGuid))
            {
                var recoveredPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (!string.IsNullOrWhiteSpace(recoveredPath))
                {
                    typedAsset = AssetDatabase.LoadAssetAtPath<TAsset>(recoveredPath);
                    if (typedAsset != null)
                    {
                        return typedAsset;
                    }
                }
            }

            var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (mainAsset != null)
            {
                warnings.Add($"{label} 경로의 에셋 타입이 올바르지 않습니다. ({assetPath})");
                return null;
            }

            if (!string.IsNullOrWhiteSpace(assetGuid))
            {
                warnings.Add($"{label} 경로와 GUID로 에셋을 찾지 못했습니다. ({assetPath} / {assetGuid})");
                return null;
            }

            warnings.Add($"{label} 경로의 에셋을 찾지 못했습니다. ({assetPath})");
            return null;
        }

        private bool TrySyncMetadataProvider(string legacyJsonPath = null)
        {
            var report = SkillTreeMetadataAssetSyncService.SyncExistingProvider(_graph, MetadataProvider, legacyJsonPath);
            if (report == null)
            {
                return false;
            }

            UpdateMetadataProviderBinding(report.Provider);
            LastMetadataSyncReport = report;
            SetStatus(report.Summary, SkillTreeEditorStatusType.Info);
            return true;
        }

        private void NotifyStateChanged(SkillTreeEditorChangeKind changeKind)
        {
            StateChanged?.Invoke(changeKind);
        }
    }
}
