using System;

namespace TerrariaTas.Runtime;

/// <summary>The TAS cannot start as asked (a file is missing or already exists...): the message says why and what to do.</summary>
public sealed class TasSetupException : Exception
{
    public TasSetupException(string message)
        : base(message)
    {
    }
}
