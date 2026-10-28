# M2 increment B2: Transcript module, hub, audio relay (backend)

Status: implemented, mock-backed (`ReplaySttProvider` only). Companion to `docs/API_CONTRACTS.md` section 15. Author: backend-engineer, 2026-10-28.

## Shape
```
browser --WS speech.v1--> /ingest/sessions/{id}/audio --(ticket, Origin, cookie)--> RelayConnection
                                                            |  bounded channel (20 frames) -> ISttStream (ReplaySttProvider)
                                                            |  provider events -> TranscriptWriter (tx: Lectures counter + INSERT) -> LiveFanout
browser <--SignalR lecture.v1-- /hubs/lecture <-- group lec:{userId}:{sessionId} <-- LiveFanout / RealtimeLifecycleObserver
```
- `Tether.Transcript` (new module) owns schema `transcript`: `transcript_events` (source transcript, never summaries) and `audio_progress`. It references only `Tether.SharedKernel` and `Tether.Lectures.Contracts`.
- `Tether.Lectures.Contracts` gained narrow ports: `ISessionDirectory`, `ISessionEventReader`, `ISessionSequencer`, `ISessionLifecycleObserver`, `ISessionActivity`, `IDemoSourceIndicator`. Lectures keeps ownership of the per-session seq counter and the state machine.
- `Tether.SharedKernel` gained `UserScope` (server-set identity override for code that runs outside an HTTP request: hub, socket pumps, jobs), `AuthClaims`, `IAuthSessionValidator` (implemented by Identity) and `ErrorVocabulary`.

## Why the sequencer takes a DbTransaction (ADR-020 proposal)
`seq` must be gap-free across Lectures' `session.state` events and the Transcript events. `ISessionSequencer.AllocateAsync(tx, ...)` runs two statements in the caller's transaction: `UPDATE session_counters ... RETURNING` (takes the row lock; pause, stop and delete need the same row) and then a fresh-snapshot `SELECT state` that must be `Active`. A write that loses the race to a pause is rejected, rolled back, and leaves no gap. This is cheaper than an outbox and keeps the invariant in the database.

The cross-schema FK allow-list is now five entries (all `ON DELETE CASCADE`): the three from ADR-014/019 plus `fk_transcript_events_session_user` and `fk_audio_progress_session_user` on `(session_id, user_id)` to `lectures.lecture_sessions (id, user_id)`. They are raw SQL in `T001_Transcript` and guarded by `Cross_schema_foreign_keys_are_exactly_the_allow_listed_set`. Migration order: Identity, Courses, Lectures, Transcript (reverse to roll back). Proposed wording for DECISIONS.md: "ADR-020: Transcript data cleanup uses the ADR-019 pattern; the per-session seq is allocated through a Lectures contract port inside the caller's transaction."

## Operational notes (for devops; no compose or CI edits were made)
- Reverse proxies must pass WebSocket upgrades for `/hubs/lecture` and `/ingest/*` and must preserve the `Origin` header. Idle timeouts must exceed the 30 s keep-alive.
- `Security:AllowedOrigins` is now also the allow-list for the two socket paths (exact ordinal match).
- Single instance only for M2 (see contract section 15, "Deferred").
- Tests need the `Microsoft.AspNetCore.SignalR.Client` NuGet package (test project only).
- New configuration keys are listed in `src/api/README.md`.

## Single-instance limits (B2 security review, ADR-020d)
**Run exactly one API instance.** Nothing below is shared between processes: hub connections and groups (`HubConnections`, SignalR groups), the live partial store, `RelayRegistry`, `SessionActivityTracker`, `JoinLimiter`, `HubInvocationLimiter`, `FailedHelloLimiter`, and the ASP.NET rate limiters. With two instances:
- Events from the instance that holds the audio socket do not reach hub clients on the other one.
- A pause, delete or logout handled by instance B does not close a socket on A until A's polling (`Realtime:RelayCheckSeconds`, default 5 s; hub 60 s). The push added in the B2 fix pass is also in process.
- Instance B's lifecycle job sees no activity for a session streaming on A and auto-pauses it after 5 minutes.
- Every limit is multiplied by the instance count, and `HubReaper` cannot abort connections that live elsewhere.

Before scale-out: a SignalR backplane, shared activity/limiter/relay state (or sticky sessions plus leader election for the job), and a cross-instance revocation and cutoff channel. Deployment configuration must pin the replica count to 1; no automated check enforces it.

## Pause, stop and push closure (after the B2 review)
- `RelayRegistry.CloseSessionAsync` (state change, delete, archive, purge) and `CloseAuthSessionAsync` (revocation) are the push paths. They set the relay's cut flag first, so no later frame is acked, then complete the provider stream, save the audio position, leave the registry, and only then send the close frame.
- `RelayConnection.RunAsync` re-reads the session after registration and before opening the provider stream (B2-04). The only test seam is `RelayRegistry.AfterTicketConsumed`, always null in production.
- Limiters share `SlidingWindowCounter<TKey>`: idle keys are swept every half window and each table has a hard key cap (`JoinLimiter` and `FailedHelloLimiter` 20000 in total, `HubInvocationLimiter` 10000). When a table is full a new key is refused for Join and hub invocations (fail closed) and dropped for failed-hello counting. IPv6 clients are keyed by /64.
