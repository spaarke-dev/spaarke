# Unified Access Control R2 — Objectives, Deliverables, and User Impact

> **Written**: 2026-09-03 · **Source**: `spec.md` (Executive Summary, Scope, FR-01…FR-32, Success
> Criteria) reconciled against the 92 task POMLs and the shipped code.
> **Audience**: the owner, and anyone deciding what this project must still deliver.
> **Status at writing**: 53 of 92 tasks complete · 37 open (~188 h) · 1 escalated · 1 blocked-shipped.

---

## 1. The problem this project exists to fix

> *"Spaarke has two disjoint authorization systems sharing a data resolver and nothing else,
> **neither of which enforces what its documentation claims**."* — `spec.md` Executive Summary

That last clause is the whole project. The gap was not missing features; it was that the
**documentation described protection that the code did not provide**. A reviewer reading the system
would conclude records were protected. They were not.

Four structural faults:

1. **Two authorization systems.** One for internal (systemuser) callers, one for external (contact)
   callers, sharing only a data resolver. Two implementations of one question means two answers, and
   the divergence is invisible until someone sees a record they shouldn't.
2. **22 confirmed enforcement gaps**, catalogued in Phase 0 — each one a place where the rule existed
   on paper and not in the request path.
3. **"Secure Project" did not isolate.** A record marked secure inherited ordinary business-unit
   visibility, so users in the Operations subtree could read it. The label was reassurance, not a
   boundary.
4. **No inheritance model.** Child records (To Dos, events, communications, invoices) had no
   principled relationship to the record they belong to, so access to a parent said nothing about
   its children.

---

## 2. Objectives

| # | Objective | Why it matters |
|---|---|---|
| **O1** | **One evaluator** returning `(recordId → rights)` for **both** principal kinds | One question, one answer. Two systems cannot be kept consistent by discipline |
| **O2** | **Close all 22 Phase 0 enforcement gaps** | Make the documented behaviour the actual behaviour |
| **O3** | **Make Secure Project genuinely isolate** | The label must be a boundary, not a decoration |
| **O4** | **Parent → child inheritance as a consequence of the model**, not a bespoke mechanism | A bespoke rule per child type is a rule that will be missed on the next child type |
| **O5** | **Explicit, reviewable policy** — an allow-list of access-conferring lookups | Access conferred by column *pattern-matching* is access nobody can audit |
| **O6** | **Answer "who can see this, and why?"** with provenance | Unauditable access is unmanageable access |
| **O7** | **Answer it for a date in the past** (attestation) | Compliance asks retrospectively, not live |

Explicitly **out of scope**: MDA authorization (Dataverse enforces natively), field-level
show/hide, break-glass emergency access, organization-hierarchy grant cascade, GDPR erasure of
grant rows, AI-search security trimming for contacts.

---

## 3. Deliverables by phase

| Phase | Deliverable | State |
|---|---|---|
| **0 — Enforcement remediation** | 22 confirmed findings closed, one regression test each | Mostly shipped; **6 tasks open** (023 expiry, 024 SPE honesty, 025 test-integrity, 026 doc repair, 028 service-request core type, 029 external To Do parity) |
| **0b/0c — Secure Documents** | Server-derived storage containers; authorization before any byte moves; external document surface | **083 CLOSED 2026-09-07 — the `ClientSupplied` sink count is 0.** No code path lets a caller name the container its bytes land in. **082** census open; **093/094/095** filed late |
| **1 — One evaluator** | Single evaluator; impersonated Dataverse reads replace column pattern-matching (FR-20) | **2 open** (035, 036) |
| **2 — One definition of member** | Access-conferring allow-list for contact- **and** org-typed lookups (FR-24); standing grants carry a baseline level (FR-25) | **3 open** (042, 043, 044) |
| **3 — Child inheritance** | Core-ancestor denormalization, re-stamped on reparent (FR-26); children inherit parent rights (FR-27) | **5 open** (054–058) |
| **4 — Secure Project + Manage Access + wizard** | BU restructure, share-only access, Manage Access PCF rework, Create Project wizard Secure step (FR-28…FR-31) | **10 open** (060–069) — *the visible half* |
| **5 — Attestation** | Append-only access-event log; point-in-time replay (FR-32) | **4 open** (086–089) |

