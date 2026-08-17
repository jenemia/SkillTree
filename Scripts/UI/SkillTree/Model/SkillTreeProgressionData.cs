using System;
using System.Collections.Generic;
using UnityEngine;

namespace SkillTree.Authoring
{
    [Serializable]
    public sealed class SkillDefinition
    {
        public string skillId;
        public string displayName;
        public string description;
        public string effectSummary;
        public int cost;
        public int maxLevel = 1;
        public Sprite icon;
    }

    [Serializable]
    public sealed class UserSkillState
    {
        public string skillId;
        public int level;
        public bool isUnlocked;
    }

    [Serializable]
    public sealed class SkillTreeSnapshot
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public string treeId = "skill_tree";
        public string selectedSkillId;
        public uint currencyBalance;
        public List<UserSkillState> userSkills = new();
    }

    [Serializable]
    public sealed class UserSkillData
    {
        public SkillDefinition definition;
        public UserSkillState state;
    }

    [Serializable]
    public enum SkillNodeProgressState
    {
        Locked = 0,
        Open = 1,
        Purchased = 2,
        Maxed = 3
    }

    [Serializable]
    public sealed class SkillStatusData
    {
        public string skillId;
        public bool isPurchasable = true;
        public SkillNodeProgressState progressState;
        public bool isLocked;
        public bool isUnlocked;
        public bool isAffordable;
        public bool isMaxed;
        public bool canUpgrade;
        public int currentLevel;
        public int maxLevel;
        public int cost;
        public string prerequisiteSummary;
        public string affordabilitySummary;
    }

    [Serializable]
    public sealed class ResolvedSkillTreeData
    {
        public string treeId;
        public string selectedSkillId;
        public uint currencyBalance;
        public List<UserSkillData> userSkills = new();
        public List<SkillStatusData> skillStatuses = new();
    }

    public enum SkillUpgradeResultStatus
    {
        Failed = 0,
        Success = 1
    }

    public enum SkillUpgradeFailureReason
    {
        None = 0,
        UnknownSkill = 1,
        Locked = 2,
        MaxLevelReached = 3,
        InsufficientCurrency = 4
    }

    [Serializable]
    public sealed class SkillUpgradeResult
    {
        public SkillTreeSnapshot updatedSnapshot;
        public ResolvedSkillTreeData resolvedData;
        public SkillUpgradeResultStatus status;
        public SkillUpgradeFailureReason failureReason;
    }
}
