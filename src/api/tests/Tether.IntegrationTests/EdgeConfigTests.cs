using System.Net;
using Tether.Host;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Tether.IntegrationTests;

/// <summary>M1R-N01 / M1R-N02: forwarded-header trust ranges, AllowedHosts and HSTS are validated or made independent of client-supplied headers.</summary>
public class EdgeConfigTests
{
    [Theory]
    [InlineData("0.0.0.0/0")]
    [InlineData("::/0")]
    [InlineData("0.0.0.0/7")]
    [InlineData("128.0.0.0/1")]
    [InlineData("::ffff:0.0.0.0/96")]   // all of IPv4, spelled as IPv4-mapped IPv6
    [InlineData("2000::/16")]
    [InlineData("not-a-network")]
    [InlineData("10.0.0.0/33")]
    [InlineData("")]
    [InlineData("*")]
    public void Trusted_proxy_networks_that_are_too_wide_or_malformed_are_refused(string network)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EdgeConfig.ParseTrustedProxies([], [network]));
        Assert.Contains("Security:TrustedProxyNetworks", ex.Message);
    }

    [Theory]
    [InlineData("0.0.0.0")]
    [InlineData("::")]
    [InlineData("*")]
    [InlineData("")]
    [InlineData("10.0.0.5/8")]
    [InlineData("not-an-ip")]
    public void Trusted_proxy_addresses_that_are_unspecified_or_malformed_are_refused(string proxy)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EdgeConfig.ParseTrustedProxies([proxy], []));
        Assert.Contains("Security:TrustedProxies", ex.Message);
    }

    [Fact]
    public void Reasonable_proxy_ranges_are_accepted()
    {
        var (proxies, networks) = EdgeConfig.ParseTrustedProxies(["10.0.0.5", "fd00::1"], ["10.0.0.0/8", "172.16.0.0/12", "192.168.1.0/24", "fd00::/48"]);
        Assert.Equal(2, proxies.Length);
        Assert.Equal(4, networks.Length);
    }

    [Theory]
    [InlineData("*")]
    [InlineData(" * ")]
    [InlineData("api.example.test;*")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    public void AllowedHosts_that_accept_any_host_are_refused_outside_Development(string? value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => EdgeConfig.EnsureAllowedHosts(value, isDevelopment: false));
        Assert.Contains("AllowedHosts", ex.Message);
    }

    [Theory]
    [InlineData("api.example.test")]
    [InlineData("localhost;api.example.test:8443")]
    [InlineData("*.example.test")]
    public void Specific_AllowedHosts_are_accepted(string value) => EdgeConfig.EnsureAllowedHosts(value, isDevelopment: false);

    [Theory]
    [InlineData("*")]
    [InlineData(null)]
    public void Development_may_use_any_host(string? value) => EdgeConfig.EnsureAllowedHosts(value, isDevelopment: true);
}

public sealed class WildcardHostsProductionFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "false"), new("AllowedHosts", "*")];
}

public sealed class WideProxyProductionFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "false"), new("Security:TrustedProxyNetworks:0", "0.0.0.0/0")];
}

public class EdgeStartupRefusalTests
{
    [Fact]
    public async Task Production_does_not_start_with_AllowedHosts_star()
    {
        await using var f = new WildcardHostsProductionFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        Assert.Contains("AllowedHosts", Assert.ThrowsAny<Exception>(() => f.CreateClient()).ToString());
        await ((IAsyncLifetime)f).DisposeAsync();
    }

    [Fact]
    public async Task The_shipped_appsettings_do_not_allow_any_host_outside_Development()
    {
        // No AllowedHosts override at all: whatever the shipped appsettings.json says must itself be acceptable.
        await using var f = new ShippedHostsProductionFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().GetAsync("/health/live")).StatusCode);
        await ((IAsyncLifetime)f).DisposeAsync();
    }

    [Fact]
    public async Task Production_does_not_start_when_every_address_is_a_trusted_proxy()
    {
        await using var f = new WideProxyProductionFactory();
        await ((IAsyncLifetime)f).InitializeAsync();
        Assert.Contains("Security:TrustedProxyNetworks", Assert.ThrowsAny<Exception>(() => f.CreateClient()).ToString());
        await ((IAsyncLifetime)f).DisposeAsync();
    }
}

/// <summary>Does not let the shared test factory supply AllowedHosts, so the shipped appsettings.json value is what runs.</summary>
public sealed class ShippedHostsProductionFactory : ApiFactory
{
    protected override string EnvironmentName => "Production";
    protected override string? AllowedHostsValue => null;
    protected override IEnumerable<KeyValuePair<string, string?>> Settings => [new("Auth:DevLogin:Enabled", "false")];
}

public class HstsTests(ProductionFactory prod) : IClassFixture<ProductionFactory>
{
    [Fact]
    public async Task Hsts_is_sent_outside_Development_whatever_the_scheme_and_does_not_depend_on_a_forwarded_header()
    {
        // Behind a TLS terminator the app sees plain http and (unless the proxy is configured) no trustworthy forwarded scheme.
        Assert.True((await prod.NewClient().GetAsync("/health/live")).Headers.Contains("Strict-Transport-Security"));
        var forged = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        forged.Headers.TryAddWithoutValidation("X-Forwarded-Proto", "http");
        var res = await prod.NewClient().SendAsync(forged);
        var hsts = res.Headers.GetValues("Strict-Transport-Security").Single();
        Assert.Contains("max-age=31536000", hsts);
        Assert.Contains("includeSubDomains", hsts);
    }
}
