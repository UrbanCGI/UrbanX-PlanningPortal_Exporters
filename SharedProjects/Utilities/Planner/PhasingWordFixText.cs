using System.Collections.Generic;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>
    /// The word-fix list as the scene stores it: a header line, then one "find TAB replace" line per fix, in order.
    /// Backslash, tab, line feed and carriage return inside the text are escaped (\\, \t, \n, \r). An empty list is
    /// stored as just the header, so a modeller who cleared the list does not get the defaults back.
    /// </summary>
    public static class PhasingWordFixText
    {
        public const string Header = "phasing-word-fixes 1";

        public static string Write(IEnumerable<PhasingWordFix> fixes)
        {
            var text = new StringBuilder(Header);
            foreach (var fix in fixes ?? new PhasingWordFix[0])
            {
                if (fix == null || string.IsNullOrEmpty(fix.Find))
                {
                    continue;
                }
                text.Append('\n').Append(Escape(fix.Find)).Append('\t').Append(Escape(fix.Replace));
            }
            return text.Append('\n').ToString();
        }

        /// <summary>The stored list, or null when the text is not one (no header): the caller then uses the defaults.</summary>
        public static List<PhasingWordFix> Read(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }
            var lines = text.Split('\n');
            if (lines[0].TrimEnd('\r') != Header)
            {
                return null;
            }
            var fixes = new List<PhasingWordFix>();
            for (int i = 1; i < lines.Length; i++)
            {
                var line = lines[i].TrimEnd('\r');
                var parts = line.Split('\t');
                if (parts.Length != 2 || parts[0].Length == 0)
                {
                    continue;
                }
                fixes.Add(new PhasingWordFix(Unescape(parts[0]), Unescape(parts[1])));
            }
            return fixes;
        }

        private static string Escape(string value)
        {
            var text = new StringBuilder();
            foreach (var c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '\\':
                        text.Append(@"\\");
                        break;
                    case '\t':
                        text.Append(@"\t");
                        break;
                    case '\n':
                        text.Append(@"\n");
                        break;
                    case '\r':
                        text.Append(@"\r");
                        break;
                    default:
                        text.Append(c);
                        break;
                }
            }
            return text.ToString();
        }

        private static string Unescape(string value)
        {
            var text = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '\\' || i + 1 == value.Length)
                {
                    text.Append(c);
                    continue;
                }
                var next = value[++i];
                switch (next)
                {
                    case 't':
                        text.Append('\t');
                        break;
                    case 'n':
                        text.Append('\n');
                        break;
                    case 'r':
                        text.Append('\r');
                        break;
                    default:
                        text.Append(next);
                        break;
                }
            }
            return text.ToString();
        }
    }
}
