using Tether.Docs;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Tether.DocsTests;

/// <summary>
/// DX-B01 (B02) and DX-B06 (B32): the startup guards of the Docs module. A configuration flag is not a human gate, so a real provider is refused
/// unconditionally (real Google stays OFF, PO-20cb), and fault injection is allowed only in Development and Test, whatever its value.
/// </summary>
public class ModuleGuardTests
{
    private static WebApplicationBuilder Builder(string environment, params (string Key, string Value)[] settings)
    {
        var b = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = environment });
        b.Configuration["ConnectionStrings:Default"] = "Host=localhost;Database=never_opened";
        foreach (var (k, v) in settings) b.Configuration[k] = v;
        return b;
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void B02_The_default_and_demo_docs_start_in_every_environment(string env)
    {
        DocsModule.AddDocsModule(Builder(env));
        DocsModule.AddDocsModule(Builder(env, ("Docs:Provider", "demo_docs")));
    }

    [Theory]
    [InlineData("google_docs")]
    [InlineData("Demo_Docs")]
    [InlineData("demo_docs ")]
    [InlineData("")]
    [InlineData("google")]
    public void B02_Any_other_provider_fails_startup_in_every_environment(string provider)
    {
        foreach (var env in new[] { "Development", "Test", "Production" })
        {
            var ex = Assert.Throws<InvalidOperationException>(() => DocsModule.AddDocsModule(Builder(env, ("Docs:Provider", provider))));
            Assert.Contains("demo_docs", ex.Message);
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    [InlineData("Testing")]
    [InlineData("production")]
    [InlineData("QA")]
    public void B32_Any_fault_key_fails_startup_outside_Development_and_Test_not_only_Enabled_true(string env)
    {
        foreach (var key in new[] { "Docs:Demo:Faults:Enabled", "Docs:Demo:Faults:Anything", "Docs:Demo:Faults:Slow" })
        foreach (var value in new[] { "true", "false", "" })
        {
            var ex = Assert.Throws<InvalidOperationException>(() => DocsModule.AddDocsModule(Builder(env, (key, value))));
            Assert.Contains("Development and Test", ex.Message);
        }
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Test")]
    public void B32_Development_and_Test_may_set_the_fault_flag(string env)
    {
        DocsModule.AddDocsModule(Builder(env, ("Docs:Demo:Faults:Enabled", "true")));
        DocsModule.AddDocsModule(Builder(env, ("Docs:Demo:Faults:Enabled", "false")));
    }

    [Fact]
    public void The_module_registers_one_provider_the_demo_one_and_the_AI_module_is_not_reachable_from_its_services()
    {
        var b = Builder("Production");
        DocsModule.AddDocsModule(b);
        var provider = b.Services.Where(d => d.ServiceType.Name == "IDocsProvider").ToList();
        Assert.Single(provider);
        Assert.Equal("DemoDocsProvider", provider[0].ImplementationType!.Name);
        Assert.DoesNotContain(b.Services, d => (d.ServiceType.FullName ?? "").StartsWith("Tether.Ai.", StringComparison.Ordinal) && !(d.ServiceType.FullName ?? "").StartsWith("Tether.Ai.Contracts", StringComparison.Ordinal));
        Assert.DoesNotContain(b.Services, d => d.ServiceType == typeof(HttpClient) || (d.ServiceType.FullName ?? "").Contains("HttpClientFactory", StringComparison.Ordinal));
    }
}
