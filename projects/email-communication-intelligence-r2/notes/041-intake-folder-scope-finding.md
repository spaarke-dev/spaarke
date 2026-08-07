# Task 041 — scope/reuse finding (STOP before implementing as authored)

**Date**: 2026-08-05 · **Rigor**: FULL · **Outcome**: escalation (§11 Component Justification + CLAUDE.md §6.5). **No code written.**

## TL;DR

Implementing task 041 **as authored** would build duplicate infrastructure. Both FR-B1 mechanisms are
already covered — mechanism (1) by existing config, mechanism (2) by the already-planned task **043**. The
genuinely-new, non-duplicative code surface in 041 right now is **thin-to-none**. Surfacing for an operator
scope decision rather than silently building the duplicate.

## What the code actually shows (verified this session)

### Mechanism (1) — shared "Spaarke" mailbox+folder → full pipeline: ALREADY WORKS (config, not code)
- `GraphSubscriptionManager` (`Services/Communication/GraphSubscriptionManager.cs`) already subscribes
  **every receive-enabled `sprk_communicationaccount`** on `users/{email}/mailFolders/{monitorFolder}/messages`
  where `monitorFolder = account.MonitorFolder ?? "Inbox"` (line 425/435), pairs it with lifecycle
  notifications, and self-heals (renew/recreate/reconcile).
- Its notifications route to the webhook → **`IncomingCommunicationProcessor`** → full association + triage +
  provenance.
- **So a shared "Spaarke" intake folder is delivered by creating a receive-enabled `sprk_communicationaccount`
  record with `sprk_monitorfolder = "Spaarke"`** (+ the Exchange folder). That is **operator configuration,
  not code.**
- The POML's prescribed step 2 — "add operator-managed intake config (ADR-018 `IOptionsMonitor`,
  `AutoFileOptions` clone): shared intake account + folder identity" — would **duplicate** the existing
  `sprk_communicationaccount` / `sprk_monitorfolder` model. **§11 violation**: no concrete behavior fails
  without the new config that the existing account model doesn't already provide.

### Mechanism (2) — add-in drag target → identical engine output: overlaps the planned task 043
- The add-in save (`useSaveFlow.ts`) POSTs to **`/api/office/save`** → `OfficeService.SaveAsync` →
  `CreateDocumentWithSpePointersAsync` → creates a **`sprk_document`** (document-centric SPE-pointer path).
  It does **NOT** run the Association Engine (`IncomingCommunicationProcessor` / `IncomingAssociationResolver`
  / rungs).
- Making the add-in drop produce "association + triage + provenance identical to a mailbox-captured email"
  therefore = **unifying the user-upload/add-in path with the capture pipeline**. That is **exactly the
  already-planned task 043** — "Unify user-upload with capture — engine + dedup" (deps **021, 024**, both
  gated). Building it inside 041 duplicates / pre-empts 043's scope.

## Why stop (governance)

- **§11 Component Justification — default to reuse.** Existing = the `sprk_communicationaccount` monitor-folder
  model. Extension test: mechanism (1) needs *no* new component; mechanism (2) belongs to task 043.
- **CLAUDE.md §6.5 / the task's own `<escalation>`.** The task escalation already anticipates a provisioning
  boundary ("if a shared Spaarke intake mailbox cannot be provisioned, STOP — tenant decision, not a code
  workaround"). The reuse finding is the same spirit: don't code around what is config + an existing planned task.
- Also: 041's acceptance criterion 3 (dedup of an already-filed intake email) soft-depends on task **021**
  (FR-C1 message-id alternate key) — gated. It cannot be *validated* autonomously now.

## Recommended operator options

- **(A) Re-scope 041 → "verify + document mechanism-1 as config; fold mechanism-2 into 043."** Mark 041's
  mechanism (1) as satisfied by existing config (add a short admin-guide note: shared intake mailbox =
  receive-enabled `sprk_communicationaccount` + `sprk_monitorfolder`). Move the "add-in drop → capture
  pipeline" work to task **043** (its natural home, once 021/024 land). **Recommended.**
- **(B) Keep 041 but narrow to a thin non-duplicative slice** — e.g. a small `CommunicationAccountService`
  convenience for the shared-intake account, only if a concrete gap in the existing model is found (none found
  yet).
- **(C) Build as authored** — NOT recommended (duplicates existing config + pre-empts 043).

## Evidence pointers
- `Services/Communication/GraphSubscriptionManager.cs:283` (subscribes all receive-enabled accounts),
  `:422-451` (`CreateSubscriptionAsync` → `mailFolders/{monitorFolder}/messages`).
- `Services/Communication/CommunicationAccountService.cs:54-61` (`QueryReceiveEnabledAccountsAsync` selects
  `sprk_monitorfolder`, `sprk_autocreaterecords`).
- `Services/Office/OfficeService.cs:301-310` (add-in save → `sprk_document`, no association engine).
- Task 043 POML — "Unify user-upload with capture (engine + dedup)", deps 021, 024.
