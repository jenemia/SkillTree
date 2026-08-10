using SkillTree.Authoring;
using UnityEngine;

namespace SkillTree.Authoring.Samples
{
    public sealed class SampleResolvedSkillTreePreviewBridge : MonoBehaviour, ISkillTreeRuntimeBridge<ResolvedSkillTreeData>
    {
        [SerializeField] private bool logApplyCalls = true;

        public int ApplyCount { get; private set; }
        public ResolvedSkillTreeData LastResolvedData { get; private set; }

        public void Apply(ResolvedSkillTreeData resolved)
        {
            ApplyCount += 1;
            LastResolvedData = resolved;

            if (logApplyCalls)
            {
                Debug.Log(
                    $"[SampleResolvedSkillTreePreviewBridge] Applied tree '{resolved?.treeId}' with {resolved?.userSkills?.Count ?? 0} skills.",
                    this);
            }
        }

        public void ResetPreview()
        {
            ApplyCount = 0;
            LastResolvedData = null;
        }
    }
}
