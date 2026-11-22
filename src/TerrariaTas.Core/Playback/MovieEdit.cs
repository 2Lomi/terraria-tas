using TerrariaTas.Core.Movies;

namespace TerrariaTas.Core.Playback;

/// <summary>
/// What a new version of the movie (the file edited in a text editor) changes for the game playing it: from which
/// frame its input differs, and whether the game went through that frame following the movie. If it did, the game
/// state is not the new movie's any more and must be replayed from a savestate made before the change.
/// </summary>
public sealed class MovieEdit
{
    /// <param name="before">The movie before the edit.</param>
    /// <param name="after">The edited movie.</param>
    /// <param name="played">The input the game state was made with, up to the current frame.</param>
    public MovieEdit(Movie before, Movie after, InputHistory played)
    {
        FirstChangedFrame = InputHistory.FirstDifference(AllInput(before), AllInput(after));
        FollowedUpTo = InputHistory.FirstDifference(played, InputHistory.Of(before.Lines, played.Frames)) ?? played.Frames;
    }

    /// <summary>
    /// The first frame whose input changed (a movie made longer or shorter changes from the end of the shorter one),
    /// or null when the edit only touched comments, breakpoints or the header.
    /// </summary>
    public int? FirstChangedFrame { get; }

    /// <summary>
    /// The game played the old movie's input up to this frame: the current frame, unless the game left the movie
    /// earlier (a savestate of another version of the movie, frames played past its end).
    /// </summary>
    public int FollowedUpTo { get; }

    /// <summary>The game went through changed input while following the movie: its state is not the new movie's.</summary>
    public bool NeedsReplay => FirstChangedFrame < FollowedUpTo;

    private static InputHistory AllInput(Movie movie) => InputHistory.Of(movie.Lines, movie.TotalFrames);
}
