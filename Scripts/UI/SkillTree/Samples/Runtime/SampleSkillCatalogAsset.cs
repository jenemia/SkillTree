using System;
using System.Collections.Generic;
using System.Linq;
using SkillTree.Authoring;
using UnityEngine;

namespace SkillTree.Authoring.Samples
{
    [CreateAssetMenu(fileName = "SampleSkillCatalog", menuName = "SkillTree/Samples/ScriptableObject Skill Catalog")]
    public sealed class SampleSkillCatalogAsset : ScriptableObject
    {
        [SerializeField] private string treeId = "scriptableobject_catalog_sample";
        [SerializeField] private List<SampleSkillData> skills = new();

        public string TreeId => string.IsNullOrWhiteSpace(treeId) ? string.Empty : treeId.Trim();
        public IReadOnlyList<SampleSkillData> Skills => skills;

        public void Configure(string value, IEnumerable<SampleSkillData> entries)
        {
            treeId = string.IsNullOrWhiteSpace(value) ? "scriptableobject_catalog_sample" : value.Trim();
            skills = entries == null
                ? new List<SampleSkillData>()
                : entries
                    .Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.skillId))
                    .Select(entry => entry.Clone())
                    .ToList();
        }

        public bool TryGetSkill(string skillId, out SampleSkillData skillData)
        {
            skillData = null;
            if (string.IsNullOrWhiteSpace(skillId))
            {
                return false;
            }

            var trimmedSkillId = skillId.Trim();
            skillData = skills.FirstOrDefault(candidate =>
                candidate != null &&
                string.Equals(candidate.skillId, trimmedSkillId, StringComparison.Ordinal));
            return skillData != null;
        }

        public IReadOnlyList<string> GetKnownSkillIds()
        {
            return skills
                .Where(skill => skill != null && !string.IsNullOrWhiteSpace(skill.skillId))
                .Select(skill => skill.skillId.Trim())
                .ToList();
        }
    }

    [Serializable]
    public sealed class SampleSkillData
    {
        public string skillId;
        public string displayName;
        [TextArea(2, 5)] public string description;
        [TextArea(2, 5)] public string effectSummary;
        public int cost;
        public int maxLevel = 1;
        public Sprite icon;

        public SampleSkillData Clone()
        {
            return new SampleSkillData
            {
                skillId = skillId,
                displayName = displayName,
                description = description,
                effectSummary = effectSummary,
                cost = cost,
                maxLevel = maxLevel,
                icon = icon
            };
        }

        public SkillDefinition ToSkillDefinition()
        {
            return new SkillDefinition
            {
                skillId = skillId,
                displayName = displayName,
                description = description,
                effectSummary = effectSummary,
                cost = cost,
                maxLevel = maxLevel,
                icon = icon
            };
        }

        public SkillNodeMetadata ToMetadata()
        {
            return new SkillNodeMetadata
            {
                nodeId = skillId,
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
