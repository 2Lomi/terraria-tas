using System;
using System.IO;
using System.Text;

namespace TerrariaTas.Core.Movies;

/// <summary>
/// The movie file on disk. Saves without ever leaving it half written, keeps the file as it was before the first save
/// as <c>.bak</c> next to it, and tells when someone else (a text editor) changed it since this object last read or
/// saved it, so that the TAS never writes over an edit.
/// </summary>
public sealed class MovieFile
{
    private static readonly Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    private bool _backupDue;

    // What this object last read or saved, and the file's write time and size when it did. Another stamp means the
    // file may hold something else.
    private string? _knownText;
    private Stamp _knownStamp;

    /// <param name="backUp">False for a file this session created: there is nothing to keep.</param>
    public MovieFile(string path, bool backUp = true)
    {
        Path = path;
        _backupDue = backUp;
    }

    public string Path { get; }

    /// <summary>The file as it was before this object first saved over it.</summary>
    public string BackupPath => Path + ".bak";

    /// <summary>True once the backup was made.</summary>
    public bool HasBackup { get; private set; }

    /// <summary>How long ago anyone last wrote the file: an editor may still be writing it right after a change shows.</summary>
    public TimeSpan SinceLastWrite => DateTime.UtcNow - File.GetLastWriteTimeUtc(Path);

    /// <summary>Reads the file. <see cref="ChangedOnDisk"/> then compares the file with what this returned.</summary>
    public string Read()
    {
        // Stamp first: a write during the read then shows as a change on the next check.
        var stamp = StampNow();
        var text = File.ReadAllText(Path);
        _knownStamp = stamp;
        _knownText = text;
        return text;
    }

    public void Save(string text)
    {
        if (_backupDue && File.Exists(Path))
        {
            File.Copy(Path, BackupPath, overwrite: true);
            HasBackup = true;
        }

        _backupDue = false;
        var temporary = Path + ".tmp";
        File.WriteAllText(temporary, text, Utf8);
        if (File.Exists(Path))
            File.Replace(temporary, Path, destinationBackupFileName: null);
        else
            File.Move(temporary, Path);

        // Someone else may have written the file right after this did: then the next check must see a change.
        var stamp = StampNow();
        _knownStamp = File.ReadAllText(Path) == text ? stamp : default;
        _knownText = text;
    }

    /// <summary>
    /// True when the file holds something else than what this object last read or saved: someone else wrote it.
    /// A file saved again with the same text is no change. A missing file is none either: saving creates it again.
    /// </summary>
    /// <exception cref="IOException">The file cannot be read right now (an editor is writing it).</exception>
    public bool ChangedOnDisk()
    {
        var stamp = StampNow();
        if (stamp.Equals(_knownStamp) || !stamp.Exists)
            return false;
        if (File.ReadAllText(Path) != _knownText)
            return true;

        _knownStamp = stamp;
        return false;
    }

    /// <summary>
    /// Writes <paramref name="text"/> to a new file next to the movie, "&lt;movie&gt;.&lt;label&gt;-&lt;time&gt;.tas",
    /// and returns its path. Never replaces a file.
    /// </summary>
    public string SaveCopy(string text, string label, DateTime time)
    {
        var directory = System.IO.Path.GetDirectoryName(Path) ?? "";
        var extension = System.IO.Path.GetExtension(Path);
        var stem = $"{System.IO.Path.GetFileNameWithoutExtension(Path)}.{label}-{time:yyyyMMdd-HHmmss}";
        var path = System.IO.Path.Combine(directory, stem + extension);
        for (var copy = 2; File.Exists(path); copy++)
            path = System.IO.Path.Combine(directory, $"{stem}-{copy}{extension}");

        using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
        using (var writer = new StreamWriter(stream, Utf8))
            writer.Write(text);
        return path;
    }

    private Stamp StampNow()
    {
        var file = new FileInfo(Path);
        return file.Exists ? new Stamp(true, file.LastWriteTimeUtc, file.Length) : default;
    }

    private readonly struct Stamp : IEquatable<Stamp>
    {
        public Stamp(bool exists, DateTime written, long length)
        {
            Exists = exists;
            Written = written;
            Length = length;
        }

        public bool Exists { get; }

        public DateTime Written { get; }

        public long Length { get; }

        public bool Equals(Stamp other) => Exists == other.Exists && Written == other.Written && Length == other.Length;

        public override bool Equals(object? obj) => obj is Stamp other && Equals(other);

        public override int GetHashCode() => Written.GetHashCode() ^ Length.GetHashCode();
    }
}
