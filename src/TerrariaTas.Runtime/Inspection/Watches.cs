using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Terraria;
using TerrariaTas.Core.Studio;
using TerrariaTas.Core.Watch;
using TerrariaTas.Runtime.GameAccess;

namespace TerrariaTas.Runtime.Inspection;

/// <summary>
/// The memory watch: paths into the game's objects that Studio asked for, read at frame boundaries, and optionally
/// written to a CSV file for every frame. Paths start from the global-state classes, which the game has certainly set
/// up (reading another class could run its static constructor in the middle of a movie), or from "player".
/// </summary>
internal sealed class Watches
{
    private static readonly Dictionary<string, Type> Roots = GlobalState.Types
        .SelectMany(type => new[] { (Name: type.Name, Type: type), (Name: type.FullName!.Replace('+', '.'), Type: type) })
        .GroupBy(root => root.Name)
        .ToDictionary(group => group.Key, group => group.First().Type);

    private readonly WatchReader _reader = new(
        name => Roots.TryGetValue(name, out var type) ? type : null,
        new Dictionary<string, Func<object?>> { ["player"] = () => Main.LocalPlayer });

    private readonly string _logPath;
    private List<(string Text, WatchPath? Path, string? Error)> _paths = new();
    private StreamWriter? _log;

    /// <param name="logPath">Where the values of every frame go when Studio asks for a log.</param>
    public Watches(string logPath)
    {
        _logPath = logPath;
    }

    public long Version { get; private set; }

    public bool IsEmpty => _paths.Count == 0;

    /// <summary>Takes Studio's new list. Returns a notice for the user when logging starts or stops, or null.</summary>
    public string? Set(WatchList list)
    {
        Version = list.Version;
        _paths = list.Paths.Select(text =>
        {
            try
            {
                return (text, (WatchPath?)WatchPath.Parse(text), (string?)null);
            }
            catch (FormatException e)
            {
                return (text, null, "! " + e.Message);
            }
        }).ToList();

        var logging = list.LogToFile && _paths.Count > 0;
        if (logging == (_log != null))
        {
            _log?.WriteLine(Header()); // the columns changed: a new header row before the rows that follow it
            return null;
        }

        if (!logging)
        {
            StopLog();
            return $"Memory watch log closed: {_logPath}";
        }

        try
        {
            _log = new StreamWriter(_logPath, append: false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)) { AutoFlush = true };
            _log.WriteLine(Header());
            return $"Logging the memory watch of every frame to {_logPath}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return $"Cannot log the memory watch to {_logPath}: {e.Message}";
        }
    }

    /// <summary>The value of each path right now. Main thread, at a frame boundary.</summary>
    public string[] Read() => _paths.Select(entry => entry.Error ?? _reader.Read(entry.Path!)).ToArray();

    /// <summary>Writes the values of the frame boundary <paramref name="frame"/> to the log, when logging.</summary>
    public void LogFrame(int frame)
    {
        if (_log == null)
            return;

        _log.Write(frame.ToString(CultureInfo.InvariantCulture));
        foreach (var value in Read())
            _log.Write("," + Csv(value));
        _log.WriteLine();
    }

    public void StopLog()
    {
        _log?.Dispose();
        _log = null;
    }

    private string Header() => "frame," + string.Join(",", _paths.Select(entry => Csv(entry.Text)));

    private static string Csv(string value) => value.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";
}
