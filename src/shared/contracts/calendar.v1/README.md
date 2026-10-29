# calendar.v1 (DRAFT, NOT frozen)

JSON Schema (draft 2020-12) for `docs/API_CONTRACTS_CALENDAR.md`: the schedule/calendar feature and the Google sign-in / data-connect API surface. Mock and fake-Google only; no real Google call has ever been made with this contract. `lecture.v1` and `syllabus.v1` are untouched. If this folder and the document disagree, the document wins and this is a bug.

```
*.schema.json                  26 schemas, all prefixed "calendar", $id https://schemas.tether.invalid/calendar.v1/{name}.schema.json
calendar-common.schema.json    shared $defs; id, itemKey, timestamp, local date/time/zone, title, textLine, flag, syncState,
                               idempotencyKey ... are byte-identical to syllabus.v1 (a test enforces it)
calendar-policy.json           machine-readable DRAFT policy the tests execute (error classification, limits, retention, digest, body allowlist); values marked PROPOSAL
fixtures/valid/                must validate    {schema}--{case}.json
fixtures/invalid/              must be rejected {schema}--{case}.json (each is a single mutation of a valid fixture)
```

Fixtures are kept here, not in `../fixtures/`, because `ContractFixtureTests` registers only `lecture.v1` and `syllabus.v1`. Same strictness policy as the other contracts: `additionalProperties: false` on every object. Cross-file `$ref`s are relative; register every file in this folder before use.

Semantic checks JSON Schema cannot express (scope and URL allowlists, event-id vector, reminder caps, run state/count consistency, doc/schema drift) are in `src/api/tests/Tether.ContractTests/CalendarContractTests.cs`.

```
docker compose -f docker-compose.yml -f docker-compose.dev.yml run --rm dotnet dotnet test src/api/tests/Tether.ContractTests
```
