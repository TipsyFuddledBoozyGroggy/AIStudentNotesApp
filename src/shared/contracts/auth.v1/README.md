# auth.v1 (Google sign-in, invite-only)

JSON Schema (draft 2020-12) for `docs/API_CONTRACTS_AUTH.md` (ADR-022). **Frozen** for the go-live build. Additive changes (a new
`purpose` or `landing` value) change the document and these schemas first. Where this folder and the document disagree, the
document wins and this is a bug.

```
auth-common.schema.json               shared $defs (timestamp and authorizationUrl copied from calendar.v1, equality-tested)
auth-methods-response.schema.json     GET  /api/v1/auth/methods
auth-signin-start-request.schema.json POST /api/v1/auth/google/signin/start (body)
auth-start-response.schema.json       POST /api/v1/auth/google/signin/start (200)
auth-callback-outcome.schema.json     GET  /api/v1/auth/google/callback, query of the 302 Location
fixtures/valid/  fixtures/invalid/    {schema}--{case}.json
```

Every object has `additionalProperties: false`. Tests: `src/api/tests/Tether.ContractTests/AuthContractTests.cs`.
