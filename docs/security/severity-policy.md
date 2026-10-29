# Security finding severity policy

Set by the Product Owner on 2026-10-29. Applies to every security finding from any agent or review.

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
