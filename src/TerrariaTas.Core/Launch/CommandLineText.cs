using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace TerrariaTas.Core.Launch;

/// <summary>Builds a Windows command line that a program splits back into the same arguments (CommandLineToArgvW rules).</summary>
public static class CommandLineText
{
    public static string Join(IEnumerable<string> arguments) => string.Join(" ", arguments.Select(Quote));

    public static string Quote(string argument)
    {
        if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            return argument;

        var text = new StringBuilder("\"");
        var backslashes = 0;
        foreach (var c in argument)
        {
            if (c == '\\')
            {
                backslashes++;
                continue;
            }

            // Backslashes count double before a quote, which is escaped itself; elsewhere they are literal.
            text.Append('\\', c == '"' ? backslashes * 2 + 1 : backslashes).Append(c);
            backslashes = 0;
        }

        return text.Append('\\', backslashes * 2).Append('"').ToString();
    }
}
