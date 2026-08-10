using System;
using UnityEditor;
using UnityEngine;
using SkillTree.Authoring.Runtime;

namespace SkillTree.Authoring.Editor
{
    internal enum SkillTreeRuntimePrefabSyncSessionStatus
    {
        Ready = 0,
        BindingMismatch = 1,
        Error = 2
    }

    internal sealed class SkillTreeRuntimePrefabSyncSession : IDisposable
    {
        private readonly GameObject _prefabRoot;
        private bool _disposed;

        internal SkillTreeRuntimePrefabSyncSession(
            SkillTreeRuntimePrefabSyncSessionStatus status,
            string prefabPath,
            GameObject prefabRoot,
            SkillTreeRuntimeView runtimeView,
            SkillTreeRuntimeBuildReport buildReport,
            string storedTreeId,
            string storedMetadataProviderGuid,
            string errorMessage)
        {
            Status = status;
            PrefabPath = prefabPath;
            _prefabRoot = prefabRoot;
            RuntimeView = runtimeView;
            BuildReport = buildReport;
            StoredTreeId = storedTreeId;
            StoredMetadataProviderGuid = storedMetadataProviderGuid;
            ErrorMessage = errorMessage;
        }

        internal SkillTreeRuntimePrefabSyncSessionStatus Status { get; }
        internal string PrefabPath { get; }
        internal SkillTreeRuntimeView RuntimeView { get; }
        internal SkillTreeRuntimeBuildReport BuildReport { get; }
        internal string StoredTreeId { get; }
        internal string StoredMetadataProviderGuid { get; }
        internal string ErrorMessage { get; }
        internal bool HasStoredBinding => !string.IsNullOrWhiteSpace(StoredTreeId) && !string.IsNullOrWhiteSpace(StoredMetadataProviderGuid);
        internal bool RequiresInitialBindingConfirmation => Status == SkillTreeRuntimePrefabSyncSessionStatus.Ready && !HasStoredBinding;

        internal void Save()
        {
            if (Status != SkillTreeRuntimePrefabSyncSessionStatus.Ready || _prefabRoot == null)
            {
                throw new InvalidOperationException("Only ready sync sessions can be saved.");
            }

            PrefabUtility.SaveAsPrefabAsset(_prefabRoot, PrefabPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_prefabRoot != null)
            {
                PrefabUtility.UnloadPrefabContents(_prefabRoot);
            }

            _disposed = true;
        }
    }

    internal static class SkillTreeRuntimePrefabSyncService
    {
        internal static SkillTreeRuntimePrefabSyncSession OpenSession(
            string prefabPath,
            SkillTreeGraphData graph,
            SkillNodeMetadataProviderAsset metadataProvider,
            string metadataProviderGuid,
            SkillTreeRuntimeNodeView fallbackNodePrefab = null)
        {
            if (string.IsNullOrWhiteSpace(prefabPath))
            {
                return CreateError(prefabPath, "대상 RuntimeView 프리팹 경로가 비어 있습니다.");
            }

            if (graph == null)
            {
                return CreateError(prefabPath, "동기화할 스킬 트리 그래프가 없습니다.");
            }

            var prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var runtimeView = prefabRoot.GetComponent<SkillTreeRuntimeView>();
                if (runtimeView == null)
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                    return CreateError(prefabPath, "선택한 프리팹에 SkillTreeRuntimeView 컴포넌트가 없습니다.");
                }

                var sourceBinding = EnsureSourceBinding(prefabRoot);
                var storedTreeId = sourceBinding.SourceTreeId;
                var storedProviderGuid = sourceBinding.SourceMetadataProviderGuid;
                if (sourceBinding.HasSourceBinding &&
                    !sourceBinding.Matches(graph.treeId, metadataProviderGuid))
                {
                    PrefabUtility.UnloadPrefabContents(prefabRoot);
                    return new SkillTreeRuntimePrefabSyncSession(
                        SkillTreeRuntimePrefabSyncSessionStatus.BindingMismatch,
                        prefabPath,
                        null,
                        null,
                        null,
                        storedTreeId,
                        storedProviderGuid,
                        "선택한 RuntimeView 프리팹이 현재 메타와 다른 tree/provider 조합에 연결되어 있습니다.");
                }

                var nodePrefab = runtimeView.NodePrefab ?? fallbackNodePrefab ?? SkillTreeRuntimePrefabFactory.EnsureDefaultNodePrefab();
                runtimeView.Configure(
                    graph,
                    metadataProvider,
                    nodePrefab,
                    runtimeView.ContentRoot,
                    runtimeView.NodeLayer,
                    runtimeView.ConnectionGraphic,
                    runtimeView.RemovedNodeLayer);
                sourceBinding.Apply(graph.treeId, metadataProviderGuid);
                var report = runtimeView.Build(SkillTreeRuntimePrefabFactory.InstantiateNodePrefab);

                return new SkillTreeRuntimePrefabSyncSession(
                    SkillTreeRuntimePrefabSyncSessionStatus.Ready,
                    prefabPath,
                    prefabRoot,
                    runtimeView,
                    report,
                    storedTreeId,
                    storedProviderGuid,
                    null);
            }
            catch (Exception ex)
            {
                PrefabUtility.UnloadPrefabContents(prefabRoot);
                return CreateError(prefabPath, $"RuntimeView 프리팹 동기화 세션을 열지 못했습니다. {ex.Message}");
            }
        }

        private static SkillTreeRuntimePrefabSyncSession CreateError(string prefabPath, string message)
        {
            return new SkillTreeRuntimePrefabSyncSession(
                SkillTreeRuntimePrefabSyncSessionStatus.Error,
                prefabPath,
                null,
                null,
                null,
                null,
                null,
                message);
        }

        private static SkillTreeRuntimeSourceBinding EnsureSourceBinding(GameObject prefabRoot)
        {
            var binding = prefabRoot.GetComponent<SkillTreeRuntimeSourceBinding>();
            if (binding == null)
            {
                binding = prefabRoot.AddComponent<SkillTreeRuntimeSourceBinding>();
            }

            binding.hideFlags = HideFlags.HideInInspector;
            return binding;
        }
    }
}
