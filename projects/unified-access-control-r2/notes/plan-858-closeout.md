# Plan: close out #858 and unblock `spaarkeai-compose-r8`

> **Written**: 2026-09-01, by the #858 closeout session (agent).
> **State when written**: branch `work/unified-access-control-r2`, HEAD `841c24117` + this session's
> uncommitted working tree. Verified on that tree, 2026-09-01:
> **full `Sprk.Bff.Api.Tests` suite 0 failed / 11,750 passed / 57 skipped / 11,807 total**
> (baseline at `841c24117`: 20 / 11,726 / 57 / 11,803 — +4 = the new behaviour tests) ·
> Compose filter 1806/1806 · **ArchTests 176/176** (census reclassification holds) ·
> `Sprk.Bff.Api` build 0 warn / 0 err · `Spe.Integration.Tests` (also links the Shared helper
> folder) compiles clean.
> **Scope**: everything that remains between "the server code landed" and "#858 is closed and
> compose-r8 resumes", in execution order.

---

## 0. What this session landed (context for the ordering below)

The 20 red tests were **not** one shared cause. The partition (full evidence in the closeout report):

| Cause | Count | Fix class |
|---|---|---|
| **A — production defect**: `ResolveCreateOnSaveContainerAsync` called `GetSessionAsync` unconditionally; the empty-`SessionId` Browse flow (task 110, DESIGNED) hit `TenantCache`'s id guard → `ArgumentException` → **400** | 8 | production (`ComposeService.cs` — session read skipped when `SessionId` empty) |
| **B — fixture gap**: no `systemuser`/`businessunit` rows arranged → `ResolveForActingUserAsync` **throws** `SdapProblemException(403 acting_user_not_resolvable)` (it does NOT return null) | 12 | ONE shared arrangement: `tests/integration/Shared/TestActingUserBusinessUnit.cs`, wired into 3 fixtures' `ResetBoundaries()`/ctor |
| **C — production defect** (revealed by B's symptom): `ExecuteSaveAsync` had no `SdapProblemException` arm, so every typed 403/409 the new resolution path throws shipped as an **opaque 500** ("Save failed: SdapProblemException: …") — violating the resolver's own documented contract and the DEF-14 guarantee | (surfaced via B) | production (`ComposeSaveEndpoints.cs` — typed mapping arm before the catch-all) |
| **D — dead premise**: `CreateOnSave_WhenContainerIdMissing_Returns400` asserted the 400 guard #858 removed on purpose | 1 (overlaps B) | rewritten to the post-#858 contract (no configured container → 200 `storage-failed`, nothing written) |

Plus: 2 tests carried a dead **assertion** ("body containerId flowed to the SPE resolve") — rewritten
to assert server derivation, one with a **decoy** containerId proving the body value is ignored.
Plus: 4 NEW wire behaviour tests (authorized secure matter → its own container · denial → typed 403
`compose_record_access_denied`, never 500 · foreign session → binding ignored, probe never called ·
unsupported host type → typed 409 `compose_host_entity_unsupported`) — the FIRST tests of these
guarantees at any layer.
Plus: the sink-census entry for the create-on-save mint reclassified (see §3 — partially different
from what the handoff instructed, with reasons).

---

## 1. Client-side cutover (`ComposeWorkspace.tsx`) — NOT ship-together; BFF lands first

**Deploy-ordering claim CONFIRMED at three levels** (this was asked to be verified, not assumed):

1. `SaveComposeDocumentBody` (`src/server/api/Sprk.Bff.Api/Api/ComposeSaveEndpoints.cs:570`) is a
   System.Text.Json-bound record with **no `containerId` member** — STJ default drops unknown JSON
   properties, so an old client sending it changes nothing.
2. The record's own doc comment states the BFF-safe-first ordering explicitly (`:566-569`).
3. `UploadThenCreateOnSave_TransientDraft_…` now posts a **decoy**
   `containerId: "b!client-supplied-must-be-ignored"` and asserts the SPE resolve saw the
   server-derived container instead — the ignore is now a pinned wire-level property, not a belief.

**The client edits** (all in
`src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.tsx`; line numbers
verified 2026-09-01):

| Step | Site | Edit |
|---|---|---|
| 1.1 | `:2086` — `containerId: saveContainerId` in the **create-on-save body** | DELETE the field. This is the only save-path send. |
| 1.2 | `:1810` (`saveContainerId` derivation), `:1873-1906` (the pre-save `resolveContainer` retry loop incl. the `'no-container'`/`'unavailable'` refusal branches) | DELETE the whole pre-save container-resolution leg. Post-#858 the client cannot influence placement, so resolving before save is a wasted round trip, and its refusal UI double-reports what the server now reports honestly via `outcome: "storage-failed"` (which the client already renders — FR-S06). |
| 1.3 | `:871` (prop decl), `:1042` (destructure), `:1088-1091` (ref) — `resolveContainer` prop | Remove the prop **if** no other consumer path uses it after 1.2; otherwise leave and mark save-path-unused. Check each host that passes it (grep `resolveContainer=` across `src/solutions/**` + widget registries). |
| 1.4 | `:1081-1083` `containerIdRef` + senders at `:4042, :4102, :4375, :4465, :4529, :4585` | ⚠️ **Do NOT bulk-delete.** These six sites send `containerId` in OTHER payloads (load/documentRef/shuttle-class calls), not the save body. Each must be checked against its endpoint's actual contract; some may legitimately still address by container. Only the save path was #858's scope. If all six turn out to be dead after inspection, `containerIdRef` itself can go. |

**Sequencing**: BFF (this branch) merges first; the client change is cleanup on any later train.
An old client is proven-harmless (see decoy test); a new client against an old BFF would fail the
old `containerId is required` 400 guard — one more reason BFF-first, never client-first.

**Note for the same PR**: the seam tests' create-on-save POST bodies still carry inert `containerId`
fields (e.g. `ComposeOriginRoutingSeamTests:296/:372`, dedup tests via `world.ContainerId`,
`ComposeFidelitySeamTests:296`). Harmless (ignored by the wire DTO) and they incidentally keep
proving old-client compat; drop them opportunistically when the client cutover lands so the test
bodies match the new client shape. Deliberately NOT churned in this pass — no assertion reads them.

---

## 2. Behaviour tests for the new #858 paths — DONE in this pass (was: to be written)

Handoff asked "which exist vs. must be written". Verified answer before this pass: **none existed at
any layer** for the four new paths (the unit suite covered only acting-user resolvable/unresolvable;
grep for `compose_record_access_denied|compose_host_entity_unsupported|foreign` under
`tests/unit/**/Compose` returned nothing). Written this pass, all green, all in
`tests/integration/contract/Api/Ai/ComposeCreateOnSaveEndpointContractTests.cs`:

| Path | Test | Notes |
|---|---|---|
| Authorized matter → its container | `CreateOnSave_WhenSessionBoundToAuthorizedSecureMatter_WritesIntoTheMattersOwnContainer` | SECURE matter variant — the strongest claim (own container wins over a resolvable shared BU container); also pins the probe was asked about exactly (`sprk_matters`, matterId) |
| Unauthorized matter → 403 | `CreateOnSave_WhenCallerLacksAppendToOnBoundMatter_Returns403WithStableCode_NotOpaque500` | The defect-C prover: typed 403 + stable code + no `SdapProblemException` leak + nothing written |
| Foreign session → binding ignored | `CreateOnSave_WhenSessionOwnedByAnotherUser_IgnoresItsMatterBinding_AndDerivesFromActingUser` | Also pins the foreign matter is never even probed |
| Unsupported host type → 409 | `CreateOnSave_WhenSessionBoundToUnsupportedHostType_Returns409Refusal_NotAGuessedContainer` | project-bound session refused with `compose_host_entity_unsupported` |
| Unresolvable container → step failure | `CreateOnSave_WhenActingUsersBusinessUnitHasNoContainer_FailsContainerStepHonestly_AndWritesNothing` | The REWRITE of the dead-premise `WhenContainerIdMissing_Returns400` — wire twin of the unit rewrite |

Remaining test work: **none required for #858**. (Optional later: unit-level twins of the four —
low value since the wire tests exercise the real service + real resolver already.)

