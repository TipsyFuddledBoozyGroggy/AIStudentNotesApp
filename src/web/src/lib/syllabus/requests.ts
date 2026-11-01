import type { BatchAction, CreateFromTextInput, CreateMeta, Decision, ItemCreate, ItemPatch } from "./types";

// Request-body builders for syllabus.v1. Each returns exactly the members the request schema allows
// (additionalProperties: false; read-only fields are never sent). syllabus.schemas.test.ts validates every builder's
// output against the real schemas with ajv.

/** Placeholder notice version (P2-D5: wording owned by the Product Owner and counsel; fixture value). */
export const NOTICE_VERSION = "draft-2026-10";

/** The `metadata` part of a multipart create (syllabus-create-request without `text`; at most 4 KB). */
export function createMetadataBody(input: CreateMeta): Record<string, unknown> {
  // Term dates are both or neither (contract Lead decision 61); the form enforces it, the schema and server reject one alone.
  return {
    ...(input.title ? { title: input.title } : {}),
    ...(input.termStart ? { termStart: input.termStart } : {}),
    ...(input.termEnd ? { termEnd: input.termEnd } : {}),
    defaultTimezone: input.defaultTimezone,
    noticeVersion: input.noticeVersion,
    attestation: { accepted: true, acceptedAt: input.acceptedAt },
  };
}

export function createFromTextBody(input: CreateFromTextInput): Record<string, unknown> {
  return { text: input.text, ...createMetadataBody(input) };
}

export function decisionBody(decision: Decision, acknowledgeCheck?: boolean): Record<string, unknown> {
  return acknowledgeCheck ? { decision, acknowledgeCheck: true } : { decision };
}

/** Merge-patch: only defined members are sent; a member set to null is sent as null (clears a nullable field). */
export function patchBody(patch: ItemPatch): Record<string, unknown> {
  const out: Record<string, unknown> = {};
  for (const [k, v] of Object.entries(patch)) if (v !== undefined) out[k] = v;
  if (Object.keys(out).length === 0) throw new Error("A patch needs at least one member (minProperties: 1).");
  return out;
}

/**
 * Student-added item (syllabus-item-create-request). Exactly kind, title, date, allDay and time: no timezone (the server
 * uses the syllabus default), no notes, and never a link to the unclaimed date it came from (the schema has none).
 */
export function itemCreateBody(input: ItemCreate): Record<string, unknown> {
  return { kind: input.kind, title: input.title, date: input.date, allDay: input.allDay, time: input.allDay ? null : input.time };
}

/** Batch decision body. `itemEtags` is an object keyed by item id (contract 6.1), so a duplicate id cannot be expressed. */
export function batchDecisionBody(action: BatchAction, itemEtags: ReadonlyMap<string, string>): Record<string, unknown> {
  if (itemEtags.size < 1 || itemEtags.size > 100) throw new Error("A batch names 1 to 100 items.");
  return { action, itemEtags: Object.fromEntries(itemEtags) };
}

/**
 * Mock calendar sync body (contract 6.3): `itemRevisions` keyed by item id (the revision the student reviewed), explicit
 * demo confirmation. There is no reminder member in v1 (contract row 13).
 */
export function calendarSyncBody(itemRevisions: ReadonlyMap<string, number>): Record<string, unknown> {
  if (itemRevisions.size < 1 || itemRevisions.size > 100) throw new Error("A sync names 1 to 100 items.");
  return { confirm: { provider: "mock", acknowledgedDemo: true }, itemRevisions: Object.fromEntries(itemRevisions) };
}

/** calendar-sync/remove body: unique item ids, 1 to 100. */
export function calendarRemoveBody(itemIds: readonly string[]): Record<string, unknown> {
  const unique = [...new Set(itemIds)];
  if (unique.length < 1 || unique.length > 100) throw new Error("A removal names 1 to 100 items.");
  return { itemIds: unique };
}

const KEY_RE = /^[A-Za-z0-9_-]{8,128}(?![\s\S])/;

/** A fresh Idempotency-Key (syllabus-common idempotencyKey: 8-128 URL-safe characters). */
export function newIdempotencyKey(): string {
  const c = globalThis.crypto;
  const key = typeof c?.randomUUID === "function" ? c.randomUUID() : `k${Date.now().toString(36)}${Math.random().toString(36).slice(2, 12)}`;
  if (!KEY_RE.test(key)) throw new Error("Generated idempotency key does not match the contract format.");
  return key;
}

export const isIdempotencyKey = (k: string) => KEY_RE.test(k);
