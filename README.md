# Tether

An ADHD-friendly college academic assistant: consented live lecture transcription, a continuously updated TL;DR, one-click **Quick recap** catch-up, contextual tutoring, editable study guides, and student-approved Google Docs/Calendar workflows.

Status: pre-implementation. This repository currently contains project instructions and Claude Code specialist agents only. There is no application code yet.

## Layout
- `CLAUDE.md` — project instructions, architecture invariants, MVP definition and agent routing. Loaded automatically by Claude Code.
- `.claude/agents/` — 12 project-level specialist subagents (one Markdown file each, with YAML frontmatter).

## Working with Claude Code
1. Launch Claude Code from the repository root (the folder containing `CLAUDE.md` and `.claude/`).
2. Start with: *"Read CLAUDE.md, inspect the project agents, and propose the first MVP vertical slice with tests and agent ownership. Do not deploy or make paid API calls."*
3. Review decisions (Google scopes, transcription vendor, recording consent, hosting) before connecting any real APIs.

## Agent tool access
Agents are limited by the `tools` field in their frontmatter:

| Agent | Tools |
|---|---|
| `product-manager`, `adhd-ux-specialist`, `learning-science` | Read, Grep, Glob, Write, Edit (docs and specs) |
| `security-engineer`, `qa-engineer` | Read, Grep, Glob, Write, Edit, Bash (reviews, tests, scans) |
| All other agents | Inherit all tools |

Tool lists cannot be scoped to paths, so staying within owned files (for example, reviewers not editing application source) relies on each agent's written instructions. Review diffs before accepting changes.

## Notes
- Subagents do not coordinate automatically; the main Claude Code session delegates, integrates and reports.
- Custom subagent reference: https://code.claude.com/docs/en/sub-agents
