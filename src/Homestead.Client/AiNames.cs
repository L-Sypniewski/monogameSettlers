namespace Homestead.Client;

/// <summary>Names of the AI characters and traits in the chosen language (<c>ai.characters.*</c>, <c>ai.traits.*</c> of <see cref="Lang"/>); unknown ids stay as they are.</summary>
public sealed class AiNames
{
    public string Character(string id) => Lang.Find("ai.characters." + id) ?? id;

    public string Trait(string id) => Lang.Find("ai.traits." + id) ?? id;
}
