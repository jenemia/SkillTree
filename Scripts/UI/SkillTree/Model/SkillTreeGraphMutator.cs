using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SkillTree.Authoring
{
    public static class SkillTreeGraphMutator
    {
        public static SkillTreeNodeRecord AddNode(SkillTreeGraphData graph, Vector2 position)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            var node = new SkillTreeNodeRecord
            {
                id = GenerateUniqueNodeId(graph),
                parentId = null,
                position = new Vector2(Mathf.Max(20f, position.x), Mathf.Max(20f, position.y))
            };

            graph.nodes.Add(node);
            return node;
        }

        public static bool DeleteNode(SkillTreeGraphData graph, string nodeId)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            var node = FindNode(graph, nodeId);
            if (node == null)
            {
                return false;
            }

            foreach (var child in graph.nodes.Where(candidate => candidate.parentId == nodeId))
            {
                child.parentId = null;
            }

            graph.nodes.Remove(node);
            return true;
        }

        public static bool RenameNode(SkillTreeGraphData graph, string currentId, string newId)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            if (string.IsNullOrWhiteSpace(currentId) || string.IsNullOrWhiteSpace(newId))
            {
                return false;
            }

            newId = newId.Trim();
            if (!string.Equals(currentId, newId, StringComparison.Ordinal) &&
                graph.nodes.Any(node => string.Equals(node.id, newId, StringComparison.Ordinal)))
            {
                return false;
            }

            var node = FindNode(graph, currentId);
            if (node == null)
            {
                return false;
            }

            node.id = newId;

            foreach (var child in graph.nodes.Where(candidate => candidate.parentId == currentId))
            {
                child.parentId = newId;
            }

            return true;
        }

        public static bool SetParent(SkillTreeGraphData graph, string nodeId, string parentId, out string errorMessage)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            if (!SkillTreeGraphValidator.CanAssignParent(graph, nodeId, parentId, out errorMessage))
            {
                return false;
            }

            var node = FindNode(graph, nodeId);
            if (node == null)
            {
                errorMessage = "노드를 찾을 수 없습니다.";
                return false;
            }

            node.parentId = string.IsNullOrWhiteSpace(parentId) ? null : parentId.Trim();
            errorMessage = null;
            return true;
        }

        public static bool MoveNode(SkillTreeGraphData graph, string nodeId, Vector2 position)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            var node = FindNode(graph, nodeId);
            if (node == null)
            {
                return false;
            }

            node.position = new Vector2(Mathf.Max(20f, position.x), Mathf.Max(20f, position.y));
            return true;
        }

        public static SkillTreeNodeRecord FindNode(SkillTreeGraphData graph, string nodeId)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            return graph.nodes.FirstOrDefault(node => string.Equals(node.id, nodeId, StringComparison.Ordinal));
        }

        public static string GenerateUniqueNodeId(SkillTreeGraphData graph)
        {
            graph = SkillTreeJsonService.Normalize(graph);
            var existingIds = new HashSet<string>(
                graph.nodes
                    .Where(node => !string.IsNullOrWhiteSpace(node.id))
                    .Select(node => node.id),
                StringComparer.Ordinal);

            for (var index = 1; index < 10_000; index += 1)
            {
                var candidate = $"node_{index:000}";
                if (!existingIds.Contains(candidate))
                {
                    return candidate;
                }
            }

            return $"node_{Guid.NewGuid():N}";
        }
    }
}
