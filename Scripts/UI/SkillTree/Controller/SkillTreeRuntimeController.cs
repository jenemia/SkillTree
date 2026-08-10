using System;
using SkillTree.Authoring.Runtime;

namespace SkillTree.Authoring
{
    public sealed class SkillTreeRuntimeController
    {
        private readonly SkillTreeRuntimeView _view;
        private readonly SkillTreeGraphData _graph;
        private readonly ISkillDefinitionProvider _definitionProvider;
        private readonly ISkillTreeRuntimeBridge<ResolvedSkillTreeData> _runtimeBridge;

        public SkillTreeRuntimeController(
            SkillTreeRuntimeView view,
            SkillTreeGraphData graph,
            ISkillDefinitionProvider definitionProvider,
            SkillTreeSnapshot snapshot,
            ISkillTreeRuntimeBridge<ResolvedSkillTreeData> runtimeBridge = null)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _definitionProvider = definitionProvider ?? throw new ArgumentNullException(nameof(definitionProvider));
            _runtimeBridge = runtimeBridge;
            CurrentSnapshot = SkillTreeProgressionService.NormalizeSnapshot(_graph, _definitionProvider, snapshot);
        }

        public SkillTreeSnapshot CurrentSnapshot { get; private set; }

        public ResolvedSkillTreeData CurrentResolvedData { get; private set; }

        // 초기 그래프 빌드와 첫 상태 리프레시를 한 번에 맞춘다.
        public void Initialize()
        {
            _view.OnNodeSelected -= HandleNodeSelected;
            _view.OnNodeSelected += HandleNodeSelected;
            _view.Configure(
                _graph,
                _definitionProvider,
                _view.NodePrefab,
                _view.ContentRoot,
                _view.NodeLayer,
                _view.ConnectionGraphic,
                _view.RemovedNodeLayer);
            _view.Build();
            CurrentSnapshot = SkillTreeProgressionService.NormalizeSnapshot(_graph, _definitionProvider, CurrentSnapshot);
            CurrentResolvedData = SkillTreeProgressionService.Resolve(_graph, _definitionProvider, CurrentSnapshot);
            _view.Refresh(CurrentResolvedData);
        }

        // 선택 변경은 저장 스냅샷에도 반영해 이후 업그레이드 기준으로 사용한다.
        public void SelectSkill(string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return;
            }

            CurrentSnapshot = SkillTreeProgressionService.NormalizeSnapshot(_graph, _definitionProvider, CurrentSnapshot);
            CurrentSnapshot.selectedSkillId = skillId.Trim();
            CurrentResolvedData = SkillTreeProgressionService.Resolve(_graph, _definitionProvider, CurrentSnapshot);
            _view.Refresh(CurrentResolvedData);
        }

        // 현재 선택 스킬에 대한 업그레이드만 명시적으로 수행한다.
        public SkillUpgradeResult TryUpgradeSelected()
        {
            var upgradeTargetId = CurrentSnapshot?.selectedSkillId;
            var result = SkillTreeProgressionService.TryUpgrade(_graph, _definitionProvider, CurrentSnapshot, upgradeTargetId);
            CurrentSnapshot = result.updatedSnapshot;
            CurrentResolvedData = result.resolvedData;
            _view.Refresh(CurrentResolvedData);
            return result;
        }

        // 외부 프로젝트가 원할 때만 현재 해석 결과를 브리지에 전달한다.
        public void ApplyResolvedData()
        {
            if (_runtimeBridge == null || CurrentResolvedData == null)
            {
                return;
            }

            _runtimeBridge.Apply(CurrentResolvedData);
        }

        // 기본 클릭 정책은 첫 클릭 선택, 같은 노드 재클릭 시 업그레이드 시도다.
        private void HandleNodeSelected(string nodeId)
        {
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return;
            }

            if (string.Equals(CurrentSnapshot?.selectedSkillId, nodeId, StringComparison.Ordinal))
            {
                TryUpgradeSelected();
                return;
            }

            SelectSkill(nodeId);
        }
    }
}
