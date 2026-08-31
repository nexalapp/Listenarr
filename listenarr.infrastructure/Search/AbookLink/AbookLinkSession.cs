/*
 * Listenarr - Audiobook Management System
 * Copyright (C) 2024-2026 Listenarr Contributors
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Affero General Public License as published
 * by the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Affero General Public License for more details.
 *
 * You should have received a copy of the GNU Affero General Public License
 * along with this program. If not, see <https://www.gnu.org/licenses/>.
 */
using System.Collections.Concurrent;
using Listenarr.Application.Search.AbookLink;
using Microsoft.Extensions.Logging;

namespace Listenarr.Infrastructure.Search.AbookLink
{
    /// <summary>Result of establishing a session.</summary>
    public sealed record AbookSignIn(bool Succeeded, string? Cookie, string? Reason);

    /// <summary>
    /// Keeps an abook.link session alive.
    ///
    /// Signs in with the configured username and password and holds the resulting cookie
    /// in memory, so a password is exchanged for a session once rather than on every
    /// request. A configured cookie is used as-is instead, for anyone who would rather the
    /// application never held their password.
    ///
    /// The cookie is cached per credentials and only refreshed when the forum shows a
    /// logged-out page, so an expired session recovers without anyone being asked to do
    /// anything.
    /// </summary>
    public class AbookLinkSession
    {
        private const string LoginUrl = "https://abook.link/book/index.php?action=login";
        private const string LoginPostUrl = "https://abook.link/book/index.php?action=login2";

        // Ask the forum for a long-lived session so signing in stays rare.
        private const string CookieLength = "3153600";

        // Some forum stacks reject requests without a browser user-agent outright; the
        // MyAnonamouse provider sets one for the same reason.
        private const string UserAgent =
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

        private static readonly ConcurrentDictionary<string, string> Cookies = new(StringComparer.Ordinal);

        private readonly HttpClient _httpClient;
        private readonly ILogger<AbookLinkSession> _logger;

