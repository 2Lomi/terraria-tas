using System;

namespace TerrariaTas.Runtime;

internal static class Log
{
    public static void Info(string message) => Console.WriteLine("[TAS] " + message);

    public static void Warn(string message) => Console.WriteLine("[TAS warning] " + message);

    public static void Error(string message) => Console.WriteLine("[TAS error] " + message);
}
