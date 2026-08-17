using System;
using System.Collections.Generic;
using System.Linq;

namespace SkillTree.Authoring
{
    public static class SkillTreeProgressionService
    {
        // 그래프와 저장 스냅샷 기준으로 누락 상태와 선택 값을 보정한다.
        public static SkillTreeSnapshot NormalizeSnapshot(
            SkillTreeGraphData graph,
            ISkillDefinitionProvider provider,
            SkillTreeSnapshot snapshot)
        {
            var normalizedGraph = SkillTreeJsonService.Normalize(SkillTreeJsonService.Clone(graph));
            var normalizedSnapshot = CloneSnapshot(snapshot);
            var knownNodeIds = new HashSet<string>(
                normalizedGraph.nodes
                    .Where(node => node != null && node.nodeKind != SkillTreeNodeKind.Hub && !string.IsNullOrWhiteSpace(node.id))
                    .Select(node => node.id),
                StringComparer.Ordinal);

            normalizedSnapshot.schemaVersion = SkillTreeSnapshot.CurrentSchemaVersion;
            normalizedSnapshot.treeId = normalizedGraph.treeId;
            normalizedSnapshot.userSkills ??= new List<UserSkillState>();

            var stateById = new Dictionary<string, UserSkillState>(StringComparer.Ordinal);
            foreach (var state in normalizedSnapshot.userSkills)
            {
                if (state == null || string.IsNullOrWhiteSpace(state.skillId) || !knownNodeIds.Contains(state.skillId))
                {
                    continue;
                }

                state.skillId = state.skillId.Trim();
                state.level = Math.Max(0, state.level);
                state.isUnlocked = state.isUnlocked || state.level > 0;
                stateById[state.skillId] = state;
            }

            normalizedSnapshot.userSkills = normalizedGraph.nodes
                .Where(node => node != null && node.nodeKind != SkillTreeNodeKind.Hub && !string.IsNullOrWhiteSpace(node.id))
                .Select(node =>
                {
                    if (stateById.TryGetValue(node.id, out var existingState))
                    {
                        return existingState;
                    }

                    return new UserSkillState
                    {
                        skillId = node.id,
                        level = 0,
                        isUnlocked = false
                    };
                })
                .ToList();

            if (string.IsNullOrWhiteSpace(normalizedSnapshot.selectedSkillId) ||
                !knownNodeIds.Contains(normalizedSnapshot.selectedSkillId))
            {
                normalizedSnapshot.selectedSkillId = normalizedGraph.nodes
                    .FirstOrDefault(node => node != null && node.nodeKind != SkillTreeNodeKind.Hub &&
                        string.IsNullOrWhiteSpace(node.parentId))?.id ??
                    normalizedGraph.nodes.FirstOrDefault(node => node != null && node.nodeKind != SkillTreeNodeKind.Hub)?.id;
            }
            else
            {
                normalizedSnapshot.selectedSkillId = normalizedSnapshot.selectedSkillId.Trim();
            }

            return normalizedSnapshot;
        }

        // 정적 정의와 유저 상태를 합쳐 뷰/브리지용 해석 결과를 만든다.
        public static ResolvedSkillTreeData Resolve(
            SkillTreeGraphData graph,
            ISkillDefinitionProvider provider,
            SkillTreeSnapshot snapshot)
        {
            var normalizedGraph = SkillTreeJsonService.Normalize(SkillTreeJsonService.Clone(graph));
            var normalizedSnapshot = NormalizeSnapshot(normalizedGraph, provider, snapshot);
            var definitionsById = BuildDefinitions(normalizedGraph, provider);
            var statesById = normalizedSnapshot.userSkills.ToDictionary(state => state.skillId, StringComparer.Ordinal);
            var resolved = new ResolvedSkillTreeData
            {
                treeId = normalizedSnapshot.treeId,
                selectedSkillId = normalizedSnapshot.selectedSkillId,
                currencyBalance = normalizedSnapshot.currencyBalance
            };

            foreach (var node in normalizedGraph.nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.id))
                {
                    continue;
                }

