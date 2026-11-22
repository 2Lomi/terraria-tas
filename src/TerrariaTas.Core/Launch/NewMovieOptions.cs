namespace TerrariaTas.Core.Launch;

/// <summary>World sizes, numbered as Terraria numbers them (<c>WorldGen.SetWorldSize</c>).</summary>
public enum WorldSize
{
    Small,
    Medium,
    Large,
}

/// <summary>World evils, numbered as Terraria's world generation takes them (<c>WorldGen.WorldGenParam_Evil</c>).</summary>
public enum WorldEvil
{
    Corruption,
    Crimson,
}

/// <summary>World difficulties, numbered as Terraria's game modes (<c>Main.GameMode</c>).</summary>
public enum WorldDifficulty
{
    Normal,
    Expert,
    Master,
    Journey,
}

/// <summary>What <c>--new</c> creates: a softcore character and a small corruption world in normal difficulty, unless told otherwise.</summary>
public sealed class NewMovieOptions
{
    public WorldSize Size { get; set; } = WorldSize.Small;

    public WorldEvil Evil { get; set; } = WorldEvil.Corruption;

    /// <summary>Journey worlds only take journey characters, so the character created for one is a journey character.</summary>
    public WorldDifficulty Difficulty { get; set; } = WorldDifficulty.Normal;

    /// <summary>World seed, as typed in Terraria's seed box; null for a random one.</summary>
    public int? Seed { get; set; }

    /// <summary>A player file of the data folder to start with instead of a new character.</summary>
    public string? Player { get; set; }

    /// <summary>A world file of the data folder to start in instead of a new world.</summary>
    public string? World { get; set; }

    /// <summary>"small corruption world, normal difficulty".</summary>
    public string DescribeWorld() => $"{Size.ToString().ToLowerInvariant()} {Evil.ToString().ToLowerInvariant()} world, {Difficulty.ToString().ToLowerInvariant()} difficulty";
}
