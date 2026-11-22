using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TerrariaTas.Core.Movies;

/// <summary>
/// The movie being played and recorded, with its file text. Recording frame N replaces the movie from N on
/// (what came after belonged to another attempt), and a frame with the same input as the one before it
/// extends that line. <see cref="Text"/> keeps the file's own text, comments and layout, up to the first line
/// that changed.
/// </summary>
public sealed class MovieRecorder
{
    private readonly List<InputLine> _lines = new();
    private readonly List<int> _starts = new();
    private readonly List<int> _breakpoints = new();
    private string _savedText = "";
    private Movie _saved = Movie.Empty;

    // Leading lines of the saved movie that are still unchanged: their text is kept as it is in the file.
    private int _unchangedLines;
    private Movie? _snapshot;

    // The last text Text() produced, parsed while checking it, so that loading it after a save parses nothing.
    private string? _checkedText;
    private Movie? _checkedMovie;

    /// <param name="text">Text of the movie file.</param>
    /// <exception cref="MovieFormatException">The text is not a valid movie.</exception>
    public MovieRecorder(string text)
    {
        Load(text);
    }

    public MovieRecorder(Movie movie)
        : this(MovieWriter.Write(movie))
    {
    }

    public MovieHeader Header => _saved.Header;

    public int TotalFrames { get; private set; }

    /// <summary>True when the movie changed since it was loaded or saved.</summary>
    public bool HasUnsavedChanges { get; private set; }

    /// <summary>Increases with every change.</summary>
    public long Version { get; private set; }

    /// <summary>The movie as it is now. Built on the first read after a change.</summary>
    public Movie Movie => _snapshot ??= new Movie(Header, _lines.ToArray(), _breakpoints);

    /// <summary>Replaces everything with <paramref name="text"/>: the file after an edit, or the text just saved.</summary>
    /// <exception cref="MovieFormatException">The text is not a valid movie. Nothing changes then.</exception>
    public void Load(string text)
    {
        var movie = ReferenceEquals(text, _checkedText) ? _checkedMovie! : MovieParser.Parse(text);
        _checkedText = null;
        _checkedMovie = null;
        _savedText = text;
        _saved = movie;
        _lines.Clear();
        _starts.Clear();
        _breakpoints.Clear();
        for (var i = 0; i < movie.Lines.Count; i++)
        {
            _lines.Add(movie.Lines[i]);
            _starts.Add(movie.StartFrameOf(i));
        }

        _breakpoints.AddRange(movie.BreakpointFrames);
        _unchangedLines = movie.Lines.Count;
        TotalFrames = movie.TotalFrames;
        HasUnsavedChanges = false;
        Version++;
        _snapshot = movie;
    }

    public InputLine LineAt(int frame)
    {
        if (frame < 0 || frame >= TotalFrames)
            throw new ArgumentOutOfRangeException(nameof(frame), $"Frame {frame} is outside the movie (0..{TotalFrames - 1}).");
        return _lines[LineIndexAt(frame)];
    }

    /// <summary>True when playback pauses before <paramref name="frame"/>.</summary>
    public bool IsBreakpoint(int frame) => _breakpoints.BinarySearch(frame) >= 0;

    /// <summary>Makes <paramref name="input"/> the input of <paramref name="frame"/>, and the last frame of the movie.</summary>
    public void Record(int frame, InputLine input)
    {
        if (frame < 0 || frame > TotalFrames)
            throw new ArgumentOutOfRangeException(nameof(frame), $"Cannot record frame {frame}: the movie has {TotalFrames} frames, the frames in between would have no input.");

        CutAt(frame);
        Append(input, 1);
        Changed();
    }

    /// <summary>The input of the first <paramref name="frames"/> frames, or of the whole movie if it is shorter.</summary>
    public InputHistory HistoryUpTo(int frames) => InputHistory.Of(_lines, frames);

    /// <summary>The first frame where the movie's input differs from <paramref name="history"/>, or null when the movie begins with it.</summary>
    public int? FirstDifferenceFrom(InputHistory history)
    {
        int frame = 0, mine = -1, theirs = -1, mineEnd = 0, theirsEnd = 0;
        while (frame < history.Frames)
        {
            if (frame >= TotalFrames)
                return frame;
            if (frame == mineEnd)
                mineEnd += _lines[++mine].FrameCount;
            if (frame == theirsEnd)
                theirsEnd += history.Lines[++theirs].FrameCount;
            if (!_lines[mine].HasSameInputs(history.Lines[theirs]))
                return frame;
            frame = Math.Min(mineEnd, theirsEnd);
        }

        return null;
    }

