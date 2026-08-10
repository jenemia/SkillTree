using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillTree.Authoring
{
    public abstract class SkillNodeMetadataProviderAsset : ScriptableObject, ISkillNodeMetadataProvider
    {
        public abstract bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata);

        public virtual IReadOnlyList<string> GetKnownNodeIds()
        {
            return Array.Empty<string>();
        }

        // 기존 metadata provider를 공용 정의 provider로 어댑터링한다.
        public virtual bool TryGetDefinition(string skillId, out SkillDefinition definition)
        {
            definition = null;
            if (!TryGetMetadata(skillId, out var metadata) || metadata == null)
            {
                return false;
            }

            definition = metadata.ToSkillDefinition();
            return true;
        }

        // 정의 식별자 목록은 기존 node id 목록을 그대로 재사용한다.
        public virtual IReadOnlyList<string> GetKnownSkillIds()
        {
            return GetKnownNodeIds();
        }
    }
}
