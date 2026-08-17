using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace SkillTree.Authoring
{
    public static class SkillTreeJsonService
    {
        public static SkillTreeGraphData CreateDefaultGraph(string treeId = "skill_tree")
        {
            return new SkillTreeGraphData
            {
                schemaVersion = SkillTreeGraphData.CurrentSchemaVersion,
                treeId = string.IsNullOrWhiteSpace(treeId) ? "skill_tree" : treeId.Trim(),
                editorBindings = new SkillTreeEditorBindingsData(),
                nodes = new()
            };
        }

        public static SkillTreeGraphData Deserialize(string json)
        {
            var graph = string.IsNullOrWhiteSpace(json)
                ? CreateDefaultGraph()
                : JsonUtility.FromJson<SkillTreeGraphData>(json);

            return Normalize(graph);
        }

        public static string Serialize(SkillTreeGraphData graph, bool prettyPrint = true)
        {
            var normalized = Clone(Normalize(graph));
            normalized.nodes = normalized.nodes
                .OrderBy(node => node.id, StringComparer.Ordinal)
                .ToList();
            return JsonUtility.ToJson(normalized, prettyPrint);
        }

        public static SkillTreeGraphData LoadFromFile(string path)
        {
            return Deserialize(File.ReadAllText(path));
        }

        public static void SaveToFile(string path, SkillTreeGraphData graph, bool prettyPrint = true)
        {
            File.WriteAllText(path, Serialize(graph, prettyPrint));
        }

        public static SkillTreeGraphData Clone(SkillTreeGraphData graph)
        {
            return Deserialize(JsonUtility.ToJson(Normalize(graph)));
        }

        public static SkillTreeGraphData Normalize(SkillTreeGraphData graph)
        {
            graph ??= CreateDefaultGraph();
            graph.schemaVersion = graph.schemaVersion < SkillTreeGraphData.CurrentSchemaVersion
                ? SkillTreeGraphData.CurrentSchemaVersion
                : graph.schemaVersion;
            graph.treeId = string.IsNullOrWhiteSpace(graph.treeId) ? "skill_tree" : graph.treeId.Trim();
            graph.editorBindings ??= new SkillTreeEditorBindingsData();
            graph.editorBindings.metadataProviderAssetPath = NormalizeAssetPath(graph.editorBindings.metadataProviderAssetPath);
            graph.editorBindings.metadataProviderAssetGuid = NormalizeAssetGuid(graph.editorBindings.metadataProviderAssetGuid);
            graph.nodes ??= new();

            foreach (var node in graph.nodes)
            {
                if (node == null)
                {
                    continue;
                }

                node.id ??= string.Empty;
                node.parentId = string.IsNullOrWhiteSpace(node.parentId) ? null : node.parentId.Trim();
                node.nodeKind = Enum.IsDefined(typeof(SkillTreeNodeKind), node.nodeKind)
                    ? node.nodeKind
                    : SkillTreeNodeKind.Skill;
                node.parentLineType = Enum.IsDefined(typeof(SkillTreeConnectionLineType), node.parentLineType)
                    ? node.parentLineType
                    : SkillTreeConnectionLineType.Curved;
                node.position = new Vector2(
                    Mathf.Max(20f, node.position.x),
                    Mathf.Max(20f, node.position.y));
            }

            graph.nodes.RemoveAll(node => node == null);
            return graph;
        }

        private static string NormalizeAssetPath(string path)
        {
            return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
        }

        private static string NormalizeAssetGuid(string guid)
        {
            return string.IsNullOrWhiteSpace(guid) ? null : guid.Trim();
        }
    }
}
