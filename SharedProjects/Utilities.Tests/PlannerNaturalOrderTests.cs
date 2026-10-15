using System;
using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>The Planner's natural order, against what JavaScript's localeCompare gives (PlannerNaturalOrderCases).</summary>
    public class PlannerNaturalOrderTests
    {
        [Fact]
        public void EveryPairComparesAsJavaScriptComparesIt()
        {
            var wrong = PlannerNaturalOrderCases.Pairs
                .Where(p => Math.Sign(PlannerNaturalOrder.Compare((string)p[0], (string)p[1])) != (int)p[2])
                .Select(p => (string)p[0] + " | " + (string)p[1] + " expected " + p[2])
                .ToList();
            Assert.True(wrong.Count == 0, wrong.Count + " of " + PlannerNaturalOrderCases.Pairs.Length + " differ: " + string.Join("; ", wrong.Take(10)));
        }

        [Fact]
        public void NumbersCompareByValueAndCapitalsCountForNothing()
        {
            Assert.True(PlannerNaturalOrder.Compare("Ph1_St04_IN_Kerb_2", "Ph1_St04_IN_Kerb_10") < 0);
            Assert.Equal(0, PlannerNaturalOrder.Compare("a01b", "A1B"));
            Assert.True(PlannerNaturalOrder.Compare("Kerb_A", "Kerb-A") < 0);
            Assert.True(PlannerNaturalOrder.Compare("1|4", "1|4_2") < 0);
            Assert.True(PlannerNaturalOrder.Compare("10|1", "2|1") > 0);
        }

        [Fact]
        public void TheStableSortKeepsEqualItemsInTheirOrder()
        {
            var sorted = PlannerNaturalOrder.StableSort(new[] { "b", "A1", "a01", "a1", "B" }, PlannerNaturalOrder.Compare);
            Assert.Equal(new[] { "A1", "a01", "a1", "b", "B" }, sorted.ToArray());
        }
    }
}
