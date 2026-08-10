using System.Collections.Generic;
using UnityEngine;

namespace SkillTree.Authoring
{
    [CreateAssetMenu(fileName = "SkillNodeMetadataProvider", menuName = "SkillTree/Metadata Provider")]
    public sealed class ScriptableObjectSkillNodeMetadataProvider : SkillNodeMetadataProviderAsset
    {
        [SerializeField] private SkillNodeMetadataCatalog catalog;

        public SkillNodeMetadataCatalog Catalog => catalog;

        public void BindCatalog(SkillNodeMetadataCatalog value)
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

            if (!catalog.TryGetEntry(nodeId, out var entry))
            {
                return false;
            }

            metadata = entry.ToMetadata();
            return true;
        }

        public override IReadOnlyList<string> GetKnownNodeIds()
        {
            return catalog == null ? base.GetKnownNodeIds() : catalog.GetKnownNodeIds();
        }

        // 카탈로그가 있는 경우 정의 조회를 직접 처리한다.
        public override bool TryGetDefinition(string skillId, out SkillDefinition definition)
        {
            definition = null;
            if (catalog == null || string.IsNullOrWhiteSpace(skillId))
            {
                return false;
            }

            if (!catalog.TryGetEntry(skillId, out var entry))
            {
                return false;
            }

            definition = entry.ToSkillDefinition();
            return true;
        }

        // 정의 식별자 목록은 카탈로그 기준으로 노출한다.
        public override IReadOnlyList<string> GetKnownSkillIds()
        {
            return catalog == null ? base.GetKnownSkillIds() : catalog.GetKnownNodeIds();
        }
    }
}
