using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Utilities.Updates;

namespace Max2Babylon
{
    /// <summary>The result of asking for the latest release: its tag (and details when GitHub was asked), or why not.</summary>
    sealed class UpdateCheckOutcome
    {
        /// <summary>The latest release's tag; null when the check failed.</summary>
        public string LatestTag { get; set; }

        /// <summary>GitHub's answer; null when the tag comes from the remembered state or the check failed.</summary>
        public LatestRelease Release { get; set; }

        public UpdateCheckFailure Failure { get; set; }

        /// <summary>Technical detail of a failure, for the MAXScript Listener.</summary>
        public string Detail { get; set; }
    }

    /// <summary>
    /// UrbanCGI fork: asks GitHub for the fork's latest release (the latest non-draft, non-pre-release one), with a
    /// User-Agent, TLS 1.2 on .NET Framework and a 10 second time-out. Every method runs off the UI thread and never
    /// throws: a failure comes back as <see cref="UpdateCheckOutcome.Failure"/>. The decisions themselves are in
    /// <see cref="UpdateCheckLogic"/>.
    /// </summary>
    static class UpdateChecker
    {
        public const string Repository = "UrbanCGI/UrbanX-PlanningPortal_Exporters";
        public const string LatestReleaseApiUrl = "https://api.github.com/repos/" + Repository + "/releases/latest";
        public const string ReleasesPageUrl = "https://github.com/" + Repository + "/releases";

        private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        /// For opening the exporter window: GitHub is asked only when <see cref="UpdateCheckLogic.ShouldCheckOnWindowOpen"/>
        /// says so, otherwise the remembered result of the last day is used. Null when the window should say nothing:
        /// a development build, a failed check (silent here), or nothing remembered.
        /// </summary>
        public static async Task<UpdateCheckOutcome> CheckOnWindowOpenAsync(CancellationToken cancel)
        {
            try
            {
                if (!ReleaseInfo.IsRelease)
                {
                    return null;
                }
                var now = DateTime.UtcNow;
                var state = UpdateStateFile.Load();
                if (UpdateCheckLogic.ShouldCheckOnWindowOpen(false, now, state.LastSuccessUtc, state.LastFailureUtc))
                {
                    var outcome = await QueryLatestAsync(cancel).ConfigureAwait(false);
                    Remember(outcome, cancel);
                    return outcome.Failure == UpdateCheckFailure.None ? outcome : null;
                }
                if (UpdateCheckLogic.IsRememberedResultCurrent(now, state.LastSuccessUtc) && ReleaseTag.IsReleaseTag(state.LatestTag))
                {
                    return new UpdateCheckOutcome { LatestTag = state.LatestTag.Trim() };
                }
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>For the buttons: always asks GitHub, and remembers the answer for the next window.</summary>
        public static async Task<UpdateCheckOutcome> CheckNowAsync(CancellationToken cancel)
        {
            var outcome = await QueryLatestAsync(cancel).ConfigureAwait(false);
            Remember(outcome, cancel);
            return outcome;
        }

        /// <summary>One request to GitHub; never throws.</summary>
        public static async Task<UpdateCheckOutcome> QueryLatestAsync(CancellationToken cancel)
        {
            try
            {
                using (var client = CreateClient(CheckTimeout))
                using (var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl))
                {
                    request.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
                    using (var response = await client.SendAsync(request, cancel).ConfigureAwait(false))
                    {
                        var status = (int)response.StatusCode;
                        var failure = UpdateCheckLogic.ClassifyStatus(status, HeaderValue(response, "X-RateLimit-Remaining"));
                        if (failure != UpdateCheckFailure.None)
                        {
                            return Failed(failure, (failure == UpdateCheckFailure.ProxyRefused ? "the network proxy" : "GitHub") + " answered HTTP " + status);
                        }
                        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        string error;
                        var release = GitHubReleaseJson.Parse(json, out error);
                        if (release == null)
                        {
                            return Failed(UpdateCheckFailure.BadAnswer, error);
                        }
                        if (release.Draft || release.Prerelease)
                        {
                            return Failed(UpdateCheckFailure.NoRelease, "the latest release is a draft or a pre-release");
                        }
                        if (!ReleaseTag.IsReleaseTag(release.Tag))
                        {
                            return Failed(UpdateCheckFailure.BadAnswer, "the latest release is called \"" + release.Tag + "\"");
                        }
                        return new UpdateCheckOutcome { LatestTag = release.Tag.Trim(), Release = release };
                    }
                }
            }
            catch (Exception e)
            {
                // No connection, a time-out (TaskCanceledException), a TLS failure, a closed window...
                return Failed(UpdateCheckFailure.Offline, Describe(e));
            }
        }

        /// <summary>
        /// An HttpClient for GitHub. On .NET Framework TLS 1.2 is switched on for the process when 3ds Max has pinned
        /// older protocols (a process left on the system default is not touched); redirects are followed, which the
        /// asset downloads need; a proxy that asks for a Windows sign-in gets the signed-in user's (only the proxy:
        /// GitHub itself is never sent them).
        /// </summary>
        public static HttpClient CreateClient(TimeSpan timeout)
        {
            EnsureTls12();
            var handler = new HttpClientHandler();
            try
            {
                handler.DefaultProxyCredentials = CredentialCache.DefaultCredentials;
            }
            catch (Exception)
            {
                // Without them only a proxy that asks for a sign-in refuses, which the check reports.
            }
            var client = new HttpClient(handler) { Timeout = timeout };
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "UrbanCGI-Exporter-3dsMax/" + (ReleaseInfo.IsRelease ? ReleaseInfo.Tag : "development"));
            return client;
        }

        /// <summary>The innermost message of an exception, for the Listener.</summary>
        public static string Describe(Exception e)
        {
            var messages = new List<string>();
            for (var current = e; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrEmpty(current.Message) && !messages.Contains(current.Message))
                {
                    messages.Add(current.Message);
                }
            }
            return messages.Count > 0 ? string.Join(" ", messages) : e.GetType().Name;
        }

        private static void Remember(UpdateCheckOutcome outcome, CancellationToken cancel)
        {
            if (cancel.IsCancellationRequested)
            {
                return; // the window closed: not a failed check
            }
            var state = UpdateStateFile.Load();
            if (outcome.Failure == UpdateCheckFailure.None)
            {
                state.LastSuccessUtc = DateTime.UtcNow;
                state.LastFailureUtc = null;
                state.LatestTag = outcome.LatestTag;
            }
            else
            {
                state.LastFailureUtc = DateTime.UtcNow;
            }
            UpdateStateFile.Save(state);
        }

        private static UpdateCheckOutcome Failed(UpdateCheckFailure failure, string detail)
        {
            return new UpdateCheckOutcome { Failure = failure, Detail = detail };
        }

        private static string HeaderValue(HttpResponseMessage response, string name)
        {
            IEnumerable<string> values;
            if (response.Headers.TryGetValues(name, out values))
            {
                foreach (var value in values)
                {
                    return value;
                }
            }
            return null;
        }

        private static void EnsureTls12()
        {
#if NETFRAMEWORK
            try
            {
                var protocols = ServicePointManager.SecurityProtocol;
                if (protocols != SecurityProtocolType.SystemDefault && (protocols & SecurityProtocolType.Tls12) == 0)
                {
                    ServicePointManager.SecurityProtocol = protocols | SecurityProtocolType.Tls12;
                }
            }
            catch (Exception)
            {
                // The request then reports why it failed.
            }
#endif
        }
    }
}
