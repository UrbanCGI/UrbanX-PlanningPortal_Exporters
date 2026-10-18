using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Utilities.Planner;
using Xunit;

namespace Utilities.Tests
{
    /// <summary>
    /// The MAXScript of the batches the Schedule tab runs, word for word: Clean up scene graph's (<see cref="PlannerCleanUpScript"/>)
    /// and Add layer's (<see cref="PlannerLayerScript"/>). The batch tests run each batch's data on a scene snapshot
    /// (<see cref="CleanUpScriptRun"/>, <see cref="TestSimulator"/>), not MAXScript, so a loop that went wrong (its bound, the
    /// slot a layer or helper it makes takes, a setUserProp) would still pass them. So everything after a batch's arrays (the
    /// lookups, the undo record, every loop and the report) is checked here against the copy kept in the Golden folder, which
    /// was read line by line against what the stand-ins do. A change to a batch's MAXScript fails here until the change is
    /// checked (in 3ds Max) and the copy updated; the failure writes the batch as it is now beside the test assembly.
    /// </summary>
    public class PlannerBatchScriptTextTests
    {
        private static string Here([CallerFilePath] string path = null)
        {
            return Path.GetDirectoryName(path);
        }

        // The script from the line that starts its fixed part to its end, with LF line ends (the copy is checked out with
        // the line ends Git gives it).
        private static string Body(string script, string from)
        {
            int at = script.IndexOf(from, StringComparison.Ordinal);
            Assert.True(at >= 0, "the batch has no line starting \"" + from.Trim() + "\"");
            return script.Substring(at).Replace("\r\n", "\n");
        }

        private static void Matches(string body, string file)
        {
            var path = Path.Combine(Here(), "Golden", file);
            var expected = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : string.Empty;
            if (expected != body)
            {
                var now = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, file);
                File.WriteAllText(now, body.Replace("\n", "\r\n"));
                Assert.Fail("The batch's MAXScript is not the copy in Golden\\" + file + ". As written now it is in " + now
                    + ": check the change in 3ds Max, then put it in place of the copy.\n" + FirstDifference(expected, body));
            }
        }

        private static string FirstDifference(string expected, string actual)
        {
            var a = expected.Split('\n');
            var b = actual.Split('\n');
            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                var x = i < a.Length ? a[i] : "(no line)";
                var y = i < b.Length ? b[i] : "(no line)";
                if (x != y)
                {
                    return "Line " + (i + 1) + ":\n  copy: " + x + "\n  now:  " + y;
                }
            }
            return string.Empty;
        }

        [Fact]
        public void CleanUpsLoopsAreWordForWordTheReadCopy()
        {
            // A batch that renames and makes layers and helpers and writes Planner IDs, and one for a scene with no phasing root:
            // the same MAXScript after their arrays.
            var scene = new TestScene();
            var root = scene.Unit("Work_Phasing", null, null);
            var works = scene.Unit("_02_Works", "Work_Phasing", root);
            var kerbs = scene.Unit("02_01_Kerbs_17-10-26", "_02_Works", works);
            scene.Object("Ph2_St01_IN_Kerb", kerbs, kerbs.Name);
            var list = TestCsv.List(
                TestCsv.Row("g2", null, "_02_Works"),
                TestCsv.Row("f1", "g2", "02_01_Kerbs", "Layer"),
                TestCsv.Row("f2", "g2", "02_02_Gates", "Layer"));
            var plan = PlannerCleanUp.Plan(scene.Snapshot, PhasingNameFixer.DefaultWordFixes(), list);
            var result = plan.Resolve(null);
            Assert.NotEmpty(result.NewLayers);
            Assert.NotEmpty(result.NewHelpers);
            Assert.NotEmpty(result.Stamps);
            Assert.NotEmpty(result.MaxLayerRenames);
            const string from = "\tlocal bLayers = for n in bLayerNames";
            var body = Body(PlannerCleanUpScript.Build(plan, result, "__t_").Script, from);
            var empty = PlannerCleanUp.Plan(new TestScene().Snapshot, PhasingNameFixer.DefaultWordFixes(), list);
            Assert.Equal(body, Body(PlannerCleanUpScript.Build(empty, empty.Resolve(null), "__t_").Script, from));
            Matches(body, "PlannerCleanUpScript.ms");
        }

        [Fact]
        public void AddLayersLoopsAreWordForWordTheReadCopy()
        {
            var scene = PlannerScheduleV2Tests.Scene();
            var plan = PlannerAddLayer.Plan(scene.Snapshot, "_02_Zone", new PlannerNewLayer { Kind = PlannerLayerKind.Layer, Phase = 2, Stage = 30, Name = "Temporary Works" }, null, null);
            Assert.True(plan.CanApply, plan.Problem);
            const string from = "\tlocal bStepped = for i = 1 to bLayerOld.count collect false";
            var body = Body(PlannerLayerScript.Build(plan.Build, "__t_").Script, from);
            Assert.Contains("undo \"Add layer\" on", body);
            Matches(body, "PlannerLayerScript.ms");
        }
    }
}