---

## 4. What this changes for users

### 4.1 The change users will *feel* most: "Manage Access" becomes answerable

Today the Manage Access surface lists who has access. It does not say **why**, and it cannot grant
access to an internal user at all.

**FR-29 / FR-30 deliver three things** (tasks 065, 066, 067):

- **A "+ User" picker for internal system users.** Today the modal handles contacts and
  organizations only — granting an internal colleague access requires leaving the record and going
  to Dataverse. After this, it is done in the modal.
- **Provenance on every row.** Each entry shows *how* the access arrived: `share` ·
  `explicit grant` · `organization grant` · `standing grant` · `derived-from-field`. This is the
  difference between a list and an explanation. Acceptance is literally *"answers 'who can see this
  record and why' without leaving the form."*
- **Suppressed rows rendered as suppressed, with a reason.** When a record is Secure or Restricted
  and a row is being vetoed, the user sees *that it was suppressed and why* — rather than an entry
  silently vanishing. **Silent absence is the failure mode this replaces**: an administrator cannot
  distinguish "nobody has access" from "the UI dropped a row".

### 4.2 Ethical walls become a first-class control

The FR-23 deny-list (No Access List, tasks 038/039 — **shipped**) lets an administrator exclude a
specific person from a specific matter even though their team, organization, or role would otherwise
grant access. Task **067** surfaces it in the modal.

For legal operations this is the conflict-of-interest wall. Before this, the only way to exclude one
person was to restructure everyone else's access around them.

### 4.3 Child records stop being invisible

**FR-27**: a contact with access to Project 1 sees its **invoices, events, communications, and To
Dos**. Today access to a parent implies nothing about its children.

This is the difference between "I can see the project" and "I can do my work". A client contact who
can open a matter but not the communications or To Dos attached to it has been given a shell.

Per-child revocation also works without touching the parent — you can withhold one sensitive
document from someone who otherwise has project access.

### 4.4 Secure Projects actually become secure — and reversible

**FR-28**: secure projects are owned by a memberless owner team in a named `Secure Project` business
unit, resolved **by name** from configuration (never by GUID), and **all human access is by explicit
share**. Provisioning **fails closed** if that BU is absent.

Two consequences a user notices:

- Someone in the Operations subtree can no longer read a secure project by virtue of where they sit
  in the org chart. Access is explicit or absent.
- **FR-31** removes the wizard's permanence warning: **the secure designation becomes reversible.**
  Users were previously told a one-way door was being closed. That copy described behaviour that
  should not have existed, and it made people avoid a feature they needed.

⚠️ **Currently in an in-between state**: task 021 shipped the isolation (the BU + memberless owner
team) but **not the explicit share** (task 061). A secure project today is *isolated but not shared*
— so **no human can reach it**. This is why 061 is not optional; isolation without sharing is a
locked box.

### 4.5 Upload and document experience (shipped this wave)

- **Name collisions no longer silently overwrite.** Previously an upload with an existing filename
  replaced the stored file and the user saw a Dataverse *"Duplicate Record"* error **after** the
  original content was gone. Now the server refuses (`conflictBehavior=fail`), and the wizard offers
  **"Keep both"** or **"Save as new version"** — amber, counted as *needing your choice*, not red
  "failed", because nothing went wrong.
- **A 4 MiB upload ceiling that no server enforced has been removed.** Files between 4 MiB and
  250 MB were being refused by client-side code alone, in three separate copies of a limit that
  existed only in comments.
- **The external portal's document features work.** Download, version history, calendar, and upload
  were each calling routes the BFF does not serve — every one 404'd for the life of the feature.
- **Phantom SPE folders stopped.** Uploading to a *path* makes Graph create every folder segment, so
  a filename containing a date produced folders named after fragments of it.

### 4.6 Compliance: answering questions about the past

**FR-32**: *"who could see record X on date D"* becomes answerable, by replaying the evaluator over
Dataverse field audit rather than materializing derived access into rows. That distinction matters —
materializing it would create a second source of truth that drifts from the first.

---

## 5. UI/UX implications — the design themes

Five principles emerge from the requirements. They are worth stating because they apply to any
future access surface.

