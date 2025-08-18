using System.Collections.Generic;
using System.Text.RegularExpressions;

public static class SimpleArgSplitter
{
    // Splits a command line string into arguments, respecting quotes (no escape support)
    public static string[] Split(string commandLine)
    {
        var args = new List<string>();
        // Pattern: quoted string or non-whitespace sequence
        var pattern = @"\\\""([^\\\""]*)\\""|[^\s]+";

        foreach (Match m in Regex.Matches(commandLine, pattern))
        {
            var v = m.Value;
            if (v.Length > 1 && v[0] == '"' && v[v.Length-1] == '"')
                v = v.Substring(1, v.Length-2);
            args.Add(v);
        }
        return args.ToArray();
    }
}
