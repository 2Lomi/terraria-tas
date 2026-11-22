using System;
using System.IO;
using System.Linq;
using TerrariaTas.Core.Launch;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

public sealed class MovieLibraryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tas-tests-" + Guid.NewGuid().ToString("N"));
    private readonly MovieLibrary _library;

    public MovieLibraryTests()
    {
        _library = new MovieLibrary(Path.Combine(_root, "movies"), Path.Combine(_root, "data"));
        Directory.CreateDirectory(_library.MoviesDir);
        Directory.CreateDirectory(_library.PlayersDir);
        Directory.CreateDirectory(_library.WorldsDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void FindsAMovieByNameOrPath()
    {
        var path = Path.Combine(_library.MoviesDir, "my-run.tas");
        File.WriteAllText(path, "10 D\n");

        Assert.Equal(path, _library.Find("my-run"));
        Assert.Equal(path, _library.Find("my-run.tas"));
        Assert.Equal(path, _library.Find(path));
        Assert.Equal(path, _library.Find(Path.Combine(_library.MoviesDir, "my-run")));

        var error = Assert.Throws<LaunchException>(() => _library.Find("other"));
        Assert.Contains("No movie 'other'", error.Message);
        Assert.Contains("--list", error.Message);
    }

    [Fact]
    public void NewMoviesGoToTheMoviesFolderAndNeverReplaceOne()
    {
        Assert.Equal(Path.Combine(_library.MoviesDir, "speedy.tas"), _library.NewMoviePath("speedy", DateTime.Now));
        Assert.Equal(Path.Combine(_library.MoviesDir, "run-20261118-153205.tas"), _library.NewMoviePath(null, new DateTime(2026, 11, 18, 15, 32, 5)));

        var elsewhere = Path.Combine(_root, "elsewhere", "run");
        Assert.Equal(elsewhere + ".tas", _library.NewMoviePath(elsewhere, DateTime.Now));

        File.WriteAllText(Path.Combine(_library.MoviesDir, "speedy.tas"), "");
        var error = Assert.Throws<LaunchException>(() => _library.NewMoviePath("speedy", DateTime.Now));
        Assert.Contains("never replaces a movie", error.Message);
        Assert.Contains("TerrariaTas speedy --edit", error.Message);

        Assert.Throws<LaunchException>(() => _library.NewMoviePath("a?b", DateTime.Now));
    }

    [Fact]
    public void FreeFileNamesAreNumbered()
    {
        Assert.Equal("speedy.plr", MovieLibrary.FreeFileName(_library.PlayersDir, "speedy", ".plr"));
        File.WriteAllText(Path.Combine(_library.PlayersDir, "speedy.plr"), "");
        File.WriteAllText(Path.Combine(_library.PlayersDir, "speedy-2.plr"), "");
        Assert.Equal("speedy-3.plr", MovieLibrary.FreeFileName(_library.PlayersDir, "speedy", ".plr"));
    }

    [Fact]
    public void FileStatusesCompareChecksums()
    {
        var player = Path.Combine(_library.PlayersDir, "a.plr");
        File.WriteAllText(player, "character");
        var checksum = FileChecksum.Of(player);

        Assert.Equal(FileStatus.None, _library.StatusOf(_library.PlayersDir, null, null));
        Assert.Equal(FileStatus.Missing, _library.StatusOf(_library.PlayersDir, "b.plr", checksum));
        Assert.Equal(FileStatus.NotPinned, _library.StatusOf(_library.PlayersDir, "a.plr", null));
        Assert.Equal(FileStatus.Matches, _library.StatusOf(_library.PlayersDir, "a.plr", checksum));

        File.WriteAllText(player, "character, played since");
        Assert.Equal(FileStatus.Differs, _library.StatusOf(_library.PlayersDir, "a.plr", checksum));
    }

    [Fact]
    public void ListsEveryMovieWithItsFiles()
    {
        File.WriteAllText(Path.Combine(_library.PlayersDir, "hero.plr"), "hero");
        File.WriteAllText(Path.Combine(_library.WorldsDir, "land.wld"), "land");
        var playerChecksum = FileChecksum.Of(Path.Combine(_library.PlayersDir, "hero.plr"));
        var worldChecksum = FileChecksum.Of(Path.Combine(_library.WorldsDir, "land.wld"));

        File.WriteAllText(Path.Combine(_library.MoviesDir, "pinned.tas"),
            $"Player: hero.plr\nPlayerChecksum: {playerChecksum}\nWorld: land.wld\nWorldChecksum: {worldChecksum}\n\n3900 D\n");
        File.WriteAllText(Path.Combine(_library.MoviesDir, "loose.tas"), "Player: hero.plr\nWorld: gone.wld\n\n60 A\n");
        File.WriteAllText(Path.Combine(_library.MoviesDir, "broken.tas"), "10 Q9\n");
        File.WriteAllText(Path.Combine(_library.MoviesDir, "pinned.tas.bak"), "not a movie\n");

        var movies = _library.List();
        Assert.Equal(new[] { "broken", "loose", "pinned" }, movies.Select(movie => movie.Name));
        Assert.Contains("line 1", movies[0].Error);
        Assert.Equal(FileStatus.NotPinned, movies[1].PlayerStatus);
        Assert.Equal(FileStatus.Missing, movies[1].WorldStatus);
        Assert.Equal(FileStatus.Matches, movies[2].PlayerStatus);
        Assert.Equal(FileStatus.Matches, movies[2].WorldStatus);

        var table = _library.Describe(movies).Split('\n');
        Assert.Equal("  MOVIE   FRAMES     TIME  PLAYER                  WORLD", table[2]);
        Assert.StartsWith("  broken  cannot be read: line 1:", table[3]);
        Assert.Equal("  loose       60  0:01.00  hero.plr (not checked)  gone.wld MISSING", table[4]);
        Assert.Equal("  pinned    3900  1:05.00  hero.plr                land.wld", table[5]);
        var notes = string.Join("\n", table);
        Assert.Contains("MISSING: the file is not in the data folder", notes);
        Assert.Contains("not checked: the movie has no checksum", notes);
        Assert.DoesNotContain("CHANGED:", notes);
    }

    [Fact]
    public void AnEmptyFolderSaysHowToStart()
    {
        Assert.Empty(_library.List());
        Assert.Contains("--new", _library.Describe(_library.List()));
        Assert.Empty(new MovieLibrary(Path.Combine(_root, "nowhere"), _library.DataDir).List());
    }
}
