using System;
using System.Collections.Generic;
using System.Linq;
using SkillTree.Authoring;
using UnityEngine;

namespace SkillTree.Authoring.Samples
{
    [CreateAssetMenu(fileName = "SampleSkillCatalogProvider", menuName = "SkillTree/Samples/ScriptableObject Sample Provider")]
    public sealed class SampleSkillCatalogProviderAsset : SkillNodeMetadataProviderAsset
    {
        [SerializeField] private SampleSkillCatalogAsset catalog;

        public SampleSkillCatalogAsset Catalog => catalog;
        public string TreeId => catalog == null ? string.Empty : catalog.TreeId;

        public void BindCatalog(SampleSkillCatalogAsset value)
        {
            catalog = value;
        }

        public override bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata)
        {
            metadata = null;
            if (catalog == null || string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            if (!catalog.TryGetSkill(nodeId, out var skillData))
            {
                return false;
            }

            metadata = skillData.ToMetadata();
            return true;
        }

        public override bool TryGetDefinition(string skillId, out SkillDefinition definition)
        {
            definition = null;
            if (catalog == null || string.IsNullOrWhiteSpace(skillId))
            {
                return false;
            }

            if (!catalog.TryGetSkill(skillId, out var skillData))
            {
                return false;
            }

            definition = skillData.ToSkillDefinition();
            return true;
        }

        public override IReadOnlyList<string> GetKnownNodeIds()
        {
            return catalog == null ? base.GetKnownNodeIds() : catalog.GetKnownSkillIds();
        }

        public override IReadOnlyList<string> GetKnownSkillIds()
        {
            return catalog == null ? base.GetKnownSkillIds() : catalog.GetKnownSkillIds();
        }

        public void ValidateGraphOrThrow(SkillTreeGraphData graph)
        {
            if (catalog == null)
            {
                throw new InvalidOperationException("Sample skill catalog provider requires a bound catalog asset.");
            }

            var normalizedGraph = SkillTreeJsonService.Normalize(SkillTreeJsonService.Clone(graph));
            if (!string.Equals(normalizedGraph.treeId, TreeId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Sample skill catalog tree mismatch. Graph treeId '{normalizedGraph.treeId}' does not match catalog treeId '{TreeId}'.");
            }

            var knownSkillIds = new HashSet<string>(GetKnownSkillIds(), StringComparer.Ordinal);
            var missingSkillIds = normalizedGraph.nodes
                .Where(node => node != null && !string.IsNullOrWhiteSpace(node.id))
                .Select(node => node.id)
                .Where(nodeId => !knownSkillIds.Contains(nodeId))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            if (missingSkillIds.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Sample skill catalog provider is missing definitions for graph nodes: {string.Join(", ", missingSkillIds)}");
            }
        }
    }
}
