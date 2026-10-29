using System.Diagnostics;
using Tether.Ai.Budget;
using Tether.Ai.Domain;
using Tether.Ai.Prompts;
using Tether.Ai.Validation;
using Tether.Lectures.Contracts;
using Tether.SharedKernel.Ai;
using Microsoft.Extensions.Logging;

namespace Tether.Ai.Provider;

/// <summary>Fixed vocabulary for why an AI step produced nothing. Mapped to <c>reason</c> on the wire and never carries content.</summary>
internal static class AiFailure
{
    public const string BudgetLimited = "budget_limited";
    public const string Unavailable = "provider_unavailable";
    public const string Timeout = "timeout";
    public const string RateLimited = "rate_limited";
    public const string InvalidOutput = "invalid_output";
    public const string Disabled = "disabled";
    /// <summary>The student paused, stopped or deleted the session (B3-06). Not a fault: nothing is shown, counted against the breaker or retried.</summary>
    public const string SessionClosed = "session_closed";

    /// <summary>Wire <c>summary.status.reason</c> values (rate limiting is reported as the provider being unavailable).</summary>
    public static string ToWireReason(string failure) => failure switch
    {
        BudgetLimited => "budget_limited",
        Timeout => "timeout",
        InvalidOutput => "invalid_output",
        _ => "provider_unavailable",
    };
}

internal sealed record GatewayOutcome<T>(T? Value, string? Failure, string Model, bool Repaired) where T : class
{
    public bool Ok => Failure is null;
}

/// <summary>
/// Circuit breaker for provider-side failures only. A malformed answer is the model's fault and does not trip it; a timeout, 5xx or
/// 429 does. A 429 without retry-after (the vendor's spend-cap shape) is terminal: the breaker stays open for a long time instead of
/// letting any caller loop on it.
/// </summary>
internal sealed class AiHealth(AiOptions options, TimeProvider clock)
{
    private readonly object _gate = new();
    private int _consecutive;
    private DateTimeOffset _openUntil = DateTimeOffset.MinValue;

    public bool IsOpen { get { lock (_gate) return clock.GetUtcNow() < _openUntil; } }

    public void RecordSuccess() { lock (_gate) _consecutive = 0; }

    /// <summary>The usage ledger could not be written: stop making paid calls for a while (fail closed) rather than spend untracked.</summary>
    public void RecordLedgerFault() { lock (_gate) { var until = clock.GetUtcNow() + options.Breaker.OpenFor; if (until > _openUntil) _openUntil = until; } }

    public void RecordFailure(AiFailureKind kind)
    {
        lock (_gate)
        {
            _consecutive++;
            if (kind == AiFailureKind.ProviderCap) _openUntil = clock.GetUtcNow() + options.Breaker.ProviderCapOpenFor;
            else if (_consecutive >= options.Breaker.FailuresToOpen) _openUntil = clock.GetUtcNow() + options.Breaker.OpenFor;
        }
    }
}

