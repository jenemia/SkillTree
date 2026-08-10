using System.Collections.Generic;

namespace SkillTree.Authoring
{
    public interface ISkillDefinitionProvider
    {
        bool TryGetDefinition(string skillId, out SkillDefinition definition);
        IReadOnlyList<string> GetKnownSkillIds();
    }

    public interface ISkillNodeMetadataProvider : ISkillDefinitionProvider
    {
        bool TryGetMetadata(string nodeId, out SkillNodeMetadata metadata);
        IReadOnlyList<string> GetKnownNodeIds();
    }
}
