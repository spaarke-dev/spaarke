# Alignment with `spaarkeai-compose-r8` — 2026-08-30

> **Reads**: `spaarke-wt-spaarkeai-compose-r8/projects/spaarkeai-compose-r8/notes/response-to-unified-access-control-r2-2026-08-27.md`
> (definitive status block, 2026-08-30)
> **Status**: their commitments accepted; three of our own artefacts corrected as a result.

---

## 1. What they have committed, verified against master

Their anchors were checked against `origin/master` after merging it into this branch. All three resolve:

| Their anchor | They said | Verified here | Δ |
|---|---|---|---|
| `ResolveDriveIdAsync(request.ContainerId, …)` — the container decision | 1510 | **1510** | 0 |
| "transient draft with no client-supplied ContainerId" guard | 1500 | **1500** | 0 |
| `PromoteIfEphemeralAsync` (definition) | 1989 | **1998** | +9 |
| `ComposeService.cs` length | 2,919 | 2,938 | +19 |

The +9/+19 drift is consistent (our tree is 19 lines longer) and immaterial — **anchor the patch on symbol
names, not line numbers**, which is the option they offered in their comment 2. Take it.

Also confirmed: **PR #905 merged as `369c3ea89`** — that is the master HEAD this branch is now level with.
`ComposeService.cs` is **frozen for us** until we signal on #858, and clusters 2a/2b are deliberately
unextracted so our patch applies to recognisable code. **We will not have to rebase.**

## 2. 🔴 The finding that changes the shape of #858

The guard they preserved for us, at `ComposeService.cs:1500`, logs this rationale:

> *"transient draft with no client-supplied ContainerId — failing the '{Step}' step honestly … **No
> server-side BU→container resolver (multi-container INV-7)**."*

**Both halves of that clause are now wrong**, and neither is the Compose team's fault — both became wrong
underneath them while `ComposeService.cs` was frozen.

**(a) "No server-side BU→container resolver" is false.**
`Infrastructure/Dataverse/RecordContainerResolver.cs` exists and has **nine** consumer files, including
`Api/OBOEndpoints.cs` (task 076), `Api/Filters/ContainerDocumentAuthorizationFilter.cs` (078),
`Services/Office/OfficeService.cs` (085, today) and `Services/Communication/Engine/CommunicationContainerResolver.cs`.
`ResolveForRecordAsync(entityLogicalName, recordId)` derives the container from the record's own
`sprk_containerid`, falls back to the record's owning business unit, and **refuses** for a secure record
with no container of its own.

**(b) The INV-7 citation is inverted.** This project already corrected the same misreading in its own
`design.md` on 2026-08-28. INV-7 (`spaarke-multi-container-multi-index-r1/design.md:82-88`) *prescribes*
server-side resolution — record's own field → **parent's BU** → tenant default (a **server** fallback in
BFF config). It is the reason to resolve server-side, not the reason not to. ⚠️ Four unrelated invariants
in this repo are numbered "INV-7"; cite the source project when quoting it.

**So #858 is smaller than its framing.** It is not "build a resolver"; it is "call the one that now
exists, the way three other paths already do". The Office save path (task 085, landed today) is the
closest worked example: same shape — a client-supplied container field on a request that was already
authorized against a record — resolved by deleting the field and calling
`RecordContainerResolver.ResolveForRecordAsync` on the authorized record.

The open question #858 must still answer is the one Compose is genuinely different on: **a transient
draft may have no owning record yet**. Task 085 hit the same branch and resolved it *without* inventing
an acting-user derivation — see §5 below.

## 3. Their §4 blind spot — accepted, and already closed by their own PR #840

They flagged that a claim-read census cannot see the `PortfolioService` / `WorkspaceLayoutService` class:
an already-resolved caller id **misused downstream** (an Entra `oid` compared against `ownerid`, which
holds a Dataverse `systemuserid`). Their proposed second signature: *a `Guid.TryParse` whose failure path
drops a security predicate*, and *any comparison of a resolved caller id against `ownerid` / `owninguser`
/ `createdby` without translation*.