                var definition = definitionsById[node.id];
                var state = node.nodeKind == SkillTreeNodeKind.Hub
                    ? new UserSkillState { skillId = node.id, level = 0, isUnlocked = true }
                    : CloneState(statesById[node.id]);
                var parentReady = IsParentRequirementSatisfied(node.parentId, normalizedGraph, statesById);
                var isHub = node.nodeKind == SkillTreeNodeKind.Hub;
                var isLocked = !isHub && !parentReady;
                var currentLevel = Math.Max(0, state.level);
                var maxLevel = isHub ? 0 : Math.Max(1, definition.maxLevel);
                var cost = Math.Max(0, definition.cost);
                var isUnlocked = isHub || state.isUnlocked || currentLevel > 0;
                var isMaxed = !isHub && currentLevel >= maxLevel;
                var isAffordable = normalizedSnapshot.currencyBalance >= (uint)cost;
                var progressState = isHub
                    ? SkillNodeProgressState.Open
                    : isLocked
                    ? SkillNodeProgressState.Locked
                    : isMaxed
                        ? SkillNodeProgressState.Maxed
                        : currentLevel > 0
                            ? SkillNodeProgressState.Purchased
                            : SkillNodeProgressState.Open;
                var status = new SkillStatusData
                {
                    skillId = node.id,
                    isPurchasable = !isHub,
                    progressState = progressState,
                    isLocked = isLocked,
                    isUnlocked = isUnlocked,
                    isAffordable = isAffordable,
                    isMaxed = isMaxed,
                    canUpgrade = !isHub && !isLocked && !isMaxed && isAffordable,
                    currentLevel = currentLevel,
                    maxLevel = maxLevel,
                    cost = cost,
                    prerequisiteSummary = isHub ? string.Empty : BuildPrerequisiteSummary(node.parentId, definitionsById, parentReady),
                    affordabilitySummary = BuildAffordabilitySummary(cost, normalizedSnapshot.currencyBalance, isAffordable)
                };

                resolved.userSkills.Add(new UserSkillData
                {
                    definition = CloneDefinition(definition),
                    state = state
                });
                resolved.skillStatuses.Add(status);
            }

