using System.Linq;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    public class PlannerTipTextTests
    {
        [Fact]
        public void ALongTipIsBrokenAtSpacesIntoNarrowLines()
        {
            var text = string.Join(" ", Enumerable.Repeat("word", 60));
            var lines = PlannerTipText.Wrap(text).Split(new[] { "\r\n" }, System.StringSplitOptions.None);
            Assert.True(lines.Length > 1);
            Assert.All(lines, l => Assert.True(l.Length <= PlannerTipText.Width, l));
            Assert.Equal(text, string.Join(" ", lines));
        }

        [Fact]
        public void LineBreaksIndentsAndLongWordsAreKept()
        {
            Assert.Equal("one\r\ntwo", PlannerTipText.Wrap("one\ntwo"));
            Assert.Equal("  aaa bbb\r\n  ccc", PlannerTipText.Wrap("  aaa bbb ccc", 10));
            var path = "D:\\" + new string('x', 100);
            Assert.Equal("in\r\n" + path, PlannerTipText.Wrap("in " + path, 20));
            Assert.Null(PlannerTipText.Wrap(null));
            Assert.Equal("short", PlannerTipText.Wrap("short"));
        }
    }
}
