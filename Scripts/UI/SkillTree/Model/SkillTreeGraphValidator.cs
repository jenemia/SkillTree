using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SkillTree.Authoring
{
    public enum SkillTreeValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    [Serializable]
    public sealed class SkillTreeValidationIssue
    {
        public SkillTreeValidationSeverity severity;
        public string code;
        public string message;
        public string nodeId;
    }

    public static class SkillTreeGraphValidator
    {
        public static IReadOnlyList<SkillTreeValidationIssue> Validate(
            SkillTreeGraphData graph,
            ISkillNodeMetadataProvider metadataProvider = null)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            var issues = new List<SkillTreeValidationIssue>();

            if (graph.schemaVersion != SkillTreeGraphData.CurrentSchemaVersion)
            {
                issues.Add(new SkillTreeValidationIssue
                {
                    severity = SkillTreeValidationSeverity.Warning,
                    code = "SchemaVersionMismatch",
                    message = $"예상 스키마 {SkillTreeGraphData.CurrentSchemaVersion}, 현재 {graph.schemaVersion}",
                    nodeId = string.Empty
                });
            }

            var idGroups = graph.nodes
                .GroupBy(node => node.id ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            foreach (var group in idGroups.Where(group => string.IsNullOrWhiteSpace(group.Key)))
            {
                foreach (var node in group)
                {
                    issues.Add(new SkillTreeValidationIssue
                    {
                        severity = SkillTreeValidationSeverity.Error,
                        code = "MissingNodeId",
                        message = "노드 ID가 비어 있습니다.",
                        nodeId = node.id ?? string.Empty
                    });
                }
            }

            foreach (var duplicateGroup in idGroups.Where(group => !string.IsNullOrWhiteSpace(group.Key) && group.Count() > 1))
            {
                issues.Add(new SkillTreeValidationIssue
                {
                    severity = SkillTreeValidationSeverity.Error,
                    code = "DuplicateNodeId",
                    message = $"중복된 노드 ID: {duplicateGroup.Key}",
                    nodeId = duplicateGroup.Key
                });
            }

            var lookup = graph.nodes
                .Where(node => !string.IsNullOrWhiteSpace(node.id))
                .GroupBy(node => node.id, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

            foreach (var node in graph.nodes)
            {
                if (string.IsNullOrWhiteSpace(node.parentId))
                {
                    ValidateMetadata(node, metadataProvider, issues);
                    continue;
                }

                if (!lookup.ContainsKey(node.parentId))
                {
                    issues.Add(new SkillTreeValidationIssue
                    {
                        severity = SkillTreeValidationSeverity.Error,
                        code = "MissingParent",
                        message = $"상위 노드를 찾을 수 없습니다: {node.parentId}",
                        nodeId = node.id
                    });
                }
                else if (string.Equals(node.id, node.parentId, StringComparison.Ordinal))
                {
                    issues.Add(new SkillTreeValidationIssue
                    {
                        severity = SkillTreeValidationSeverity.Error,
                        code = "SelfParent",
                        message = "자기 자신을 부모로 지정할 수 없습니다.",
                        nodeId = node.id
                    });
                }

                ValidateMetadata(node, metadataProvider, issues);
            }

            foreach (var node in graph.nodes)
            {
                if (string.IsNullOrWhiteSpace(node.id))
                {
                    continue;
                }

                if (DetectCycle(node.id, lookup))
                {
                    issues.Add(new SkillTreeValidationIssue
                    {
                        severity = SkillTreeValidationSeverity.Error,
                        code = "CycleDetected",
                        message = $"순환 연결이 감지되었습니다: {node.id}",
                        nodeId = node.id
                    });
                }
            }

            if (metadataProvider != null)
            {
                var graphNodeIds = new HashSet<string>(lookup.Keys, StringComparer.Ordinal);
                var staleMetadataNodeIds = metadataProvider.GetKnownNodeIds()
                    .Where(nodeId => !string.IsNullOrWhiteSpace(nodeId))
                    .Distinct(StringComparer.Ordinal)
                    .Where(nodeId => !graphNodeIds.Contains(nodeId));

                foreach (var staleMetadataNodeId in staleMetadataNodeIds)
                {
                    issues.Add(new SkillTreeValidationIssue
                    {
                        severity = SkillTreeValidationSeverity.Warning,
                        code = "UnusedMetadata",
                        message = $"그래프에 없는 메타데이터가 남아 있습니다: {staleMetadataNodeId}",
                        nodeId = staleMetadataNodeId
                    });
                }
            }

            return issues;
        }

        public static bool CanAssignParent(
            SkillTreeGraphData graph,
            string nodeId,
            string proposedParentId,
            out string errorMessage)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                errorMessage = "노드 ID가 필요합니다.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(proposedParentId))
            {
                return true;
            }

            if (string.Equals(nodeId, proposedParentId, StringComparison.Ordinal))
            {
                errorMessage = "자기 자신을 부모로 지정할 수 없습니다.";
                return false;
            }

            var node = SkillTreeGraphMutator.FindNode(graph, nodeId);
            var parent = SkillTreeGraphMutator.FindNode(graph, proposedParentId);
            if (node == null || parent == null)
            {
                errorMessage = "부모 연결 대상이 유효하지 않습니다.";
                return false;
            }

            var current = parent;
            while (current != null)
            {
                if (string.Equals(current.id, nodeId, StringComparison.Ordinal))
                {
                    errorMessage = "후손 노드를 부모로 지정하면 순환이 발생합니다.";
                    return false;
                }

                current = string.IsNullOrWhiteSpace(current.parentId)
                    ? null
                    : SkillTreeGraphMutator.FindNode(graph, current.parentId);
            }

            return true;
        }

        private static void ValidateMetadata(
            SkillTreeNodeRecord node,
            ISkillNodeMetadataProvider metadataProvider,
            List<SkillTreeValidationIssue> issues)
        {
            SkillNodeMetadata metadata = null;
            if (metadataProvider != null && !string.IsNullOrWhiteSpace(node.id))
            {
                metadataProvider.TryGetMetadata(node.id, out metadata);
                if (metadata == null)
                {
                    issues.Add(new SkillTreeValidationIssue
                    {
                        severity = SkillTreeValidationSeverity.Warning,
                        code = "MissingMetadata",
                        message = $"메타데이터를 찾을 수 없습니다: {node.id}",
                        nodeId = node.id
                    });
                }
            }

            if (metadata == null || metadata.icon == null)
            {
                issues.Add(new SkillTreeValidationIssue
                {
                    severity = SkillTreeValidationSeverity.Warning,
                    code = "MissingIcon",
                    message = "메타데이터 아이콘이 비어 있습니다.",
                    nodeId = node.id
                });
            }
        }

        private static bool DetectCycle(
            string startNodeId,
            IReadOnlyDictionary<string, SkillTreeNodeRecord> lookup)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var currentId = startNodeId;
            while (!string.IsNullOrWhiteSpace(currentId) && lookup.TryGetValue(currentId, out var node))
            {
                if (!visited.Add(currentId))
                {
                    return true;
                }

                currentId = node.parentId;
            }

            return false;
        }
    }
}
