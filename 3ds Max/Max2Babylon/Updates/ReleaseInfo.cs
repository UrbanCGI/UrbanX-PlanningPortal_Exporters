using Utilities.Updates;

namespace Max2Babylon
{
    /// <summary>
    /// UrbanCGI fork: which release this build is, for the version text, the export log and the update check. The CD
    /// workflow (.github/workflows/cd.yml) writes the release tag (v&lt;yyyyMMdd&gt;.&lt;run&gt;) into <see cref="Tag"/>
    /// before it builds 3ds Max. A build made anywhere else keeps the placeholder, which is not a release tag, and
    /// calls itself a development build. The workflow replaces every copy of the placeholder text in this file, so
    /// it appears here exactly once (a unit test checks that). BabylonExporter.exporterVersion and the glTF generator
    /// string stay as they are. Free of the 3ds Max SDK, so the unit tests compile it too.
    /// </summary>
    static class ReleaseInfo
    {
        /// <summary>The release tag, or the placeholder in a development build.</summary>
        public const string Tag = "Custom.Release.Tag";

        /// <summary>True for a build the CD workflow made.</summary>
        public static bool IsRelease
        {
            get { return ReleaseTag.IsReleaseTag(Tag); }
        }

        /// <summary>"Version v20261009.3" or "Development build".</summary>
        public static string VersionText
        {
            get { return UpdateCheckLogic.VersionText(Tag); }
        }

        /// <summary>"release v20261009.3" or "development build", for the window title and the export log.</summary>
        public static string ShortText
        {
            get { return IsRelease ? "release " + Tag : "development build"; }
        }

        /// <summary>The 3ds Max year this build is for (its Max_&lt;year&gt;.zip release asset); 0 when not known.</summary>
        public static int MaxYear
        {
            get
            {
#if MAX2027
                return 2027;
#elif MAX2026
                return 2026;
#elif MAX2025
                return 2025;
#elif MAX2024
                return 2024;
#elif MAX2023
                return 2023;
#elif MAX2022
                return 2022;
#else
                return 0;
#endif
            }
        }
    }
}
