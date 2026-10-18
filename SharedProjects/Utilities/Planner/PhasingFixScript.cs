using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Utilities.Planner
{
    /// <summary>
    /// The MAXScript text the tool's batches share, pure text so it is tested without 3ds Max: string literals
    /// (<see cref="Quote"/>), counts and name lists for their reports (<see cref="Count"/>, <see cref="FirstNames"/>,
    /// <see cref="OneLine"/>), and the Max layer steps of Clean up scene graph's batch (<see cref="PlannerCleanUpScript"/>):
    /// the moves (<see cref="AppendMaxLayerMoves"/>), then the renames in two steps (<see cref="AppendMaxLayerRenames"/>),
    /// each with its failure's sentence. Fix phasing names' own batch went with its button (Clean up scene graph replaces
    /// it, naming v2 spec 5.1); these steps were written for it first.
    /// </summary>
    public static class PhasingFixScript
    {
        /// <summary>
        /// The text as a MAXScript string literal, quotes included, or null when it holds a control character other
        /// than tab, line feed or carriage return (which MAXScript cannot be given safely as a literal).
        /// </summary>
        public static string Quote(string text)
        {
            var value = text ?? string.Empty;
            var literal = new StringBuilder(value.Length + 2);
            literal.Append('"');
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\':
                        literal.Append(@"\\");
                        break;
                    case '"':
                        literal.Append("\\\"");
                        break;
                    case '\n':
                        literal.Append(@"\n");
                        break;
                    case '\r':
                        literal.Append(@"\r");
                        break;
                    case '\t':
                        literal.Append(@"\t");
                        break;
                    default:
                        if (c < ' ' || c == '\u007f')
                        {
                            return null;
                        }
                        literal.Append(c);
                        break;
                }
            }
            return literal.Append('"').ToString();
        }

        /// <summary>The text on one line (line ends and tabs as spaces), cut to <paramref name="max"/> characters.</summary>
        public static string OneLine(string text, int max)
        {
            var flat = (text ?? string.Empty).Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ').Trim();
            return flat.Length <= max ? flat : flat.Substring(0, max) + "\u2026";
        }

        internal static string Count(int count, string one, string many)
        {
            return count.ToString(CultureInfo.InvariantCulture) + " " + (count == 1 ? one : many);
        }

        /// <summary>The first <paramref name="shown"/> names, comma-separated, then "and N more" for the rest.</summary>
        internal static string FirstNames(IList<string> names, int shown)
        {
            var text = string.Join(", ", names.Take(shown).ToArray());
            return names.Count > shown ? text + " and " + (names.Count - shown).ToString(CultureInfo.InvariantCulture) + " more" : text;
        }

        /// <summary>
        /// The Max layer steps of a phasing batch (Clean up scene graph's, <see cref="PlannerCleanUpScript"/>), the first of
        /// two: each Max layer moved under its new parent layer; then <see cref="AppendMaxLayerRenames"/>. The batch's
        /// variables are named with <paramref name="prefix"/> (its Layers, LayerNames, LayerMoveChild, LayerMoveParent,
        /// LayerRenameTemp, LayerRenameNew, Stepped, LayersMoved, LayersRenamed, Layer, Parent, Other and Log), every line takes
        /// <paramref name="indent"/> more, and <paramref name="catchLine"/> writes a catch block's lines for a kind
        /// ("layermove", "layer"). Each step reports its own failure (<see cref="MaxLayerMoveFailure"/>,
        /// <see cref="MaxLayerRenameFailure"/>).
        /// </summary>
        internal static void AppendMaxLayerMoves(StringBuilder s, string prefix, string indent, Func<string, string> catchLine)
        {
            Func<string, string> named = text => text.Replace("@P@", prefix).Replace("\n\t\t\t\t", "\n\t\t\t\t" + indent);

            // Max layers under their group's Max layer.
            s.Append(named(@"
				for i = 1 to @P@LayerMoveChild.count do
				(
					try
					(
						@P@Layer = @P@Layers[@P@LayerMoveChild[i]]
						@P@Parent = @P@Layers[@P@LayerMoveParent[i]]
						if (@P@Layer == undefined or @P@Parent == undefined) then
						(
							format ""FAIL\tlayermove\t%\tgone\n"" i to:@P@Log
						)
						else
						(
							@P@Layer.setParent @P@Parent
							@P@Other = @P@Layer.getParent()
							if (@P@Other != undefined and @P@Other.name == @P@Parent.name) then (@P@LayersMoved += 1) else (format ""FAIL\tlayermove\t%\trefused\n"" i to:@P@Log)
						)
					)
					catch
					(
")).Append(catchLine("layermove")).Append(named(@"
					)
				)
"));
        }

        /// <summary>The second of the Max layer steps (<see cref="AppendMaxLayerMoves"/>): the renames, in two steps through temporary names.</summary>
        internal static void AppendMaxLayerRenames(StringBuilder s, string prefix, string renameIndex, string indent, Func<string, string> catchLine)
        {
            Func<string, string> named = text => text.Replace("@I@", renameIndex).Replace("@P@", prefix).Replace("\n\t\t\t\t", "\n\t\t\t\t" + indent);

            // Max layer renames, step 1: every layer that changes takes a temporary name.
            s.Append(named(@"
				for i = 1 to @I@.count do
				(
					try
					(
						@P@Layer = @P@Layers[@I@[i]]
						if (@P@Layer == undefined) then
						(
							format ""FAIL\tlayer\t%\tgone\n"" i to:@P@Log
						)
						else
						(
							@P@Layer.setName @P@LayerRenameTemp[i]
							if (@P@Layer.name == @P@LayerRenameTemp[i]) then (@P@Stepped[i] = true) else (format ""FAIL\tlayer\t%\trefused\n"" i to:@P@Log)
						)
					)
					catch
					(
")).Append(catchLine("layer")).Append(named(@"
					)
				)
"));

            // Step 2: the new names; a layer whose new name is refused goes back to its old one.
            s.Append(named(@"
				for i = 1 to @I@.count where @P@Stepped[i] do
				(
					try
					(
						@P@Layer = @P@Layers[@I@[i]]
						@P@Layer.setName @P@LayerRenameNew[i]
						if (@P@Layer.name == @P@LayerRenameNew[i]) then
						(
							@P@LayersRenamed += 1
						)
						else
						(
							@P@Layer.setName @P@LayerNames[@I@[i]]
							if (@P@Layer.name == @P@LayerNames[@I@[i]]) then (format ""FAIL\tlayer\t%\ttaken\n"" i to:@P@Log) else (format ""FAIL\tlayer\t%\tstuck\n"" i to:@P@Log)
						)
					)
					catch
					(
")).Append(catchLine("layer")).Append(named(@"
					)
				)
"));
        }

        /// <summary>What a Max layer move the batch reported failed says (<see cref="AppendMaxLayerMoves"/>).</summary>
        internal static string MaxLayerMoveFailure(PhasingMaxLayerMove move, string code, string error)
        {
            switch (code)
            {
                case "gone":
                    return "the Max layer " + move.Name + " was not moved under " + move.NewParentName + ": one of them is no longer in the scene";
                case "refused":
                    return "the Max layer " + move.Name + " could not be moved under " + move.NewParentName;
                default:
                    return "the Max layer " + move.Name + " was not moved under " + move.NewParentName + ": " + error;
            }
        }

        /// <summary>What a Max layer rename the batch reported failed says (<see cref="AppendMaxLayerRenames"/>).</summary>
        internal static string MaxLayerRenameFailure(PhasingMaxLayerRename rename, string temporaryName, string code, string error)
        {
            switch (code)
            {
                case "gone":
                    return "the Max layer " + rename.OldName + " is no longer in the scene";
                case "refused":
                    return "the Max layer " + rename.OldName + " could not be renamed; it keeps its name";
                case "taken":
                    return "the Max layer " + rename.OldName + " could not become " + rename.NewName + ": that name is taken; it keeps its name";
                case "stuck":
                    return "the Max layer " + rename.OldName + " could not become " + rename.NewName + " and is left as " + temporaryName + " - rename it by hand";
                default:
                    return "the Max layer " + rename.OldName + " was not renamed to " + rename.NewName + ": " + error;
            }
        }
    }
}
