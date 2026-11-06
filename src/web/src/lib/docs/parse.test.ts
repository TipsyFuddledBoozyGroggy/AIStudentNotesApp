import { describe, expect, it } from "vitest";
import { parseDemoDoc, parseDocRef, parseExportRecord, parsePreview, parseProblem, parseState } from "./parse";
import { confirmBody, newIdempotencyKey, parseConfirmBody, parsePreviewRequest, previewBody } from "./requests";
import { loadDocsFixtures, loadDocsValidators } from "./schemas.node";
import type { DocsPreview } from "./types";

// DX-F01: the strict client parsers accept every valid docs.export.v1 fixture and reject every invalid one, read straight
// from src/shared/contracts/docs.export.v1/fixtures (READ-ONLY here). The request builders are checked against the real
// schemas with ajv, so a body the client sends is the body the contract describes.

const valid = loadDocsFixtures("valid");
const invalid = loadDocsFixtures("invalid");
const ajv = loadDocsValidators();

const P: Record<string, (v: unknown) => unknown> = {
  "docs-preview": parsePreview,
  "docs-export-record": parseExportRecord,
  "docs-state": parseState,
  "docs-demo-doc": parseDemoDoc,
  "docs-problem": parseProblem,
  "docs-export-request": parseConfirmBody,
  "docs-preview-request": parsePreviewRequest,
};
const fx = (file: string) => {
  const f = valid.find((x) => x.file === file) ?? invalid.find((x) => x.file === file);
  if (!f) throw new Error(`no fixture ${file}`);
  return structuredClone(f.json) as Record<string, unknown>;
};

describe("DX-F01 docs.export.v1 fixtures through the client parsers", () => {
  it("has the 21 valid and 49 invalid fixtures, each for a schema the client parses", () => {
    expect(valid.length).toBe(21);
    expect(invalid.length).toBe(49);
    for (const f of [...valid, ...invalid]) expect([f.file, f.schema in P]).toEqual([f.file, true]);
  });

  it("the fixtures agree with ajv over the real schemas (the parsers are compared with the contract, not a copy)", () => {
    for (const f of valid) expect([f.file, ajv.byName(f.schema)(f.json)]).toEqual([f.file, true]);
    for (const f of invalid) expect([f.file, ajv.byName(f.schema)(f.json)]).toEqual([f.file, false]);
  });

  it.each(valid.map((f) => [f.file, f] as const))("accepts valid %s", (_n, f) => {
    expect(P[f.schema]!(f.json)).not.toBeNull();
  });

  it.each(invalid.map((f) => [f.file, f] as const))("rejects invalid %s", (_n, f) => {
    expect(P[f.schema]!(f.json)).toBeNull();
  });

  it("refuses an unknown member at every level (never dropped silently)", () => {
    const p = fx("docs-preview--create.json");
    expect(parsePreview({ ...p, accessToken: "x" })).toBeNull();
    expect(parsePreview({ ...p, destination: { ...(p.destination as object), url: "x" } })).toBeNull();
    const r = fx("docs-export-record--created.json");
    expect(parseExportRecord({ ...r, retry: { ...(r.retry as object), extra: 1 } })).toBeNull();
    const d = fx("docs-demo-doc--basic.json");
    const doc = d.document as { title: string; blocks: object[] };
    expect(parseDemoDoc({ ...d, document: { ...doc, blocks: [{ ...doc.blocks[0], html: "<b>x</b>" }] } })).toBeNull();
  });

  it("refuses the external docRef branch in v1, even when ajv would accept it", () => {
    const r = fx("docs-export-record--created.json");
    const ext = { ...(r.docRef as object), label: "Google Docs", openKind: "external", openPath: null, openUrl: "https://docs.google.com/document/d/abcdefghijk12/edit" };
    expect(ajv.byName("docs-export-record")({ ...r, docRef: ext })).toBe(true);
    expect(parseExportRecord({ ...r, docRef: ext })).toBeNull();
    expect(parseDocRef(ext)).toBeNull();
  });

  it("refuses an openPath that names a different document than docId", () => {
    const ref = fx("docs-export-record--created.json").docRef as { docId: string; openPath: string };
    const other = "/demo-docs/ddc_00000000-0000-4000-8000-000000000000";
    expect(other).not.toBe(ref.openPath);
    expect(parseDocRef({ ...ref, openPath: other })).toBeNull();
  });

  it("refuses a body about another session or another document than the one asked for", () => {
    const p = fx("docs-preview--create.json");
    expect(parsePreview(p, p.sessionId as string)).not.toBeNull();
    expect(parsePreview(p, "00000000-0000-4000-8000-000000000000")).toBeNull();
    const d = fx("docs-demo-doc--basic.json");
    expect(parseDemoDoc(d, "ddc_00000000-0000-4000-8000-000000000000")).toBeNull();
  });

  it("refuses a preview whose actions contradict canWrite or the existing document", () => {
    const p = fx("docs-preview--create.json");
    expect(parsePreview({ ...p, actions: ["update"] })).toBeNull();
    expect(parsePreview({ ...p, canWrite: false })).toBeNull();
    const ex = fx("docs-preview--existing-update.json");
    expect(parsePreview({ ...ex, actions: ["create"] })).toBeNull();
  });

  it("counts title and block length in code points (an astral character counts once)", () => {
    const d = fx("docs-demo-doc--basic.json");
    const doc = d.document as { title: string; blocks: unknown[] };
    expect(parseDemoDoc({ ...d, document: { ...doc, title: "\u{1F600}".repeat(200) } })).not.toBeNull();
    expect(parseDemoDoc({ ...d, document: { ...doc, title: "\u{1F600}".repeat(201) } })).toBeNull();
  });
});

describe("DX-F03 request builders produce schema-valid bodies with no text, title or document", () => {
  const create = parsePreview(fx("docs-preview--create.json")) as DocsPreview;
  const update = parsePreview(fx("docs-preview--existing-update.json")) as DocsPreview;

  it("preview body is exactly {notesId}", () => {
    expect(previewBody(create.notesId)).toEqual({ notesId: create.notesId });
    expect(ajv.byName("docs-preview-request")(previewBody(create.notesId))).toBe(true);
    expect(() => previewBody("nt_x")).toThrow();
  });

  it("create sends null expectations; update and create_another send the reviewed document's id and version", () => {
    const c = confirmBody(create, "create");
    expect(c).toEqual({
      notesId: create.notesId,
      contentDigest: create.contentDigest,
      mode: "create",
      expectedExistingVersion: null,
      expectedExistingDocId: null,
      confirm: { provider: "demo_docs", acknowledgedDemo: true },
    });
    for (const mode of ["update", "create_another"] as const) {
      const b = confirmBody(update, mode);
      expect(b.expectedExistingDocId).toBe(update.existing!.docRef.docId);
      expect(b.expectedExistingVersion).toBe(update.existing!.docRef.version);
      expect(ajv.byName("docs-export-request")(b)).toBe(true);
      expect(parseConfirmBody(b)).toEqual(b);
    }
    expect(ajv.byName("docs-export-request")(c)).toBe(true);
    const json = JSON.stringify(confirmBody(update, "update"));
    expect(json).not.toContain(update.document.title);
    expect(json).not.toMatch(/"(title|blocks|text|document)"/);
  });

  it("idempotency keys match the header schema", () => {
    const v = (k: string) => /^[A-Za-z0-9_-]{8,128}(?![\s\S])/.test(k);
    for (let i = 0; i < 20; i++) expect(v(newIdempotencyKey())).toBe(true);
  });
});