---

## 3. Sink-census + ArchTest reclassification — PARTIALLY done; handoff instruction corrected

The handoff said: move "3 `ComposeSaveStorageCoordinator` sinks + the create-on-save decision"
from `ClientSupplied` → `ServerDerivedRecord`. **Half of that is not true of the code and was not
applied**:

- ✅ **DONE (this pass)** — `Services/Compose/ComposeService.cs :: UploadSmallAsUserAsync #1` (the
  create-on-save MINT) reclassified `ClientSupplied` → `ServerDerivedRecord` in
  `tests/Spaarke.ArchTests/SpeWriteSinkContainerProvenanceGuardTests.cs`, with the new tracer
  (session → ownership → authorize → `ResolveForRecordAsync` / `ResolveForActingUserAsync`) and the
  stale "no server-side BU→container resolver / blocked on #806 / thread (entity,recordId) onto the
  request" narrative replaced (that proposed fix was REJECTED during implementation as relocating
  the defect). ClientSupplied work-list header recounted (7).
- ❌ **NOT moved, deliberately** — the 3 `ComposeSaveStorageCoordinator :: ReplaceFileContentAsUserAsync`
  ordinals trace `driveId parameter ← ComposeService.cs (replace branch) ← request.DriveId (client
  body)`. #858 changed the CREATE path only; the REPLACE path still takes the client's `DriveId`
  verbatim (`ComposeService.SaveAsync`, `request.DriveId` guard + `ReplaceWithPreconditionAsync`).
  Reclassifying them would be a false census entry — the exact failure mode the guard exists to
  prevent ("never reclassify a ClientSupplied site to make a build green"). They stay
  `ClientSupplied`, owner `#858`-family, and become follow-on work (next row).
