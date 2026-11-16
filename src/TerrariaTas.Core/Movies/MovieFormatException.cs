using System;

namespace TerrariaTas.Core.Movies;

public sealed class MovieFormatException : Exception
{
    public MovieFormatException(int lineNumber, string message)
        : base($"Line {lineNumber}: {message}")
    {
        LineNumber = lineNumber;
    }

    public int LineNumber { get; }
}
