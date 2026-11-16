using Terraria.Testing;

namespace TerrariaTas.Runtime.SaveStates;

/// <summary>A frozen copy of the game taken at a frame boundary, before <see cref="Frame"/> runs.</summary>
internal sealed class SaveState
{
    public SaveState(int frame, StateSnapshot gameplay, StateSnapshot input, object?[] extras)
    {
        Frame = frame;
        Gameplay = gameplay;
        Input = input;
        Extras = extras;
    }

    public int Frame { get; }

    internal StateSnapshot Gameplay { get; }

    internal StateSnapshot Input { get; }

    internal object?[] Extras { get; }
}
