# Task 046 — `Secure Project Owner` role

> **Completed**: 2026-08-25 · Rigor FULL · opus @ xhigh · live config against `spaarkedev1`
> **Deliverables**: the Dataverse role (live) · [`docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md`](../../../docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md) · design §5.1a / §5.1a-2 / §5.1d / §5.2 · spec NFR-05
> **Outcome**: role shipped as specified. **A blocking security finding was confirmed that makes the
> surrounding isolation model non-functional today** — see §3. One owner decision required.

---

## 1. What shipped

`Secure Project Owner`, `roleid e4ebabd9-b4a0-f111-aaac-000d3a99d1d7`, in BU `Secure Project`
(`d9ec0b6f-80a0-f111-aaac-000d3a99d1d7`).

| Property | Final state |
|---|---|
| Privileges | **exactly 1** — `prvReadsprk_Project` @ **User (`Basic`)** depth |
| Assigned to | the `Secure Project` default owner team (`daec0b6f-…`) and **nothing else** — 0 users, 0 other teams |
| `System Administrator` on that team | **REMOVED** |
| Team members | **0** |
| Test artifacts | probe project deleted; 0 `sprk_issecure=true` rows; 0 projects in the secure BU |

Assignment was re-proven **after** the System Administrator removal, which is the only ordering in
which the proof means anything.

---

## 2. The privilege answer — hypothesis vs. reality

design §5.1a specified *"Create / Read / Write / Delete / Append / AppendTo / Share on `sprk_project`
and its child entities, at **Business Unit** depth."* Tested, that is wrong in every dimension:

| | Hypothesised | Actually required |
|---|---|---|
| Privileges | 7 | **1** (`Read`) |
| Depth | Business Unit (`Local`) | **User (`Basic`)** |
| Child entities | included | not required |

**Evidence for `Read`** — Dataverse named it itself when it was absent:

> `Principal team (Id=daec0b6f-…, type=9, teamType=0, privilegeCount=5, businessUnitId=d9ec0b6f-…), is missing prvReadsprk_Project privilege (Id=9c329413-…)`

The reported `privilegeCount=5` matched the role's actual count exactly — that is what established the
message described current state, not a stale cache.

**Evidence `Write` is NOT required** — with `Read` only (role verified at 5 privileges: 4 platform
SharePoint + `Read`), assignment was `ALLOWED` on 3 consecutive polls spanning ~50 s, and
`owningbusinessunit` moved root `Spaarke` → `Secure Project`. The team is an ownership anchor, never an
actor; the BFF application user performs every mutation.

**Why `User` depth is correct rather than merely tighter**: the team owns exactly the secure projects,
so "records this principal owns" already covers 100% of the intended scope. Wider depth adds reach
without adding capability — and re-opens §3.

---

## 3. 🔴 The blocking finding — secure projects are NOT isolated in dev

**Proven empirically, not inferred.** A secure project (`sprk_issecure = true`, owned by the
`Secure Project` owner team, `owningbusinessunit = Secure Project`) was **read successfully via
impersonation** (`MSCRMCallerID`) by **`Test User 1`** — an ordinary enabled user in the root BU holding
only `Basic User`, `Spaarke Basic User`, `Spaarke Office Add In User`, `Spaarke Reporting Access Viewer`.
It also appeared in an unfiltered `sprk_projects` list as that user, 1 of 19 rows.

