using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TerrariaTas.Core.Movies;

namespace TerrariaTas.Core.Launch;

/// <summary>A launch that cannot go ahead as asked (no such movie, a name taken...): the message says why and what to do.</summary>
public sealed class LaunchException : Exception
{
    public LaunchException(string message)
        : base(message)
    {
    }
}

/// <summary>How a movie's player or world file compares with the one the movie names.</summary>
public enum FileStatus
{
    /// <summary>The movie names no file (it starts from the menus).</summary>
    None,

    /// <summary>Not in the data folder.</summary>
    Missing,

    /// <summary>There, but the movie has no checksum to check it against.</summary>
    NotPinned,

    /// <summary>The very file the movie was made with.</summary>
    Matches,

    /// <summary>Another file by that name: the movie would not replay.</summary>
    Differs,
}

/// <summary>A movie of the movies folder, as <c>--list</c> shows it.</summary>
public sealed class MovieSummary
{
    public MovieSummary(string name, Movie? movie, string? error, FileStatus playerStatus, FileStatus worldStatus)
    {
        Name = name;
        Movie = movie;
        Error = error;
        PlayerStatus = playerStatus;
        WorldStatus = worldStatus;
    }

    /// <summary>File name without the .tas extension.</summary>
    public string Name { get; }

    /// <summary>Null when the file could not be read (see <see cref="Error"/>).</summary>
    public Movie? Movie { get; }

    public string? Error { get; }

    public FileStatus PlayerStatus { get; }

    public FileStatus WorldStatus { get; }
}

/// <summary>
/// The movies folder and the data folder the movies' players and worlds live in: finds a movie by name, picks the path and
/// file names of a new one, and lists them with the state of their files.
/// </summary>
public sealed class MovieLibrary
{
    public const string Extension = ".tas";

    // Movie files live on Windows, the only system the game runs on, so names follow its rules wherever this code runs:
    // Path.GetInvalidFileNameChars() on Linux (where CI runs the tests) lets '?', '*', ':' and the like through. These are
    // the sets .NET Framework uses on Windows.
    private static readonly char[] InvalidFileNameChars = WithControlChars("\"<>|:*?\\/");
    private static readonly char[] InvalidPathChars = WithControlChars("\"<>|");

    public MovieLibrary(string moviesDir, string dataDir)
    {
        MoviesDir = Path.GetFullPath(moviesDir);
        DataDir = Path.GetFullPath(dataDir);
    }

    public string MoviesDir { get; }

    public string DataDir { get; }

    public string PlayersDir => Path.Combine(DataDir, "Players");

    public string WorldsDir => Path.Combine(DataDir, "Worlds");

    /// <summary>
    /// The movie <paramref name="nameOrPath"/> stands for: a path to it (the .tas can be left out), or the name of a movie
    /// in the movies folder.
    /// </summary>
    /// <exception cref="LaunchException">No such movie.</exception>
    public string Find(string nameOrPath)
    {
        var candidates = new List<string> { nameOrPath };
        if (!HasMovieExtension(nameOrPath))
            candidates.Add(nameOrPath + Extension);
        if (!Path.IsPathRooted(nameOrPath))
            candidates.AddRange(candidates.Select(candidate => Path.Combine(MoviesDir, candidate)).ToList());

        var found = candidates.FirstOrDefault(File.Exists);
        if (found != null)
            return Path.GetFullPath(found);

        throw new LaunchException($"No movie '{nameOrPath}': it is neither a file nor a movie of {MoviesDir}. "
                                  + "'--list' shows the movies there, '--new' creates one.");
    }

    /// <summary>Where <c>--new</c> creates the movie <paramref name="name"/> (null for a name made from the time).</summary>
    /// <exception cref="LaunchException">A movie with that name exists: --new never replaces one.</exception>
    public string NewMoviePath(string? name, DateTime now)
    {
        name ??= "run-" + now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var isPath = Path.IsPathRooted(name) || name.IndexOfAny(new[] { '/', '\\' }) >= 0;
        if (name.Trim().Length == 0 || name.IndexOfAny(isPath ? InvalidPathChars : InvalidFileNameChars) >= 0)
            throw new LaunchException($"'{name}' cannot be the name of a movie file.");

        var path = Path.GetFullPath(isPath ? name : Path.Combine(MoviesDir, name));
        if (!HasMovieExtension(path))
            path += Extension;

        if (File.Exists(path))
        {
            var shortName = Path.GetFileNameWithoutExtension(path);
            throw new LaunchException($"{path} already exists, and --new never replaces a movie. To work on it: 'TerrariaTas {shortName} --edit'. "
                                      + "To make another one, give --new a name that is not taken.");
        }

        return path;
    }

    /// <summary>
    /// A file name for a new player or world in <paramref name="folder"/>: <paramref name="baseName"/>, numbered when the
    /// folder has a file by that name already.
    /// </summary>
    public static string FreeFileName(string folder, string baseName, string extension)
    {
        var name = baseName + extension;
        for (var number = 2; File.Exists(Path.Combine(folder, name)); number++)
            name = $"{baseName}-{number}{extension}";
        return name;
    }

