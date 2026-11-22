using System;

namespace TerrariaTas.Core.Movies;

public sealed class MovieFormatException : Exception
{
    public MovieFormatException(int lineNumber, string reason)
        : base($"Line {lineNumber}: {reason}")
    {
        LineNumber = lineNumber;
        Reason = reason;
    }

    public int LineNumber { get; }

    /// <summary>What is wrong, without the line number.</summary>
    public string Reason { get; }
}
