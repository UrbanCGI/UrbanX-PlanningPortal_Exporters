using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Utilities.Planner
{
    /// <summary>A layer name in the coded scheme, <c>&lt;code&gt;_&lt;name&gt;</c> (no dates, no TBC).</summary>
    public sealed class PlannerCodedLayerName
    {
        /// <summary>The folder code as written ("01", "01-04.2"), or null when the name carries none.</summary>
        public string Code;
        /// <summary>The folder name without the code; empty when the name is the code alone.</summary>
        public string Name;
    }

    /// <summary>How a legacy (dated) layer name wrote its number.</summary>
    public enum PlannerLegacyForm
    {
        /// <summary><c>_GG_&lt;Name&gt;</c>: a group layer.</summary>
        Group,
        /// <summary><c>GG_RR[.P]_&lt;Name&gt;</c>: a child layer of a group.</summary>
        GroupRow,
        /// <summary><c>GG.RR.P_&lt;Name&gt;</c>: a child layer whose group number was written with a dot.</summary>
        DottedGroupRow,
        /// <summary><c>GG_&lt;Name&gt;</c> (or <c>GG-RR</c> / <c>GG.P</c>): a single number.</summary>
        Single
    }

    /// <summary>A legacy v3 layer name read as a code, a name and the dates it used to carry.</summary>
    public sealed class PlannerLegacyLayer
    {
        public string Code;
        /// <summary>The name with the number, dates and TBC removed and whitespace turned into underscores.</summary>
        public string Name;
        public PlannerLegacyForm Form;
        /// <summary>First day, inclusive. Null when the name carried no readable date.</summary>
        public DateTime? Start;
        /// <summary>Last day, inclusive (as the legacy names were); equals Start for a single day.</summary>
        public DateTime? Finish;
        /// <summary>
        /// True when the name carried a TBC token, or when a child layer (GG_RR) had no readable date, an unreadable
        /// date or a finish before its start, matching how the Planner reads the same name.
        /// </summary>
        public bool Tbc;
        /// <summary>Problems worth a warning: unreadable dates, finish before start, no dates, no name, parent code mismatch.</summary>
        public List<string> Issues = new List<string>();
        /// <summary>How the number was tidied on the way to a code ("group number padded", "dotted group number normalised").</summary>
        public List<string> Notes = new List<string>();

        public bool IsGroup { get { return Form == PlannerLegacyForm.Group; } }
        /// <summary>True when the name carried dates or a TBC token worth carrying over as legacy values.</summary>
        public bool HasSchedule { get { return Start.HasValue || Tbc; } }
    }

    /// <summary>An object name read under the coded scheme: the layer decides the folder, tags are optional.</summary>
    public sealed class PlannerCodedObjectName
    {
        public PlannerTaskType Type;
        /// <summary>The text between the lead and the trailing removal, edge underscores trimmed.</summary>
        public string Description;
        /// <summary>The code of a leading <c>Ph&lt;n&gt;_St&lt;n&gt;[.&lt;m&gt;]</c> tag, or null.</summary>
        public string LeadCode;
        /// <summary>Install objects only: the code of the folder whose work removes the object, or null.</summary>
        public string RemovalCode;
        /// <summary>True when the name starts with a lead (a Ph/St tag or a bare IN_/RM_).</summary>
        public bool Tagged;
        /// <summary>
        /// A trailing <c>_Ph&lt;n&gt;_St&lt;n&gt;_IN</c> on a removal object (today's re-install), as a code. Read so it
        /// can be reported, otherwise ignored for now.
        /// </summary>
        public string IgnoredInstallCode;
    }

    /// <summary>
    /// The code helpers of the Planner &lt;-&gt; 3ds Max contract (sections 1 to 4), shared with the Planner's
    /// TypeScript, which implements the same rules; the contract tables are mirrored case for case in
    /// <c>PlannerCodesTests</c>. Codes belong to folders and sub-folders only, are free-form digits separated
    /// by <c>-</c> or <c>.</c> and are stored, never derived from position.
    /// </summary>
    public static class PlannerCodes
    {
        private const RegexOptions Options = RegexOptions.CultureInvariant;
        private const RegexOptions OptionsIgnoreCase = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

        // [0-9] rather than \d throughout: .NET's \d matches every Unicode digit, JavaScript's only ASCII digits.
        private static readonly Regex RootSeparators = new Regex(@"[\s_-]", Options);
        private static readonly Regex CodeOnly = new Regex(@"^[0-9]+(?:[-.][0-9]+)*$", Options);
        private static readonly Regex CodedLayer = new Regex(@"^([0-9]+(?:[-.][0-9]+)*)(?:[_ ]+(.*))?$", Options);
        private static readonly Regex Whitespace = new Regex(@"\s+", Options);
        private static readonly Regex Underscores = new Regex(@"_+", Options);
        // The contract's " (n)": a space before the bracket is required, so "Phase_(2)" keeps its "(2)". The older
        // PlannerNaming rules (DuplicateSuffix) also take "(n)" without a space and are left as they are.
        private static readonly Regex DuplicateSuffix = new Regex(@"\s+\([0-9]+\)\s*$", Options);

        // Legacy v3 numbers, tried in this order: "_GG_", "GG.RR.P_", "GG_RR[.P]_", then a single "GG_" (or the
        // older "GG-RR" / "GG.P", the dotted form only with an underscore after it, as in PlannerNaming).
        private static readonly Regex LegacyGroup = new Regex(@"^_([0-9]+)(?:[_ ]+|$)", Options);
        private static readonly Regex LegacyDotted = new Regex(@"^([0-9]+)\.([0-9]+)\.([0-9]+)(?:[_ ]+|$)", Options);
        private static readonly Regex LegacyGroupRow = new Regex(@"^([0-9]+)_([0-9]+)(?:\.([0-9]+))?(?:[_ ]+|$)", Options);
        private static readonly Regex LegacySingle = new Regex(@"^([0-9]+)(?:-([0-9]+))?(?:[_ ]+|$)|^([0-9]+)\.([0-9]+)(?:_+|$)", Options);

        private static readonly Regex ObjectLead = new Regex(@"^Ph([0-9]+)_St([0-9]+)(?:\.([0-9]+))?_(IN|RM)(?:_|$)", OptionsIgnoreCase);
        private static readonly Regex ObjectBareLead = new Regex(@"^(IN|RM)_", OptionsIgnoreCase);
        private static readonly Regex RemovalTag = new Regex(@"_Ph([0-9]+)_St([0-9]+)(?:\.([0-9]+))?_RM$", OptionsIgnoreCase);
        private static readonly Regex RemovalCodeTrail = new Regex(@"_RM_([0-9]+(?:[-.][0-9]+)*)$", OptionsIgnoreCase);
        private static readonly Regex InstallTag = new Regex(@"_Ph([0-9]+)_St([0-9]+)(?:\.([0-9]+))?_IN$", OptionsIgnoreCase);
        private static readonly Regex EdgeUnderscores = new Regex(@"^_+|_+$", Options);

        public const string DefaultRootName = "Work_Phasing";

        /// <summary>
        /// A layer named like "Work_Phasing" (any case, spaces, underscores and dashes ignored) is the phasing
        /// root when it sits at the top level: everything under it is phased, everything outside is context.
        /// </summary>
        public static bool IsPhasingRoot(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }
            return RootSeparators.Replace(name.ToLowerInvariant(), string.Empty).Contains("workphasing");
        }

        /// <summary>"6" gives "06"; two or more digits are kept as written.</summary>
        public static string Pad2(string digits)
        {
            if (digits == null)
            {
                return null;
            }
            return digits.Length >= 2 ? digits : digits.PadLeft(2, '0');
        }

        /// <summary><c>Ph6 St05</c> gives "06-05", <c>Ph2 St01.5</c> "02-01.5"; the sub-stage is kept as written.</summary>
        public static string PhStToCode(string phase, string stage, string sub)
        {
            return Pad2(phase) + "-" + Pad2(stage) + (sub != null ? "." + sub : string.Empty);
        }

        /// <summary>True for a well-formed code: digits separated by single dashes or dots.</summary>
        public static bool IsCode(string text)
        {
            return text != null && CodeOnly.IsMatch(text);
        }

        /// <summary>
        /// True when the codes are equal, or one is the other followed by "." or "-" and more: "01-04" covers
        /// "01-04.1" and "01-04.1" is covered by "01-04". Used for lead-tag mismatch warnings.
        /// </summary>
        public static bool CodeCovers(string folderCode, string code)
        {
            if (folderCode == null || code == null)
            {
                return false;
            }
            return string.Equals(folderCode, code, StringComparison.Ordinal) || IsPrefixCode(folderCode, code) || IsPrefixCode(code, folderCode);
        }

        /// <summary>True when <paramref name="code"/> is <paramref name="prefix"/> itself or sits under it ("02" and "02-01.5").</summary>
        public static bool StartsWithCode(string code, string prefix)
        {
            if (code == null || prefix == null)
            {
                return false;
            }
            return string.Equals(code, prefix, StringComparison.Ordinal) || IsPrefixCode(prefix, code);
        }

        private static bool IsPrefixCode(string prefix, string code)
        {
            return code.Length > prefix.Length
                && code.StartsWith(prefix, StringComparison.Ordinal)
                && (code[prefix.Length] == '.' || code[prefix.Length] == '-');
        }

        /// <summary>Removes a trailing " (n)" duplicate suffix (a space before the bracket) and the surrounding whitespace.</summary>
        public static string StripDuplicateSuffix(string name)
        {
            return name == null ? null : DuplicateSuffix.Replace(name, string.Empty, 1).Trim();
        }

        /// <summary>Whitespace runs become "_", repeated underscores collapse, edge underscores are trimmed.</summary>
        public static string SanitiseName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            var text = Underscores.Replace(Whitespace.Replace(name, "_"), "_");
            return text.Trim('_');
        }

        /// <summary>Reads <c>&lt;code&gt;_&lt;name&gt;</c>; a name without a leading code gives a null code and the name unchanged.</summary>
        public static PlannerCodedLayerName ParseCodedLayerName(string name)
        {
            var s = StripDuplicateSuffix(name) ?? string.Empty;
            var m = CodedLayer.Match(s);
            if (!m.Success)
            {
                return new PlannerCodedLayerName { Code = null, Name = s };
            }
            return new PlannerCodedLayerName
            {
                Code = m.Groups[1].Value,
                Name = m.Groups[2].Success ? m.Groups[2].Value : string.Empty
            };
        }

        /// <summary>The Max layer (and helper) name for a folder: <c>&lt;code&gt;_&lt;name&gt;</c>, or the name alone when there is no code.</summary>
        public static string LayerNameFor(string code, string name)
        {
            var clean = SanitiseName(name);
            if (string.IsNullOrEmpty(code))
            {
                return clean;
            }
            return clean.Length == 0 ? code : code + "_" + clean;
        }

        /// <summary>
        /// Reads a legacy v3 layer name (adopt and Planner re-link only): group layers <c>_GG_&lt;Name&gt;</c> and
        /// child layers <c>GG_RR[.P]_&lt;Name&gt;[_TBC][_DD-MM-YY[_DD-MM-YY]]</c>, with the date and TBC token rules of
        /// <see cref="PlannerNaming.ParseLayerName"/>. Returns null when the name does not start with a number.
        /// When <paramref name="parentCode"/> is given and the code does not start with it, the code is kept and
        /// the mismatch is reported.
        /// </summary>
        public static PlannerLegacyLayer ParseLegacyLayer(string name, string parentCode)
        {
            if (name == null)
            {
                return null;
            }
            var s = StripDuplicateSuffix(name);
            var result = new PlannerLegacyLayer();
            string rest;

            Match m;
            if ((m = LegacyGroup.Match(s)).Success)
            {
                result.Form = PlannerLegacyForm.Group;
                result.Code = PadNoting(m.Groups[1].Value, result.Notes, "group number padded");
            }
            else if ((m = LegacyDotted.Match(s)).Success)
            {
                result.Form = PlannerLegacyForm.DottedGroupRow;
                result.Notes.Add("dotted group number normalised");
                result.Code = PadNoting(m.Groups[1].Value, result.Notes, "group number padded") + "-"
                              + PadNoting(m.Groups[2].Value, result.Notes, "row number padded") + "." + m.Groups[3].Value;
            }
            else if ((m = LegacyGroupRow.Match(s)).Success)
            {
                result.Form = PlannerLegacyForm.GroupRow;
                result.Code = PadNoting(m.Groups[1].Value, result.Notes, "group number padded") + "-"
                              + PadNoting(m.Groups[2].Value, result.Notes, "row number padded")
                              + (m.Groups[3].Success ? "." + m.Groups[3].Value : string.Empty);
            }
            else if ((m = LegacySingle.Match(s)).Success)
            {
                result.Form = PlannerLegacyForm.Single;
                if (m.Groups[1].Success)
                {
                    result.Code = PadNoting(m.Groups[1].Value, result.Notes, "group number padded")
                                  + (m.Groups[2].Success ? "-" + PadNoting(m.Groups[2].Value, result.Notes, "row number padded") : string.Empty);
                }
                else
                {
                    result.Code = PadNoting(m.Groups[3].Value, result.Notes, "group number padded") + "." + m.Groups[4].Value;
                }
            }
            else
            {
                return null;
            }
            rest = s.Substring(m.Length);

            // Only child layers (GG_RR) were dated; group and single-number layers carry no dates by design.
            var datesExpected = result.Form == PlannerLegacyForm.GroupRow || result.Form == PlannerLegacyForm.DottedGroupRow;
            var tokens = PlannerNaming.ReadScheduleTokens(rest, datesExpected);
            result.Name = SanitiseName(tokens.Name);
            result.Start = tokens.Start;
            result.Finish = tokens.End;
            // As the Planner reads it: a child layer with no readable date, an unreadable date or a finish before
            // its start is TBC as well as one carrying the token.
            var dateTrouble = tokens.Issues.Exists(i => i.StartsWith("unreadable date", StringComparison.Ordinal)
                                                        || i.StartsWith("finish ", StringComparison.Ordinal));
            result.Tbc = tokens.Tbc || (datesExpected && (dateTrouble || !tokens.Start.HasValue));
            result.Issues.AddRange(tokens.Issues);

            if (parentCode != null && !StartsWithCode(result.Code, parentCode))
            {
                result.Issues.Add("code " + result.Code + " does not start with the parent code " + parentCode);
            }
            return result;
        }

        private static string PadNoting(string digits, List<string> notes, string note)
        {
            if (digits.Length < 2 && !notes.Contains(note))
            {
                notes.Add(note);
            }
            return Pad2(digits);
        }

        /// <summary>
        /// Reads an object name under the coded scheme. The lead is optional (<c>Ph&lt;n&gt;_St&lt;n&gt;[.&lt;m&gt;]_IN|RM_</c>
        /// or a bare <c>IN_</c> / <c>RM_</c>); an Install object may end in a removal (<c>_Ph&lt;n&gt;_St&lt;n&gt;_RM</c> or
        /// <c>_RM_&lt;code&gt;</c>). A trailing install tag on a removal object is read, reported and otherwise ignored.
        /// </summary>
        public static PlannerCodedObjectName ParseCodedObjectName(string name)
        {
            var s = StripDuplicateSuffix(name) ?? string.Empty;
            var result = new PlannerCodedObjectName { Type = PlannerTaskType.Install };

            // Where the description starts, and from where a trailing tag may start: a lead ending in "_" lends
            // that underscore to an abutting trail ("Ph1_St00_IN_Ph2_St01_RM").
            int descriptionStart = 0;
            int trailFrom = 0;
            var lead = ObjectLead.Match(s);
            if (lead.Success)
            {
                result.Tagged = true;
                result.LeadCode = PhStToCode(lead.Groups[1].Value, lead.Groups[2].Value, lead.Groups[3].Success ? lead.Groups[3].Value : null);
                result.Type = IsInstall(lead.Groups[4].Value) ? PlannerTaskType.Install : PlannerTaskType.Dismantle;
                descriptionStart = lead.Length;
            }
            else
            {
                var bare = ObjectBareLead.Match(s);
                if (bare.Success)
                {
                    result.Tagged = true;
                    result.Type = IsInstall(bare.Groups[1].Value) ? PlannerTaskType.Install : PlannerTaskType.Dismantle;
                    descriptionStart = bare.Length;
                }
            }
            trailFrom = descriptionStart > 0 && s[descriptionStart - 1] == '_' ? descriptionStart - 1 : descriptionStart;

            int descriptionEnd = s.Length;
            if (result.Type == PlannerTaskType.Install)
            {
                var tag = RemovalTag.Match(s, trailFrom);
                if (tag.Success)
                {
                    result.RemovalCode = PhStToCode(tag.Groups[1].Value, tag.Groups[2].Value, tag.Groups[3].Success ? tag.Groups[3].Value : null);
                    descriptionEnd = tag.Index;
                }
                else
                {
                    var code = RemovalCodeTrail.Match(s, trailFrom);
                    if (code.Success)
                    {
                        result.RemovalCode = code.Groups[1].Value;
                        descriptionEnd = code.Index;
                    }
                }
            }
            else
            {
                var install = InstallTag.Match(s, trailFrom);
                if (install.Success)
                {
                    result.IgnoredInstallCode = PhStToCode(install.Groups[1].Value, install.Groups[2].Value, install.Groups[3].Success ? install.Groups[3].Value : null);
                    descriptionEnd = install.Index;
                }
            }

            var description = descriptionEnd > descriptionStart ? s.Substring(descriptionStart, descriptionEnd - descriptionStart) : string.Empty;
            result.Description = EdgeUnderscores.Replace(description, string.Empty);
            return result;
        }

        private static bool IsInstall(string type)
        {
            return string.Equals(type, "IN", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The folder a removal code points at, as an index into <paramref name="folderCodes"/> (outline order):
        /// the folder whose code equals it, else the first folder whose code starts with it followed by "." or
        /// "-". -1 when nothing matches (the Planner then parks the removal as TBC and reports it).
        /// </summary>
        public static int ResolveRemovalTarget(string removalCode, IList<string> folderCodes)
        {
            if (removalCode == null || folderCodes == null)
            {
                return -1;
            }
            for (int i = 0; i < folderCodes.Count; i++)
            {
                if (string.Equals(folderCodes[i], removalCode, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            for (int i = 0; i < folderCodes.Count; i++)
            {
                var code = folderCodes[i];
                if (code != null && IsPrefixCode(removalCode, code))
                {
                    return i;
                }
            }
            return -1;
        }
    }
}
