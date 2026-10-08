namespace IntegratedModManager.Core;

public readonly record struct OnlineCharacterRailLayout(bool Horizontal, double ColumnWidth);

public static class OnlineLayoutPolicy
{
    /// <summary>A short window must reserve height for the Mod list, not a second toolbar row.</summary>
    public static OnlineCharacterRailLayout CharacterRail(double contentWidth, double windowHeight)
    {
        bool narrow = contentWidth < 1040;
        return new(narrow && windowHeight >= 740, narrow ? 152 : 188);
    }
}
