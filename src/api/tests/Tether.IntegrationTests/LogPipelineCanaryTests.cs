using System.Net;
using System.Net.Http.Json;
using System.Text;
using Tether.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tether.IntegrationTests;

/// <summary>
/// P2B1-02 / B1-02: a log canary through the REAL logging pipeline (WebApplicationFactory, the Host's own filters, a capturing
/// provider) with every category forced to Trace, in Development and in a production-like environment. Sentinels for lecture text,
/// syllabus text, a fake bearer token, a fake cookie value and a query-string token must never appear in any record from any
/// category, in particular Npgsql, EF Core (Command, Query, Update, Infrastructure), ADO and Microsoft.AspNetCore.
/// </summary>
public static class LogPipelineCanary
{
    public const string LectureText = "CANARY-02-LECTURE-TEXT-4d1f";
    public const string SyllabusText = "CANARY-02-SYLLABUS-TEXT-77ab";
    public const string FakeToken = "CANARY-02-FAKE-TOKEN-ya29.c0ffee";
    public const string QueryToken = "CANARY-02-QUERY-TOKEN-91e3";
    public const string CookieValue = "CANARY-02-COOKIE-VALUE-b00b";

    public static readonly string[] All = [LectureText, SyllabusText, FakeToken, QueryToken, CookieValue];

