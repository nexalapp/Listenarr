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
using System.Net;
using Listenarr.Application.Security.Outbound;
using Listenarr.Tests.Common;

namespace Listenarr.Tests.Features.Application.Security
{
    /// <summary>
    /// Which callers count as being on the same network, which decides who may read the
    /// server's own API key and anything else gated on being local.
    /// </summary>
    [Trait("Name", "SecurityRequestUtilsTests")]
    [Trait("Category", "Security")]
    public sealed class SecurityRequestUtilsTests : BaseTests
    {
        [Theory]
        [InlineData("10.10.10.5")]
        [InlineData("192.168.1.20")]
        [InlineData("172.16.0.9")]
        [InlineData("127.0.0.1")]
        public void PrivateIpv4_IsPrivate(string address)
        {
            Assert.True(SecurityRequestUtils.IsPrivateOrLoopback(IPAddress.Parse(address)));
        }

        [Theory]
        // The form a dual-stack listener reports IPv4 clients in, which is what
        // ASPNETCORE_URLS=http://*:port produces. Read as plain IPv6 these match none of
        // the private rules, so every caller on the LAN was judged public and refused
        // everything gated on being local - including reading the API key from settings.
        [InlineData("::ffff:10.10.10.5")]
        [InlineData("::ffff:192.168.1.20")]
        [InlineData("::ffff:172.16.0.9")]
        [InlineData("::ffff:127.0.0.1")]
        public void PrivateIpv4_MappedIntoIpv6_IsStillPrivate(string address)
        {
            Assert.True(SecurityRequestUtils.IsPrivateOrLoopback(IPAddress.Parse(address)));
        }

        [Theory]
        [InlineData("8.8.8.8")]
        [InlineData("::ffff:8.8.8.8")]
        [InlineData("2606:4700:4700::1111")]
        [InlineData("172.32.0.1")] // just outside 172.16/12
        public void PublicAddresses_AreNotPrivate(string address)
        {
            Assert.False(SecurityRequestUtils.IsPrivateOrLoopback(IPAddress.Parse(address)));
        }

        [Theory]
        [InlineData("fd00::1")]
        [InlineData("fe80::1")]
        public void PrivateIpv6_IsPrivate(string address)
        {
            Assert.True(SecurityRequestUtils.IsPrivateOrLoopback(IPAddress.Parse(address)));
        }
    }
}
