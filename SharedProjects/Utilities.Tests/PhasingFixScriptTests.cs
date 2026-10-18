using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>Reads back the MAXScript the tool's batches write: string literals, array literals and bracket balance.</summary>
    internal static class MaxScriptText
    {
        /// <summary>The contents of a MAXScript string literal starting at <paramref name="start"/> (the opening quote).</summary>
        public static string ReadLiteral(string script, ref int position)
        {
            Assert.Equal('"', script[position]);
            var text = new StringBuilder();
            for (position++; position < script.Length; position++)
            {
                var c = script[position];
                if (c == '"')
                {
                    position++;
                    return text.ToString();
                }
                if (c == '\\')
                {
                    position++;
                    Assert.True(position < script.Length, "a string literal ends in a backslash");
                    var next = script[position];
                    switch (next)
                    {
                        case 'n': text.Append('\n'); break;
                        case 'r': text.Append('\r'); break;
                        case 't': text.Append('\t'); break;
                        case '\\': text.Append('\\'); break;
                        case '"': text.Append('"'); break;
                        default: throw new Xunit.Sdk.XunitException("unknown escape \\" + next);
                    }
                    continue;
                }
                Assert.True(c != '\n' && c != '\r', "a raw line break inside a string literal");
                text.Append(c);
            }
            throw new Xunit.Sdk.XunitException("unterminated string literal");
        }

        /// <summary>Every bracket outside strings and comments is matched, and every string literal ends.</summary>
        public static void AssertBalanced(string script)
        {
            int depth = 0;
            for (int i = 0; i < script.Length;)
            {
                var c = script[i];
                if (c == '"')
                {
                    ReadLiteral(script, ref i);
                    continue;
                }
                if (c == '-' && i + 1 < script.Length && script[i + 1] == '-')
                {
                    while (i < script.Length && script[i] != '\n') i++;
                    continue;
                }
                if (c == '(') depth++;
                if (c == ')') depth--;
                Assert.True(depth >= 0, "a closing bracket without an opening one at " + i);
                i++;
            }
            Assert.Equal(0, depth);
        }

        /// <summary>The items of "local name = #(...)": string literals read back, numbers as written.</summary>
        public static List<string> ArrayItems(string script, string name)
        {
            var head = "local " + name + " = #(";
            int i = script.IndexOf(head, StringComparison.Ordinal);
            Assert.True(i >= 0, "no array " + name);
            i += head.Length;
            var items = new List<string>();
            var number = new StringBuilder();
            while (true)
            {
                var c = script[i];
                if (c == '"')
                {
                    items.Add(ReadLiteral(script, ref i));
                    continue;
                }
                if (char.IsDigit(c))
                {
                    number.Append(c);
                }
                else if (c == ',' || c == ')')
                {
                    if (number.Length > 0)
                    {
                        items.Add(number.ToString());
                        number.Clear();
                    }
                    if (c == ')') return items;
                }
                else
                {
                    Assert.True(char.IsWhiteSpace(c), "unexpected '" + c + "' in array " + name);
                }
                i++;
            }
        }
    }

    /// <summary>
    /// The MAXScript text the batches share (<see cref="PhasingFixScript"/>), and the fixer's plan carried out on the abstract
    /// scene. Fix phasing names' own batch is gone with its button (Clean up scene graph replaces it), and its tests with it.
    /// </summary>
    public class PhasingFixScriptTests
    {
        // A scene with something of every kind to do: a top node and a layer to rename, a layer to move into its
        // group, objects to rename, and the Max layers that carry the same names.
        private static FixerScene MixedScene()
        {
            var scene = new FixerScene();
            var top = scene.Add("Road Phasing \"Main\" v.01");
            var group = scene.Add("_01_Works", top);
            var layer = scene.Add("1.01_Site set-up_01-01-27", group);
            scene.Add("Ph1_St1_IN_Fence $A & B!!! 100%", layer);
            scene.Add("Ph1_St1_IN_Back\\slash", layer);
            var moved = scene.Add("01_02_Hoarding_TBC", top);
            scene.Add("Ph1_St02_IN_Hoarding", moved);
            scene.AddMaxLayer("Road Phasing \"Main\" v.01");
            scene.AddMaxLayer("_01_Works", "Road Phasing \"Main\" v.01");
            scene.AddMaxLayer("1.01_Site set-up_01-01-27", "_01_Works");
            scene.AddMaxLayer("01_02_Hoarding_TBC", "Road Phasing \"Main\" v.01");
            return scene;
        }

        [Fact]
        public void QuoteEscapesWhatMaxScriptReadsSpecially()
        {
            Assert.Equal("\"plain_name\"", PhasingFixScript.Quote("plain_name"));
            Assert.Equal("\"a\\\\b\"", PhasingFixScript.Quote("a\\b"));
            Assert.Equal("\"say \\\"hi\\\"\"", PhasingFixScript.Quote("say \"hi\""));
            Assert.Equal("\"tab\\there\\nnew\\rline\"", PhasingFixScript.Quote("tab\there\nnew\rline"));
            // $, !!!, %, & and spaces mean nothing inside a MAXScript string literal; they pass through.
            Assert.Equal("\"$box !!! 100% & more\"", PhasingFixScript.Quote("$box !!! 100% & more"));
            Assert.Equal("\"\"", PhasingFixScript.Quote(string.Empty));
            Assert.Equal("\"\"", PhasingFixScript.Quote(null));
            Assert.Equal("\"Stra\u00dfe_\u2013_caf\u00e9\"", PhasingFixScript.Quote("Stra\u00dfe_\u2013_caf\u00e9"));
            Assert.Null(PhasingFixScript.Quote("bell" + (char)7));
            Assert.Null(PhasingFixScript.Quote("del" + (char)0x7f));
            Assert.Null(PhasingFixScript.Quote("nul" + (char)0));
        }

        [Fact]
        public void QuotedNamesReadBackExactly()
        {
            var names = new[] { "a\\b", "\"", "\\\"", "x\\", "$", "!!!", "%d %s", "&&", "  two  spaces ", "\\n not a line feed", "line\nfeed", "\\\\server\\share" };
            foreach (var name in names)
            {
                var literal = PhasingFixScript.Quote(name);
                int position = 0;
                Assert.Equal(name, MaxScriptText.ReadLiteral(literal, ref position));
                Assert.Equal(literal.Length, position);
            }
        }

        [Fact]
        public void OneLineFlattensWhatMaxSentBack()
        {
            Assert.Equal("-- Syntax error: at ), expected <factor> -- In line: x", PhasingFixScript.OneLine("-- Syntax error: at ),\texpected <factor>\r\n-- In line: x\n", 300));
            Assert.Equal("abc\u2026", PhasingFixScript.OneLine("abcdef", 3));
            Assert.Equal(string.Empty, PhasingFixScript.OneLine(null, 10));
        }

        [Fact]
        public void AppliedPlanLeavesNothingToDo()
        {
            // The plan carried out on the abstract scene, as Fix phasing names' batch did: the next plan is empty.
            var scene = MixedScene();
            var plan = scene.Plan();
            scene.Apply(plan);
            var again = scene.Plan();
            Assert.False(again.HasChanges());
            Assert.Equal(new[] { "Road_Phasing_\"Main\"_v.01", "_01_Works", "01_01_Site_set-up_01-01-27", "01_02_Hoarding_TBC" },
                scene.MaxLayers.Select(l => l.Name));
            Assert.Equal("_01_Works", scene.MaxLayers.Single(l => l.Name == "01_02_Hoarding_TBC").ParentName);
            Assert.Equal("Road_Phasing_\"Main\"_v.01", scene.MaxLayers.Single(l => l.Name == "_01_Works").ParentName);
        }
    }

    public class PhasingWordFixTextTests
    {
        [Fact]
        public void DefaultsRoundTrip()
        {
            var text = PhasingWordFixText.Write(PhasingNameFixer.DefaultWordFixes());
            Assert.Equal("phasing-word-fixes 1\nConstraction\tConstruction\nSubGgrade\tSubGrade\nCource\tCourse\nIslandsl\tIslands\nSub-grade_and_Sub-base\tSubGrade_and_SubBase\n", text);
            var read = PhasingWordFixText.Read(text);
            Assert.Equal(PhasingNameFixer.DefaultWordFixes().Select(f => f.Find + "|" + f.Replace), read.Select(f => f.Find + "|" + f.Replace));
        }

        [Fact]
        public void AwkwardTextRoundTrips()
        {
            var fixes = new List<PhasingWordFix>
            {
                new PhasingWordFix("tab\there", "back\\slash"),
                new PhasingWordFix("line\nfeed", "carriage\rreturn"),
                new PhasingWordFix("\\t literal", "\\\\"),
                new PhasingWordFix("Stra\u00dfe", string.Empty),
                new PhasingWordFix(" lead and trail ", null),
                new PhasingWordFix("end\\", "x")
            };
            var read = PhasingWordFixText.Read(PhasingWordFixText.Write(fixes));
            Assert.Equal(fixes.Select(f => f.Find + "|" + (f.Replace ?? string.Empty)), read.Select(f => f.Find + "|" + f.Replace));
        }

        [Fact]
        public void EmptyListStaysEmpty()
        {
            var text = PhasingWordFixText.Write(new PhasingWordFix[0]);
            Assert.Equal(PhasingWordFixText.Header + "\n", text);
            var read = PhasingWordFixText.Read(text);
            Assert.NotNull(read);
            Assert.Empty(read);
            Assert.Empty(PhasingWordFixText.Read(PhasingWordFixText.Write(null)));
        }

        [Fact]
        public void TextThatIsNotAListReadsAsNone()
        {
            Assert.Null(PhasingWordFixText.Read(null));
            Assert.Null(PhasingWordFixText.Read(string.Empty));
            Assert.Null(PhasingWordFixText.Read("Constraction\tConstruction\n"));
            Assert.Null(PhasingWordFixText.Read("phasing-word-fixes 2\nA\tB\n"));
        }

        [Fact]
        public void BrokenLinesAreSkipped()
        {
            var read = PhasingWordFixText.Read("phasing-word-fixes 1\r\nA\tB\r\n\r\nno tab\r\n\tempty find\r\nC\tD\tE\r\nF\t\r\n");
            Assert.Equal(new[] { "A|B", "F|" }, read.Select(f => f.Find + "|" + f.Replace));
            // Fixes with no text to find are not stored.
            var text = PhasingWordFixText.Write(new[] { new PhasingWordFix(string.Empty, "x"), null, new PhasingWordFix("a", "b") });
            Assert.Equal("phasing-word-fixes 1\na\tb\n", text);
        }
    }
}