    /// <summary>The movies of the movies folder, by name.</summary>
    public IReadOnlyList<MovieSummary> List()
    {
        if (!Directory.Exists(MoviesDir))
            return Array.Empty<MovieSummary>();

        return Directory.GetFiles(MoviesDir, "*" + Extension)
            .Where(HasMovieExtension) // the pattern also matches longer extensions such as .tas.bak
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(Summarize)
            .ToList();
    }

    /// <summary>How the file a movie names compares with the one it was made with.</summary>
    public FileStatus StatusOf(string folder, string? file, string? checksum)
    {
        if (file == null)
            return FileStatus.None;
        var path = Path.Combine(folder, file);
        if (!File.Exists(path))
            return FileStatus.Missing;
        if (checksum == null)
            return FileStatus.NotPinned;
        return FileChecksum.Of(path) == checksum ? FileStatus.Matches : FileStatus.Differs;
    }

    /// <summary>The <c>--list</c> table.</summary>
    public string Describe(IReadOnlyList<MovieSummary> movies)
    {
        var text = new StringBuilder();
        text.Append("Movies in ").Append(MoviesDir).Append(" (players and worlds in ").Append(DataDir).Append("):\n");
        if (movies.Count == 0)
        {
            text.Append("\n  none yet: 'TerrariaTas --new' creates one.\n");
            return text.ToString();
        }

        // A movie that cannot be read gets its reason instead of the other columns.
        var rows = new List<(string[] Cells, string? Error)> { (new[] { "MOVIE", "FRAMES", "TIME", "PLAYER", "WORLD" }, null) };
        foreach (var movie in movies)
        {
            if (movie.Movie is not { } parsed)
            {
                rows.Add((new[] { movie.Name, "", "", "", "" }, "cannot be read: " + movie.Error));
                continue;
            }

            var header = parsed.Header;
            rows.Add((new[]
            {
                movie.Name,
                parsed.TotalFrames.ToString(CultureInfo.InvariantCulture),
                Time(parsed.TotalFrames),
                FileColumn(header.Player, movie.PlayerStatus),
                FileColumn(header.World, movie.WorldStatus),
            }, null));
        }

        var widths = Enumerable.Range(0, 5).Select(column => rows.Max(row => row.Cells[column].Length)).ToArray();
        text.Append('\n');
        foreach (var (cells, error) in rows)
        {
            var line = new StringBuilder("  ").Append(cells[0].PadRight(widths[0])).Append("  ");
            if (error != null)
            {
                line.Append(error);
            }
            else
            {
                line.Append(cells[1].PadLeft(widths[1])).Append("  ").Append(cells[2].PadLeft(widths[2])).Append("  ")
                    .Append(cells[3].PadRight(widths[3])).Append("  ").Append(cells[4]);
            }

            text.Append(line.ToString().TrimEnd()).Append('\n');
        }

        var statuses = movies.SelectMany(movie => new[] { movie.PlayerStatus, movie.WorldStatus }).ToList();
        text.Append('\n');
        if (statuses.Contains(FileStatus.Differs))
            text.Append("CHANGED: the file is not the one the movie was made with, so the movie would not replay. Put the original back.\n");
        if (statuses.Contains(FileStatus.Missing))
            text.Append("MISSING: the file is not in the data folder. Copy it there.\n");
        if (statuses.Contains(FileStatus.NotPinned))
            text.Append("not checked: the movie has no checksum of the file yet. Opening it with --edit adds them.\n");
        text.Append("Play one with 'TerrariaTas <movie>', work on it with 'TerrariaTas <movie> --edit'.\n");
        return text.ToString();
    }

    private MovieSummary Summarize(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        try
        {
            var movie = MovieParser.Load(path);
            return new MovieSummary(name, movie, null,
                StatusOf(PlayersDir, movie.Header.Player, movie.Header.PlayerChecksum),
                StatusOf(WorldsDir, movie.Header.World, movie.Header.WorldChecksum));
        }
        catch (MovieFormatException e)
        {
            return new MovieSummary(name, null, $"line {e.LineNumber}: {e.Reason}", FileStatus.None, FileStatus.None);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new MovieSummary(name, null, e.Message, FileStatus.None, FileStatus.None);
        }
    }

    private static string FileColumn(string? file, FileStatus status) => status switch
    {
        FileStatus.None => "(menus)",
        FileStatus.Missing => file + " MISSING",
        FileStatus.NotPinned => file + " (not checked)",
        FileStatus.Differs => file + " CHANGED",
        _ => file!,
    };

    /// <summary>Game time at 60 frames a second, to the hundredth: "1:05.25".</summary>
    private static string Time(int frames)
    {
        var hundredths = frames * 100L / 60;
        return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}.{2:00}", hundredths / 6000, hundredths / 100 % 60, hundredths % 100);
    }

    private static bool HasMovieExtension(string path) => Path.GetExtension(path).Equals(Extension, StringComparison.OrdinalIgnoreCase);

    private static char[] WithControlChars(string characters) => characters.Concat(Enumerable.Range(0, 32).Select(code => (char)code)).ToArray();
}
