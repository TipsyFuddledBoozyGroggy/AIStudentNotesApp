# docs.export.v1 (DRAFT, NOT frozen, mock only)

JSON Schema (draft 2020-12) for `docs/API_CONTRACTS_DOCS_EXPORT.md`: preview and confirm of an export of Ready lecture notes to a "Demo Doc" held in our own database. No real Google call, token, OAuth or network is expressible: `provider` is the constant `demo_docs`. `lecture.v1`, `syllabus.v1` and `calendar.v1` are untouched. If this folder and the document disagree, the document wins and this is a bug.

```
*.schema.json                 8 schemas, $id https://schemas.tether.invalid/docs.export.v1/{name}.schema.json
docs-common.schema.json       shared $defs (id, timestamp, idempotencyKey are the same patterns as syllabus-common)
fixtures/valid/               must validate    {schema}--{case}.json   (21)
fixtures/invalid/             must be rejected {schema}--{case}.json   (49, each one mutation of a valid fixture)
```

Same strictness as the other contracts: `additionalProperties: false` on every object, end anchor `(?![\s\S])`. Cross-file `$ref`s are relative; register every file in this folder before use. Fixtures live here, not in `../fixtures/`, so `ContractFixtureTests` (which registers only lecture.v1 and syllabus.v1) is not affected; add a `DocsExportContractTests` file like `CalendarContractTests`.

Semantic rules JSON Schema cannot express (digest recomputation, mode versus existing document, block cap, TextPolicy, error-code table equals schema) are listed in section 9 of the contract document and are not yet tested.

Checked 2026-11-06 with ajv 2020 (strict off; `date-time` format not checked, patterns are): 21 valid pass, 49 invalid rejected, after the security-review and backend-feedback changes (hyphenated dxp_/ddc_ ids, unsupported_media_type). No .NET test has been run.
