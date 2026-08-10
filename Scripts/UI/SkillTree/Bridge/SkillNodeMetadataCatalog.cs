using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SkillTree.Authoring
{
    [CreateAssetMenu(fileName = "SkillNodeMetadataCatalog", menuName = "SkillTree/Metadata Catalog")]
    public sealed class SkillNodeMetadataCatalog : ScriptableObject
    {
        [SerializeField] private List<SkillNodeMetadataEntry> entries = new();

        public IReadOnlyList<SkillNodeMetadataEntry> Entries => entries;

        public bool TryGetEntry(string nodeId, out SkillNodeMetadataEntry entry)
        {
            entry = entries.FirstOrDefault(candidate => string.Equals(candidate.nodeId, nodeId, StringComparison.Ordinal));
            return entry != null;
        }

        public SkillNodeMetadataEntry AddEntry(string nodeId)
        {
            var trimmedNodeId = nodeId?.Trim() ?? string.Empty;
            var entry = new SkillNodeMetadataEntry
            {
                nodeId = trimmedNodeId,
                displayName = trimmedNodeId,
                description = string.Empty,
                effectSummary = string.Empty,
                cost = 0,
                maxLevel = 1,
                icon = null
            };

            entries.Add(entry);
            return entry;
        }

        public void SortEntries()
        {
            entries = entries
                .OrderBy(entry => entry?.nodeId, StringComparer.Ordinal)
                .ToList();
        }

        public IReadOnlyList<string> GetKnownNodeIds()
        {
            return entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.nodeId))
                .Select(entry => entry.nodeId)
                .ToList();
        }

        // 카탈로그 전체를 공용 정의 목록으로 변환한다.
        public IReadOnlyList<SkillDefinition> GetDefinitions()
        {
            return entries
                .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.nodeId))
                .Select(entry => entry.ToSkillDefinition())
                .ToList();
        }
    }

    [Serializable]
    public sealed class SkillNodeMetadataEntry
    {
        public string nodeId;
        public string displayName;
        [TextArea(2, 5)] public string description;
        [TextArea(2, 5)] public string effectSummary;
        public int cost;
        public int maxLevel = 1;
        public Sprite icon;

        public SkillNodeMetadata ToMetadata()
        {
            return new SkillNodeMetadata
            {
                nodeId = nodeId,
                displayName = displayName,
                description = description,
                effectSummary = effectSummary,
                cost = cost,
                maxLevel = maxLevel,
                icon = icon
            };
        }

        // authoring entry를 런타임 계산용 정의로 변환한다.
        public SkillDefinition ToSkillDefinition()
        {
            return new SkillDefinition
            {
                skillId = nodeId,
                displayName = displayName,
                description = description,
                effectSummary = effectSummary,
                cost = cost,
                maxLevel = maxLevel,
                icon = icon
            };
        }
    }
}