            return resolved;
        }

        // 특정 스킬의 현재 계산 상태만 빠르게 조회한다.
        public static SkillStatusData DescribeSkill(
            SkillTreeGraphData graph,
            ISkillDefinitionProvider provider,
            SkillTreeSnapshot snapshot,
            string skillId)
        {
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return null;
            }

            return Resolve(graph, provider, snapshot)
                .skillStatuses
                .FirstOrDefault(candidate => string.Equals(candidate.skillId, skillId.Trim(), StringComparison.Ordinal));
        }

        // 선택된 스킬에 대한 업그레이드 시도 결과를 일관된 형식으로 돌려준다.
        public static SkillUpgradeResult TryUpgrade(
            SkillTreeGraphData graph,
            ISkillDefinitionProvider provider,
            SkillTreeSnapshot snapshot,
            string skillId)
        {
            var normalizedSnapshot = NormalizeSnapshot(graph, provider, snapshot);
            var resolved = Resolve(graph, provider, normalizedSnapshot);
            var selectedStatus = resolved.skillStatuses
                .FirstOrDefault(candidate => string.Equals(candidate.skillId, skillId?.Trim(), StringComparison.Ordinal));

            if (selectedStatus == null)
            {
                return CreateFailureResult(normalizedSnapshot, resolved, SkillUpgradeFailureReason.UnknownSkill);
            }

            if (!selectedStatus.isPurchasable)
            {
                return CreateFailureResult(normalizedSnapshot, resolved, SkillUpgradeFailureReason.UnknownSkill);
            }

            if (selectedStatus.isLocked)
            {
                return CreateFailureResult(normalizedSnapshot, resolved, SkillUpgradeFailureReason.Locked);
            }

            if (selectedStatus.isMaxed)
            {
                return CreateFailureResult(normalizedSnapshot, resolved, SkillUpgradeFailureReason.MaxLevelReached);
            }

            if (!selectedStatus.isAffordable)
            {
                return CreateFailureResult(normalizedSnapshot, resolved, SkillUpgradeFailureReason.InsufficientCurrency);
            }

            var upgradedSnapshot = CloneSnapshot(normalizedSnapshot);
            var upgradedState = upgradedSnapshot.userSkills
                .First(state => string.Equals(state.skillId, selectedStatus.skillId, StringComparison.Ordinal));
            upgradedState.level += 1;
            upgradedState.isUnlocked = true;
            upgradedSnapshot.currencyBalance -= (uint)selectedStatus.cost;
            upgradedSnapshot.selectedSkillId = selectedStatus.skillId;

            var resolvedAfterUpgrade = Resolve(graph, provider, upgradedSnapshot);
            return new SkillUpgradeResult
            {
                updatedSnapshot = NormalizeSnapshot(graph, provider, upgradedSnapshot),
                resolvedData = resolvedAfterUpgrade,
                status = SkillUpgradeResultStatus.Success,
                failureReason = SkillUpgradeFailureReason.None
            };
        }

        // 계산 중 null 정의가 생기지 않도록 최소 기본값을 채운다.
        private static Dictionary<string, SkillDefinition> BuildDefinitions(
            SkillTreeGraphData graph,
            ISkillDefinitionProvider provider)
        {
            var definitions = new Dictionary<string, SkillDefinition>(StringComparer.Ordinal);
            foreach (var node in graph.nodes)
            {
                if (node == null || string.IsNullOrWhiteSpace(node.id))
                {
                    continue;
                }

                if (node.nodeKind == SkillTreeNodeKind.Hub)
                {
                    definitions[node.id] = new SkillDefinition
                    {
                        skillId = node.id,
                        displayName = string.Empty,
                        description = string.Empty,
                        effectSummary = string.Empty,
                        cost = 0,
                        maxLevel = 0,
                        icon = null
                    };
                    continue;
                }

                if (provider != null && provider.TryGetDefinition(node.id, out var resolvedDefinition) && resolvedDefinition != null)
                {
                    definitions[node.id] = CloneDefinition(resolvedDefinition);
                    definitions[node.id].skillId = node.id;
                    definitions[node.id].maxLevel = Math.Max(1, definitions[node.id].maxLevel);
                    definitions[node.id].cost = Math.Max(0, definitions[node.id].cost);
                    continue;
                }

                definitions[node.id] = new SkillDefinition
                {
                    skillId = node.id,
                    displayName = node.id,
                    description = string.Empty,
                    effectSummary = string.Empty,
                    cost = 0,
                    maxLevel = 1,
                    icon = null
                };
            }

            return definitions;
        }

        // 부모 관계가 충족된 경우에만 자식 업그레이드를 허용한다.
        private static bool IsParentRequirementSatisfied(
            string parentId,
            SkillTreeGraphData graph,
            IReadOnlyDictionary<string, UserSkillState> statesById)
        {
            if (string.IsNullOrWhiteSpace(parentId))
            {
                return true;
            }

            // 저장된 이전 노드 레벨을 루트까지 확인해 그래프 순서와 isUnlocked 플래그에 의존하지 않는다.
            var nodesById = graph.nodes
                .Where(node => node != null && !string.IsNullOrWhiteSpace(node.id))
                .ToDictionary(node => node.id, StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var currentId = parentId;
            while (!string.IsNullOrWhiteSpace(currentId))
            {
                if (!visited.Add(currentId) || !nodesById.TryGetValue(currentId, out var parentNode))
                {
                    return false;
                }

                if (parentNode.nodeKind == SkillTreeNodeKind.Hub)
                {
                    return true;
                }

                if (!statesById.TryGetValue(currentId, out var parentState) || parentState.level < 1)
                {
                    return false;
                }

                currentId = parentNode.parentId;
            }

            return true;
        }

        // 선행 조건 문구는 외부 UI가 바로 표시할 수 있는 기본 텍스트로 맞춘다.
        private static string BuildPrerequisiteSummary(
            string parentId,
            IReadOnlyDictionary<string, SkillDefinition> definitionsById,
            bool parentReady)
        {
            if (string.IsNullOrWhiteSpace(parentId))
            {
                return "Root skill";
            }

            if (parentReady)
            {
                return "Ready";
            }

            var parentName = definitionsById.TryGetValue(parentId, out var parentDefinition) &&
                             !string.IsNullOrWhiteSpace(parentDefinition.displayName)
                ? parentDefinition.displayName
                : parentId;
            return $"Requires {parentName}";
        }

        // 비용 문구는 재화 부족 여부를 빠르게 읽을 수 있게 만든다.
        private static string BuildAffordabilitySummary(int cost, uint currencyBalance, bool isAffordable)
        {
            if (isAffordable)
            {
                return "Affordable";
            }

            return $"Need {(uint)cost - currencyBalance} more";
        }

        // 업그레이드 실패 응답도 동일한 스냅샷/해석 결과 형태를 유지한다.
        private static SkillUpgradeResult CreateFailureResult(
            SkillTreeSnapshot snapshot,
            ResolvedSkillTreeData resolved,
            SkillUpgradeFailureReason failureReason)
        {
            return new SkillUpgradeResult
            {
                updatedSnapshot = CloneSnapshot(snapshot),
                resolvedData = resolved,
                status = SkillUpgradeResultStatus.Failed,
                failureReason = failureReason
            };
        }

        // 계산 중 원본 저장 데이터가 바뀌지 않도록 스냅샷을 복제한다.
        private static SkillTreeSnapshot CloneSnapshot(SkillTreeSnapshot snapshot)
        {
            snapshot ??= new SkillTreeSnapshot();
            return new SkillTreeSnapshot
            {
                schemaVersion = snapshot.schemaVersion < SkillTreeSnapshot.CurrentSchemaVersion
                    ? SkillTreeSnapshot.CurrentSchemaVersion
                    : snapshot.schemaVersion,
                treeId = string.IsNullOrWhiteSpace(snapshot.treeId) ? "skill_tree" : snapshot.treeId.Trim(),
                selectedSkillId = string.IsNullOrWhiteSpace(snapshot.selectedSkillId) ? null : snapshot.selectedSkillId.Trim(),
                currencyBalance = snapshot.currencyBalance,
                userSkills = snapshot.userSkills?
                    .Where(state => state != null)
                    .Select(CloneState)
                    .ToList() ?? new List<UserSkillState>()
            };
        }

        // 유저 상태는 정의 데이터와 분리된 값 객체로 복제한다.
        private static UserSkillState CloneState(UserSkillState state)
        {
            state ??= new UserSkillState();
            return new UserSkillState
            {
                skillId = string.IsNullOrWhiteSpace(state.skillId) ? string.Empty : state.skillId.Trim(),
                level = Math.Max(0, state.level),
                isUnlocked = state.isUnlocked || state.level > 0
            };
        }

        // 정적 정의도 복제해서 런타임 계산 결과를 안전하게 유지한다.
        private static SkillDefinition CloneDefinition(SkillDefinition definition)
        {
            definition ??= new SkillDefinition();
            return new SkillDefinition
            {
                skillId = string.IsNullOrWhiteSpace(definition.skillId) ? string.Empty : definition.skillId.Trim(),
                displayName = definition.displayName,
                description = definition.description,
                effectSummary = definition.effectSummary,
                cost = Math.Max(0, definition.cost),
                maxLevel = Math.Max(1, definition.maxLevel),
                icon = definition.icon
            };
        }
    }
}
