# BLOCKED-031 — Job B apply endpoint: no supported OBO/impersonated **write** path to Dataverse

> ## ✅ RESOLVED 2026-07-29 — owner chose **Option 2** (Path A + additive impersonated-write extension)
> The owner selected this doc's **recommended Option 2**: extend the shared write core with an optional
> `Guid? impersonateSystemUserId` so the Job B apply PATCH runs AS the confirming user via `MSCRMCallerID`
> (native Dataverse RLS + honest `modifiedby` = the confirming human — the "who accepted the change" the owner
> wants). True OBO-token-to-Dataverse (Option 3) rejected as unnecessary; app-only (Option 1) rejected because
> `modifiedby` would show the service principal. Go-live prerequisite acknowledged: BFF app user must hold
> `prvActOnBehalfOfAnotherUser` (flagged for task 060 deploy). Decision record:
> [`notes/031-write-identity-decision.md`](notes/031-write-identity-decision.md). **Implementation proceeding.**
>
> ---
>
> **Task**: 031 (P3, FULL rigor, opus/high, SECURITY-SENSITIVE — the only record-mutating task in r1)
> **Status**: ~~ESCALATED~~ → **RESOLVED (Option 2)** — escalation branch closed by owner decision.
> **Date raised**: 2026-07-29 · **Date resolved**: 2026-07-29
> **Raised by**: task-031 implementation agent, per root CLAUDE.md §6 / §6.5 and the task-031 brief.
> **No code was written at escalation time.** Implementation now proceeds under Option 2.

---

## Why this is a stop, not a proceed

The task's central, owner-locked invariant (POML `<goal>`, `<constraints>` NFR-05/ADR-028, brief) is:

> apply the confirmed proposal **under the confirming user's OBO** (delegated) — **NEVER app-only**, never a
> client-supplied identity; the write goes through the blessed `IActionSeam.UpdateRecordAsync`; do **NOT** fork
> a bespoke per-user Dataverse write.

The STEP 0 investigation establishes that **this codebase has no OBO (nor any per-user delegated) *write* path to
Dataverse at all**, and the blessed write seam is structurally app-only. Making the apply honor the confirming
user's Dataverse identity is therefore impossible without doing one of the three things the task explicitly
forbids or scopes out. Both the POML `<escalation>` trigger and the brief's DECISION GATE name this exact
situation as a legitimate stop.

---

## STEP 0 investigation — exactly how `IActionSeam.UpdateRecordAsync` resolves the write identity

Full call chain traced (all in this worktree):

1. `IActionSeam.UpdateRecordAsync(UpdateRecordRequest, ct)` — **no token/user parameter**; the identity is whatever
   the underlying client uses. (`src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/IActionSeam.cs`)
2. `ActionSeam.UpdateRecordAsync` → constructs `UpdateRecordActionCore(_fieldMappingService, _scopeFactory, _logger)`
   and calls `core.UpdateAsync(...)`. No identity is threaded. (`…/PublicContracts/ActionSeam.cs:102`)
3. `UpdateRecordActionCore.UpdateAsync` → coerces values, then calls
   `_fieldMappingService.UpdateRecordFieldsAsync(entity, recordId, payload, ct)`.
   (`…/Services/Ai/Nodes/ActionCore/UpdateRecordActionCore.cs:129`)
4. `IFieldMappingDataverseService.UpdateRecordFieldsAsync` — **no caller/impersonation parameter**.
   (`src/server/shared/Spaarke.Dataverse/IFieldMappingDataverseService.cs:33`)
5. Impl `DataverseWebApiService.UpdateRecordFieldsAsync` → `SendPatchAsJsonAsync($"{set}({id})", fields, ct)`.
   (`src/server/shared/Spaarke.Dataverse/DataverseWebApiService.cs:2063`)
6. `SendPatchAsJsonAsync` → `CreateAuthenticatedRequestAsync(HttpMethod.Patch, url, ct)` **with no
   `impersonateSystemUserId` argument** → the request carries only the app/service bearer token; **no
   `MSCRMCallerID` header is ever stamped on a PATCH**. (`DataverseWebApiService.cs:154`, `:121`)
7. The bearer token is an **app/service identity** — `ClientSecretCredential` in this build (ADR-028 says it
   *should* be `DefaultAzureCredential`/managed identity, but that is still an **app identity, not per-user OBO**).
   (`DataverseWebApiService.cs:40-56`, `:75-109`)

**Conclusion: `IActionSeam.UpdateRecordAsync` issues an app-only Dataverse PATCH.** It does not, and cannot as
shaped, run under the confirming user's delegated identity.

### How the codebase *does* do "delegated" Dataverse identity — and why it doesn't help the write

- Spaarke's delegated-Dataverse mechanism is **`MSCRMCallerID` impersonation** (`DataverseImpersonation.Apply`,
  `src/server/shared/Spaarke.Dataverse/DataverseImpersonation.cs`): the app user impersonates the caller's
  `systemuserid`; Dataverse applies effective privileges = **intersection** of app user + impersonated user (native
  row-level security). **This is Spaarke's answer to "run as the user," not an OAuth OBO token exchange to Dataverse
  — there is no OBO-to-Dataverse anywhere in the codebase.**
