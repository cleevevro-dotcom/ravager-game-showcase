public interface ISkill
{
    SkillId       Id          { get; }
    SkillCategory Category    { get; }
    string        DisplayName { get; }
    int           Level       { get; }

    void SkillTick(float dt);
}