    /// <summary>Every category at Trace, set through the same configuration an operator would use. The Host's own filters must still win.</summary>
    public static IEnumerable<KeyValuePair<string, string?>> TraceEverything(string connectionString) =>
    [
        new("ConnectionStrings:Default", connectionString + ";Include Error Detail=true"), // worst case: Npgsql echoes row data
        new("Logging:LogLevel:Default", "Trace"),
        new("Logging:LogLevel:Microsoft", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore", "Trace"),
        new("Logging:LogLevel:Microsoft.EntityFrameworkCore", "Trace"),
        new("Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command", "Trace"),
        new("Logging:LogLevel:Microsoft.EntityFrameworkCore.Query", "Trace"),
        new("Logging:LogLevel:Microsoft.EntityFrameworkCore.Update", "Trace"),
        new("Logging:LogLevel:Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware", "Trace"),
        new("Logging:LogLevel:Npgsql", "Trace"),
        new("Logging:LogLevel:Npgsql.Command", "Trace"),
        new("Logging:LogLevel:Npgsql.Connection", "Trace"),
        new("Logging:LogLevel:System.Net.Http", "Trace"),
    ];

    public static void AssertClean(IEnumerable<string> lines, bool requireDbCategories)
    {
        var all = lines.ToArray();
        Assert.True(all.Length > 50, $"Trace logging must be on for this test to mean anything (saw {all.Length} records)");
        Assert.Contains(all, l => l.StartsWith("Microsoft.AspNetCore.", StringComparison.Ordinal)); // the pipeline under test is really logging
        if (requireDbCategories)
            Assert.Contains(all, l => l.StartsWith("Npgsql", StringComparison.Ordinal) || l.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));
        foreach (var s in All)
        {
            var bad = all.Where(l => l.Contains(s, StringComparison.Ordinal)).Select(l => l.Length > 200 ? l[..200] : l).ToArray();
            Assert.True(bad.Length == 0, $"sentinel {s} leaked into {bad.Length} log record(s), first: {bad.FirstOrDefault()}");
        }
        // Parameter values and SQL text must not be logged at all, whatever they contain.
        Assert.DoesNotContain(all, l => l.StartsWith("Microsoft.EntityFrameworkCore.Database.Command", StringComparison.Ordinal));
        Assert.DoesNotContain(all, l => l.StartsWith("Npgsql.Command", StringComparison.Ordinal) && l.Contains("insert into", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Requests that carry every sentinel but need no login (the only kind a production-like environment without dev login can serve).</summary>
    public static async Task AnonymousTrafficAsync(HttpClient c)
    {
        var r1 = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/courses?token={QueryToken}&q={LectureText}");
        r1.Headers.TryAddWithoutValidation("Authorization", "Bearer " + FakeToken);
        r1.Headers.TryAddWithoutValidation("Cookie", "cc.session=" + CookieValue);
        Assert.Equal(HttpStatusCode.Unauthorized, (await c.SendAsync(r1)).StatusCode);

        var r2 = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/courses/{Guid.NewGuid()}/syllabi?access_token={QueryToken}")
        { Content = new StringContent($$"""{"text":"{{SyllabusText}} {{LectureText}}"}""", Encoding.UTF8, "application/json") };
        r2.Headers.TryAddWithoutValidation("Authorization", "Bearer " + FakeToken);
        await c.SendAsync(r2);

        var r3 = new HttpRequestMessage(HttpMethod.Get, $"/no-such-route/{LectureText}?x={QueryToken}");
        r3.Headers.TryAddWithoutValidation("Cookie", "cc.session=" + CookieValue);
        await c.SendAsync(r3);

        var r4 = new HttpRequestMessage(HttpMethod.Post, "/api/v1/courses") { Content = new StringContent("{\"name\":\"" + LectureText + "\"", Encoding.UTF8, "application/json") }; // malformed JSON
        await c.SendAsync(r4);
    }
}

public class DevelopmentLogPipelineCanaryTests(DevelopmentLogPipelineCanaryTests.Factory f) : IClassFixture<DevelopmentLogPipelineCanaryTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        public LogCanaryTests.CapturingProvider Logs { get; } = new();
        protected override IEnumerable<KeyValuePair<string, string?>> Settings => LogPipelineCanary.TraceEverything(ConnectionString);
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    [Fact]
    public async Task Lecture_text_syllabus_text_and_tokens_never_reach_any_log_category_in_development()
    {
        var c = await f.LoginAsync("canary-dev@dev.local");

        // Lecture text: a course name stands in for any student-authored string stored through EF; it is read back so a Query log would carry it too.
        var created = await c.PostAsJsonAsync("/api/v1/courses", new { name = "Bio " + LogPipelineCanary.LectureText });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Contains(LogPipelineCanary.LectureText, await (await c.GetAsync("/api/v1/courses")).Content.ReadAsStringAsync()); // real data, so a leak is possible
        var courseId = (await created.BodyAsync()).GetProperty("id").GetString()!;

        // Syllabus text through the real upload path and the real background worker.
        var body = new Dictionary<string, object?>
        {
            ["title"] = "BIO 101", ["text"] = LogPipelineCanary.SyllabusText + "\nPS1 due Mon Sept 14, 11:59 PM", ["termStart"] = "2026-08-24", ["termEnd"] = "2026-12-11",
            ["defaultTimezone"] = "America/New_York", ["allowDuplicate"] = false, ["noticeVersion"] = "draft-2026-10",
            ["attestation"] = new { accepted = true, acceptedAt = DateTimeOffset.UtcNow.ToString("O") },
        };
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/courses/{courseId}/syllabi?access_token={LogPipelineCanary.QueryToken}") { Content = JsonContent.Create(body) };
        req.Headers.TryAddWithoutValidation("Idempotency-Key", "k" + Guid.NewGuid().ToString("N"));
        req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + LogPipelineCanary.FakeToken);
        var accepted = await c.SendAsync(req);
        Assert.Equal(HttpStatusCode.Accepted, accepted.StatusCode);
        var sid = (await accepted.BodyAsync()).GetProperty("id").GetString()!;
        for (var i = 0; i < 100; i++)
        {
            var s = await (await c.GetAsync($"/api/v1/syllabi/{sid}")).BodyAsync();
            if (s.GetProperty("status").GetString() is "ready" or "failed") break;
            await Task.Delay(100);
        }

        // A failing row: Postgres echoes the row ("Failing row contains ...") and Npgsql is told to include it.
        await f.ExecAsync($"alter table courses.courses add constraint ck_canary check (name not like '%FAIL-{LogPipelineCanary.LectureText}%')");
        var failed = await c.PostAsJsonAsync("/api/v1/courses", new { name = "FAIL-" + LogPipelineCanary.LectureText });
        await failed.AssertProblemAsync(HttpStatusCode.InternalServerError, "internal_error");

        await LogPipelineCanary.AnonymousTrafficAsync(f.NewClient());
        await Task.Delay(300);

        LogPipelineCanary.AssertClean(f.Logs.Lines.ToArray(), requireDbCategories: true);
    }
}

public class ProductionLikeLogPipelineCanaryTests(ProductionLikeLogPipelineCanaryTests.Factory f) : IClassFixture<ProductionLikeLogPipelineCanaryTests.Factory>
{
    public sealed class Factory : ApiFactory
    {
        public LogCanaryTests.CapturingProvider Logs { get; } = new();
        protected override string EnvironmentName => "Production";
        protected override IEnumerable<KeyValuePair<string, string?>> Settings =>
            [.. LogPipelineCanary.TraceEverything(ConnectionString), new("Auth:DevLogin:Enabled", "false"), new("Database:MigrateOnStartup", "true")];
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(s => s.AddSingleton<ILoggerProvider>(Logs));
        }
    }

    [Fact]
    public async Task Sentinels_never_reach_any_log_category_in_a_production_like_environment()
    {
        await LogPipelineCanary.AnonymousTrafficAsync(f.NewClient());
        await Task.Delay(300);
        LogPipelineCanary.AssertClean(f.Logs.Lines.ToArray(), requireDbCategories: true); // MigrateOnStartup runs real SQL through EF and Npgsql
    }
}
