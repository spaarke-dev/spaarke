# Task 050 — Post-Deploy UAT Checklist (live-env cases)

> **Purpose**: The live-browser portions of the task-050 verification sweep that require a **deployed** Email surface. Run these **after task 051 deploy** (BFF + code page + widget seed) against a real MDA/Code-Page session with seeded `sprk_communication` data.
> **Why deferred**: nothing is deployed at task-050 time; the automatable (code/build/test) proofs are complete and green in `notes/verification-report.md`. These cases exercise behavior only observable on the live surface.
> **How to run**: open the Email surface in BOTH mounts (SpaarkeAi `email` widget/section + standalone `sprk_emailpage` code page). Use the `ui-test` skill (Claude Code `--chrome`) or manual QA. **A security or regression failure is a HARD BLOCKER** (POML `<escalation>`), not a documented deviation — stop and escalate per CLAUDE.md §6.

**Prerequisites to seed before UAT**
- [ ] ≥1 Email `sprk_communication` WITH an archived `.eml` that contains quoted history + inline (cid) images.
- [ ] ≥1 Email record WITHOUT an archive (`.eml` missing) → degradation path.
- [ ] ≥1 Email record whose `.eml` AND `sprk_body` carry the 4 malicious payloads (`<script>`, `onerror=`, a `javascript:` link, a remote tracking pixel).
- [ ] ≥1 non-Email record (`Teams`/`SMS` channel) to prove exclusion.
- [ ] ≥1 Email the test user has NO access to (for the fail-closed/direct-id case).
- [ ] A record with sibling associations (to prove additive write preserves siblings).
- [ ] A reply whose parent carries a `regarding` (to prove inheritance display).

---

## SC 1 / NFR-06 — Dual-mount parity, live, light + dark (structural parity already proven)
- [ ] Open the same Email record in the **SpaarkeAi `email` widget** and the **standalone code page**; confirm identical card list, reading pane, `.eml` body, toolbar, associations, tracking.
- [ ] Repeat in **dark theme** (host `FluentProvider`, ADR-021): both mounts render identically and legibly; no theme-only rendering defect (chrome, note, skeleton, error states, the `.eml` iframe frame).
- [ ] (Optional 3rd mount) Confirm the **LegalWorkspace `email` section** renders the same surface — **blocked today** by the pre-existing `@spaarke/document-operations` LegalWorkspace vite failure (see verification-report caveat); verify only once that bundle builds.

## SC 2 — Left-list + view cases (Email-only filtering)
- [ ] Only Email-type records appear in every Email view.
- [ ] The seeded non-Email (`Teams`/`SMS`) record is **excluded** from every Email view.
- [ ] Switching views re-populates the card list correctly.

## SC 3 / SC 4 — Reading-pane fidelity + degradation
- [ ] The quoted-history email renders the **full chain as sent** from the `.eml`, with inline (cid) images resolved to `data:` URIs.
- [ ] The archive-less email **degrades** to `sprk_body` + the "Full history unavailable" note — **no error banner** (degradation is a normal state).
- [ ] Header paints first (record); the body shows a loading skeleton while the `.eml` render is in flight and never blocks the header (NFR-02).

## SC 5 / SC 6 — Compose + association cases
- [ ] **Reply / ReplyAll / Forward / New** each prefill recipients correctly and **send via the existing composer/send path** (no forked composer — canonical `EmailComposer`/`SendEmailDialog`).
- [ ] Association review **writes additively** — existing sibling associations are **preserved** (no clear-and-set); uses `applyRegardingSelection`.
- [ ] A reply shows the **inherited parent `regarding`**.
- [ ] "Open full form" modal opens the OOB record form correctly.

## SC 7 / NFR-03 — XSS closed set, LIVE (8 combinations) — HARD BLOCKER on any script execution
For EACH payload — `<script>`, `onerror=` handler, `javascript:` link, remote tracking pixel — on BOTH paths:
- [ ] **`.eml` path (server-sanitized + sandboxed iframe)**: open the malicious email in the reading pane → **no script executes** (no alert/console/DOM side effect); confirm via DevTools the body renders inside `<iframe sandbox="">` with the `sandbox` attribute carrying **no** `allow-scripts` and **no** `allow-same-origin`.
- [ ] **`sprk_body` field render (client-sanitized)**: force the degradation path (archive-less) with the same payloads → **no script executes**; scripts/handlers/`javascript:` neutralized; safe content still renders.
- [ ] Confirm across the network tab that the reading pane makes exactly one `GET /api/documents/{id}/eml-render` per open and the response is `text/html` immutable-cached.

## Negative / authorization (fail-closed) — HARD BLOCKER
- [ ] An Email the user has **no access to** is **not returned** in any view.
- [ ] A **direct-id** load of a no-access / missing `.eml` **fails closed** — error/empty state, **no content leaked** (endpoint returns 404 before producing HTML; client shows degradation/error, never partial content).

## SC 8 / NFR-04 — OOB `sprk_communication` form + 4 PCFs, LIVE regression — HARD BLOCKER on any regression
Open the OOB `sprk_communication` **main form** and exercise each PCF **read + write**, confirming behavior is unchanged after the Layer-1 extraction (020–023):
- [ ] `CommunicationActions` — action bar reads state and fires actions as before.
- [ ] `CommunicationAttachments` — attachment list reads + add/remove writes as before.
- [ ] `CommunicationConnections` — connections read + additive write (siblings preserved) as before.
- [ ] `TrackingFieldTrio` — the three tracking fields read + write as before.
- [ ] No console errors; no changed behavior. **If a real regression surfaces → escalate to the owning extraction task (020–023); do NOT patch PCF views.**

---

### Sign-off
- [ ] All boxes checked in a deployed env, light + dark.
- [ ] Zero script execution across all 8 XSS combinations; every `.eml` open in a hardened `sandbox=""` iframe.
- [ ] OOB form + 4 PCFs regression-free.
- [ ] Result recorded back into `notes/verification-report.md`; any failure escalated per CLAUDE.md §6 before marking the project verifiable.