/// <summary>
/// The only way the application calls <see cref="IAiClient"/>: budget check, one call with a deadline, schema validation, ONE bounded
/// repair attempt, and a ledger row per call. No retry on 429 or 5xx (the next scheduled attempt is the retry), so a provider that
/// is refusing us is never hammered. Scoped: run it inside the owning user's scope.
/// </summary>
internal sealed class AiGateway(
    IAiClient client, UsageLedger ledger, BudgetGate budget, AiHealth health, AiOptions options, SessionCutoff cutoff, ISessionDirectory directory, ILogger<AiGateway> log,
    IEnumerable<IAiDocumentScopeProbe>? documentProbes = null)
{
    public async Task<GatewayOutcome<T>> RunAsync<T>(AiRequest request, Func<string, Validated<T>> validate, CancellationToken ct) where T : class
    {
        if (request.EffectiveScope.IsLecture == (request.Workflow == AiWorkflow.SyllabusExtract))
            throw new ArgumentException("Workflow does not belong to the request scope.", nameof(request)); // programming error, never user-reachable
        if (!options.Enabled) return new(null, AiFailure.Disabled, "", false);
        // Taken before any state check: a pause that commits after the check below still cancels the call (see SessionCutoff).
        using var lease = cutoff.Acquire(request.EffectiveScope, ct);
        var sct = lease.Token;
        var repaired = false;
        string? repairCode = null;
        var model = "";
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (health.IsOpen) return new(null, AiFailure.Unavailable, model, repaired);
            if (sct.IsCancellationRequested && !ct.IsCancellationRequested) return new(null, AiFailure.SessionClosed, model, repaired);

            var req = request with { RepairHint = repairCode };

            // N-01: a cheap read first, so a closed session never costs a reserve and settle pair (a scheduler that still tracks one would
            // otherwise write two ledger rows every interval). The check after the reservation below stays: it is the last gate before the vendor.
            if (!await IsActiveAsync(req, ct)) return new(null, AiFailure.SessionClosed, model, repaired);

            // Hold the estimated cost against every cap BEFORE the call (B3-02). A ledger that cannot be written fails closed.
            ReserveOutcome held;
            try { held = await budget.ReserveAsync(req.UserId, req.EffectiveScope, req.Workflow, req.PromptVersion, options.Price(req.Tier).Model, EstimateMicro(req), ct); }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning("AI budget reservation failed: {ExceptionType}", ex.GetType().Name);
                health.RecordLedgerFault();
                return new(null, AiFailure.Unavailable, model, repaired);
            }
            if (held.Reservation is not { } reservation)
            {
                await ledger.RecordAsync(req.UserId, req.EffectiveScope, req.Workflow, req.PromptVersion, "none", new AiTokenUsage(0, 0, 0), 0, 0, Outcomes.BudgetDenied, ct);
                return new(null, AiFailure.BudgetLimited, model, repaired);
            }
            if (held.Decision.ForceTier is { } forced) req = req with { Tier = forced };
            var price = options.Price(req.Tier);

            var settled = false;
            async Task Settle(string m, AiTokenUsage usage, long cost, int latency, string outcome)
            {
                settled = true; // attempted; SettleAsync keeps the hold counted if it cannot finish, and the reaper settles what is left
                if (!await ledger.SettleAsync(reservation, m, usage, cost, latency, outcome)) health.RecordLedgerFault(); // reservation stays counted; stop calling
            }

            // N-02: from here on every exit settles the hold exactly once. The paths below settle explicitly; this catches the rest (an exception
            // from the validator, the host shutting down during the Active check, a cancellation we did not anticipate).
            try
            {
                // B3-06: the last gate before the vendor. Owned by this user and Active, or no call, no spend (the hold is given back).
                if (!await IsActiveAsync(req, ct))
                {
                    await Settle(price.Model, new AiTokenUsage(0, 0, 0), 0, 0, Outcomes.Error);
                    return new(null, AiFailure.SessionClosed, model, repaired);
                }

                AiResponse response;
                var sw = Stopwatch.StartNew();
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(sct);
                    cts.CancelAfter(req.Timeout);
                    response = await client.CompleteAsync(req, cts.Token);
                }
                catch (OperationCanceledException) when (!sct.IsCancellationRequested)
                {
                    health.RecordFailure(AiFailureKind.Timeout);
                    await Settle(price.Model, new AiTokenUsage(0, 0, 0), 0, (int)sw.ElapsedMilliseconds, Outcomes.Timeout);
                    return new(null, AiFailure.Timeout, price.Model, repaired);
                }
                catch (OperationCanceledException)
                {
                    await Settle(price.Model, new AiTokenUsage(0, 0, 0), 0, (int)sw.ElapsedMilliseconds, Outcomes.Error); // release the hold
                    if (!ct.IsCancellationRequested) return new(null, AiFailure.SessionClosed, price.Model, repaired); // cut off by pause, stop or delete (B3-06)
                    throw; // shutting down
                }
                catch (AiClientException ex)
                {
                    health.RecordFailure(ex.Kind);
                    var rate = ex.Kind is AiFailureKind.RateLimited or AiFailureKind.ProviderCap;
                    await Settle(price.Model, new AiTokenUsage(0, 0, 0), 0, (int)sw.ElapsedMilliseconds,
                        ex.Kind == AiFailureKind.Timeout ? Outcomes.Timeout : rate ? Outcomes.RateLimited : Outcomes.Error);
                    log.LogWarning("AI call failed {Workflow} {Kind}", req.Workflow, ex.Kind);
                    return new(null, ex.Kind == AiFailureKind.Timeout ? AiFailure.Timeout : rate ? AiFailure.RateLimited : AiFailure.Unavailable, price.Model, repaired);
                }
                catch (Exception)
                {
                    await Settle(price.Model, new AiTokenUsage(0, 0, 0), 0, (int)sw.ElapsedMilliseconds, Outcomes.Error);
                    throw;
                }
                health.RecordSuccess();
                model = response.Model;

                var validated = response.StopReason == "max_tokens" ? Validated<T>.Fail(ValidationCodes.Truncated) : validate(response.Json);
                var cost = Pricing.CostMicro(response.Usage, price);
                if (validated.Ok)
                {
                    await Settle(response.Model, response.Usage, cost, response.LatencyMs, repaired ? Outcomes.Repaired : Outcomes.Ok);
                    return new(validated.Value, null, response.Model, repaired);
                }
                await Settle(response.Model, response.Usage, cost, response.LatencyMs, Outcomes.Invalid);
                log.LogInformation("AI output rejected {Workflow} {Code} attempt {Attempt}", req.Workflow, validated.Error, attempt + 1);
                // A stale base cannot be repaired by asking again with the same base; everything else gets one repair attempt (a code, never content).
                if (validated.Error == ValidationCodes.StaleBase) break;
                repaired = true;
                repairCode = validated.Error;
            }
            finally
            {
                if (!settled) await Settle(price.Model, new AiTokenUsage(0, 0, 0), 0, 0, Outcomes.Error);
            }
        }
        return new(null, AiFailure.InvalidOutput, model, repaired);
    }

    /// <summary>
    /// Fails closed: a scope that cannot be confirmed live for this user gets no vendor call. A lecture scope must be an Active session
    /// (missing, someone else's, paused, stopped, or a lookup error all refuse). A document scope asks the probe the owning module
    /// registered for that kind; no probe, a false answer or an error all refuse.
    /// </summary>
    private async Task<bool> IsActiveAsync(AiRequest req, CancellationToken ct)
    {
        try
        {
            var scope = req.EffectiveScope;
            if (scope.IsLecture) return await directory.FindAsync(req.UserId, scope.Id, ct) is { State: LectureState.Active };
            var probe = documentProbes?.FirstOrDefault(p => p.Kind == scope.Kind);
            return probe is not null && await probe.IsLiveAsync(req.UserId, scope.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning("AI session state check failed: {ExceptionType}", ex.GetType().Name);
            return false;
        }
    }

    private long EstimateMicro(AiRequest r)
    {
        var p = options.Price(r.Tier);
        var inTokens = PromptRenderer.EstimateTokens(PromptRenderer.Render(r));
        return Pricing.CostMicro(new AiTokenUsage(inTokens, 0, r.MaxOutputTokens), p);
    }
}
