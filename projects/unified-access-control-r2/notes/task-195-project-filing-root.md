# Task 195: a project is filed under its matter through the regarding pair

> Design note, written before code. It is checked by the owner before implementation.
> Owner choice (a), #2352, 2026-10-27: the regarding pair (`sprk_regardingrecordtype` + `sprk_regardingrecordid`) is the project's one filing root. The Project ↔ Matter N:N (`sprk_Project_Matter_nn`, intersect `sprk_project_sprk_matter`) stays an association and confers nothing.
> Facts were gathered on 2026-10-31 at master b083d62183, with a read-only dev query. All 7 N:N rows in dev were read.

## 1. Facts

**How the wizards write today. Neither writes the pair.**
- CreateProjectWizard:
  - creates the project as the user with Xrm.WebApi (`projectService.ts:368`);
  - links the matter only through the N:N, with a cookie `fetch` to `sprk_Project_Matter_nn/$ref` (`CreateProjectWizard.tsx:62-81`, :743-756).
- CreateMatterWizard, when given an existing project, writes only the N:N (`CreateMatterWizard.tsx:114-141`).

**Server paths**
- **`RecordCreationService`** accepts the pair on a project create (`RecordTableRules.cs:169-198`, `CallerWriteAuthorization.cs:89-129`). Only chat, Office quick-create and the budget inquiry call it; no browser route creates a project through the BFF.
- **The child-records re-file route** (`PATCH /api/v1/child-records/{table}/{id}`) refuses `sprk_project` (`ChildRecordEndpoints.cs:84-87`). Adding the table alone is not enough: `OwnedChildWrite.RefileAsync` treats a root as "not a re-file" (:890-895), and the route would then send the caller's own PATCH.
- **The chat update tool** (`DataverseUpdateRecordHandler.cs:239-368`) is the only path today that sets the pair on an existing project. It does all of the following:
  - completes the pair;
  - checks AppendTo on the named record;
  - runs the secure gate;
  - writes the pair app-only;
  - secures afterwards.

  It writes with `If-Match: *`, does no read-back and does not enforce one root.
- **Precedent for a versioned write with read-back:** `SecureChildReconciler.ReownFiledRootAsync` (:1047-1075).

**Field security on the pair**
- The writers profile holds only the two BFF app users.
- The readers profile is read-only.
- The System Administrator profile keeps full rights, which is platform behaviour.

