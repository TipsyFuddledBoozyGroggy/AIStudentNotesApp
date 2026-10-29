# Product backlog

Items added by the Product Owner. Status: `BACKLOG` (not started), `DESIGN` (spec in progress), `READY` (spec done, awaiting build), `BUILDING`, `DONE`. Design and build never touch consent, recording or Demo-source wording (gate G1).

| ID | Item | Added | Status | Spec |
|---|---|---|---|---|
| BL-001 | **Themes.** Add selectable visual themes to the front end: **Canvas** (the look of a student LMS portal), **Gamer** (very high tech), **Calm**, and **Glass** (Apple-style translucent glass). Every theme has a light and a dark mode. Themes are chosen in Preferences. Everything on every page must line up properly (consistent grid, alignment and spacing). The UX agent must follow the latest design best practices. | 2026-10-29 | DESIGN | `docs/ux/themes-spec.md` |

## BL-001 notes
- Themes sit on top of the existing design tokens (`docs/ux/design-direction.md`: Paper, Night desk, Fog, Dusk). The existing low-stimulation looks must stay available.
- Each theme must meet WCAG 2.2 AA in both modes, honour `prefers-reduced-motion`, `prefers-contrast` and `prefers-reduced-transparency`, and never lower contrast on lecture text.
- A theme is visual only: no streaks, points, levels or pressure mechanics, even in Gamer.
- Branding: take inspiration from the general feel of an LMS portal. Do not copy a vendor's logo, name, marks or proprietary assets.
