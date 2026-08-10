namespace SkillTree.Authoring
{
    public interface ISkillTreeRuntimeBridge<in TResolved>
    {
        void Apply(TResolved resolved);
    }
}
