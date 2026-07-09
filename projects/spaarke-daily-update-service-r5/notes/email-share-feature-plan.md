# Email-Share Feature — Design & Plan (Email Briefing + Email Item)

> **Added**: 2026-07-09 · **Operator decision**: **A + system-sender + build-now** (2026-07-09)
> **Scope**: New feature on top of r5's accuracy/appearance/hardening charter. Reuses existing components (CLAUDE.md §11). Batched into the pending r5 deploy.
> **Rigor**: FULL (touches `.cs` BFF + `.tsx` shared lib) + TEST-MODIFYING (adds tests). Step 9.5 gates (code-review + adr-check) at the end.

## User request

> "How do I share this with a colleague?" — (#2) email the **whole briefing**, and (#3) email an **individual item**. Operator suggested reusing the email wizard shared component.

## Locked design

### #2 Email Briefing (share the whole briefing) — **Option A: server sends its existing HTML**
- **Trigger**: new `onEmailBriefing?` callback on `DigestHeader` → adds an **"Email Briefing"** item to the existing ⋮ overflow menu (mirrors the `onBrowsePlaybooks` prop pattern already there). Menu only renders the item when the host wires the callback (back-compat).
- **UI**: `DailyBriefingApp` hosts a Fluent v9 `Dialog` containing the shared **`SendEmailStep`** — recipient picker (LookupField → systemuser search, constrains to internal colleagues) + subject prefilled `"Daily Briefing — {date}"`. Body field repurposed as an **optional personal note** (see server note below).
- **Send**: `POST /api/ai/daily-briefing/email` with `{ recipientEmail }`. Server **collects the caller's own briefing, renders its existing HTML, and delivers via the existing Communication leg** (`DailyBriefingCompositeService.EmailAsync`, unchanged) to the colleague. `systemUserId` stays the caller ⇒ colleague receives the *caller's* briefing = "share my briefing." **system-sender** = the existing Communication delivery path (no new user-mailbox OBO send).
- **BFF change (minimal, in-lane)**: `HandleEmail` currently hardcodes recipient = caller's claim. Change: read an **optional `recipientEmail`** from a request body; if present + valid email → use it; else fall back to the caller's claim (preserves the scheduled-trigger + backward compat). `EmailAsync` already takes `recipientEmail` — no composite/OutputRouter change.
  - **MVP decision on the note/subject**: to avoid touching the OutputRouter/Communication template composition (r2-core-adjacent surface), **MVP passes `recipientEmail` only**; subject + body are display-only in the dialog for context. Prepending a personal note / subject override is a documented **fast-follow** (needs a small, careful HTML-composition change). Keeps the server delta to one optional field.

### #3 Email Item (share one item) — **client-composed draft email activity**
- **Trigger**: new `onEmailItem?(item)` callback threaded from `DailyBriefingApp` to `HighPrioritySection` rows (and optionally `NarrativeBullet`) → small "Email" affordance per item (mirrors `onOpenRecord`).
- **UI**: same `SendEmailStep` in a `Dialog`, but subject + body are **client-composed** from the item: subject `"{item.name}"`, body = item summary/description + **deep link to the record** (built from structured `entityType`/`entityId`, NOT narrative text — reuses the deterministic-link rule proven by `deterministicRenderer.test.tsx`).
- **Send**: **no BFF leg**. `DailyBriefingApp` creates a **draft `email` activity** via its existing `webApi.createRecord('email', …)` — `regardingobjectid_<entity>@odata.bind` → the item's record, `email_activity_parties` from=caller / to=recipient. "Saved as a draft activity" (matches `SendEmailStep`'s default infoNote). Reuses the `DocumentEmailWizard` send pattern. No new BFF surface (§10-friendly).
- **Asymmetry rationale**: #2 server-sends because the server owns the briefing HTML; #3 has no server rendering for a single item, so a client-composed draft activity is the lowest-surface reuse. Documented deliberately.

## Placement Justification (CLAUDE.md §10 BFF Hygiene + §11 Reuse)
- **Existing?** `POST /api/ai/daily-briefing/email` + `EmailAsync` already render+send the briefing. `SendEmailStep` already does recipients→subject→body. `DailyBriefingApp` already holds `webApi` + creates records + navigates.
- **Extension?** Yes — extend `/email` with one optional field; reuse `SendEmailStep`; add callbacks to existing leaf components. **No new service, no new endpoint, no new component tree.**
- **Cost-of-doing-nothing**: users cannot share a briefing/item with a colleague — the concrete requested behavior fails.
- **BFF hygiene**: no new package, no new DI, no new endpoint. Publish-size delta ≈ 0 (one optional DTO field + a few lines in an existing handler). Will still run `dotnet publish` size check + `dotnet list package --vulnerable` on the BFF change per §10.4/§10.5. Test obligation (§10.6): add `HandleEmail` recipient-override test.

