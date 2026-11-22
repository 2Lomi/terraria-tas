using System;
using System.IO;
using TerrariaTas.Core.Movies;
using Xunit;

namespace TerrariaTas.Core.Tests;

public sealed class MovieFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tas-tests-" + Guid.NewGuid().ToString("N"));
    private readonly string _path;

    public MovieFileTests()
    {
        Directory.CreateDirectory(_folder);
        _path = Path.Combine(_folder, "run.tas");
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AnotherWriterIsAChangeItsOwnSavesAreNot()
    {
        var file = new MovieFile(_path, backUp: false);
        file.Save("10 D\n");
        Assert.False(file.ChangedOnDisk());

        File.WriteAllText(_path, "10 D\n5 A\n");
        Assert.True(file.ChangedOnDisk());
        Assert.True(file.ChangedOnDisk()); // until read

        Assert.Equal("10 D\n5 A\n", file.Read());
        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void TheSameTextWrittenAgainIsNoChange()
    {
        File.WriteAllText(_path, "10 D\n");
        var file = new MovieFile(_path);
        file.Read();

        File.WriteAllText(_path, "10 D\n");
        File.SetLastWriteTimeUtc(_path, DateTime.UtcNow.AddMinutes(1));

        Assert.False(file.ChangedOnDisk());
    }

    [Fact]
    public void AMissingFileIsNoChangeAndSavingCreatesItAgain()
    {
        var file = new MovieFile(_path);
        file.Save("10 D\n");
        File.Delete(_path);

        Assert.False(file.ChangedOnDisk());
        file.Save("10 D\n");
        Assert.Equal("10 D\n", File.ReadAllText(_path));
    }

    [Fact]
    public void CopiesAreNamedAfterTheMovieAndNeverReplaceAFile()
    {
        var file = new MovieFile(_path);
        var time = new DateTime(2026, 11, 18, 14, 30, 5);

        var first = file.SaveCopy("10 D\n", "recording", time);
        var second = file.SaveCopy("5 A\n", "recording", time);

        Assert.Equal(Path.Combine(_folder, "run.recording-20261118-143005.tas"), first);
        Assert.Equal(Path.Combine(_folder, "run.recording-20261118-143005-2.tas"), second);
        Assert.Equal("10 D\n", File.ReadAllText(first));
        Assert.Equal("5 A\n", File.ReadAllText(second));
    }
}
