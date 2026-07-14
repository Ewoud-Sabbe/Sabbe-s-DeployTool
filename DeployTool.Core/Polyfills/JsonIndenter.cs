#if NETFRAMEWORK
using System.Text;

namespace DeployTool.Core.Polyfills;

/// <summary>
/// Reformats the compact JSON that JavaScriptSerializer produces with 2-space indentation,
/// matching System.Text.Json's WriteIndented output, so the config files on the share stay
/// human-editable.
/// </summary>
internal static class JsonIndenter
{
    public static string Indent(string json)
    {
        var sb = new StringBuilder();
        var depth = 0;
        var inString = false;

        for (var i = 0; i < json.Length; i++)
        {
            var c = json[i];

            if (inString)
            {
                sb.Append(c);
                if (c == '\\' && i + 1 < json.Length)
                    sb.Append(json[++i]);
                else if (c == '"')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '"':
                    inString = true;
                    sb.Append(c);
                    break;
                case '{':
                case '[':
                    sb.Append(c);
                    if (i + 1 < json.Length && (json[i + 1] == '}' || json[i + 1] == ']'))
                    {
                        sb.Append(json[++i]);
                    }
                    else
                    {
                        depth++;
                        sb.Append('\n').Append(' ', depth * 2);
                    }
                    break;
                case '}':
                case ']':
                    depth--;
                    sb.Append('\n').Append(' ', depth * 2).Append(c);
                    break;
                case ',':
                    sb.Append(c).Append('\n').Append(' ', depth * 2);
                    break;
                case ':':
                    sb.Append(": ");
                    break;
                default:
                    sb.Append(c);
                    break;
            }
        }

        return sb.ToString();
    }
}
#endif