**That rule now exists, and they wrote it.** `tests/Spaarke.ArchTests/CallerIdentityGuardTests.cs`
(PR #840) ships:

- `Rule1_NoDirectIdentityClaimReadOutsideTheAllowlist`
- `Rule2_NoOwnershipPredicateGatedOnAGuidTryParse` ← their §4 signature
- `EveryAllowlistEntryCarriesAReason` · `EveryAllowlistEntryStillExists`
- two negative controls, including one proving comment-stripping respects string literals

## 4. ⚠️ Consequence for OUR task 082 — rescoped, not cancelled

Task **082** was *"caller-identity census — a downward ratchet on direct claim reads + the §11
four-primitive decision"*. Its ratchet half is **delivered** by #840, with both rules, an allowlist
carrying reasons, and non-vacuity controls. Building a second ratchet over the same population would be
the duplication this project exists to remove.

**What remains ours** is the half #840 explicitly did not take (their §8): the **four-primitive
consolidation decision**. #832 added `CallerResolution` and deliberately did **not** touch
`Spaarke.Core/Auth/CallerIdentity.cs`, so the primitives still number four. 082 is now that decision in
writing — consolidate, or justify each as distinct — plus recording their `sub == oid` app-only
discriminator warning (normalising `CallerIdentity.cs` toward the house `oid ?? NameIdentifier` pattern
would make that comparison `sub == sub`, i.e. always true).

082's dependency line is also stale: it says *"⛔ Seed the count only AFTER #832 and the ten worktrees
merge"*. #832 and #840 have merged; there is no count left to seed.

## 5. Their coverage warning — recorded, and it lands on us

> *Cluster 2a — the region you are editing — sits at **76.8% branch coverage**. A seeded-mutation pass
> over its neighbours found **eleven** documented guarantees with no test at all, two of which could
> destroy a user's document. A green suite in that region is weaker evidence than it looks.*

Taken seriously, not filed. This is the same lesson this project keeps re-learning from a different
direction — task 083's census reported three SpeAdmin routes where there were nine because its instrument
only scanned write sinks; the client/server agreement guard written yesterday had four defects its own
controls caught. **A green suite is evidence about what was asked, not about what is true.**

Practical consequence for the #858 patch: it must carry its own tests rather than lean on the
1,791-test Compose suite, and the two document-destroying guarantees should be identified before editing,
not after.

## 6. Their dead-reference note — one stale citation found

`Api/Filters/WorkspaceLayoutAuthorizationFilter.cs` is deleted (`7db7e91e3`) — confirmed absent from
master. One live citation remains in our notes:
`notes/task-074-route-authorization-forcing-function.md:170` names it as a governed surface. It is
descriptive prose in a completed task's notes, not an instruction, and
`RouteAuthorizationGuardTests`' own `GovernedFiles` does **not** reference it — so nothing is broken.
Flagged so a future reader does not go looking for the file.

## 7. What we owe them

1. **Patch create-on-save on master**, anchored on symbol names (`PromoteIfEphemeralAsync`, the
   `ResolveDriveIdAsync(request.ContainerId, …)` call, the `:1500` guard).
2. **Comment on #858 when it merges** — that is their signal to resume clusters 5a, then 2a/2b.

Until then `ComposeService.cs` is frozen for us and they continue task 070 on non-overlapping files.

## 8. Census state after all of this

The three Compose write sinks now live in `Services/Compose/ComposeSaveStorageCoordinator.cs`
(extracted by #806, split three ways by FR-S02's `If-Match`), declared in
`SpeWriteSinkContainerProvenanceGuardTests` as `ClientSupplied` owned by `#858`. `ComposeService.cs:1510`
— the create-on-save `ResolveDriveIdAsync` — is the remaining container **decision** and is what #858
patches. ArchTests are **176/176** after absorbing all of master; the six-failure baseline this project
carried throughout is gone.