**What setting a pair does in dev today (both root-owner switches on)**
- **Owner.** It is not changed inline. Within about 5 minutes, the root-owner step re-owns the project to the default team of the matter's unit (`SecureRootInheritance.Cascade.cs:1853-2080`). If the matter is person-owned, the matter's own unit decides (`RecordOwnershipResolver.cs:3796-3798`). The project's children follow through the ordinary-moves pass, which runs every 2 minutes.
- **Secure matter.** The project becomes secure-if-any (`ProvisionInheritedAsync`). Its children are secured and its documents move into its own container. Nothing releases it.
  - Container check (done at implementation, 2026-10-31; was the open check). How `RecordContainerResolver` decides:
    - A record that is secure itself resolves to its own container and skips the ancestor walk (:705).
    - Any other record walks its ancestors transitively, past a secure hop. Two different secure ROOTS anywhere above it answer `container_ancestor_ambiguous` (409, :874-891); the count is of roots, not of containers. `ChildRecordContainerResolutionTests.TwoDifferentSecureRoots_AnywhereInTheChain_AreAmbiguous("stacked")` pins this.
    - `ProvisionInheritedAsync` gives an inherited-secure project a container of its own; one container per secure family is task 178 (#1506), not done.
  - The three cases:
    - **(a) Ordinary project filed under a secure matter.** Secure-if-any secures the project with its own container. The project's own content resolves to that container. Every record filed under the project (documents, to-dos, and so on) then sees two secure roots (the project and the matter): its content is refused with `container_ancestor_ambiguous`.
    - **(b) Secure project (own container) filed under a secure matter.** The same: the project's own content resolves, everything filed under it is refused.
    - **(c) Inherited-secure project re-filed from secure matter A.** To secure matter B: records under it were already ambiguous under A and stay so under B. To an ORDINARY matter: one secure root (the project) remains above its records, so nothing is ambiguous, and the project stays secure: the follow-parents step never loosens on a re-file (round 87 item 5, `SecureRootInheritance.Cascade.cs` `Decide`: a stored value beyond the new floor becomes the record's own), the root-owner step never touches a root whose `sprk_issecure` is not exactly false (`SecureChildReconciler.ReownFiledRootAsync`), the old parent's inherited shares end in the provenance pass, and the project keeps its own container.
  - What the core does: filing under a secure matter is refused before any write with 409 `project_filing.secure_matter_unsupported` (cases a, b and secure A → secure B), as the fail-closed interim until one container per secure family (#1506). Re-filing from a secure matter to an ordinary one is filed, and the project stays secure (the ordinary half of c). A flag that cannot be read is refused as undetermined, never "not secure". The resolver is unchanged.
  - The same condition already exists, outside this task, for a project CREATED under a secure matter (`RecordCreationService`) and for work assignments under secure matters: to be filed as a finding, not changed here.
- **Matter in another unit.** Visibility follows the new owning unit, and the expected document container follows that unit. In dev, BU1 and the root unit share one container, so no files move.

**Dev data**
- 28 projects. None has a pair, all are team-owned and 3 are secure.
- Exactly 7 projects have N:N rows, and each has exactly one matter. All 7 are backfill candidates, and none of them or their matters is secure.
- If backfilled, these 3 projects change unit and the other 4 do not:

| Project | Matter | Effect |
|---|---|---|
| Project 6 (e42aa40d) | LITG-153116 (person-owned, root unit) | BU1 team → root team; 3 documents follow |
| PRJ-000034 (a8dbfb72) | PAT-415062 | root → BU1 team; 1 document follows |
| PRJ-000038 (479c5a63) | PAT-191184 | root → BU1 team; no documents |

- No server code reads the N:N.
- Nothing reports "an N:N matter but no pair". The root-owner step's per-run report already reads every project's pair.
- Field-mapping W7 already models the pair as the Matter → Project edge (`RecordFamilyEdges.cs:231-233`). Task 040 is gated on this task.

## 2. Proposed design (for the owner check)

1. **One server core, two callers.**
   - Move the update tool's project-pair sequence into one helper beside `OwnedChildWrite.RefileAsync`.
   - Call it from the update tool and from the child-records route, which accepts `sprk_project` with a filing-only body (the pair only).
   - No new endpoint, table or column (I-2: one server-side owner).
2. **Order of the core.**
   1. Read the row and its version.
   2. Apply the one-root rule.
   3. Ask AppendTo on the matter and Write+Append on the project, both as the caller.
   4. Run the secure gate, which fails closed.
   5. Write the pair app-only, conditional on the version read; a changed version answers 409.
   6. Read back and log the previous pair.
   7. Secure afterwards, inline. Ownership then follows through the existing root-owner and ordinary-moves passes, with no new owner code.
3. **One root; re-file allowed (owner, 2026-10-31).**
   - Same matter again: 204, nothing written.
   - A pair already naming another matter: the project is **re-filed** to the new matter through the same core (the version condition, read-back, and the previous pair logged). The earlier N:N row stays as an association and confers nothing.
   - Filing under a secure matter secures the project. Leaving a secure matter never un-secures it (nothing releases isolation).
   - The chat update tool gets the same rules through the same core.
4. **The wizards** keep writing the N:N as today, then call the route for the selected matter. A failure shows as a visible warning; it is never skipped silently. The client never writes the pair.
5. **One create path (owner, 2026-10-31).** Adding a matter to a project's matter list goes through the wizard only. The model-driven app's out-of-the-box "Add Existing" and quick-create/"New" commands on the Project ↔ Matter subgrids are replaced with a command that opens the wizard (ribbon change, `ribbon-edit`; no plugins, I-2). The root-owner step's report still counts the two drift cases, because a System Administrator can still associate through the platform:
   - projects with an N:N matter but no pair;
   - projects whose pair matter has no N:N row.
6. **Backfill.**
   - `scripts/Set-ProjectFilingFromMatterLink.ps1` runs report-only by default.
   - It sets the pair for each project with exactly one N:N matter and no pair, through the same route. That makes the BFF the setter, so `Repair-RegardingPairFiling.ps1 -Verify` trusts the pair.
   - It reports and skips projects with more than one matter or an existing pair.
   - `-Apply` needs the owner's go.
   - In dev it would cause the 3 unit moves above over about 5–7 minutes, with no secure change and no file moves.
7. **Known limit.** A System Administrator can still write the pair from a client, which is field-security behaviour of the platform. The re-file census and the secure gate on the BFF paths are unchanged.

## 3. Owner decisions (2026-10-31, recorded in `notes/decisions.md`)

1. Re-filing to another matter is allowed (§2.3). Nothing is ever released from isolation.
2. Adding a matter to a project's matter list goes through the wizard only; the out-of-the-box subgrid commands are replaced (§2.5).
3. Backfill accepted with all 3 unit moves, applied after deploy, report first.

The secure-matter container check (open check above) is engineering work. It is done and tested before the wizard path is enabled for secure matters.

## 4. Implementation (server half, 2026-10-31)

**What was built**
- `Services/Dataverse/ProjectMatterFiling.cs`: the one core, `FileAsync`, beside `OwnedChildWrite.RefileAsync`. The chat update tool's project-pair sequence (`OwnedChildWrite.PatchWithBffManagedPairAsync`, with `If-Match: *` and no read-back) is removed; the tool calls the core.
- `PATCH /api/v1/child-records/sprk_project/{id}` dispatches to the core before the child re-file. It accepts exactly this body, and nothing else (400 `child_record.not_filing`):

  ```json
  {
    "sprk_regardingrecordid": "<matter id>",
    "sprk_regardingrecordtype@odata.bind": "/sprk_recordtype_refs(<the sprk_matter record type id>)"
  }
  ```

  204 when filed, and when already filed under that matter. 404 `child_record.not_found` for a project or matter the caller cannot use or that does not exist (one answer). 403 `child_record.denied` without Write and Append on the project. 400 `project_filing.not_a_matter`. 409 `sdap.inherit.changed_concurrently`, `project_filing.secure_matter_unsupported` (#1506). The secure-root gate's refusals keep their usual statuses.
- The core's order: the pair must be whole and name a matter (from the payload and the catalog, read as the caller) → the caller can read the project → the application reads the pair and `versionnumber` → as the caller, Write (+Append) on the project and AppendTo on the matter and the record-type row → same pair: nothing written → the secure-root gate (fail closed) → the secure-record rules (§1) → the chat tool's other columns as the caller's PATCH → the pair written by the application only if the version is unchanged → read back, previous pair logged → re-stamp → `SecureAfterWriteAsync`. No owner is written; the root-owner step and ordinary-moves pass move ownership as before.
- The root-owner report (`rootOwners` in the job's ResultJson) has two new counts, report only: `projectsWithMatterLinkButNoPair` and `projectsWhosePairMatterHasNoLink`. The N:N is read paged under the listing's bound (5,000 × 20); an unreadable read reports null and a problem line.

**Deviations from §2 (for the owner)**
1. §2.3 said filing under a secure matter secures the project. The container check (§1) shows that leaves every record under the project refused by the storage resolver, so the core refuses it (`project_filing.secure_matter_unsupported`) as the fail-closed interim until #1506 (accepted by the main session, 2026-10-31). This also changes the chat update tool: filing a project under a secure matter by chat was allowed before and is now refused.
2. Re-filing away from a secure matter to an ordinary one is filed (an earlier draft refused it; removed after review, F1): the project stays secure through the existing rules (§1, case c), which is the owner's "leaving a secure matter never un-secures the project". The chat tool's clear of the pair is allowed the same way.
3. "Same matter → 204" is decided after the caller checks, not before them, so that a 204 never tells a caller who cannot file the project what it is filed under. The row and version are still read first.
4. The chat tool may still clear a project's pair (both columns null) through the core; the route never accepts a clear.

**Component justification (CLAUDE.md §11)**

| New surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `ProjectMatterFiling.FileAsync` (static helper, no DI registration) | The chat tool's pair sequence (removed) and `OwnedChildWrite.RefileAsync` | `RefileAsync` is the child re-file, which treats a root as "not a re-file" and re-derives an owner; a root's version-conditioned pair write is a different sequence, so it sits beside it | The wizards cannot file a project (the pair is field-secured) and the chat tool keeps writing it with `If-Match: *` and no read-back |
| Codes `project_filing.not_a_matter`, `.secure_matter_unsupported` (the conflict reuses `sdap.inherit.changed_concurrently`) | `child_record.*` and the `sdap.inherit.*` codes | No existing code says "not a matter" or names the secure-matter refusal (#1506); the conflict code is reused | Clients and the backfill script cannot tell these refusals apart from a generic 400/409 |
| `sprk_project` on the existing PATCH route (a dispatch, no new endpoint) | `PATCH /api/v1/child-records/{table}/{id}` | It is the extension: the same route and handler, a table-specific branch | The browser has no BFF path to file a project |