- 🔲 **FOLLOW-ON (new task — suggest `096`; check `ls tasks/` first, numbering has collided 3×)**:
  convert the replace-path drive provenance. The shape already exists in the same census —
  `ComposeService.cs :: ReplaceFileContentAsUserAsync #1` (dedup path) is `ServerDerivedRecord`
  because it reads `match.DriveId/SpeId` from the Dataverse row. The replace path should derive
  drive+item from the authorized `sprk_document` row (by `DocumentRecordId` / alt-key) instead of
  `request.DriveId`, then the 3 coordinator entries move honestly. Blast radius: the replace path is
  OBO (user ACL constrains it), so this is LATENT-bypass class, not the app-only live-hole class —
  correct to sequence after #858, wrong to silently forget. When #858 closes, re-point the 3 entries'
  owner text from "#858" to the new task id in the same edit.
- 🔲 **`notes/task-083-sink-inventory.md`** — row 6 (Compose) still describes the pre-#858 state
  ("three sinks … stays behind PR #806"). Update the row to: mint = converted by #858 (see census),
  replace trio = remaining, owned by the follow-on task. One paragraph; do together with the
  `current-task.md` §858 status flip.

---

## 4. Closing #858 — the comment that unblocks compose-r8

Post on #858 after this branch's PR merges (compose-r8's stated resume signal is a comment on #858;
they resume clusters 5a → 2a/2b). Suggested body:

