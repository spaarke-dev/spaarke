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

## 8. Owner decisions required

1. **Which §3 fix** — A (BU restructure, already the decided direction, larger blast radius) or B
   (narrow `Spaarke Basic User` `Deep`→`Local`, zero measured blast radius today, reversible), or B now
   and A later. Detail + measured impact in design §5.1a-2.
2. **Task 047 framing** — proceed as "provisioning runs end-to-end" (valid now), or block it until §3 is
   fixed so it can also assert isolation. Recommend proceeding with the narrower claim, explicitly
   labelled, since provisioning has never succeeded in any environment and that is worth establishing.
3. **Child-entity ownership** (§4) — needs its own task; recommend sequencing with
   `spaarke-secure-project-r1`.
