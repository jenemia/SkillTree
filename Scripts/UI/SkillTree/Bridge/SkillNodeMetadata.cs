using System;
using UnityEngine;

namespace SkillTree.Authoring
{
    [Serializable]
    public sealed class SkillNodeMetadata
    {
        public string nodeId;
        public string displayName;
        public string description;
        public string effectSummary;
        public int cost;
        public int maxLevel = 1;
        public Sprite icon;

        // 기존 authoring 메타데이터를 공용 정의 계약으로 변환한다.
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
