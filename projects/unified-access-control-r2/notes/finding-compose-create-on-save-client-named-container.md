# Finding — Compose create-on-save writes bytes into a CLIENT-NAMED container

> **Raised**: 2026-08-27, during task 076, by the owner asking what *"the resolver stays in the
> wizards"* actually means.
> **Status**: 🔲 **WORK REQUIRED. Not unified-access-control-r2 scope.** Needs handover to
> `spaarkeai-compose-r8` (PR #806) or its own task.
> **GitHub Issue**: https://github.com/spaarke-dev/spaarke/issues/858 (filed 2026-08-27 — the
> two-write rule per `/project-defer-issue-tracking`; `push-to-github` Step 1.6 audits for unfiled
> entries, so this link is what stops it warning).
> **Severity**: same *shape* as task 073's finding (write into a caller-named container), on a
> second surface. Not independently exploitable today — see "Exposure" below.

---

## 1. What the comment means

`Services/Compose/IComposeService.cs:743-751`, on `SaveComposeDocumentRequest.ContainerId`:

> *"CLIENT-SUPPLIED SPE container (or drive) id for the create-on-save path (FR-05, Fork A). The
> client resolves this via the existing wizard cascade (`resolveBusinessUnitContainerId` →
> `businessunit.sprk_containerid`) and passes it in. Required when `DocumentSpeId` is absent; **the
> BFF does NOT resolve a business-unit → container mapping server-side (multi-container INV-7 — the
> resolver stays in the wizards)**. Ignored when `DocumentSpeId` is present."*

It governs the **create-on-save** path. When Compose saves a TRANSIENT draft (Browse / Upload /
AI-drafted — compose tasks 010/012), there is no `DocumentSpeId`, so no SPE drive-item exists yet.
The save must CREATE one, which requires a container. The client names it.

When `DocumentSpeId` IS present (replace-content, the original R1 behaviour), `ContainerId` is
ignored and the drive comes off the existing item. **Only the create path is affected.**

## 2. Why it is that way — the honest reason, which is NOT just scope

The owner's hypothesis was *"at the time of file upload there is no record/document created yet."*
**Confirmed, and it is stronger than a timing problem — it is a contract gap.**

Every property on `SaveComposeDocumentRequest`, enumerated:

```
DriveId · DocumentSpeId · ContainerId · Content · BaselineVersionId · BaselineETag ·
OperationLog · Comments · ContentModel · DocumentRecordId · DisplayName · ParaIdMap ·
SummaryPage · TransientKey · ForkNew · SourceDocumentRecordId
```

**There is no parent-record key.** No matter, no project, no regarding. `DocumentRecordId` is the
document row and is nullable (absent for a transient draft, which is exactly this path). So at
create-on-save the server has **nothing to key a container resolution on**. The client supplying it
is not laziness; under the current contract it is the only available answer.

### But the owning record IS known one step earlier

`LoadComposeDocumentRequest.MatterId` exists (`IComposeService.cs:469`), and **ADR-040 session
binding is keyed on `DocumentId + MatterId`** — the comment at `:456` says so, and `:461` describes
an `EntityId == MatterId` check before session reuse.

So the owning record is already in the Compose session. It is simply **not threaded to save**.

## 3. Why this must not be left as "consistency nice-to-have"

Task 076 converts `PUT /api/obo/containers/{id}/files/{*path}` from container-keyed to record-keyed
precisely because *"write into a caller-named container"* is the shape that made task 073's
vulnerability expressible. **Compose create-on-save is the same shape on a different surface.**

Convert uploads and leave Compose, and the project ships having fixed one of two instances of the
same defect class. That is the exact failure mode this project has now hit three times:

- the search **filters** were fixed while their **handlers** kept the broken claim read (caught by
  #840's ratchet, not by review)
- `SemanticSearchEndpoints.cs:650` documents a mirror invariant that fixing one half silently broke
- compose-r8's own §4 reported two id-space defects our census would not have caught

**A per-project container is worthless if any writer can still name a different one.** SPE
permissions are additive-only, so a document created in a shared container by this path can never
be retracted — identical to the reasoning in task 075/076.

## 4. Exposure — stated precisely, not inflated

**Not independently exploitable today**, for the same reason task 071's OBO routes were "latent, not
live": the create runs **under OBO**, so SPE denies any caller lacking a container permission, and
under the broker-only decision no user is ever granted one (`GrantMembershipAsync` stays at zero
callers). It is a **bypass by construction**, not a live hole.

What makes it matter anyway: it is the *default* path for secure content once secure projects exist.
The acting user's BU container is what gets named, and per
[`secure-project-workflow-review-2026-08-24.md`](secure-project-workflow-review-2026-08-24.md) §A,
users sit in the Operations subtree while secure records are owned in `Secure Projects` — so a
secure matter's Compose draft would be created **in the general Operations container**. No permission
change afterwards retracts it.

## 5. Governance status — visible, classified, not invisible

`tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.cs:155`:

```
new GovernedFile("Api/ComposeEndpoints.cs", Scope.HandlerAuthorized,
    "… plus in-handler checks. Listed rather than gated because converting Compose to route-level
     filters is a design change owned by the Compose ADR (ADR-049), not by this guard."),
```

So task 074's forcing function **does** account for this file. It will not fail the build (the
classification is `HandlerAuthorized`, deliberately), and it will not be forgotten either. This is
the guard behaving correctly: it made the file get classified, and the classification names its own
deferral and owner.

## 6. The work required

| Step | Change | Owner |
|---|---|---|
| 1 | Add the owning record `(entityLogicalName, recordId)` to `SaveComposeDocumentRequest`, populated from the Compose session that already holds `MatterId` (ADR-040 binding) | compose-r8 |
| 2 | On the create-on-save path, resolve the container via `RecordContainerResolver.ResolveForRecordAsync(entity, recordId, …)` — secure → own `sprk_containerid`, else the record's `owningbusinessunit` → BU container | compose-r8, reusing 075 |
| 3 | Delete `SaveComposeDocumentRequest.ContainerId`, and the client's `resolveBusinessUnitContainerId` call that feeds it | compose-r8 |
| 4 | Re-classify `Api/ComposeEndpoints.cs` in the ArchTest once the create path is record-keyed — it may become `RouteLevelGate` | whoever lands step 2 |

**Estimated shape**: one contract field threaded through an existing session + one call to an
existing resolver + two deletions. Small, but it crosses an ADR-049 surface under active development
in **PR #806**, so it is a handover, not a drive-by edit.

## 7. Correction this finding forces

`design.md:450` states INV-7 as though it were a technical constraint:

> *"The BFF deliberately does not resolve this server-side (INV-7)"*

**There is no technical reason.** INV-7 originates in the `spaarke-multi-container-multi-index`
project and its only concrete statement is the `IComposeService.cs` comment above — *"the resolver
stays in the wizards"* is a **scope boundary** ("we are not doing that work here"), which then got
cited downstream as a constraint. Nothing prevents server-side resolution: the BFF already reads
Dataverse and already reads/writes `sprk_containerid` in provisioning, and `owningbusinessunit` is
present and populated on every securable row (verified live 2026-08-27).

Task 076 corrects `design.md:450` accordingly.