## Security (CLAUDE.md §6 — flagged for code-review)
- New capability: an authenticated user can cause the Communication service to send **their own briefing** to an arbitrary email. Mitigations: (a) content is the caller's *own* data — no cross-user exfil; (b) UI constrains recipient to systemuser search (internal); (c) `RequireAuthorization` + `ai-batch` rate limit already on the route; (d) server validates the email format. Documented for explicit code-review sign-off.

## Build order (lowest-risk first)
1. **BFF**: optional `recipientEmail` on `/email` request + `HandleEmail` wiring + email-format validation. Unit test (recipient-override + fallback-to-caller). `dotnet build` + publish-size + CVE check.
2. **Shared lib #3 (self-contained)**: `onEmailItem` callback on `HighPrioritySection` (+ `NarrativeBullet`); `DailyBriefingApp` dialog + `webApi.createRecord('email')` draft; client-composed deterministic body. Jest test for body/link composition.
3. **Shared lib #2**: `onEmailBriefing` on `DigestHeader` (menu item); `DailyBriefingApp` dialog wiring the `/email` POST with picked recipient. Jest test for recipient-passthrough.
4. **Gates**: code-review + adr-check (Step 9.5). Then batch into the pending r5 deploy (BFF re-deploy + SpaarkeAi widget deploy) + UAT.

## As-built notes (2026-07-09)
- **Reused `SendEmailDialog`** (`@spaarke/ui-components`), not a hand-rolled dialog — even better than the planned `SendEmailStep` reuse: it already bundles the Dialog chrome, recipient LookupField, subject/body, send spinner, and error surface. **Zero new components** authored (§11 win). Recipient email parsed via the shared `extractEmailKey`.
- **All wiring lives in `DailyBriefingApp`** (the Xrm-bridge); leaf components (`DigestHeader`, `HighPrioritySection`) only receive callbacks → stay Xrm-free (ADR-021/012). No SpaarkeAi host changes.
- **#3 composition extracted to exported pure helpers** `buildItemEmailDraft` / `buildRecordDeepLink` in `DailyBriefingApp.tsx` → unit-tested in `test/emailShareDraft.test.ts` (8/8 green): subject/body/link are built only from structured fields (name, kindLabel, description, entityType, entityId), never narrative.
- **#2 server change**: `/email` now takes optional `{ recipientEmail }` + internal-only egress guard (commit `5c3c1a9ee`, 8/8 contract tests).
- **MVP wart (documented)**: in briefing mode the `SendEmailDialog` "Message" field is **cosmetic** — the server owns the briefing HTML, so a typed message is not sent. Prepend/subject-override is the deferred note-passthrough fast-follow (needs an OutputRouter-adjacent change). The dialog's default body states the briefing is included.
- **Pre-existing, unrelated test failures** on this branch (verified by stashing my changes): `test/legalWorkspaceSectionRegistry.test.ts` + one case in `test/ActivityNotesSection.callbacks.test.tsx` (onKeep ttl 604800→0). NOT caused by email-share; candidates for the 090 `/defer` list.

## Step 9.5 gate — independent adversarial code-review (2026-07-09)
Verdict: **no Critical issues.** Security clean (egress guard closes the external-exfiltration hole; systemuserid stays token-derived; FetchXML injection not exploitable — format-validation-first + `SecurityElement.Escape`; nullable body binding verified optional so the scheduled no-body POST still works). ADR-012/021/§10/§11 all clean.
- **Fixed (Warning):** the `createRecord('email', …)` party payload was untested (the runtime-risky surface). Extracted `buildEmailActivityRecord(senderId, payload)` as an exported pure helper + 2 tests asserting exact `partyid_systemuser@odata.bind` + `participationtypemask` (From=1/To=2). Now 11/11.
- **Fixed (Suggestion):** `buildRecordDeepLink` now returns '' when `clientUrl` is empty (was emitting a dead relative `/main.aspx` link into the email body). Added a test.
- **Accepted (Suggestion) — empty-briefing "sent" toast:** not reachable in practice — the "Email Briefing" menu item renders ONLY on the success render path (a non-empty briefing), so `EmailAsync`'s empty short-circuit can't fire for a colleague share except via a benign render→send data race. Documented, not plumbed.
- **Accepted (Suggestion) — "internal = any active systemuser":** the guard treats any active `systemuser` as internal; orgs that provision external guests as systemusers with external `internalemailaddress` domains could still receive. Accepted as documented defense-in-depth for MVP; a domain allow-list is a future hardening if strict employee-only is required.

## Open / deferred
- Personal-note prepend + subject override for #2 (needs OutputRouter-adjacent HTML change) → **fast-follow**, not MVP.
- #3 send-now (vs draft activity) → enhancement; draft is the safe MVP matching the SendEmailStep contract.
- Deep-link target for the SpaarkeAi Daily Briefing surface (for #2 body, if note support lands) → confirm the code-page URL at deploy time.
