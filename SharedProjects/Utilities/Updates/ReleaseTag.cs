using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Utilities.Updates
{
    /// <summary>
    /// A release tag as the CD workflow writes it: "v" + the build date (yyyyMMdd) + "." + the workflow run number,
    /// e.g. v20261009.3 (UrbanCGI fork). Tags compare by date, then by run number, both as numbers, so v20261009.10 is
    /// newer than v20261009.9. Anything else (such as the placeholder of a development build) is not a release tag.
    /// </summary>
    public struct ReleaseTag : IComparable<ReleaseTag>, IEquatable<ReleaseTag>
    {
        private static readonly Regex Pattern = new Regex("^v([0-9]{8})\\.([0-9]{1,9})$", RegexOptions.CultureInvariant);

        /// <summary>The build date as yyyyMMdd, e.g. 20261009.</summary>
        public readonly int Date;

        /// <summary>The workflow run number.</summary>
        public readonly int Run;

        public ReleaseTag(int date, int run)
        {
            Date = date;
            Run = run;
        }

        /// <summary>
        /// Reads a tag such as "v20261009.3" (surrounding spaces ignored). False for anything else, including a date
        /// that does not exist (v20261340.1).
        /// </summary>
        public static bool TryParse(string text, out ReleaseTag tag)
        {
            tag = default(ReleaseTag);
            if (text == null)
            {
                return false;
            }
            var match = Pattern.Match(text.Trim());
            if (!match.Success)
            {
                return false;
            }
            DateTime date;
            int run;
            if (!DateTime.TryParseExact(match.Groups[1].Value, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || !int.TryParse(match.Groups[2].Value, NumberStyles.None, CultureInfo.InvariantCulture, out run))
            {
                return false;
            }
            tag = new ReleaseTag(date.Year * 10000 + date.Month * 100 + date.Day, run);
            return true;
        }

        /// <summary>True when <paramref name="text"/> is a release tag, i.e. a build made by the CD workflow.</summary>
        public static bool IsReleaseTag(string text)
        {
            ReleaseTag tag;
            return TryParse(text, out tag);
        }

        public int CompareTo(ReleaseTag other)
        {
            return Date != other.Date ? Date.CompareTo(other.Date) : Run.CompareTo(other.Run);
        }

        public bool Equals(ReleaseTag other)
        {
            return Date == other.Date && Run == other.Run;
        }

        public override bool Equals(object obj)
        {
            return obj is ReleaseTag && Equals((ReleaseTag)obj);
        }

        public override int GetHashCode()
        {
            return Date * 31 + Run;
        }

        public static bool operator >(ReleaseTag a, ReleaseTag b)
        {
            return a.CompareTo(b) > 0;
        }

        public static bool operator <(ReleaseTag a, ReleaseTag b)
        {
            return a.CompareTo(b) < 0;
        }

        public override string ToString()
        {
            return "v" + Date.ToString("D8", CultureInfo.InvariantCulture) + "." + Run.ToString(CultureInfo.InvariantCulture);
        }
    }
}
