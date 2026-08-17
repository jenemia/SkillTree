using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillTree.Authoring
{
    public enum SkillTreeConnectionLineType
    {
        Curved = 0,
        Straight = 1
    }

    public enum SkillTreeNodeKind
    {
        Skill = 0,
        Hub = 1
    }

    [Serializable]
    public sealed class SkillTreeGraphData
    {
        public const int CurrentSchemaVersion = 5;

        public int schemaVersion = CurrentSchemaVersion;
        public string treeId = "skill_tree";
        public SkillTreeEditorBindingsData editorBindings = new();
        public List<SkillTreeNodeRecord> nodes = new();
    }

    [Serializable]
    public sealed class SkillTreeEditorBindingsData
    {
        public string metadataProviderAssetPath;
        public string metadataProviderAssetGuid;
    }

    [Serializable]
    public sealed class SkillTreeNodeRecord
    {
        public string id;
        public string parentId;
        public SkillTreeNodeKind nodeKind = SkillTreeNodeKind.Skill;
        public SkillTreeConnectionLineType parentLineType = SkillTreeConnectionLineType.Curved;
        public Vector2 position = new(100f, 100f);
    }
}
