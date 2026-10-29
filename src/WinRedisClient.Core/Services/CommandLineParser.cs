using System.Text;

using WinRedisClient.Core.Localization;
namespace WinRedisClient.Core.Services;

/// <summary>命令行分词：支持单/双引号与反斜杠转义，不执行任何 shell 语义。</summary>
public static class CommandLineParser
{
    public static IReadOnlyList<string> Parse(string input)
    {
        var tokens = new List<string>();
        if (string.IsNullOrEmpty(input)) return tokens;

        var current = new StringBuilder();
        var inSingle = false;
        var inDouble = false;
        var hasToken = false;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (inSingle)
            {
                if (c == '\'') inSingle = false;
                else current.Append(c);
                continue;
            }

            if (inDouble)
            {
                if (c == '\\' && i + 1 < input.Length)
                {
                    i++;
                    current.Append(Unescape(input[i]));
                }
                else if (c == '"') inDouble = false;
                else current.Append(c);
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
                continue;
            }

            switch (c)
            {
                case '\'':
                    inSingle = true;
                    hasToken = true;
                    break;
                case '"':
                    inDouble = true;
                    hasToken = true;
                    break;
                case '\\' when i + 1 < input.Length:
                    i++;
                    current.Append(Unescape(input[i]));
                    hasToken = true;
                    break;
                default:
                    current.Append(c);
                    hasToken = true;
                    break;
            }
        }

        if (inSingle || inDouble)
            throw new FormatException(Loc.T("引号未闭合"));

        if (hasToken) tokens.Add(current.ToString());
        return tokens;
    }

    private static char Unescape(char c) => c switch
    {
        'n' => '\n',
        'r' => '\r',
        't' => '\t',
        _ => c,
    };
}