        public AbookLinkSession(HttpClient httpClient, ILogger<AbookLinkSession> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Returns a usable cookie, signing in if necessary.
        /// <paramref name="forceRefresh"/> discards a cached cookie the forum has rejected.
        /// </summary>
        public async Task<AbookSignIn> GetCookieAsync(
            AbookCredentials credentials,
            bool forceRefresh = false,
            CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(credentials);

            if (credentials.SessionCookie is { Length: > 0 } supplied)
            {
                return new AbookSignIn(true, supplied, null);
            }

            if (!credentials.CanSignIn)
            {
                return new AbookSignIn(false, null,
                    "No abook.link username and password are configured.");
            }

            var key = credentials.Username!;

            if (!forceRefresh && Cookies.TryGetValue(key, out var cached))
            {
                return new AbookSignIn(true, cached, null);
            }

            var signIn = await SignInAsync(credentials, ct);
            if (signIn.Succeeded && signIn.Cookie is { Length: > 0 })
            {
                Cookies[key] = signIn.Cookie;
            }
            else
            {
                Cookies.TryRemove(key, out _);
            }

            return signIn;
        }

        /// <summary>
        /// Reports what the forum replies to a sign-in, for diagnosing a login that does
        /// not take. Returns only what the forum said - status, redirect target and which
        /// markers its response carried - never the credentials.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, string>> DiagnoseAsync(
            AbookCredentials credentials,
            CancellationToken ct = default)
        {
            var report = new Dictionary<string, string>(StringComparer.Ordinal);

            try
            {
                using var formRequest = new HttpRequestMessage(HttpMethod.Get, LoginUrl);
                formRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                using var formResponse = await _httpClient.SendAsync(formRequest, ct);
                var loginHtml = await formResponse.Content.ReadAsStringAsync(ct);

                var hidden = SmfLoginForm.ReadHiddenFields(loginHtml);
                var salt = SmfLoginForm.ReadPasswordHashSalt(loginHtml);
                var action = SmfLoginForm.ReadLoginAction(loginHtml);

                report["loginPageStatus"] = ((int)formResponse.StatusCode).ToString();
                report["loginPageSetCookie"] = CollectCookies(formResponse).Length > 0 ? "yes" : "no";
                report["hiddenFields"] = string.Join(",", hidden.Keys);
                report["hashSaltFound"] = salt is { Length: > 0 } ? "yes" : "no";
                report["formAction"] = action ?? "(not found)";

                if (!credentials.CanSignIn)
                {
                    report["result"] = "no credentials configured";
                    return report;
                }

                var fields = new Dictionary<string, string>(hidden, StringComparer.Ordinal)
                {
                    ["user"] = credentials.Username!,
                    ["cookielength"] = CookieLength
                };

                if (salt is { Length: > 0 })
                {
                    fields["hash_passwrd"] = SmfLoginForm.HashPassword(credentials.Username!, credentials.Password!, salt);
                    fields["passwd"] = string.Empty;
                    report["passwordMode"] = "hashed";
                }
                else
                {
                    fields["passwd"] = credentials.Password!;
                    fields.Remove("hash_passwrd");
                    report["passwordMode"] = "plain";
                }

                using var loginRequest = new HttpRequestMessage(HttpMethod.Post, action ?? LoginPostUrl)
                {
                    Content = new FormUrlEncodedContent(fields)
                };
                loginRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                loginRequest.Headers.TryAddWithoutValidation("Referer", LoginUrl);
                loginRequest.Headers.TryAddWithoutValidation("Cookie", CollectCookies(formResponse));

                using var loginResponse = await _httpClient.SendAsync(loginRequest, ct);
                var body = await loginResponse.Content.ReadAsStringAsync(ct);

                report["loginStatus"] = ((int)loginResponse.StatusCode).ToString();
                report["loginLocation"] = loginResponse.Headers.Location?.ToString() ?? "(none)";
                report["loginSetCookie"] = CollectCookies(loginResponse).Length > 0 ? "yes" : "no";
                report["bodyHasLoginForm"] = body.Contains("action=login2", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
                report["bodyHasLogoutLink"] = body.Contains("action=logout", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
                report["bodyLooksLikeBadCredentials"] = SmfLoginForm.LooksLikeBadCredentials(body) ? "yes" : "no";
                report["bodyLength"] = body.Length.ToString();

                // A short excerpt of visible text so an unexpected message from the forum
                // is legible. Stripped of markup and capped; credentials are never echoed.
                var text = System.Text.RegularExpressions.Regex.Replace(body, "<[^>]+>", " ");
                text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
                report["bodyExcerpt"] = text.Length > 400 ? text[..400] : text;

                var cookie = MergeCookies(CollectCookies(formResponse), CollectCookies(loginResponse));
                report["verifiedSignedIn"] = await IsAuthenticatedAsync(cookie, ct) ? "yes" : "no";
                report["cookieNames"] = string.Join(",", cookie
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(part => part.Split('=', 2)[0].Trim()));

                // Probe the pages the browser actually uses, because a session that
                // authenticates on the forum index has still been rejected elsewhere.
                await ProbeAsync(report, "forumIndex", "https://abook.link/book/index.php", cookie, ct);
                await ProbeAsync(report, "fuzzySearch",
                    "https://abook.link/book/tools/search_abook.php?search=mistborn", cookie, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report["error"] = ex.Message;
            }

            return report;
        }

        private async Task ProbeAsync(
            Dictionary<string, string> report,
            string label,
            string url,
            string cookie,
            CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                using var response = await _httpClient.SendAsync(request, ct);
                var body = await response.Content.ReadAsStringAsync(ct);

                report[$"{label}.status"] = ((int)response.StatusCode).ToString();
                report[$"{label}.location"] = response.Headers.Location?.ToString() ?? "(none)";
                report[$"{label}.bytes"] = body.Length.ToString();
                report[$"{label}.hasLoginForm"] =
                    body.Contains("action=login2", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
                report[$"{label}.hasLogoutLink"] =
                    body.Contains("action=logout", StringComparison.OrdinalIgnoreCase) ? "yes" : "no";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                report[$"{label}.error"] = ex.Message;
            }
        }

        /// <summary>Drops a cached cookie the forum has stopped accepting.</summary>
        public static void Invalidate(AbookCredentials credentials)
        {
            if (credentials?.Username is { Length: > 0 } username)
            {
                Cookies.TryRemove(username, out _);
            }
        }

        private async Task<AbookSignIn> SignInAsync(AbookCredentials credentials, CancellationToken ct)
        {
            try
            {
                // SMF randomises the session token's field name per installation, so the
                // login page is read first and every hidden field carried across.
                using var formRequest = new HttpRequestMessage(HttpMethod.Get, LoginUrl);
                formRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                using var formResponse = await _httpClient.SendAsync(formRequest, ct);

                if (!formResponse.IsSuccessStatusCode)
                {
                    return new AbookSignIn(false, null,
                        $"abook.link returned HTTP {(int)formResponse.StatusCode} for the login page.");
                }

                var loginHtml = await formResponse.Content.ReadAsStringAsync(ct);
                var fields = new Dictionary<string, string>(SmfLoginForm.ReadHiddenFields(loginHtml), StringComparer.Ordinal)
                {
                    ["user"] = credentials.Username!,
                    ["cookielength"] = CookieLength
                };

                // SMF hashes the password in the browser and posts hash_passwrd instead of
                // passwd. Some forums accept the plaintext fallback and this one does not,
                // so do what the browser does: send the hash and blank the plain field.
                var salt = SmfLoginForm.ReadPasswordHashSalt(loginHtml);
                if (salt is { Length: > 0 })
                {
                    fields["hash_passwrd"] = SmfLoginForm.HashPassword(
                        credentials.Username!, credentials.Password!, salt);
                    fields["passwd"] = string.Empty;
                }
                else
                {
                    fields["passwd"] = credentials.Password!;
                    fields.Remove("hash_passwrd");
                }

                var cookieJar = CollectCookies(formResponse);

                // The form's own action carries the PHP session id in its query string;
                // posting to a constructed URL drops the session the page just started.
                var postUrl = SmfLoginForm.ReadLoginAction(loginHtml) ?? LoginPostUrl;

                using var loginRequest = new HttpRequestMessage(HttpMethod.Post, postUrl)
                {
                    Content = new FormUrlEncodedContent(fields)
                };

                loginRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
                loginRequest.Headers.TryAddWithoutValidation("Referer", LoginUrl);

                if (cookieJar.Length > 0)
                {
                    loginRequest.Headers.TryAddWithoutValidation("Cookie", cookieJar);
                }

                using var loginResponse = await _httpClient.SendAsync(loginRequest, ct);

                // The session arrives on the redirect response itself. A client that
                // follows redirects automatically discards those headers before we see
                // them, which is why this client is registered without auto-redirect.
                var cookie = MergeCookies(cookieJar, CollectCookies(loginResponse));

                var status = (int)loginResponse.StatusCode;
                var location = loginResponse.Headers.Location?.ToString();
                var body = await loginResponse.Content.ReadAsStringAsync(ct);

                if (SmfLoginForm.LooksLikeBadCredentials(body))
                {
                    return new AbookSignIn(false, null, "abook.link rejected the username or password.");
                }

                if (cookie.Length == 0)
                {
                    return new AbookSignIn(false, null,
                        $"abook.link returned no session (HTTP {status}). The login form may have changed.");
                }

                // Prove the cookie actually authenticates rather than assuming it. A
                // failed SMF login still hands back a session cookie, so the presence of
                // one says nothing on its own.
                var verified = await IsAuthenticatedAsync(cookie, ct);
                if (!verified)
                {
                    return new AbookSignIn(false, null,
                        $"abook.link accepted the request but the session is not signed in "
                        + $"(login returned HTTP {status}{(location is null ? string.Empty : $", redirect to {location}")}). "
                        + "Check the username and password.");
                }

                _logger.LogInformation("Signed in to abook.link as {Username}", credentials.Username);
                return new AbookSignIn(true, cookie, null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "abook.link sign-in failed");
                return new AbookSignIn(false, null, $"Could not sign in to abook.link: {ex.Message}");
            }
        }

        /// <summary>
        /// Flattens Set-Cookie headers into a request cookie string. The shared HttpClient
        /// has no cookie container by design, so cookies are carried explicitly rather than
        /// leaking between callers of the same client.
        /// </summary>
        /// <summary>
        /// Fetches the forum index and checks whether it renders as signed in.
        /// </summary>
        private async Task<bool> IsAuthenticatedAsync(string cookie, CancellationToken ct)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://abook.link/book/index.php");
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
                request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);

                using var response = await _httpClient.SendAsync(request, ct);
                // The forum index always renders navigation, so demand the positive
                // signal rather than the lenient one used for the site's tool pages.
                return SmfLoginForm.IsDefinitelySignedIn(await response.Content.ReadAsStringAsync(ct));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "Could not verify the abook.link session");
                return false;
            }
        }

        /// <summary>
        /// Overlays newly issued cookies onto the ones already held, keeping the latest
        /// value for a name. SMF re-issues PHPSESSID on login, and sending the stale one
        /// alongside it makes the session ambiguous.
        /// </summary>
        private static string MergeCookies(string existing, string issued)
        {
            var jar = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var source in new[] { existing, issued })
            {
                foreach (var pair in source.Split(';', StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = pair.Split('=', 2);
                    if (parts.Length == 2 && parts[0].Trim().Length > 0)
                    {
                        jar[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }

            return string.Join("; ", jar.Select(entry => $"{entry.Key}={entry.Value}"));
        }

        private static string CollectCookies(HttpResponseMessage response)
        {
            if (!response.Headers.TryGetValues("Set-Cookie", out var values))
            {
                return string.Empty;
            }

            var pairs = new List<string>();
            foreach (var value in values)
            {
                var pair = value.Split(';', 2)[0].Trim();
                if (pair.Length > 0 && pair.Contains('='))
                {
                    pairs.Add(pair);
                }
            }

            return string.Join("; ", pairs);
        }
    }
}
