using System;
using SkillTree.Authoring.Runtime;
using UnityEngine;

namespace SkillTree.Authoring.Samples
{
    [ExecuteAlways]
    public sealed class SampleSkillTreeBootstrap : MonoBehaviour
    {
        [SerializeField] private SkillTreeRuntimeView runtimeView;
        [SerializeField] private TextAsset graphJson;
        [SerializeField] private TextAsset snapshotJson;
        [SerializeField] private SampleSkillCatalogProviderAsset provider;
        [SerializeField] private MonoBehaviour runtimeBridge;

        private SkillTreeRuntimeController _controller;
        private bool _isInitialized;
        private int _initializationCount;

        public SkillTreeRuntimeView RuntimeView => runtimeView;
        public TextAsset GraphJson => graphJson;
        public TextAsset SnapshotJson => snapshotJson;
        public SampleSkillCatalogProviderAsset Provider => provider;
        public SkillTreeRuntimeController CurrentController => _controller;
        public SkillTreeSnapshot CurrentSnapshot => _controller?.CurrentSnapshot;
        public ResolvedSkillTreeData CurrentResolvedData => _controller?.CurrentResolvedData;
        public int InitializationCount => _initializationCount;
        public bool IsInitialized => _isInitialized;

        private void Start()
        {
            if (Application.isPlaying)
            {
                InitializeNow();
                return;
            }

            BuildPreviewNow();
        }

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                BuildPreviewNow();
            }
        }

        private void OnValidate()
        {
            if (!Application.isPlaying && isActiveAndEnabled)
            {
                BuildPreviewNow();
            }
        }

        public void InitializeNow()
        {
            if (!Application.isPlaying)
            {
                BuildPreviewNow();
                return;
            }

            if (_isInitialized)
            {
                return;
            }

            var loadedGraph = LoadGraphOrThrow();
            ValidateConfigurationOrThrow(loadedGraph);
            var bridge = ResolveBridgeOrThrow();
            var loadedSnapshot = LoadSnapshot(loadedGraph);

            _controller = new SkillTreeRuntimeController(runtimeView, loadedGraph, provider, loadedSnapshot, bridge);
            _controller.Initialize();
            _isInitialized = true;
            _initializationCount += 1;
        }

        public bool BuildPreviewNow()
        {
            // 에디터에서는 컨트롤러를 만들지 않고 Bootstrap 입력으로 RuntimeView 프리뷰만 갱신한다.
            if (Application.isPlaying || runtimeView == null || graphJson == null || provider == null)
            {
                return false;
            }

            try
            {
                var graph = SkillTreeJsonService.Deserialize(graphJson.text);
                provider.ValidateGraphOrThrow(graph);
                runtimeView.Build(graph, provider);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public void ApplyResolvedDataNow()
        {
            if (!_isInitialized || _controller == null)
            {
                throw new InvalidOperationException("Sample bootstrap must be initialized before applying resolved data.");
            }

            _controller.ApplyResolvedData();
        }

        public SkillTreeGraphData LoadGraphOrThrow()
        {
            if (graphJson == null)
            {
                throw new InvalidOperationException("Sample bootstrap requires a graph TextAsset.");
            }

            return SkillTreeJsonService.Deserialize(graphJson.text);
        }

        public SkillTreeSnapshot LoadSnapshot(SkillTreeGraphData graph)
        {
            var snapshot = snapshotJson == null || string.IsNullOrWhiteSpace(snapshotJson.text)
                ? new SkillTreeSnapshot()
                : JsonUtility.FromJson<SkillTreeSnapshot>(snapshotJson.text) ?? new SkillTreeSnapshot();

            return SkillTreeProgressionService.NormalizeSnapshot(graph, provider, snapshot);
        }

        public void ValidateConfigurationOrThrow(SkillTreeGraphData graph)
        {
            if (runtimeView == null)
            {
                throw new InvalidOperationException("Sample bootstrap requires a SkillTreeRuntimeView reference.");
            }

            if (provider == null)
            {
                throw new InvalidOperationException("Sample bootstrap requires a SampleSkillCatalogProviderAsset reference.");
            }

            provider.ValidateGraphOrThrow(graph);
        }

        private ISkillTreeRuntimeBridge<ResolvedSkillTreeData> ResolveBridgeOrThrow()
        {
            if (runtimeBridge == null)
            {
                return null;
            }

            if (runtimeBridge is ISkillTreeRuntimeBridge<ResolvedSkillTreeData> resolvedBridge)
            {
                return resolvedBridge;
            }

            throw new InvalidOperationException(
                $"Assigned runtime bridge '{runtimeBridge.GetType().Name}' does not implement ISkillTreeRuntimeBridge<ResolvedSkillTreeData>.");
        }
    }
}
