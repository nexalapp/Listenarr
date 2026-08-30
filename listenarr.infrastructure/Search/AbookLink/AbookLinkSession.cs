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
                    ["passwd"] = credentials.Password!,
                    ["cookielength"] = CookieLength
                };

                var cookieJar = CollectCookies(formResponse);

                using var loginRequest = new HttpRequestMessage(HttpMethod.Post, LoginPostUrl)
                {
                    Content = new FormUrlEncodedContent(fields)
                };

                if (cookieJar.Length > 0)
                {
                    loginRequest.Headers.TryAddWithoutValidation("Cookie", cookieJar);
                }

                using var loginResponse = await _httpClient.SendAsync(loginRequest, ct);
                var cookie = CollectCookies(loginResponse);

                if (cookie.Length == 0)
                {
                    var body = await loginResponse.Content.ReadAsStringAsync(ct);
                    return new AbookSignIn(false, null, SmfLoginForm.LooksLikeBadCredentials(body)
                        ? "abook.link rejected the username or password."
                        : "abook.link did not return a session. The login form may have changed.");
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
