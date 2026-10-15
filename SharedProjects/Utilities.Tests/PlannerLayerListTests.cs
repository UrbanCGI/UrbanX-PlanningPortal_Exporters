using System;
using System.Linq;
using System.Text;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>Reading and checking the Planner's layer list (CSV).</summary>
    public class PlannerLayerListTests
    {
        private const string Header = TestCsv.Header;

        [Fact]
        public void ReadsThePlannersFileWithItsByteOrderMarkQuotesAndCrlf()
        {
            var text = "\uFEFF" + Header + "\r\n"
                + "f1,,_01_Zone5_Working,Group,,,,,_01_Zone5_Working,,,,1\r\n"
                + "f2,f1,01_04_2_Phase1_Retainment_Installation_18-09-26_23-10-26,Layer,01_04_2,1,4,2,Phase1_Retainment_Installation,2026-09-18,2026-10-23,,1\r\n"
                + "f3,f1,\"02_02_10_Integration,_\"\"FULL\"\"_CLOSURE_17-10-26\",Layer,02_02_10,2,2,10,\"Integration, \"\"FULL\"\"\",2026-10-17,2026-10-17,Yes,2\r\n";
            var list = PlannerLayerList.Parse(text);

            Assert.True(list.Usable);
            Assert.Equal(3, list.Rows.Count);
            Assert.Equal(0, list.RowsWithProblems);
            var group = list.Rows[0];
            Assert.Equal("f1", group.PlannerId);
            Assert.Equal(string.Empty, group.ParentId);
            Assert.Equal(PlannerLayerKind.Group, group.Kind);
            Assert.Null(group.IdKey);
            Assert.Equal(3, list.Rows[1].Line);
            Assert.Equal("01_04_2_Phase1_Retainment_Installation_18-09-26_23-10-26", list.Rows[1].Layer);
            Assert.Equal(new PlannerLayerIdKey(1, 4, 2), list.Rows[1].IdKey);
            var quoted = list.Rows[2];
            Assert.Equal("02_02_10_Integration,_\"FULL\"_CLOSURE_17-10-26", quoted.Layer);
            Assert.Equal("Integration, \"FULL\"", quoted.Name);
            Assert.True(quoted.Tbc);
            Assert.Equal(2, quoted.Order);
            Assert.Same(group, quoted.Parent);
            Assert.Equal(1, quoted.Depth);
            Assert.Equal(new[] { "f1", "f2", "f3" }, list.Outline().Select(r => r.PlannerId).ToArray());
        }

        [Fact]
        public void ReadsWithoutByteOrderMarkWithLfAndATrailingEmptyLine()
        {
            var text = Header + "\n" + "a,,_01_Zone,Group,,,,,,,,,\n" + "b,a,01_01_Drainage_TBC,Layer,01_01,1,1,,Drainage,,,Yes,\n\n";
            var list = PlannerLayerList.Parse(text);
            Assert.True(list.Usable);
            Assert.Equal(2, list.Rows.Count);
            Assert.Equal("01_01_Drainage_TBC", list.Rows[1].Layer);
            Assert.Equal(3, list.Rows[1].Line);
            Assert.Empty(list.Notes);
        }

        [Fact]
        public void ReadsTheBytesOfAFileInEveryEncodingItMayCome()
        {
            var text = Header + "\r\na,,_01_Zone\u2013A,Group,,,,,,,,,\r\n";
            var withBom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();
            Assert.Equal("_01_Zone\u2013A", PlannerLayerList.Parse(withBom).Rows[0].Layer);
            Assert.Equal("_01_Zone\u2013A", PlannerLayerList.Parse(new UTF8Encoding(false).GetBytes(text)).Rows[0].Layer);
            var utf16 = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray();
            Assert.Equal("_01_Zone\u2013A", PlannerLayerList.Parse(utf16).Rows[0].Layer);

            // Excel's "CSV (Comma delimited)": Windows-1252, where 0x96 is the en dash.
            var ansi = Encoding.ASCII.GetBytes(Header + "\r\na,,_01_Zone")
                .Concat(new byte[] { 0x96, (byte)'A', 0xE9 })
                .Concat(Encoding.ASCII.GetBytes(",Group,,,,,,,,,\r\n")).ToArray();
            var read = PlannerLayerList.Parse(ansi);
            Assert.Equal("_01_Zone\u2013A\u00e9", read.Rows[0].Layer);
            Assert.Contains(read.Notes, n => n.Contains("Windows-1252"));
        }

        [Fact]
        public void ReadsTheSemicolonsExcelWritesInSomeRegions()
        {
            var text = Header.Replace(',', ';') + "\r\na;;_01_Zone;Group;;;;;;;;;\r\nb;a;\"01_01_Kerbs;_x\";Layer;01_01;1;1;;Kerbs;;;;\r\n";
            var list = PlannerLayerList.Parse(text);
            Assert.True(list.Usable);
            Assert.Equal("01_01_Kerbs;_x", list.Rows[1].Layer);
            Assert.Contains(list.Notes, n => n.Contains("semicolons"));
        }

        [Fact]
        public void HeaderIsMatchedWithoutCapitalsInAnyOrderAndUnknownColumnsAreIgnored()
        {
            var text = "kind,LAYER,Comment,parent id,planner id\r\nGroup,_01_Zone,hello,,g1\r\nlayer,01_01_Kerbs_01-02-27,,g1,l1\r\n";
            var list = PlannerLayerList.Parse(text);
            Assert.True(list.Usable);
            Assert.Equal(PlannerLayerKind.Layer, list.Rows[1].Kind);
            Assert.Equal("g1", list.Rows[1].ParentId);
            Assert.Contains(list.Notes, n => n.Contains("Comment"));
            // No ID column: the ID comes from the name.
            Assert.Equal(new PlannerLayerIdKey(1, 1, null), list.Rows[1].IdKey);
        }

        [Fact]
        public void AFileWithoutTheRequiredColumnsCannotBeUsed()
        {
            var list = PlannerLayerList.Parse("Status,Type,Layer it sits in (corrected name),Current name in Max,Corrected name,What changed\r\nChanged,Layer,a,b,c,d\r\n");
            Assert.False(list.Usable);
            Assert.Contains("\"Planner ID\"", list.Errors[0]);
            Assert.Contains("\"Kind\"", list.Errors[0]);
            Assert.Empty(list.Rows);
            Assert.False(PlannerLayerList.Parse(string.Empty).Usable);
            Assert.False(PlannerLayerList.Parse("\r\n\r\n").Usable);
        }

        [Fact]
        public void EachRowProblemIsFound()
        {
            var list = TestCsv.List(
                TestCsv.Row("g1", null, "_01_Zone"),
                TestCsv.Row("g1", null, "_02_Other"),                         // duplicate Planner ID
                TestCsv.Row("l1", "nope", "01_01_Kerbs_01-02-27"),             // unknown parent
                TestCsv.Row("l2", "g1", ""),                                   // empty layer
                new[] { "l3", "g1", "01_02_Gully_01-02-27", "Folder", "", "", "", "", "", "", "", "", "" }, // bad kind
                TestCsv.Row("", "g1", "01_03_Ducts_01-02-27"),                 // no Planner ID
                TestCsv.Row("l5", "g1", "_01_ZONE"),                           // same layer name in other capitals
                TestCsv.Row("c1", "c2", "01_05_A_01-02-27"),                   // a circle
                TestCsv.Row("c2", "c1", "01_06_B_01-02-27"),
                TestCsv.Row("s1", "s1", "01_07_C_01-02-27"),                   // its own parent
                TestCsv.Row("l6", "g1", "01_08_Line\nbreak_01-02-27"));        // a line break in the name
            Assert.True(list.Usable);
            Func<int, string> problems = i => string.Join(" | ", list.Rows[i].Problems);
            Assert.Empty(list.Rows[0].Problems);
            Assert.Contains("also on line 2", problems(1));
            Assert.Contains("Parent ID nope is not in the list", problems(2));
            Assert.Contains("Layer is empty", problems(3));
            Assert.Contains("\"Folder\", not Group or Layer", problems(4));
            Assert.Contains("no Planner ID", problems(5));
            Assert.Contains("same layer name in other capitals", problems(6));
            Assert.Contains("circle", problems(7));
            Assert.Contains("circle", problems(8));
            Assert.Contains("its own parent", problems(9));
            Assert.Contains("control character", problems(10));
            Assert.Equal(10, list.RowsWithProblems);
            // Rows that hang under nothing still show, at the end of the outline.
            Assert.Equal(list.Rows.Count, list.Outline().Count);
        }

        [Fact]
        public void RowsUnderARowWithAProblemCannotBeBuiltEither()
        {
            var list = TestCsv.List(
                new[] { "g1", "", "_01_Zone", "Gruop", "", "", "", "", "", "", "", "", "" },
                TestCsv.Row("l1", "g1", "01_01_Kerbs_01-02-27"),
                TestCsv.Row("l2", "l1", "01_01_1_Kerbs_East_01-02-27"));
            Assert.Contains("under _01_Zone, which has a problem", list.Rows[1].Problems.Single());
            Assert.Contains("under 01_01_Kerbs_01-02-27, which has a problem", list.Rows[2].Problems.Single());
        }

        [Fact]
        public void OutlineFollowsParentsAndTheOrderColumn()
        {
            var text = Header + "\r\n"
                + "b,,_02_B,Group,,,,,,,,,2\r\n"
                + "b2,b,02_02_Y_01-02-27,Layer,02_02,2,2,,,,,,2\r\n"
                + "a,,_01_A,Group,,,,,,,,,1\r\n"
                + "b1,b,02_01_X_01-02-27,Layer,02_01,2,1,,,,,,1\r\n"
                + "b11,b1,02_01_1_X_01-02-27,Layer,02_01_1,2,1,1,,,,,1\r\n";
            var list = PlannerLayerList.Parse(text);
            Assert.Equal(new[] { "a", "b", "b1", "b11", "b2" }, list.Outline().Select(r => r.PlannerId).ToArray());
            Assert.Equal(new[] { 0, 0, 1, 2, 1 }, list.Outline().Select(r => r.Depth).ToArray());
            Assert.Equal("5 rows: 2 groups and 3 layers.", list.Describe());
        }

        [Fact]
        public void TheIdColumnIsTheMatchKeyUnlessItDisagreesWithTheName()
        {
            var list = TestCsv.List(
                new[] { "a", "", "02_01_2_Kerbs_01-02-27", "Layer", "2_1_2", "", "", "", "", "", "", "", "" },
                new[] { "b", "", "02_01_3_Kerbs_01-02-27", "Layer", "02_01_9", "", "", "", "", "", "", "", "" },
                new[] { "c", "", "06_Hoarding_01-02-27", "Layer", "06", "", "", "", "", "", "", "", "" },
                new[] { "d", "", "Drainage_(client_wording)", "Layer", "", "", "", "", "", "", "", "", "" },
                new[] { "e", "", "02_04_Gully_01-02-27", "Layer", "abc", "", "", "", "", "", "", "", "" });
            Assert.Equal(new PlannerLayerIdKey(2, 1, 2), list.Rows[0].IdKey);
            Assert.Null(list.Rows[1].IdKey);
            Assert.Contains(list.Rows[1].Notes, n => n.Contains("not the ID its name carries (02_01_3)"));
            Assert.Equal(new PlannerLayerIdKey(null, 6, null), list.Rows[2].IdKey);
            Assert.Null(list.Rows[3].IdKey);
            Assert.Null(list.Rows[4].IdKey);
            Assert.Contains(list.Rows[4].Notes, n => n.Contains("does not read as a layer ID"));
            Assert.Equal(0, list.RowsWithProblems);
        }

        [Fact]
        public void IdKeysReadTheWaysAnIdIsWritten()
        {
            Assert.Equal(new PlannerLayerIdKey(2, 1, 2), PlannerLayerIdKey.FromText("02_01_2"));
            Assert.Equal(new PlannerLayerIdKey(2, 1, 2), PlannerLayerIdKey.FromText("2_1_2"));
            Assert.Equal(new PlannerLayerIdKey(2, 1, 2), PlannerLayerIdKey.FromText("02_01.2"));
            Assert.Equal(new PlannerLayerIdKey(2, 1, null), PlannerLayerIdKey.FromText(" 02_01 "));
            Assert.Equal(new PlannerLayerIdKey(null, 6, null), PlannerLayerIdKey.FromText("06"));
            Assert.Null(PlannerLayerIdKey.FromText("02-01"));
            Assert.Null(PlannerLayerIdKey.FromText("\u0661\u0662"));
            Assert.Equal("02_01_2", PlannerLayerIdKey.FromName("02_01.2_Construction_31-08-26").ToString());
            Assert.Equal("02_01", PlannerLayerIdKey.FromName("02_01_17-08-26_Kerbs").ToString());
            Assert.Null(PlannerLayerIdKey.FromName("_01_Zone5_Working"));
        }

        [Fact]
        public void RecordsKeepLineBreaksAndQuotesInsideQuotedFields()
        {
            var records = PlannerLayerList.ReadRecords("a,\"b\r\nc\",\"d\"\"e\"\r\nf,,\r\n\"g\"x,h", ',');
            Assert.Equal(3, records.Count);
            Assert.Equal(new[] { "a", "b\r\nc", "d\"e" }, records[0].Fields.ToArray());
            Assert.Equal(1, records[0].Line);
            Assert.Equal(new[] { "f", "", "" }, records[1].Fields.ToArray());
            Assert.Equal(3, records[1].Line);
            Assert.Equal(new[] { "gx", "h" }, records[2].Fields.ToArray());
            Assert.Equal(new[] { "a", "" }, PlannerLayerList.ReadRecords("a,", ',')[0].Fields.ToArray());
            Assert.Equal(2, PlannerLayerList.ReadRecords("a\rb", ',').Count);
        }

        [Fact]
        public void TheListKeptWithTheSceneReadsBack()
        {
            var csv = TestCsv.Write(new[] { TestCsv.Row("g1", null, "_01_Zone"), TestCsv.Row("l1", "g1", "01_01_Kerbs_01-02-27") }) + "\r\n\r\n";
            var when = new DateTime(2026, 10, 15, 9, 30, 0, DateTimeKind.Utc);
            var stored = PlannerLayerListStore.Write("C:\\Jobs\\HS2\nlist.csv", when, csv);
            string file;
            DateTime? imported;
            string text;
            Assert.True(PlannerLayerListStore.TryRead(stored, out file, out imported, out text));
            Assert.Equal("C:\\Jobs\\HS2 list.csv", file);
            Assert.Equal(when, imported);
            Assert.Equal(csv, text);
            Assert.Equal(2, PlannerLayerList.Parse(text).Rows.Count);
            Assert.False(PlannerLayerListStore.TryRead("something else", out file, out imported, out text));
            Assert.False(PlannerLayerListStore.TryRead(null, out file, out imported, out text));
            Assert.False(PlannerLayerListStore.TryRead("PlannerLayerList 1\nFile: x", out file, out imported, out text));
        }
    }
}
