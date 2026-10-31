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
  - Open check: `RecordContainerResolver.cs:705` and :874-891 refuse `container_ancestor_ambiguous` when two different secure roots sit above a row. A secure project filed under a secure matter must be checked against this rule before the wizard can file under a secure matter.
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
3. **One root.**
   - Same matter again: 204, nothing written.
   - A pair already naming another record: 409 `child_record.project_already_filed`, nothing written.
   - The chat update tool gets the same rule. Moving a project to another matter is out of scope here; it would be a separate, explicit re-file.
4. **The wizards** keep writing the N:N as today, then call the route for the selected matter. A failure shows as a visible warning; it is never skipped silently. The client never writes the pair.
5. **N:N links added in the model-driven app subgrid** (no plugins, I-2) do not file the project. The root-owner step's report counts two cases, and the project stays unfiled until the pair is set:
   - projects with an N:N matter but no pair;
   - projects whose pair matter has no N:N row.
6. **Backfill.**
   - `scripts/Set-ProjectFilingFromMatterLink.ps1` runs report-only by default.
   - It sets the pair for each project with exactly one N:N matter and no pair, through the same route. That makes the BFF the setter, so `Repair-RegardingPairFiling.ps1 -Verify` trusts the pair.
   - It reports and skips projects with more than one matter or an existing pair.
   - `-Apply` needs the owner's go.
   - In dev it would cause the 3 unit moves above over about 5–7 minutes, with no secure change and no file moves.
7. **Known limit.** A System Administrator can still write the pair from a client, which is field-security behaviour of the platform. The re-file census and the secure gate on the BFF paths are unchanged.

## 3. Decisions for the owner

1. Approve the design in §2: one shared core, the child-records route, and the one-root 409.
2. N:N links added in the subgrid: report only (proposed), or also give staff a "file under this matter" action?
3. Backfill: accept the 3 unit moves, including Project 6 moving to the root unit because its matter LITG-153116 is person-owned in the root unit?

The secure-matter container check (open check above) is engineering work. It is done and tested before the wizard path is enabled for secure matters.
