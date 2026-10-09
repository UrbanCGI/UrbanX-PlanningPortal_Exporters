using System;

namespace Utilities.Updates
{
    /// <summary>How the installed exporter stands against the latest release.</summary>
    public enum UpdateVerdict
    {
        /// <summary>Nothing is known about the latest release (not checked, the check failed, or its tag does not read).</summary>
        Unknown,

        /// <summary>The installed release is the latest one (or newer).</summary>
        UpToDate,

        /// <summary>A newer release exists.</summary>
        UpdateAvailable,

        /// <summary>This is a development build; the latest release is known and can be installed over it.</summary>
        DevelopmentBuild
    }

    /// <summary>Why a check for the latest release found nothing.</summary>
    public enum UpdateCheckFailure
    {
        None,

        /// <summary>No answer: offline, a time-out, or GitHub itself down.</summary>
        Offline,

        /// <summary>The repository has no published (non-draft, non-pre-release) release.</summary>
        NoRelease,

        /// <summary>GitHub's limit of checks per hour for this network was reached.</summary>
        RateLimited,

        /// <summary>An answer that does not describe a release.</summary>
        BadAnswer,

        /// <summary>A network proxy refused the request (HTTP 407: it wants a sign-in the exporter could not give).</summary>
        ProxyRefused
    }

    /// <summary>
    /// The decisions behind "Check for updates" in the 3ds Max exporter (UrbanCGI fork), kept free of 3ds Max, the
    /// network and the clock so they can be unit-tested: how a build compares with the latest release, what the
    /// window says about it, and when opening the window asks GitHub again (at most once a day, and after a failed
    /// check not again for an hour; a development build never asks by itself).
    /// </summary>
    public static class UpdateCheckLogic
    {
        /// <summary>How long a successful check is remembered.</summary>
        public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

        /// <summary>How long opening the window waits after a failed check before trying again.</summary>
        public static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

        /// <summary>
        /// Compares the installed build's tag (a release tag, or the placeholder of a development build) with the
        /// latest release's tag.
        /// </summary>
        public static UpdateVerdict Compare(string installedTag, string latestTag)
        {
            ReleaseTag latest;
            if (!ReleaseTag.TryParse(latestTag, out latest))
            {
                return UpdateVerdict.Unknown;
            }
            ReleaseTag installed;
            if (!ReleaseTag.TryParse(installedTag, out installed))
            {
                return UpdateVerdict.DevelopmentBuild;
            }
            return latest > installed ? UpdateVerdict.UpdateAvailable : UpdateVerdict.UpToDate;
        }

        /// <summary>True when the Update button has something to install.</summary>
        public static bool CanUpdate(UpdateVerdict verdict)
        {
            return verdict == UpdateVerdict.UpdateAvailable || verdict == UpdateVerdict.DevelopmentBuild;
        }

        /// <summary>"Version v20261009.3", or "Development build" for a build the CD workflow did not make.</summary>
        public static string VersionText(string installedTag)
        {
            return ReleaseTag.IsReleaseTag(installedTag) ? "Version " + installedTag.Trim() : "Development build";
        }

        /// <summary>The status line for a verdict; empty when nothing is known.</summary>
        public static string StatusText(UpdateVerdict verdict, string latestTag)
        {
            switch (verdict)
            {
                case UpdateVerdict.UpToDate:
                    return "Up to date";
                case UpdateVerdict.UpdateAvailable:
                    return "Update available: " + latestTag.Trim();
                case UpdateVerdict.DevelopmentBuild:
                    return "Latest release: " + latestTag.Trim();
                default:
                    return string.Empty;
            }
        }

        /// <summary>The status line after a check that failed (shown only when the button asked).</summary>
        public static string FailureText(UpdateCheckFailure failure)
        {
            switch (failure)
            {
                case UpdateCheckFailure.Offline:
                    return "Couldn't reach GitHub";
                case UpdateCheckFailure.NoRelease:
                    return "No release published yet";
                case UpdateCheckFailure.RateLimited:
                    return "GitHub is busy, try again in an hour";
                case UpdateCheckFailure.BadAnswer:
                    return "Unexpected answer from GitHub";
                case UpdateCheckFailure.ProxyRefused:
                    return "Blocked by the network proxy";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// Whether opening the exporter window should ask GitHub now. No for a development build (it never prompts by
        /// itself), no within a day of a successful check (its result is shown instead), no within an hour of a
        /// failed one (so an offline machine does not wait on every open). A remembered time in the future (the clock
        /// was put back) does not count.
        /// </summary>
        public static bool ShouldCheckOnWindowOpen(bool developmentBuild, DateTime nowUtc, DateTime? lastSuccessUtc, DateTime? lastFailureUtc)
        {
            if (developmentBuild)
            {
                return false;
            }
            if (IsWithin(lastSuccessUtc, nowUtc, CheckInterval))
            {
                return false;
            }
            return !IsWithin(lastFailureUtc, nowUtc, RetryAfterFailure);
        }

        /// <summary>True while the remembered result of the last successful check may be shown (less than a day old).</summary>
        public static bool IsRememberedResultCurrent(DateTime nowUtc, DateTime? lastSuccessUtc)
        {
            return IsWithin(lastSuccessUtc, nowUtc, CheckInterval);
        }

        /// <summary>
        /// Reads GitHub's HTTP status for the latest-release request. <paramref name="rateLimitRemaining"/> is the
        /// X-RateLimit-Remaining header (null when absent): GitHub answers 403 with "0" there when the limit is hit.
        /// </summary>
        public static UpdateCheckFailure ClassifyStatus(int httpStatus, string rateLimitRemaining)
        {
            if (httpStatus >= 200 && httpStatus < 300)
            {
                return UpdateCheckFailure.None;
            }
            if (httpStatus == 404)
            {
                return UpdateCheckFailure.NoRelease;
            }
            if (httpStatus == 429 || (httpStatus == 403 && (rateLimitRemaining ?? string.Empty).Trim() == "0"))
            {
                return UpdateCheckFailure.RateLimited;
            }
            if (httpStatus == 407)
            {
                return UpdateCheckFailure.ProxyRefused;
            }
            if (httpStatus >= 500)
            {
                return UpdateCheckFailure.Offline;
            }
            return UpdateCheckFailure.BadAnswer;
        }

        private static bool IsWithin(DateTime? whenUtc, DateTime nowUtc, TimeSpan window)
        {
            if (!whenUtc.HasValue)
            {
                return false;
            }
            var age = nowUtc - whenUtc.Value;
            return age >= TimeSpan.Zero && age < window;
        }
    }
}
