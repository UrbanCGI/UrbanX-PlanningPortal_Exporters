using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>
    /// The order the Planner sorts names in: JavaScript's <c>a.localeCompare(b, undefined, { numeric: true, sensitivity:
    /// 'base' })</c> (its <c>cmp</c>), which Generate from model uses to order layers, objects and activities. ICU's root
    /// collation at its first (base) strength: capitals and accents count for nothing, punctuation and spaces sort before
    /// digits and digits before letters, in the order of the table below (generated from Node v26.1.0, ICU 78.3);
    /// a run of digits compares by its number ("St9" before "St10"; "01" is "1"); a few letters stand for two (the ligature ae for a and e);
    /// control characters count for nothing. Two names that differ only in what counts for nothing compare equal, and a
    /// sort keeps them in the order it was given (List.Sort is not stable: use <see cref="StableSort{T}"/>). Characters outside
    /// the table (beyond Latin Extended-B and general punctuation) sort after it, by their code. Pure logic, no 3ds Max.
    /// </summary>
    public static class PlannerNaturalOrder
    {
        // Characters equal at the base strength, group by group, in ICU's order.
        private static readonly string[] Groups =
        {
            "\u0009", "\u000A", "\u000B", "\u000C", "\u000D", "\u0085",
            "\u2028", "\u2029", "\u0020\u00A0\u202F\u3000", "\u203E", "\u005F", "\u2017",
            "\u002D", "\u2010\u2011", "\u2012", "\u2013", "\u2014", "\u2015",
            "\u2053", "\u002C", "\u003B", "\u204F", "\u003A", "\u0021",
            "\u203C", "\u2049", "\u00A1", "\u003F", "\u2048", "\u2047",
            "\u00BF", "\u203D", "\u002E\u2024", "\u2025", "\u2026", "\u00B7",
            "\u2055", "\u2056", "\u2058", "\u2059", "\u205A", "\u205B",
            "\u205C", "\u205D", "\u205E", "\u0027\u2018\u2019\u201A\u201B", "\u2039", "\u203A",
            "\u0022\u201C\u201D\u201E\u201F", "\u00AB", "\u00BB", "\u0028", "\u0029", "\u005B",
            "\u005D", "\u007B", "\u007D", "\u2045", "\u2046", "\u2016",
            "\u00A7", "\u00B6", "\u204B", "\u0040", "\u002A", "\u204E",
            "\u2051", "\u002F", "\u005C", "\u0026", "\u204A", "\u0023",
            "\u0025", "\u2030", "\u2031", "\u2020", "\u2021", "\u2022",
            "\u2023", "\u2027", "\u2043", "\u204C", "\u204D", "\u2032",
            "\u2033", "\u2034", "\u2057", "\u2035", "\u2036", "\u2037",
            "\u2038", "\u203B", "\u203F", "\u2054", "\u2040", "\u2050",
            "\u2041", "\u2042", "\u0060", "\u00B4", "\u005E", "\u00AF",
            "\u00A8", "\u00B8", "\u00B0", "\u00A9", "\u00AE", "\u002B",
            "\u00B1", "\u00F7", "\u00D7", "\u003C", "\u003D", "\u003E",
            "\u00AC", "\u007C", "\u00A6", "\u007E", "\u2052", "\u2044",
            "\u00A4", "\u00A2", "\u0024", "\u00A3", "\u00A5", "\u20AC",
            "0", "1", "2", "3", "4", "5",
            "6", "7", "8", "9", "\u00B9", "\u00BD",
            "\u00BC", "\u00B2", "\u00B3", "\u00BE", "Aa\u00AA\u00C0\u00C1\u00C2\u00C3\u00C4\u00C5\u00E0\u00E1\u00E2\u00E3\u00E4\u00E5\u0100\u0101\u0102\u0103\u0104\u0105\u01CD\u01CE\u01DE\u01DF\u01E0\u01E1\u01FA\u01FB\u0200\u0201\u0202\u0203\u0226\u0227", "\u023A",
            "Bb", "\u0180\u0243", "\u0181", "\u0182\u0183", "Cc\u00C7\u00E7\u0106\u0107\u0108\u0109\u010A\u010B\u010C\u010D", "\u023B\u023C",
            "\u0187\u0188", "Dd\u00D0\u00F0\u010E\u010F\u0110\u0111", "\u0189", "\u018A", "\u018B\u018C", "\u0221",
            "Ee\u00C8\u00C9\u00CA\u00CB\u00E8\u00E9\u00EA\u00EB\u0112\u0113\u0114\u0115\u0116\u0117\u0118\u0119\u011A\u011B\u0204\u0205\u0206\u0207\u0228\u0229", "\u0246\u0247", "\u018E\u01DD", "\u018F", "\u0190", "Ff",
            "\u0191\u0192", "Gg\u011C\u011D\u011E\u011F\u0120\u0121\u0122\u0123\u01E6\u01E7\u01F4\u01F5", "\u01E4\u01E5", "\u0193", "\u0194", "\u01A2\u01A3",
            "Hh\u0124\u0125\u0126\u0127\u021E\u021F", "\u0195\u01F6", "Ii\u00CC\u00CD\u00CE\u00CF\u00EC\u00ED\u00EE\u00EF\u0128\u0129\u012A\u012B\u012C\u012D\u012E\u012F\u0130\u01CF\u01D0\u0208\u0209\u020A\u020B", "\u0131", "\u0197", "\u0196",
            "Jj\u0134\u0135\u01F0", "\u0237", "\u0248\u0249", "Kk\u0136\u0137\u01E8\u01E9", "\u0198\u0199", "Ll\u0139\u013A\u013B\u013C\u013D\u013E\u013F\u0140\u0141\u0142",
            "\u019A\u023D", "\u0234", "\u019B", "Mm", "Nn\u00D1\u00F1\u0143\u0144\u0145\u0146\u0147\u0148\u01F8\u01F9", "\u019D",
            "\u019E\u0220", "\u0235", "\u014A\u014B", "Oo\u00BA\u00D2\u00D3\u00D4\u00D5\u00D6\u00D8\u00F2\u00F3\u00F4\u00F5\u00F6\u00F8\u014C\u014D\u014E\u014F\u0150\u0151\u01A0\u01A1\u01D1\u01D2\u01EA\u01EB\u01EC\u01ED\u01FE\u01FF\u020C\u020D\u020E\u020F\u022A\u022B\u022C\u022D\u022E\u022F\u0230\u0231", "\u0186", "\u019F",
            "\u0222\u0223", "Pp", "\u01A4\u01A5", "Qq", "\u024A\u024B", "\u0138",
            "Rr\u0154\u0155\u0156\u0157\u0158\u0159\u0210\u0211\u0212\u0213", "\u01A6", "\u024C\u024D", "Ss\u015A\u015B\u015C\u015D\u015E\u015F\u0160\u0161\u017F\u0218\u0219", "\u023F", "\u01A9",
            "\u01AA", "Tt\u0162\u0163\u0164\u0165\u021A\u021B", "\u0166\u0167", "\u023E", "\u01AB", "\u01AC\u01AD",
            "\u01AE", "\u0236", "Uu\u00D9\u00DA\u00DB\u00DC\u00F9\u00FA\u00FB\u00FC\u0168\u0169\u016A\u016B\u016C\u016D\u016E\u016F\u0170\u0171\u0172\u0173\u01AF\u01B0\u01D3\u01D4\u01D5\u01D6\u01D7\u01D8\u01D9\u01DA\u01DB\u01DC\u0214\u0215\u0216\u0217", "\u0244", "\u019C", "\u01B1",
            "Vv", "\u01B2", "\u0245", "Ww\u0174\u0175", "Xx", "Yy\u00DD\u00FD\u00FF\u0176\u0177\u0178\u0232\u0233",
            "\u024E\u024F", "\u01B3\u01B4", "\u021C\u021D", "Zz\u0179\u017A\u017B\u017C\u017D\u017E", "\u01B5\u01B6", "\u0224\u0225",
            "\u0240", "\u01B7\u01EE\u01EF", "\u01B8\u01B9", "\u01BA", "\u00DE\u00FE", "\u01BF\u01F7",
            "\u01BB", "\u01A7\u01A8", "\u01BC\u01BD", "\u0184\u0185", "\u0241\u0242", "\u0149",
            "\u01C0", "\u01C1", "\u01C2", "\u01C3", "\u00B5"
        };

        // Characters that count for nothing.
        private const string Ignorable =
            "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u000E\u000F\u0010\u0011\u0012\u0013\u0014" +
            "\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F\u007F\u0080\u0081\u0082\u0083" +
            "\u0084\u0086\u0087\u0088\u0089\u008A\u008B\u008C\u008D\u008E\u008F\u0090\u0091\u0092\u0093\u0094" +
            "\u0095\u0096\u0097\u0098\u0099\u009A\u009B\u009C\u009D\u009E\u009F\u00AD\u202A\u202B\u202C\u202D" +
            "\u202E\uFEFF";

        // Letters that sort as two.
        private static readonly KeyValuePair<char, string>[] Expansions =
        {
            new KeyValuePair<char, string>('\u00C6', "ae"),
            new KeyValuePair<char, string>('\u00DF', "ss"),
            new KeyValuePair<char, string>('\u00E6', "ae"),
            new KeyValuePair<char, string>('\u0132', "ij"),
            new KeyValuePair<char, string>('\u0133', "ij"),
            new KeyValuePair<char, string>('\u0152', "oe"),
            new KeyValuePair<char, string>('\u0153', "oe"),
            new KeyValuePair<char, string>('\u018D', "zw"),
            new KeyValuePair<char, string>('\u01BE', "ts"),
            new KeyValuePair<char, string>('\u01C4', "dz"),
            new KeyValuePair<char, string>('\u01C5', "dz"),
            new KeyValuePair<char, string>('\u01C6', "dz"),
            new KeyValuePair<char, string>('\u01C7', "lj"),
            new KeyValuePair<char, string>('\u01C8', "lj"),
            new KeyValuePair<char, string>('\u01C9', "lj"),
            new KeyValuePair<char, string>('\u01CA', "nj"),
            new KeyValuePair<char, string>('\u01CB', "nj"),
            new KeyValuePair<char, string>('\u01CC', "nj"),
            new KeyValuePair<char, string>('\u01E2', "ae"),
            new KeyValuePair<char, string>('\u01E3', "ae"),
            new KeyValuePair<char, string>('\u01F1', "dz"),
            new KeyValuePair<char, string>('\u01F2', "dz"),
            new KeyValuePair<char, string>('\u01F3', "dz"),
            new KeyValuePair<char, string>('\u01FC', "ae"),
            new KeyValuePair<char, string>('\u01FD', "ae"),
            new KeyValuePair<char, string>('\u0238', "db"),
            new KeyValuePair<char, string>('\u0239', "qp"),
            new KeyValuePair<char, string>('\u2122', "tm")
        };

        private static readonly Dictionary<char, int> Rank = BuildRanks();
        private static readonly Dictionary<char, string> Expanded = BuildExpansions();
        private static readonly HashSet<char> Skipped = new HashSet<char>(Ignorable);
        // Where a run of digits sorts among single characters: with the digits.
        private static readonly int DigitRank = Rank['0'];
        private const int Unknown = 1000000;

        /// <summary>The comparer for sorts.</summary>
        public static readonly IComparer<string> Comparer = new NaturalComparer();

        /// <summary>Negative, zero or positive as JavaScript's localeCompare with numeric and base sensitivity gives it.</summary>
        public static int Compare(string a, string b)
        {
            var x = Cached(a ?? string.Empty);
            var y = Cached(b ?? string.Empty);
            int count = Math.Min(x.Count, y.Count);
            for (int i = 0; i < count; i++)
            {
                int c = CompareUnits(x[i], y[i]);
                if (c != 0)
                {
                    return c;
                }
            }
            return x.Count.CompareTo(y.Count);
        }

        /// <summary>A stable sort (equal items keep their order), as JavaScript's Array.prototype.sort is.</summary>
        public static List<T> StableSort<T>(IEnumerable<T> items, Comparison<T> comparison)
        {
            var indexed = new List<KeyValuePair<int, T>>();
            foreach (var item in items)
            {
                indexed.Add(new KeyValuePair<int, T>(indexed.Count, item));
            }
            indexed.Sort((p, q) =>
            {
                int c = comparison(p.Value, q.Value);
                return c != 0 ? c : p.Key.CompareTo(q.Key);
            });
            var result = new List<T>(indexed.Count);
            foreach (var pair in indexed)
            {
                result.Add(pair.Value);
            }
            return result;
        }

        // One unit of a name: a character's rank, or a run of digits as its number without leading zeros.
        private struct Unit
        {
            public int Rank;
            public string Number;
        }

        private static int CompareUnits(Unit x, Unit y)
        {
            if (x.Number != null && y.Number != null)
            {
                if (x.Number.Length != y.Number.Length)
                {
                    return x.Number.Length < y.Number.Length ? -1 : 1;
                }
                int c = string.CompareOrdinal(x.Number, y.Number);
                return c < 0 ? -1 : c > 0 ? 1 : 0;
            }
            int rx = x.Number != null ? DigitRank : x.Rank;
            int ry = y.Number != null ? DigitRank : y.Rank;
            if (rx == ry && (x.Number != null || y.Number != null))
            {
                // A digit against a run of digits cannot happen (a digit is a run); anything else in the digits' place
                // sorts after them.
                return x.Number != null ? -1 : 1;
            }
            return rx.CompareTo(ry);
        }

        // Sorts compare the same names many times: their units are kept, per thread, for a while.
        [ThreadStatic] private static Dictionary<string, List<Unit>> cache;

        private static List<Unit> Cached(string text)
        {
            if (cache == null || cache.Count > 200000)
            {
                cache = new Dictionary<string, List<Unit>>(StringComparer.Ordinal);
            }
            List<Unit> units;
            if (!cache.TryGetValue(text, out units))
            {
                units = Units(text);
                cache[text] = units;
            }
            return units;
        }

        private static List<Unit> Units(string text)
        {
            var units = new List<Unit>(text.Length);
            int i = 0;
            while (i < text.Length)
            {
                char ch = text[i];
                int digit = DigitValue(ch);
                if (digit >= 0)
                {
                    var number = new StringBuilder();
                    while (i < text.Length && (digit = DigitValue(text[i])) >= 0)
                    {
                        if (number.Length > 0 || digit != 0)
                        {
                            number.Append((char)('0' + digit));
                        }
                        i++;
                    }
                    units.Add(new Unit { Number = number.Length > 0 ? number.ToString() : "0" });
                    continue;
                }
                i++;
                if (Skipped.Contains(ch))
                {
                    continue;
                }
                string two;
                if (Expanded.TryGetValue(ch, out two))
                {
                    foreach (var part in two)
                    {
                        units.Add(new Unit { Rank = Rank[part] });
                    }
                    continue;
                }
                units.Add(new Unit { Rank = RankOf(ch) });
            }
            return units;
        }

        // A decimal digit's value (any script's, as ICU reads digits), or -1.
        private static int DigitValue(char ch)
        {
            if (ch >= '0' && ch <= '9')
            {
                return ch - '0';
            }
            return ch > 127 && char.IsDigit(ch) ? CharUnicodeInfo.GetDecimalDigitValue(ch) : -1;
        }

        // The table's rank; else the rank of its letter without accents; else after the table, by its code.
        private static int RankOf(char ch)
        {
            int rank;
            if (Rank.TryGetValue(ch, out rank))
            {
                return rank;
            }
            var plain = ch.ToString().Normalize(NormalizationForm.FormD);
            if (plain.Length > 0 && plain[0] != ch && Rank.TryGetValue(plain[0], out rank))
            {
                return rank;
            }
            return Unknown + ch;
        }

        private static Dictionary<char, int> BuildRanks()
        {
            var ranks = new Dictionary<char, int>();
            for (int g = 0; g < Groups.Length; g++)
            {
                foreach (var ch in Groups[g])
                {
                    ranks[ch] = g;
                }
            }
            return ranks;
        }

        private static Dictionary<char, string> BuildExpansions()
        {
            var map = new Dictionary<char, string>();
            foreach (var pair in Expansions)
            {
                map[pair.Key] = pair.Value;
            }
            return map;
        }

        private sealed class NaturalComparer : IComparer<string>
        {
            public int Compare(string x, string y)
            {
                return PlannerNaturalOrder.Compare(x, y);
            }
        }
    }
}
