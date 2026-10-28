# Tether wire contracts (lecture.v1 / speech.v1)

JSON Schema (draft 2020-12) for the frozen contract in `docs/API_CONTRACTS.md` (sections 2, 3, 4, 8, 10). Where this directory and that document disagree, the document wins and this is a bug.

```
lecture.v1/            one *.schema.json per hub event, DTO and socket frame, plus common.schema.json
fixtures/valid/        must validate         name: {schema}--{case}.json
fixtures/invalid/      must be rejected      name: {schema}--{case}.json
```

`{schema}` is the schema basename, e.g. `transcript.final--null-seq.json` is checked against `lecture.v1/transcript.final.schema.json`. Each schema has `$id` `https://schemas.tether.invalid/lecture.v1/{name}.schema.json`; cross-file `$ref`s are relative, so a validator must register every file in `lecture.v1/` before use. `hub-event.schema.json` is a `oneOf` over all ten events (discriminated by `type`) for clients that want one entry point.

## Strictness policy

Every object in every schema has `additionalProperties: false`, **envelope and payload alike**. Unknown fields are rejected, not ignored. This is deliberate (it catches contract drift and accidental leakage such as an unplanned `speaker`, transcript echo or exception text). Consequence: any additive change within `v1` (new optional payload field) requires updating the schema first and shipping it to both stacks before the sender emits it. Consumers at runtime may still choose to tolerate unknown fields for forward compatibility; that is a client decision, not a schema allowance.

Other rules encoded: `v` is the integer `1`; `seq` is a decimal string (int64, `^(0|[1-9][0-9]{0,18})$`) for `session.state`, `transcript.final`, `transcript.correction`, `gap` and exactly `null` for the six ephemeral events; `speaker` is optional everywhere and never in any `required` list (the M2 server must not emit it: C-20; a test asserts it is never required); `windowMinutes` is 2, 5 or 10 only; the audio socket control schema allows only `pause` and `stop` (`resume` is REST only, C-15).

## error.detail vocabulary

`error.code` is limited to the ProblemDetails table (`docs/architecture/backend-foundation.md` section 5): `validation_failed`, `unauthenticated`, `consent_required`, `csrf_failed`, `not_found`, `invalid_state`, `concurrency_conflict`, `precondition_required`, `precondition_failed`, `rate_limited`, `provider_unavailable`. No other codes (note `forbidden`, `payload_too_large` etc. exist in `ApiProblem.cs` for REST but are not in the contract table and are not allowed on the hub/socket).

`detail` is a string, 1..200 chars, no control characters (no newlines, so no stack traces). The schema cannot enforce a closed vocabulary because the contract has not fixed the strings. Rule for the implementation (C-24): `detail` is the constant server `title` for its `code`, never exception text, input echo or model output. Strings already fixed in `src/api/src/Tether.SharedKernel/Http/ApiProblem.cs`:

| code | detail |
|---|---|
| `unauthenticated` | Please sign in. |
| `csrf_failed` | Request origin was not accepted. |
| `validation_failed` | The request was invalid. |
| `precondition_required` | An If-Match header is required. |
| `precondition_failed` | The resource has changed. Reload and try again. |
| `concurrency_conflict` | The resource was changed by another request. |
| `rate_limited` | Too many requests. Please slow down. |
| `not_found` | Resource not found. |
| `consent_required`, `invalid_state`, `provider_unavailable` | not yet defined in code; backend must pick fixed strings and add them here |

The socket `error` frame carries `code` only (no `detail`), per section 4.

## Running the contract tests

Validator: `JsonSchema.Net` **7.3.4** (test project only, `Tether.ContractTests`). Chosen because it is the maintained, MIT-licensed .NET implementation with full draft 2020-12 support and a schema registry for cross-file `$ref`; it has no native dependencies and does not touch production assemblies.

```
# via the repo's dev compose (mounts the whole repo at /repo, so src/shared is found automatically)
docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test src/api/tests/Tether.ContractTests

# or the whole solution (architecture + contract + integration)
docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test src/api/Tether.slnx

# standalone mounts; point the tests at the contracts directory explicitly
MSYS_NO_PATHCONV=1 docker run --rm -v "$PWD/src/api:/src" -v "$PWD/src/shared:/shared" \
  -e TETHER_CONTRACTS_DIR=/shared/contracts -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
  dotnet test tests/Tether.ContractTests
```

The test locates the contracts by `TETHER_CONTRACTS_DIR`, else by walking up from the test binary to `src/shared/contracts`.

The frontend should validate the same fixtures with a draft 2020-12 validator (for example Ajv 2020 with every file in `lecture.v1/` added via `addSchema`).

## Contract ambiguities resolved here (need Lead Architect confirmation)

1. Event payload is nested under `payload`; the contract only says "Payload" next to an envelope.
2. Socket control frames use `type` as discriminator (`hello`, `ack`, `gap`, `error`, `pause`, `stop`); section 4 names the frames but not the field.
3. `lastSeq`, `currentSeq`, `replay.fromSeq/toSeq`, `basedOnSeq`, `Session.lastSeq` are all strings (C-10 says seq is serialized as a string); `Join` previously said `long`. `replay.fromSeq/toSeq` are `null` when nothing is replayed; `currentSummaryRevision` is `null` before the first summary.
4. `seq` may be `0` as a value (start value not specified).
5. Ids (`sessionId`, `courseId`, `catchUpId`) are opaque strings `[A-Za-z0-9_-]{1,64}`; the format is not specified. `ts`/`startedAt`/`expiresAt` are RFC 3339 with offset.
6. Length limits (`text` 4000, `tldr` 2000, up to 20 bullets of 500, 50 citations, `title` 200, ticket 16..512 url-safe chars) are proposals, not contract values.
7. `Session.title` and `stoppedAt` may be `null` (or absent for `stoppedAt`) since the DTO shows `title?` on create and `stoppedAt?`.
8. `session.state.reason` is a free string (1..100); no vocabulary is defined.
9. `transcript.correction.supersedes` is validated as an integer revision, not constrained to be less than `revision` (JSON Schema cannot express it; add a server test). Likewise `toMs >= fromMs`, `endMs >= startMs`.
10. `error.detail` vocabulary cannot be schema-closed (see above).
11. The REST `CatchUp` DTO is assumed identical to the `catchup.ready` payload; `SummaryRevision` identical to the `summary.revision` payload.
12. Socket `error` frame has `code` only; `ack.frameSeq` and `resumeFromFrame` are uint32.

## syllabus.v1 (Phase 2 slice 1, DRAFT, not frozen)

`syllabus.v1/` holds the DRAFT schemas for `docs/API_CONTRACTS_P2.md`; `lecture.v1` stays frozen and is untouched. Every syllabus schema basename starts with `syllabus` so fixtures stay flat in `fixtures/valid` and `fixtures/invalid` with the same `{schema}--{case}.json` naming; `ContractFixtureTests` registers both folders. Extra semantic checks (item key derivation, snippet offsets, approval invariants, doc/schema drift) live in `SyllabusContractTests`. Same strictness policy as above (`additionalProperties: false` everywhere). Same test commands.