1. **Explain, don't just list.** Provenance per row (FR-30) is the project's central UX idea. An
   access list without provenance cannot be acted on: an admin who cannot see *why* someone has
   access cannot safely remove it.
2. **Make suppression visible.** A vetoed row renders *as suppressed, with a reason*. Silent
   omission is indistinguishable from a bug — and it teaches users to distrust the surface.
3. **Copy must match implemented behaviour.** FR-31 exists because the wizard warned about
   permanence that shouldn't exist and cited a retired Power Pages integration. Stale UI copy is a
   defect with the same standing as stale code — it changes user decisions.
4. **A recoverable outcome is not a failure.** The collision dialog is the worked example: nothing
   was lost, the user has a choice, so it is amber "needs your choice", not red "failed", and it
   stays visible instead of being replaced by a success summary.
5. **Do the work where the user already is.** "+ User" in the modal, rather than a trip to
   Dataverse. Provenance on the form, rather than a separate report.

### Surfaces affected

| Surface | Change |
|---|---|
| **Access Permission PCF** ("Manage Access") | The main event: + User picker, provenance + level per row, suppressed-row rendering, deny-list management |
| **Create Project wizard** — Secure step | Aligned to share-only; retired Power Pages claim removed; permanence warning dropped |
| **DocumentUploadWizard** | Two-option collision dialog; pending choices survive above the summary *(shipped)* |
| **External SPA** (client portal) | Download, version history, calendar, upload all repointed at routes that exist *(shipped)* |
| **MDA forms** | Child records (To Dos, events, communications, invoices) become visible to parent-access holders |
| **Office add-ins** | **No user-visible change** — verified 2026-09-03. Word "Save" still replaces in place; the add-in's container field is a *response* field, which is the correct direction |

---

## 6. How "done" will be judged

From `spec.md` §Success Criteria — nine criteria, each with a named verification:

1. All 22 Phase 0 findings closed *(regression test per finding)*
2. One evaluator; no caller-scoped path passes `userAccessToken: null`
3. Negative canary green *(NFR-04 in CI)*
4. Role-depth assertion green *(NFR-05 in CI)*
5. **A user in the Operations subtree cannot read a `Secure Project`-owned record** *(live dev)*
6. **A shared user reads a secure project in both MDA and SPA** *(live dev)*
7. **Manage Access answers "who can see this and why" with provenance per row** *(UAT)*
8. **A contact with Project access sees its invoices, events, communications and To Dos** *(live dev)*
9. **Point-in-time attestation answerable** *(replay a historical date)*

**Four of the nine require live-environment verification, not a green test suite.** Criteria 5, 6,
and 8 are live dev tests; 7 is UAT. That is the honest reason this project cannot be closed from CI
alone — and why task **047** (live provisioning validation) should not be treated as optional.

Criterion 9 is the only one gated entirely on Phase 5 (086–089). If attestation is spun out, **say
so explicitly** rather than declaring nine-of-nine met.

---

## 7. The single most important open item

**Task 061 — the explicit share for Secure Projects.**

Everything else on the list improves or completes the model. 061 is the difference between a feature
that is *isolated* and one that is *usable*: task 021 delivered the memberless owner team and BU
isolation, but until per-record access teams land, **no human can reach a secure project at all**.

Runner-up **083 is now CLOSED** (2026-09-07) — the project's founding defect class is finished. Its
last two sinks (`PUT /api/drives/{driveId}/upload` and `DELETE /api/drives/{driveId}/items/{itemId}`,
both app-only managed-identity) were **deleted**, along with the `canwritefiles` policy behind them.
The deliverable is an argument from **absence**, not an inventory:
`grep -c "^            Provenance.ClientSupplied," tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs`
returns **0**, and Rule A fails the build on any undeclared SPE write sink — so a new one cannot be
added silently.

One calibration note worth carrying: both of those routes were described as "live holes" in the task
brief and in this document. They were **not exploitable as written** — the policy resolved the route
value as `sprk_documents({id})`, so a real drive id (`b!…`) is not a GUID and denied, while a valid
document GUID is not addressable as a drive. They were **accidentally** safe, via value-space
disjointness, and the source carried a comment recording that accident as a design decision. The
disposition (delete) was right; the stated reason was overstated. That distinction matters because the
accident stops holding the moment either id domain widens.