- Impersonation is wired **only into reads**: `CreateAuthenticatedRequestAsync`/`SendGetAsync` take an optional
  `impersonateSystemUserId`, and `ExecuteImpersonatedQueryAsync` / `RetrieveMultipleImpersonatedAsync`
  (`DataverseWebApiService.cs:135`, `:1888-1935`) issue impersonated **GET**s. `IImpersonatedCommunicationQuery`
  is explicitly a **read** seam (`Services/Communication/IImpersonatedCommunicationQuery.cs`).
- **No write method accepts an impersonation identity.** `SendPatchAsJsonAsync` / `SendPostAsJsonAsync`
  (`DataverseWebApiService.cs:144`, `:154`) do not take `impersonateSystemUserId`; `IGenericEntityService`
  `CreateAsync` / `UpdateAsync` / `BulkUpdateAsync` (`IGenericEntityService.cs:13-15`) have no caller parameter.
  **There is zero impersonated-write plumbing.**

### How the sibling user-initiated writes in the SAME endpoint family actually behave

Every user-initiated write on `/api/communications` (rename, pin, create-record-thread) follows the same de-facto
pattern — **resolve the caller server-side → authorize via an *impersonated read* → then write app-only**:

- Endpoints resolve the caller from `HttpContext.User` (via `ICallerSystemUserResolver` or
  `CommunicationThreadReadService.CanCallerSeeThreadAsync`), fail-closed **403** on an unresolved caller.
  (`Api/CommunicationEndpoints.cs` — `StartDirectThreadAsync`, `CreateRecordThreadAsync`, `RenameThreadAsync`,
  `SetThreadPinnedAsync`)
- The authorization gate is an **impersonated read** (`CanCallerSeeThreadAsync`).
- The **write itself is app-only**: `ThreadResolver.RenameThreadAsync` / `SetPinnedAsync` /
  `CreateRecordThreadAsync` call `_entityService.UpdateAsync` / `CreateAsync` with **no impersonation**
  (`Services/Communication/ThreadResolver.cs:322-421`).

So "app-only write after server-side caller resolution + an impersonated-read authorization gate" **is** the
established Spaarke realization of a user-initiated Dataverse write. The task-031 brief forbids me from silently
adopting it, because task 031's entire purpose is the OBO-not-app-only invariant — hence this escalation instead of
a quiet pivot.

### What ADR-028 actually says (relevant to the tension)

ADR-028 mandates **managed identity (an app/service identity)** for all server outbound, incl. "Dataverse service
identity" — **not** a per-user OBO token to Dataverse. Its OBO language is about **Graph/SPE** delegated file
access (ADR-007) and the client→BFF MSAL contract; for the external portal it even mandates **"no OBO"**. **ADR-028
does not describe, require, or provide an OBO token to Dataverse for writes.** The brief/coordination-doc phrase
"`IActionSeam.UpdateRecordAsync`, OBO, audited" (C-2) therefore encodes an expectation the current architecture
cannot satisfy literally.

---

## 🔔 ADR Conflict — Resolution Required (root CLAUDE.md §6.5 format)

- **ADR in question**: ADR-028 (Spaarke Auth Architecture v2), as applied via spec **NFR-05** and task-031's
  OBO-not-app-only invariant.
- **Specific rule being challenged / relied on**: "apply **under the confirming user's OBO** … **never app-only**"
  (POML `<constraints source="NFR-05 / ADR-028">`; brief). The literal reading assumes an OBO/delegated **write**
  path to Dataverse that does not exist in this codebase (ADR-028 itself mandates *app/service identity* for
  Dataverse outbound; the only delegated mechanism is `MSCRMCallerID` impersonation, which is read-only today).
