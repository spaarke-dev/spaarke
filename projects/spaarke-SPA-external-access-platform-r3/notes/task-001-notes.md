# Task 001 — ADR-028 Amendment A8

**Status:** done, 2026-10-10. Main session (opus), FULL rigor.

## Owner approval
- 2026-10-10. The owner read the summary of the five parts (why, and alternatives for each) and replied:
  > "the ADR modifications are approved. follow your recommendations for how to proceed. continue"
- **Scope:** all five parts:
  - A2 workforce client;
  - the `user_impersonation` scope;
  - A3 default modules;
  - the A1 CIAM scoping;
  - the self-registration rule.
- **The split** with the auth project's §12b also follows the recommendation: two separate amendments.

## What was written
- **`.claude/adr/ADR-028-spaarke-auth-architecture.md`:**
  - a new section, `## Amendment A8 (2026-10-10)`, after A6, taken verbatim from `notes/drafts/adr-028-a7-draft.md` (title shortened, date filled in);
  - one-line `⚠️` pointer markers under the rules at :36, :52, :56, :71 and :125.
  - **The diff only adds lines. No existing rule text was edited** (acceptance criterion 3).
- **`.claude/adr/INDEX.md`:** the ADR-028 row now says "amended A1–A8" and summarises A8.
- **`notes/coordination/2026-10-10-adr-028-amendment.md`:** names which amendment carries which items (acceptance criterion 4). The §12b items stay with `spaarke-auth-system-of-record-r1`.

## Acceptance criteria
1. A dated amendment covering A2, A3, A1 (:52 and :56) and the self-registration rule, written as MUST / MUST NOT: **met**.
2. The owner's approval recorded with date and quote: **met** (above).
3. No other ADR-028 rule text changed: **met**. Additions only.
4. The coordination note names which project's amendment carries §12b: **met**.

## Notes
- `.claude/patterns/auth/oauth-scopes.md:20` (`access_as_user`) is still correct for the internal surfaces. Its update for the SPA and Teams belongs to the auth project's pattern sweep, as recorded in the coordination note.
- The full `docs/adr/ADR-028-*.md` still does not exist; A8 is concise-only, like A1–A6.
