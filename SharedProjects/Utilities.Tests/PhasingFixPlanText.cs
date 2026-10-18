using System.Globalization;
using System.Linq;
using System.Text;
using Utilities.Planner;

namespace Utilities.Tests
{
    /// <summary>
    /// The phasing name fixer's plan as the Fix phasing names window showed it: each row's status and type, the counts, the
    /// one-line summary and the review list as CSV in the reference format. The window and its batch are gone (Clean up
    /// scene graph replaces them), but the export's naming check on the old names still builds the plan, and the fixer's
    /// tests read it this way, so the text lives here with them.
    /// </summary>
    internal static class PhasingFixPlanText
    {
        public const string CsvHeader = "Status,Type,Layer it sits in (corrected name),Current name in Max,Corrected name,What changed";

        public static string StatusText(this PhasingFixRow row)
        {
            return row.Status == PhasingFixStatus.Changed ? "Changed" : "Check";
        }

        public static string TypeText(this PhasingFixRow row)
        {
            return ItemText(row.Item);
        }

        public static string ItemText(PhasingFixItem item)
        {
            switch (item)
            {
                case PhasingFixItem.TopLayer:
                    return "Top layer";
                case PhasingFixItem.Group:
                    return "Group";
                case PhasingFixItem.Layer:
                    return "Layer";
                default:
                    return "Object";
            }
        }

        /// <summary>Changed rows for the top layer, groups and layers.</summary>
        public static int LayersToChange(this PhasingFixPlan plan)
        {
            return plan.Rows.Count(r => r.Status == PhasingFixStatus.Changed && r.Item != PhasingFixItem.Object);
        }

        public static int ObjectsToChange(this PhasingFixPlan plan)
        {
            return plan.Rows.Count(r => r.Status == PhasingFixStatus.Changed && r.Item == PhasingFixItem.Object);
        }

        public static int ToCheck(this PhasingFixPlan plan)
        {
            return plan.Rows.Count(r => r.Status == PhasingFixStatus.Check);
        }

        /// <summary>True when carrying the plan out would rename or move anything.</summary>
        public static bool HasChanges(this PhasingFixPlan plan)
        {
            return plan.NodeRenames.Count > 0 || plan.NodeMoves.Count > 0 || plan.MaxLayerRenames.Count > 0 || plan.MaxLayerMoves.Count > 0;
        }

        /// <summary>The names the fix would correct: its Changed rows (or, failing those, its operations).</summary>
        public static int CorrectableCount(PhasingFixPlan plan)
        {
            if (plan == null || !plan.HasChanges())
            {
                return 0;
            }
            var rows = plan.Rows.Count(r => r.Status == PhasingFixStatus.Changed);
            return rows > 0 ? rows : plan.NodeRenames.Count + plan.NodeMoves.Count + plan.MaxLayerRenames.Count + plan.MaxLayerMoves.Count;
        }

        /// <summary>One line, e.g. "37 layers and 120 objects to change, 11 to check."</summary>
        public static string Summary(this PhasingFixPlan plan)
        {
            if (plan.TopKey == null)
            {
                return plan.Message ?? string.Empty;
            }
            return string.Format(CultureInfo.InvariantCulture, "{0} and {1} to change, {2} to check.",
                PhasingFixScript.Count(plan.LayersToChange(), "layer", "layers"), PhasingFixScript.Count(plan.ObjectsToChange(), "object", "objects"), plan.ToCheck());
        }

        /// <summary>The review list as CSV, in the reference format: a byte order mark, the header, CRLF line ends.</summary>
        public static string ToCsv(this PhasingFixPlan plan)
        {
            var text = new StringBuilder();
            text.Append((char)0xFEFF).Append(CsvHeader);
            foreach (var row in plan.Rows)
            {
                text.Append("\r\n");
                text.Append(CsvField(row.StatusText())).Append(',')
                    .Append(CsvField(row.TypeText())).Append(',')
                    .Append(CsvField(row.LayerItSitsIn)).Append(',')
                    .Append(CsvField(row.CurrentName)).Append(',')
                    .Append(CsvField(row.CorrectedName)).Append(',')
                    .Append(CsvField(row.WhatChanged));
            }
            text.Append("\r\n");
            return text.ToString();
        }

        // Quoted only when it holds a quote, a comma or a line feed, as the reference writer does.
        private static string CsvField(string value)
        {
            var text = value ?? string.Empty;
            if (text.IndexOf('"') < 0 && text.IndexOf(',') < 0 && text.IndexOf('\n') < 0)
            {
                return text;
            }
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }
    }
}