- **Conflict**: `IActionSeam.UpdateRecordAsync` is structurally app-only, and no impersonated/OBO **write** path
  exists anywhere. The apply cannot honor the confirming user's Dataverse identity without one of: (a) an app-only
  write **[forbidden by the task]**, (b) a **bespoke per-user Dataverse write** routed around the seam **[forbidden
  by the task]**, or (c) a signature change to the **shared blessed write core** to add impersonation — a new
  first-ever *write* auth mechanism on a shared seam that the brief scopes out ("no new auth mechanism"; "the route
  is new only because REST needs a new path").
- **Proposed paths** (human chooses): see the three options below.
- **Alternative considered and rejected**: silently mirroring the sibling app-only-write pattern (Path C content
  without sign-off). Rejected because the task explicitly forbids proceeding app-only without surfacing the
  conflict, and because it would ship the ONLY record-mutating endpoint in r1 with an identity contract weaker than
  its spec states, unacknowledged.

---

## Options for the human (pick one; then I implement)

### Option 1 — Path C (pivot to comply): mirror the established Spaarke user-write pattern; reframe "OBO"
- **What**: Implement the apply exactly like rename/pin/create-thread — resolve the confirming caller server-side
  (fail-closed 403), re-validate the `sprk_emailupdatefield` allow-list at apply time, re-verify the citation,
  write via `IActionSeam.UpdateRecordAsync` (**app-only PATCH**), and write the append-only `sprk_emailreviewlog`
  human-decision row with `sprk_actortype = Human` and actor = the confirming user. Compliance rests on
  **server-side caller resolution + apply-time allow-list re-validation + append-only audit**, not on Dataverse RLS
  at the write.
- **Faithful to intent?** Partially. The *actor of record* is captured (audit row), but the Dataverse
  `modifiedby`/RLS on the write is the app user, not the confirming user — i.e. **app-only at the write**, which the
  task's literal wording forbids.
- **Blast radius**: none beyond task 031; matches every existing Spaarke user-write.
- **Requires owner to accept**: that "the confirming user's write" in Spaarke = caller-resolution + audit, and that
  literal OBO/impersonation on the write is **not** applied. (This is effectively an ADR-028/NFR-05 **project-scoped
  exception, Path A**, documented in spec/design.)

### Option 2 — Path A (project-scoped exception) + minimal additive seam extension for an impersonated write  ⟵ recommended
- **What**: Add an **optional** `Guid? impersonateSystemUserId = null` through the write path so the apply PATCH runs
  **AS the confirming user** via `MSCRMCallerID` (Dataverse RLS applies + honest `modifiedby` = the confirming
  user). Threading points: `SendPatchAsJsonAsync` → already-impersonation-capable `CreateAuthenticatedRequestAsync`
  (trivial), `IFieldMappingDataverseService.UpdateRecordFieldsAsync`, `UpdateRecordActionCore.UpdateAsync`, and
  `IActionSeam.UpdateRecordAsync` (all as an **optional, default-null** param → byte-unchanged for every existing
  caller, incl. the node executors). Then the apply endpoint resolves the caller (`ICallerSystemUserResolver`),
  passes the `systemuserid`, plus allow-list re-validation + citation re-verify + audit row.
- **Faithful to intent?** Yes — this is the genuine "run the write as the confirming user; Dataverse row-level
  security applies under their identity" that NFR-05/ADR-028's *intent* describes, using Spaarke's native delegated
  mechanism (impersonation), not a bespoke write.
- **Blast radius**: touches the **shared** `IActionSeam` / `UpdateRecordActionCore` / `IFieldMappingDataverseService`
  write core (additive/optional, but a shared-seam signature change) and introduces the **first impersonated write**
  in the codebase. **Go-live prerequisite**: the BFF app user must hold `prvActOnBehalfOfAnotherUser` (per
  `DataverseImpersonation` remarks) or the impersonated PATCH fails closed at Dataverse.
- **Requires owner to approve**: (i) the additive impersonation param on the shared write core (is this "no new auth
  mechanism" acceptable as an additive extension of the *existing* read-impersonation mechanism to writes?), and
  (ii) the `prvActOnBehalfOfAnotherUser` config prerequisite. Recommend documenting as an ADR-028 **project-scoped
  exception (Path A)** in `spec.md`/`design.md`, since it extends impersonation to the write plane.

### Option 3 — Path B (ADR amendment) for literal OBO-token exchange to Dataverse
- **What**: If "OBO" must mean a real OAuth on-behalf-of **token** minted for the user against the Dataverse
  audience, that is a **new auth mechanism the codebase does not have** and ADR-028 does not describe for Dataverse.
  It would need new token-exchange infra + an ADR-028 amendment.
- **Recommendation**: **Not recommended.** Impersonation (`MSCRMCallerID`) is the Dataverse-native delegated
  mechanism and achieves the same RLS/authorship guarantee; true OBO-to-Dataverse is not Spaarke's model and is
  disproportionate for one endpoint.

---

## Recommendation

**Option 2 (Path A + minimal additive impersonated-write extension).** It is the only option that literally
satisfies "the write runs under the confirming user's identity / Dataverse RLS applies under them" while reusing the
blessed seam (no bespoke write) and Spaarke's existing impersonation mechanism (no true new auth mechanism — an
additive extension of read-impersonation to writes). It needs two explicit owner acknowledgements: the additive
shared-seam param, and the `prvActOnBehalfOfAnotherUser` go-live prerequisite.

If the owner prefers zero change to the shared write core, **Option 1 (Path C)** ships fastest and matches every
existing Spaarke user-write, but must be accepted as a documented ADR-028/NFR-05 project-scoped exception because
the write is app-only (compliance via caller-resolution + audit, not RLS on the write).

**I will not proceed on either until the owner selects a path** (per the brief: do not app-only silently, do not
fork a bespoke write). Once selected, the rest of the apply flow (allow-list re-validation, citation re-verify,
audit-row write, ADR-008 filter + ADR-019 ProblemDetails, unconditional registration, fail-closed 403, the five
negative/auth tests) is unaffected by the choice and ready to implement.
