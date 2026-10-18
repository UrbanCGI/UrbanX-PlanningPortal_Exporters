using System;
using System.Globalization;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The Planner's natural order past its table's characters, against what JavaScript's localeCompare gives
    /// (PlannerNaturalOrderUnicodeCases): an accent typed after its letter and zero-width or other format characters count
    /// for nothing, every space separator sorts as a space, and letters past the table sort as their small letters and their
    /// compatibility forms (a fullwidth K as K, a Roman numeral as its letters, the micro sign as the Greek mu).
    /// </summary>
    public class PlannerNaturalOrderUnicodeTests
    {
        private static string Shown(string text)
        {
            return string.Concat(text.Select(ch => ch < 32 || ch > 126 ? "U+" + ((int)ch).ToString("X4", CultureInfo.InvariantCulture) : ch.ToString()));
        }

        [Fact]
        public void EveryPairComparesAsJavaScriptComparesIt()
        {
            var wrong = PlannerNaturalOrderUnicodeCases.Pairs
                .Where(p => Math.Sign(PlannerNaturalOrder.Compare((string)p[0], (string)p[1])) != (int)p[2])
                .Select(p => p[3] + ": " + Shown((string)p[0]) + " | " + Shown((string)p[1]) + " expected " + p[2])
                .ToList();
            Assert.True(wrong.Count == 0, wrong.Count + " of " + PlannerNaturalOrderUnicodeCases.Pairs.Length + " differ: " + string.Join("; ", wrong.Take(12)));
        }

        [Fact]
        public void OwnNamesSortAsThePlannerSortsThem()
        {
            // The Planner's D5 order for these own names in one layer (the edge parity scene unicode-own-name-order).
            var accent = "Ke" + (char)0x0301 + "rb";
            var zeroWidth = "Kerb" + (char)0x200B + "_2";
            var fullwidth = (char)0xFF2B + "erb_3";
            var vowelSeparator = "Kerb" + (char)0x180E + "_4";
            var roman = "Wall_" + (char)0x2162;
            var smallDelta = (char)0x03B4 + "T_1";
            var capitalDelta = (char)0x0394 + "T_2";
            var names = new[] { "Kerc", accent, zeroWidth, "Kerb_10", capitalDelta, smallDelta, fullwidth, vowelSeparator, roman, "Wall_II" };
            var sorted = PlannerNaturalOrder.StableSort(names, PlannerNaturalOrder.Compare);
            Assert.Equal(new[] { accent, zeroWidth, fullwidth, vowelSeparator, "Kerb_10", "Kerc", "Wall_II", roman, smallDelta, capitalDelta }.Select(Shown), sorted.Select(Shown));
        }
    }
}
