# Security and legal finding severity policy

Set by the Product Owner on 2026-10-29. Applies to every security finding AND every legal, privacy and compliance concern from any agent or review (extended to legal concerns by the Product Owner on 2026-10-29). Legal concerns live in `docs/legal/legal-register.md`; security findings in `docs/security/findings-register.md`. The same gates and the same commit protections apply to both.

| Severity | What the Lead and agents must do |
|---|---|
| **CRITICAL** | Bring it to the Product Owner's attention **immediately**: stop what is running and report it in the same message, before any other work or summary. |
| **HIGH** | Must be **resolved before anything is pushed to git**. Do not push, and do not offer to push, while a HIGH is open. Say so when asked to push. |
| **MEDIUM** | Must be **resolved before production**. Does not block local work or pushing to git, but blocks any production deployment or go/no-go. |
| **LOW** | **Noted** in the findings register (`docs/security/findings-register.md`). No deadline. |

## How agents apply it
- Every finding carries exactly one of CRITICAL / HIGH / MEDIUM / LOW. `Info` items are observations, not findings, and are listed separately.
- A reviewer who is unsure between two levels picks the higher one and says why.
- A fix is only "resolved" when a test or reviewer evidence shows it, and the register links that evidence. A claimed fix without evidence stays open.
- A finding may be downgraded only with a written reason, by the Product Owner or with their approval.
- The Lead checks the register before every push (no open HIGH or CRITICAL) and before any production go/no-go (no open MEDIUM, HIGH or CRITICAL), and says the result plainly.
- This policy does not clear any release gate in `docs/RELEASE_GATES.md`.

## Applying it to legal concerns
- Same four levels and same rules: CRITICAL goes to the Product Owner immediately; **HIGH blocks any push to git**; MEDIUM must be resolved before production or real users; LOW is noted.
- Meaning of HIGH for legal: a legal or privacy exposure that already exists in what would be pushed, or that would harm real people if the pushed code, data or documents were used as-is (for example real personal data, third-party copyrighted material, or a design the PO has already ruled needs counsel first). MEDIUM: needed before real students, real recordings, real documents, real vendors or production. LOW: housekeeping and watch items.
- A legal item is **resolved only with written evidence** from an accountable human (qualified counsel, campus official or the Product Owner as decision-maker), named and dated. An AI summary, checklist or draft never resolves a legal item and never clears a release gate.
- Ratings by an AI agent are PROVISIONAL until counsel confirms; when unsure pick the higher level. Only the Product Owner can downgrade, in writing.
- Before every push the Lead checks BOTH registers for open CRITICAL or HIGH and says the result plainly; before production, MEDIUM too.
