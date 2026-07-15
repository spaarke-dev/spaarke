# Deferrals & Issues — spaarke-daily-update-service-r5

> Source of truth for this project's deferred work. Each entry is mirrored to a GitHub Issue on portfolio board #2 (Spaarke Core). Filed at project close (2026-07-10) via `/project-defer-issue-tracking`.

## Deferrals

### DEF-001 — Monitored-For schema (D-3)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-10 |
| **Source** | R5 design.md / spec.md — explicitly scoped OUT ("Deferred: Monitored-For schema (D-3)"); project CLAUDE.md "Deferred (do NOT build)" |
| **GitHub Issue** | [#650](https://github.com/spaarke-dev/spaarke/issues/650) |

**Description**

R5 keeps the briefing's "monitored" surface driven by the existing binary `sprk_monitor` flag. The intended replacement is a richer **Monitored-For** schema — new `sprk_monitorreason` (why a record is monitored) + `sprk_monitornotes` (free-text context) columns on the monitored entities — so the High Priority / monitored surface can explain *why* a record is flagged rather than just *that* it is. R5 deferred this to keep scope bounded; the binary flag remains the contract until the schema lands.

**Concrete failure mode without it**: the briefing can show a monitored record but cannot tell the attorney *why* it's being monitored — the reason lives only in someone's head, so a monitored record is indistinguishable from noise when the flag-setter is unavailable.

**Entry-points**

- Data model: add `sprk_monitorreason` (Choice or Text) + `sprk_monitornotes` (Multiline Text) to the monitored entities (`sprk_matter`, `sprk_event`, etc. — confirm target set during design).
- Collector consumption: `src/server/api/Sprk.Bff.Api/Services/Ai/Narrators/DailyBriefingCollector.cs` — the `QueryHighPriority*` path (currently keys on `sprk_monitor`/`sprk_highpriority` booleans).
- Widget surface: `HighPrioritySection.tsx` in `@spaarke/daily-briefing-components`.

**Suggested fix**: schema-first — add the two columns via `dataverse-create-schema`, seed reason option-set, then thread `reason`/`notes` through collector → DTO → HighPrioritySection. A future-round project (R6+).

**Estimated effort**: 1–2 days (schema + collector + widget + tests)
**Blockers**: none
**Related**: R5 design.md v0.2 D-3

---

### DEF-002 — EventDetailSidePane `@odata.bind` casing fix (D-5)

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | someday |
| **Filed** | 2026-07-10 |
| **Source** | R5 odata-bind-audit (`notes/odata-bind-audit.md`) — flagged one-liner; deferred because the side-pane is not in use |
| **GitHub Issue** | [#651](https://github.com/spaarke-dev/spaarke/issues/651) |

**Description**

The R5 `@odata.bind` audit found a lookup-binding casing issue in the EventDetailSidePane / `TodoSection.tsx` write path (a navigation-property `@odata.bind` whose casing/target doesn't match the metadata, of the same class the audit fixed elsewhere). It was deferred rather than fixed because **the EventDetailSidePane surface is not currently in use** — no runtime path exercises it, so the defect is latent.

**Concrete failure mode without it**: if/when the EventDetailSidePane is re-enabled, the affected create/update call will 400 (invalid `@odata.bind` target) exactly like the other casing bugs the audit fixed — a regression that will look mysterious because the code "was always there."

**Entry-points**

- `TodoSection.tsx` (EventDetailSidePane consumer) — locate the `@odata.bind` write; cross-reference the fix pattern in `projects/spaarke-daily-update-service-r5/notes/odata-bind-audit.md`.
- Fix pattern: use the metadata-verified navigation-property name + `cleanGuid` (see FAILURE-MODES.md AP-6, `@spaarke/ui-components` `cleanGuid` export).

**Suggested fix**: apply the same metadata-verified `@odata.bind` correction the audit applied to the in-use call sites; add/enable a test only if the side-pane is being reactivated.

**Estimated effort**: <1 hour (one-liner) — do it as part of whatever reactivates the side-pane.
**Blockers**: not worth doing until the EventDetailSidePane is actually reactivated.
**Related**: `notes/odata-bind-audit.md`; FAILURE-MODES.md AP-6 (braced GUIDs in `@odata.bind`)

---

### DEF-003 — Email Briefing typed-note passthrough

| Field | Value |
|---|---|
| **Status** | Open |
| **Urgency** | next-round |
| **Filed** | 2026-07-10 |
| **Source** | Operator UAT 2026-07-09/10 (email-share feature) — flagged fast-follow; operator elected to defer at project close |
| **GitHub Issue** | [#652](https://github.com/spaarke-dev/spaarke/issues/652) |

**Description**

The r5 "Email Briefing" colleague-share feature reuses the shared `SendEmailDialog`, which exposes a **Message** field. Today that field is **cosmetic** for the briefing-share path: the server (`/api/ai/daily-briefing/email` → Communication service) owns and composes the full HTML briefing body, so a personal note the sender types in the dialog is **not** included in the sent email. The per-item email path (`mode:'item'`) DOES carry the typed body (client-composed activity), so the inconsistency is specific to the whole-briefing share.

**Concrete failure mode without it**: a user types "Hi Sarah, take a look at the Acme matter" into the Email Briefing dialog, hits send, and the recipient receives the briefing with **no personal note** — the typed text is silently dropped, which reads as a bug ("did my message not send?").

**Entry-points**

- Client: `src/client/shared/Spaarke.DailyBriefing.Components/src/components/DailyBriefingApp.tsx` — `handleEmailSend` (mode `'briefing'`) calls `emailBriefingToColleague(recipientEmail)`; the `payload` (which carries the typed body) is not forwarded.
- Service: `src/client/shared/Spaarke.DailyBriefing.Components/src/services/briefingService.ts` — `emailBriefingToColleague(recipientEmail)` → extend to accept an optional `personalNote`.
- Server: `src/server/api/Sprk.Bff.Api/Api/Ai/DailyBriefingEndpoints.cs` — `HandleEmail` / `EmailDailyBriefingRequest` — add an optional `personalNote` and have the Communication service prepend it above the composed HTML (sanitize/encode it — it's user text).

**Suggested fix**: thread `personalNote` (optional) client → `/email` request → prepend a sanitized `<p>` block above the server-composed briefing HTML. Keep server ownership of the briefing body; the note is an additive prefix only.

**Estimated effort**: ~2–3 hours (client passthrough + server field + HTML-encode + one contract test)
**Blockers**: none. NOTE — BFF change: it only lands durably on dev via a master merge (see [[bff-dev-continuous-deploy-from-master]]).
**Related**: `notes/email-share-feature-plan.md`; DailyBriefingEmailEndpointContractTests.cs (add a note-passthrough case)

---
