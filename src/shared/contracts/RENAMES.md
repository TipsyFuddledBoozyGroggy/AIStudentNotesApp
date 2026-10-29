# Frozen wire-name renames

## 2026-10-29 - "I lost track" -> "Quick recap"

Reason: PO sensitive-language rule (neurodivergent users). Clean rename; nothing was deployed and no real data existed, so there is no backward-compatible alias.

| Old | New |
|---|---|
| `POST /api/v1/sessions/{id}/lost-track` | `POST /api/v1/sessions/{id}/quick-recap` |
| `lecture.v1/lost-track-request.schema.json` (`$id`, title `lost-track-request`) | `lecture.v1/quick-recap-request.schema.json` (title `quick-recap-request`) |
| prompt version `lost-track.v1` | `quick-recap.v1` |
| persisted `ai.ai_usage.workflow` value `LostTrack` (and its CHECK constraint) | `QuickRecap` (migration `A004_RenameLostTrackToQuickRecap`) |
| C# `AiWorkflow.LostTrack`, `LostTrack*` types/methods | `AiWorkflow.QuickRecap`, `QuickRecap*` |

The `catchup` / `catch-up` resource names are unchanged.