> **#858 is closed by `unified-access-control-r2`** (PR #887 branch, commits `841c24117` + the
> closeout commit).
>
> **What landed**
> - `SaveComposeDocumentRequest.ContainerId` DELETED (tombstoned in `IComposeService.cs`), the wire
>   field and the `containerId is required` 400 guard removed. The server derives the container in
>   `ComposeService.ResolveCreateOnSaveContainerAsync`: caller oid → session (ownership-checked) →
>   if a matter is bound, authorize via `CallerRecordAccessProbe` + `OperationAccessPolicy
>   entity.associate_document` (AppendTo — same key as Office save) → `RecordContainerResolver
>   .ResolveForRecordAsync`; no matter → `ResolveForActingUserAsync` (systemuser → BU →
>   `sprk_containerid`).
> - Contract points that differ from this issue's original proposal, all deliberate:
>   (1) record identity comes from SERVER-side session state, not the request — threading
>   (entity, recordId) through the save body would relocate the defect (a caller-named matter is the
>   same primitive one hop earlier); (2) an UNRESOLVABLE container returns the honest per-step
>   `storage-failed` projection (200), never a throw; denial/unsupported-host/unattributable-caller
>   throw typed 403/409; (3) host-entity support is a hard-coded `sprk_matter` const —
>   `BuildMatterHostContext` is the only producer — and any other type is refused with
>   `compose_host_entity_unsupported` (409), so a future project-bound session is visible, not
>   misfiled.
> - Two production defects found and fixed while landing it: the empty-`SessionId` Browse save
>   (task 110 flow) was 400-ing via the session store's id guard; and the new typed refusals were
>   surfacing as opaque 500s because the save route had no `SdapProblemException` mapping arm.
> - Tests: full BFF suite green (counts in the PR); 5 wire tests rewritten whose premise #858
>   inverted; 4 NEW wire behaviour tests covering authorized-matter/denial/foreign-session/
>   unsupported-host — the previously-untested guarantees compose-r8's coverage audit flagged.
> - Deploy ordering: BFF-first is safe — an old client's `containerId` is ignored (proven by a
>   decoy-container wire test). The client cutover (`ComposeWorkspace.tsx`) follows separately.
>
> **For compose-r8**: `ComposeService.cs` is unfrozen for you as of this comment; anchor on symbol
> names per our 2026-08-30 alignment note. The create-on-save container region now carries wire
> tests — extend them rather than re-deriving fixtures (shared arrangement:
> `tests/integration/Shared/TestActingUserBusinessUnit.cs`).

---

## 5. Ordered checklist to done

1. 🔲 Main session: review this working tree (2 production files, 8 test files incl. 1 new helper +
   census edit + this plan), run `code-review` + `adr-check` per task-execute 9.5 discipline.
2. 🔲 Commit on `work/unified-access-control-r2` (do NOT push from the agent — owner directive);
   suggested message: `fix(uac-r2 #858): repair the 20 wire tests + 2 production defects the
   server-derived container surfaced; census reclassification; 4 new behaviour tests`.
3. 🔲 Update `current-task.md` §858: server done → **#858 fully green**, with the cause partition
   (A/B/C/D above) so the "one shared cause" note doesn't get re-derived; update
   `notes/task-083-sink-inventory.md` row 6 (see §3).
4. 🔲 PR → master (protected; Router check; no review gate), publish-size + CVE checks per
   CLAUDE.md §10 (this change adds no packages and ~40 lines of BFF code — expect ~0 MB delta, but
   MEASURE and report per NFR-01).
5. 🔲 Post the §4 comment on #858 → compose-r8 resumes.
6. 🔲 Client cutover per §1 (any later train; not ship-together).
7. 🔲 File the replace-path drive-provenance follow-on task (§3, suggest 096) + re-point the 3
   census entries' owner text when it gets its number.
8. 🔲 Standing residual (already filed, owner-deferred, NOT #858's):
   `notes/finding-secure-transition-container-migration.md` — a draft created matter-less lands in
   the BU container and a later secure association moves nothing. Unchanged by #858 (documented in
   `ResolveForActingUserAsync`'s remarks and `ResolveCreateOnSaveContainerAsync`'s 🔴 note).

**Nothing else is knowingly deferred.** Items 6-8 are the only work not in this pass's tree, each
with its reason above (deploy-ordering independence; a census entry that would be false today;
owner-deferred standing finding).