    /// <summary>
    /// Puts the movie on the timeline of <paramref name="history"/>: from the first frame where they differ, the movie
    /// takes the history's input and ends with it. Nothing changes when the movie already begins with the history.
    /// </summary>
    /// <returns>The first frame that changed, or null.</returns>
    public int? Adopt(InputHistory history)
    {
        if (FirstDifferenceFrom(history) is not { } from)
            return null;

        CutAt(from);
        var start = 0;
        foreach (var line in history.Lines)
        {
            var end = start + line.FrameCount;
            if (end > from)
                Append(line, end - Math.Max(start, from));
            start = end;
        }

        Changed();
        return from;
    }

    /// <summary>
    /// The movie as file text: the saved text up to the first changed line, comments included, then the lines from
    /// there. Should that ever not read back as this movie, the whole movie is written out instead.
    /// </summary>
    public string Text()
    {
        if (!HasUnsavedChanges)
            return _savedText;

        var newline = _savedText.Contains("\r\n") ? "\r\n" : "\n";
        var text = new StringBuilder();

        // A changed line of a Repeat block cannot be written alone: the text is kept up to the block's Repeat command,
        // and the block is written out round by round.
        var first = _unchangedLines;
        while (first > 0 && first < _saved.Lines.Count && _saved.Lines[first].RepeatLine != 0 && _saved.Lines[first - 1].RepeatLine == _saved.Lines[first].RepeatLine)
            first--;

        if (first < _saved.Lines.Count)
        {
            var line = _saved.Lines[first];
            text.Append(_savedText, 0, StartOfLine(_savedText, line.RepeatLine != 0 ? line.RepeatLine : line.SourceLine));
        }
        else
        {
            text.Append(_savedText);
            if (text.Length > 0 && text[text.Length - 1] != '\n')
                text.Append(newline);
        }

        var cursor = first > 0 ? _lines[first - 1].Cursor : null;
        for (var i = first; i < _lines.Count; i++)
        {
            if (i > first && IsBreakpoint(_starts[i]))
                text.Append("***").Append(newline);
            MovieWriter.AppendLine(text, _lines[i], cursor, newline);
            cursor = _lines[i].Cursor;
        }

        var result = text.ToString();
        if (!ReadsBackAsThisMovie(result, out var parsed))
        {
            result = MovieWriter.Write(Movie);
            parsed = MovieParser.Parse(result);
        }

        _checkedText = result;
        _checkedMovie = parsed;
        return result;
    }

    private int LineIndexAt(int frame)
    {
        var index = _starts.BinarySearch(frame);
        return index >= 0 ? index : ~index - 1;
    }

    /// <summary>Ends the movie before <paramref name="frame"/>, with the breakpoints after it.</summary>
    private void CutAt(int frame)
    {
        _breakpoints.RemoveAll(breakpoint => breakpoint > frame);
        if (frame >= TotalFrames)
            return;

        var index = LineIndexAt(frame);
        var kept = frame - _starts[index];
        var removeFrom = kept > 0 ? index + 1 : index;
        if (kept > 0)
            _lines[index] = _lines[index].WithFrameCount(kept);
        _lines.RemoveRange(removeFrom, _lines.Count - removeFrom);
        _starts.RemoveRange(removeFrom, _starts.Count - removeFrom);
        Touch(index);
        TotalFrames = frame;
    }

    /// <summary>
    /// Adds <paramref name="frames"/> frames of <paramref name="input"/> at the end: onto the last line when it has the
    /// same input, unless a breakpoint separates them.
    /// </summary>
    private void Append(InputLine input, int frames)
    {
        var last = _lines.Count - 1;
        if (last >= 0 && _lines[last].HasSameInputs(input) && !IsBreakpoint(TotalFrames))
        {
            _lines[last] = _lines[last].WithFrameCount(_lines[last].FrameCount + frames);
            Touch(last);
        }
        else
        {
            _starts.Add(TotalFrames);
            _lines.Add(new InputLine(frames, input.Keys, input.Buttons, input.Cursor, input.Wheel));
        }

        TotalFrames += frames;
    }

    private void Touch(int lineIndex) => _unchangedLines = Math.Min(_unchangedLines, lineIndex);

    private void Changed()
    {
        HasUnsavedChanges = true;
        Version++;
        _snapshot = null;
    }

    private bool ReadsBackAsThisMovie(string text, out Movie written)
    {
        try
        {
            written = MovieParser.Parse(text);
        }
        catch (MovieFormatException)
        {
            written = Movie.Empty;
            return false;
        }

        if (written.Lines.Count != _lines.Count || !written.BreakpointFrames.SequenceEqual(_breakpoints))
            return false;

        for (var i = 0; i < _lines.Count; i++)
        {
            if (written.Lines[i].FrameCount != _lines[i].FrameCount || !written.Lines[i].HasSameInputs(_lines[i]))
                return false;
        }

        return true;
    }

    /// <summary>Index in <paramref name="text"/> where its 1-based line <paramref name="lineNumber"/> starts.</summary>
    private static int StartOfLine(string text, int lineNumber)
    {
        var index = 0;
        for (var line = 1; line < lineNumber; line++)
            index = text.IndexOf('\n', index) + 1;
        return index;
    }
}
