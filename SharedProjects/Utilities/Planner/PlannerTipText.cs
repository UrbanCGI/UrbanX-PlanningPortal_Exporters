using System.Collections.Generic;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>
    /// UrbanCGI fork: a tooltip's text broken into lines of at most <see cref="Width"/> characters at spaces, so a long tip
    /// shows as a narrow box instead of one line across the screen (a Windows tooltip breaks a line only where its text
    /// does). Line breaks already in the text are kept, a line's leading spaces carry on to the lines it is broken into, and
    /// a word longer than a line stays whole.
    /// </summary>
    public static class PlannerTipText
    {
        public const int Width = 80;

        public static string Wrap(string text)
        {
            return Wrap(text, Width);
        }

        public static string Wrap(string text, int width)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text;
            }
            var lines = new List<string>();
            foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
            {
                int indent = paragraph.Length - paragraph.TrimStart(' ').Length;
                var pad = new string(' ', indent);
                var line = new StringBuilder(pad);
                foreach (var word in paragraph.Substring(indent).Split(' '))
                {
                    if (line.Length > indent && line.Length + 1 + word.Length > width)
                    {
                        lines.Add(line.ToString());
                        line = new StringBuilder(pad);
                    }
                    if (line.Length > indent)
                    {
                        line.Append(' ');
                    }
                    line.Append(word);
                }
                lines.Add(line.ToString());
            }
            return string.Join("\r\n", lines);
        }
    }
}