**Cause**: `Spaarke Basic User` grants `prvReadsprk_Project` at **`Deep`** depth (mask 4, "Parent: Child
Business Units"). `Deep` held at the **root** BU reaches every descendant, and `Secure Project` is a
child of root.

**This is not a new defect.** design §5.2 recorded the same depth census on 2026-08-20 and carries an
operator decision (2026-08-21) to restructure the BU tree. What 046 adds is the **end-to-end proof**:
§5.2 inferred the exposure from depth masks; this exercised the entire mechanism against a real record
owned by the real team. The inference was right, and **the prerequisite is still open**.

**A correction I made mid-task**: I first wrote that no BU-tree rearrangement could fix this, since
every BU is a descendant of root. That holds only *while users sit in root*. §5.2's fix moves users into
an Operations BU and makes the secure BU a **sibling** — a sibling is not a descendant, so `Deep` at
Operations does not reach it. The tree fix works; it has not been applied.

**The negative control passed**, which is what makes the diagnosis precise: a principal holding
`prvReadsprk_Project` at `Basic` depth (`Support User`) was **denied** on the same record. BU containment
works exactly as designed — once no ordinary role holds `Deep` or `Global`.

### Depth census, `prvReadsprk_Project`, live 2026-08-25

| Role | BU | Depth | Held by |
|---|---|---|---|
| Service Reader · Service Writer · System Customizer | root | Global | application users only — **no human** |
| System Administrator | root | Global | app users + `Ralph Schroeder`, `Delegated Admin` (expected) |
| **`Spaarke Basic User`** | root | **🔴 Deep** | **`Test User 1`**, `Ralph Schroeder` (external identity) |
| Support User | root | Basic | `Support User` (Microsoft-managed) |
| `Secure Project Owner` | Secure Project | Basic | the owner team only |

### Consequences

1. **Task 047 cannot conclude "isolation works"** — only "provisioning runs". Different claims; the
   second does not imply the first.
2. **FR-28's share→read assertion is untestable** until this is fixed: every human with `sprk_project`
   Read holds `Deep` or `Global`, so no record exists that they cannot already read. Nothing to
   discriminate against.
3. **NFR-05 must assert depth, not BU reachability** — see §5.

---

## 4. Child-entity ownership — answered, and it is worse than the parent case

Enumerated from live relationship metadata: **18 Spaarke business entities carry a lookup to
`sprk_project`, via 19 lookups** (the POML listed 3). `sprk_document` carries **two** — `sprk_project`
*and* `sprk_relatedproject` — so any "is this on a secure project?" test that checks one misses half the
cases. Full table in design §5.1d.

**Nothing assigns children to the secure team**, so a document created by an ordinary user is owned by
that user in *their* BU regardless of the parent's BU. Children are not isolated at all, independent of
§3, and would remain unisolated even after §3 is fixed.

**Filed, not implemented** (per the task's own instruction, and CLAUDE.md §11). Extending task 021's
assign is the wrong fix: children are created continuously long after provisioning returns, so a
one-shot assign cannot cover them — this needs a create-time rule. It also interacts with the
evaluator's existing 1-hop child inheritance, which must be reasoned about rather than discovered.
Sequence with `spaarke-secure-project-r1`, which owns the container half of the same problem.

---

## 5. Doc amendments made

| Doc | Change |
|---|---|
| design §5.1a | Privilege list corrected 7→1 and BU→User depth, with the forcing error quoted; platform-injection + cache-lag method notes |
| design §5.1a-2 (new) | The depth defect, the proof, the census, two fixes with measured blast radius, the self-correction |
| design §5.1d (new) | Child-entity enumeration + recommendation to file separately |
| design §5.2 | Cross-referenced the end-to-end proof; marked still-unremediated |
| spec NFR-05 | Exemption narrowed to one privilege at one depth; assertion restated as a **depth** assertion naming `Deep`/`Global`; noted the clause currently **fails**; prefer the empirical (impersonated-read) form over a configuration check |
| `docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md` (new) | Full reproducible runbook |

---

## 6. Platform behaviours worth remembering

**Privilege caching lags role edits by ~one operation.** An early pass produced a false *"assignment
allowed with zero privileges"* — which, taken at face value, would have justified shipping a role that
grants nothing. Defences: re-probe until stable across ≥3 polls, and cross-check `privilegeCount` in the
denial against the role's real count. A zero-privilege control run is worth the minute.

*This is a fourth cause of a misleading empirical result, alongside the three already in
`current-task.md`: test at the wrong level · perturbed code unreachable · a fake that ignores part of
the contract. The new one is: **the platform answered from a stale cache.** All four share a shape — the
observation was real, but it was not an observation of the thing you thought.*

**Creating a role injects ~9 privileges you did not ask for**, at `Global` depth (SDK/plugin metadata
reads, legacy SharePoint integration). `AddPrivilegesRole` **re-injects the SharePoint four** on every
call. `ReplacePrivilegesRole` does **not** remove them. Only `RemovePrivilegeRole` does — one per call,
parameter is an **entity reference** named `Privilege`, not a GUID named `PrivilegeId`.

**`pac`'s active profile pointed at production** while this work targeted dev. Avoided `pac` entirely;
minted tokens against an explicit environment URL and verified the org name before every mutation.

**Dataverse refuses to share with the Microsoft-managed support user** (*"The support user has
insufficient privileges. OrgType :5"*), which is why `Support User` served only as the negative control.

---

## 7. Acceptance criteria

| Criterion | Status |
|---|---|
| Role exists in the secure BU, assigned to its default owner team | ✅ |
| `System Administrator` removed; assignment re-proven **after** removal | ✅ |
| Every privilege justified by a recorded failure | ✅ — 1 privilege, forcing error quoted (§2) |
| Team has 0 human members; role on no user and no other team | ✅ verified post-change |
| A non-admin cannot read a secure project except by explicit share | ❌ **FAILS** — §3. Not a defect in this task's deliverable; the role is correct. The failure is `Spaarke Basic User`'s `Deep` depth (§5.2 prerequisite) |
| A shared non-admin CAN read it, proving Read was not stripped | ⚠️ **partially** — negative half verified (`Basic`-depth principal denied). Positive half **untestable** until §3 is fixed; nothing was stripped from any ordinary role |
| Runbook reproduces the configuration on a fresh environment | ✅ |
| design §5.1a + spec NFR-05 state the depth actually required | ✅ |
| Child-entity question answered from live metadata, with a recommendation | ✅ §4 |

**7 of 9 met; 1 fails and 1 is partial — both for the same external reason (§5.2's unremediated depth
prerequisite), not because of anything in this task's scope.** Reported rather than worked around: the
POML is explicit that widening the role to make a failure disappear is forbidden, and narrowing an
ordinary end-user role is an owner decision.

---

## 7b. Follow-up round — operator testing, 2026-08-25 (after the task was first marked complete)

Manual testing in the MDA found two things the agent-side work missed, and **validated the §5.1a-2
fix**. Both corrections are folded into design §5.1a / §5.1a-2, spec NFR-05, and the runbook.

### ✅ Fix A validated — the model works end-to-end

`Spaarke Business Unit 1` created as a child of root; `testuser1@spaarke.com` moved into it; user
**kept `Deep` depth**. Because that BU and `Secure Project` are **siblings**, `Deep` covers the user's
own subtree and cannot reach the secure BU.

| Test | Result |
|---|---|
| Matter owned by `Spaarke Business Unit 1` | visible |
| Records in other BUs | not visible |
| Same Matter reassigned to `Secure Project` BU/team | **DENIED** ✅ |
| Same Matter after an explicit **share** | visible ✅ |

**This discharges §3's consequence 2 and 3.** FR-28's share→read half is no longer untestable — it was
tested and it **passes**. And the correct statement of the guarantee is not "reduce the depth" but
**"do not let ordinary users sit at or above the secure BU in the tree"**. `Deep` is fine; `Deep` held
at an *ancestor* of the secure BU is not.

⚠️ **Migration caveat**: existing records were left in the root BU, so the relocated user can see none
of them. Moving users out of root is therefore a **data-migration** decision as much as a security one.

### 🔴 My role was under-scoped — `sprk_issecure` is on THREE entities, not one

`Secure Project Owner` shipped covering `sprk_project` only, because this task and design §5.1a both
talk about *projects*. Live attribute metadata says otherwise:

| Entity | `sprk_issecure` |
|---|---|
| `sprk_project` | ✅ |
| **`sprk_matter`** | ✅ |
| **`sprk_workassignment`** | ✅ |
| `sprk_servicerequest` | ❌ |
| `sprk_document` | ❌ |

So assigning a **Matter** to the owner team failed. **The field workaround was to add
`Spaarke Basic User`, `Spaarke AI Analysis User` and `Spaarke Reporting Access Viewer` to the owner
team** — recreating precisely the over-grant posture that removing System Administrator was meant to
end. That is the predictable response to this failure mode, which makes the under-scoping my defect,
not a mistake in the workaround.

**Fixed**: role now holds `Read` @ `Basic` on all three; the three workaround roles were removed; and
assignment was re-proven for **each** of project, matter and work assignment with only
`Secure Project Owner` on the team. Team is back to **1 role, 0 members**.

**Generalised rule, now in the runbook and spec**: *when assignment fails, the cause is a missing
`Read` on that one entity — add the privilege, never a role.* And derive the entity list from
metadata (`sprk_issecure` present ⇒ `Read` required), never from a written list, so a fourth securable
entity cannot silently reintroduce this.

### 🔴 Document / SPE access bypasses Dataverse row-level security (open)

Observed on a Matter the user reached **only by share**:

| Path | Result |
|---|---|
| Open the `sprk_document` record directly | ❌ denied — Dataverse enforcing correctly |
| Documents PCF on the Matter form | ⚠️ **documents listed** |
| Open a document in the Viewer PCF | ⚠️ **opens** |
| Download / open the file | ⚠️ **succeeds** |

**This contradicts design §5's surface table**, which says the MDA is *"enforced by Dataverse natively
— no code"*. It is not: the MDA hosts PCFs that read through the BFF, and BFF reads are app-only, so
Dataverse row-level security is inert on those paths — the project's own fact #1, occurring on the
surface the design assumed was safe. Being denied on the record while being served its *content* is
the worst possible split.

Note this is **independent of** §5.1d (children never assigned to the secure team). Even with correct
child ownership, an app-only read path would still bypass it.

#### Traced. It is TWO separate defects, with different fixes.

The control is **`SemanticSearchControl` PCF v1.1.80** (footer matches; "Threshold"/"Mode: Hybrid"/
"Similarity"/"Associated Only" are its filters). It never uses `Xrm.WebApi` on the read path, so
nothing is Dataverse-trimmed.

**Baseline established by impersonation** — Dataverse denies Test User 1 Read on **all 6** documents
linked to the test matter, and they can see **0 of 442** documents org-wide. `Spaarke Basic User` grants
`prvReadsprk_Document` at `Deep`, and all 442 documents sit in **root** — which is Test User 1's
*parent*, not a descendant, so `Deep` cannot reach them. Share-cascade was checked and ruled out:
**every** project/matter→child relationship is `Share=NoCascade`. The share on the Matter did **not**
extend to its documents. So the correct answer for every document was "deny".

**Defect 1 — the LIST leak. Un-remediated at HEAD. This is a genuinely new finding.**

`POST /api/ai/search` (`Api/Ai/SemanticSearchEndpoints.cs:30-45`) authorizes on the **tenant claim and
nothing else**. `Api/Filters/SemanticSearchAuthorizationFilter.cs:131-158` returns
`new AuthorizationResult(true, null)` for entity scope, and its own remarks (`:44-49`) list
*"Document-level authorization (validate user has access to specific documents)"* as a **future
enhancement**. Both sub-paths are app-only:

- `associatedOnly=true` → `SemanticSearchService.cs:665` → `DataverseServiceClientImpl.cs:1013-1039`,
  a bare `QueryExpression("sprk_document")` whose only criterion is the `sprk_matter` FK. **No
  `CallerId`, no impersonation**; connects via `DefaultAzureCredential` (`:85-102`).
- `associatedOnly=false` → Azure AI Search filtered by `tenantId` + `parentEntityId` only
  (`SearchFilterBuilder.cs:71`, `:128`). **No principal/ACL term.** Enrichment never drops a result
  (`SemanticSearchService.cs:588-591`).

So the grid leaks document **names, summaries, TLDRs, similarity scores, `driveId` and `speFileId`** to
anyone holding a tenant token who can name a parent record. **Not mentioned anywhere in this project's
`spec.md` or `design.md`** — the only reference to `SemanticSearchControl` in the notes is about
*container* resolution. Needs its own task: a per-document authorization filter on
`/api/ai/search`, or routing the associated-list path through the impersonated reader.

**Defect 2 — the file opens. Already FIXED in code; the running dev build does not have it.**

Every endpoint the PCF uses for opening/downloading is keyed by `documentId` and **is** gated at HEAD
with a real per-record check (`DocumentAuthorizationFilter` → `AuthorizationService.AuthorizeAsync` →
`RetrievePrincipalAccess`, requiring `AccessRights.Read`):
`open-links` (`SemanticSearchApiService.ts:301`), `preview-url` (`:346`), `/content`
(`SemanticSearchControl.tsx:668`), `bulk-download` (`:1261`).

Both gate commits are **on `origin/master`** — `2123c8de7` (2026-08-22, download/content) and
`f076b1e38` (2026-08-24, the five URL-minting reads), both this project's own task 002 / FR-01.

**Inference, and it is behavioural rather than assumed**: the gate requires exactly the Read that
Dataverse demonstrably refuses for this user on these documents, so a build containing the gates would
have denied the opens. They succeeded. Therefore **the running `spaarke-bff-dev` build predates task
002's gates** — consistent with `Deploy BFF API` being `disabled_manually` and today's four deploys
being manual `OneDeploy` pushes of an older artifact. Not verified by reading the deployed assembly;
**confirm by redeploying from master and re-running the exact test.**

> **This raises task 047's priority and changes what it is for.** The operator deploy it waits on is
> not only "so provisioning can be tested" — it **closes a live document-content bypass in dev**.

**Defect 3 — `POST /api/documents/{documentId}/share-link` has NO per-document gate, even at HEAD**
(`FileAccessEndpoints.cs:116-125` — no `AddDocumentAuthorizationFilter`, unlike the eight routes
around it). It mints a recipient-openable SPE sharing link authorized only by the caller's
**container-scoped** OBO access. Same shape as the holes task 002 closed; it was missed.

**Also worth a look** (lower severity, same family):
- `DocumentAuthorizationFilter.ExtractResourceId` (`:105-118`) falls back to `containerId` → `driveId`
  → `itemId` when no document id is present, so on container-keyed routes a filter that reads as
  "document read" actually evaluates **container** rights.
- `/download` and `/eml-render` stream **app-only** downstream (`FileAccessEndpoints.cs:920`), so the
  Dataverse filter is the *only* gate — correct today, but single-layered.
- SPE OBO is container-scoped by nature, coarser than per-document Dataverse rights
  (`FileAccessEndpoints.cs:44-48` says so outright).
- Minted URLs outlive revocation (`:52-55`, deliberately unsolved).

### ⚠️ Manage Access — added contacts do not save (open)

The shared non-admin user can open Manage Access and appears able to add access contacts, but the
additions **do not persist**. Two defects in one: a likely authorization failure being **silently
swallowed** in the UI (the user is shown success-shaped behaviour for a write that did not happen),
and a question of whether that user should see the control at all. Relevant to FR-07/FR-29's
delegation rule — *"you may grant if you have Write on the record"* — which a read-only shared user
does not satisfy, so the correct behaviour is probably to disable the control and say why.

---

## 8. Owner decisions required

1. **Which §3 fix** — A (BU restructure, already the decided direction, larger blast radius) or B
   (narrow `Spaarke Basic User` `Deep`→`Local`, zero measured blast radius today, reversible), or B now
   and A later. Detail + measured impact in design §5.1a-2.
2. **Task 047 framing** — proceed as "provisioning runs end-to-end" (valid now), or block it until §3 is
   fixed so it can also assert isolation. Recommend proceeding with the narrower claim, explicitly
   labelled, since provisioning has never succeeded in any environment and that is worth establishing.
3. **Child-entity ownership** (§4) — needs its own task; recommend sequencing with
   `spaarke-secure-project-r1`.

*(Decisions 1 and 2 were resolved by operator testing on 2026-08-25 — fix A was applied and validated;
see §7b. The list below supersedes them.)*

## 9. What now needs doing, in priority order

| # | Item | Why now | Owner |
|---|---|---|---|
| 1 | **Redeploy the BFF to dev from master** | Closes a **live document-content bypass** in dev (§7b defect 2 — fixed in code since 2026-08-22/24, not in the running build). Then re-run the exact operator test to confirm | operator — `Deploy BFF API` is `disabled_manually` |
| 2 | **Gate `POST /api/ai/search` per document** | §7b defect 1 — **un-remediated at HEAD**, and deploying does not fix it. Leaks document names/summaries/TLDRs/`driveId`/`speFileId` on tenant-token-plus-parent-id alone | new task |
| 3 | **Gate `POST /api/documents/{id}/share-link`** | §7b defect 3 — no per-document filter at HEAD; mints a recipient-openable SPE link on container-scoped OBO only. Task 002 closed its eight siblings and missed this one | new task (can fold into #2) |
| 4 | **Child-entity ownership rule** | §4 — 18 entities / 19 lookups, unisolated independently of everything above | new task, sequence with `spaarke-secure-project-r1` |
| 5 | **Manage Access silent write failure** | §7b — a write that fails but presents as success. Likely FR-29 (`Write on the record`) unmet by a read-only shared user; correct behaviour is to disable the control with a reason | new task |
| 6 | **BU migration plan** | §7b — moving users out of root orphans records left behind. Needed before this configuration goes anywhere populated | design decision |
| 7 | **Correct design §5's surface table** | It claims the MDA is "enforced by Dataverse natively — no code". The MDA hosts PCFs that read through the BFF, so app-only reads apply there too. The table understates the attack surface | doc fix |
