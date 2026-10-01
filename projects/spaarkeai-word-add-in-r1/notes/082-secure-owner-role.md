# Task 082: the Secure Record Owner role covers the children task 080 assigns to it

> **Date**: 2026-09-30 · **Issue**: [#1046](https://github.com/spaarke-dev/spaarke/issues/1046) (ISS-013)
> **Environment**: `spaarkedev1` · **Role**: `Secure Record Owner` `e4ebabd9-b4a0-f111-aaac-000d3a99d1d7`
> **Team**: `Secure Record` (default Owner team) `daec0b6f-80a0-f111-aaac-000d3a99d1d7`, in BU `d9ec0b6f-80a0-f111-aaac-000d3a99d1d7`

## 1. Why

Task 080 (I-6) owns each record by the default Owner team of the business unit of the record it is filed to. A
child of a secure record is therefore owned by the Secure Record team. Dataverse refuses an owner whose roles lack
`Read` on the table. The setup guide granted `Read` on the three root tables only, because "nothing assigns children
to this team". Task 080 made that false.

Nobody owned the gap. Our 080 note said "UAC-r2's C10 adds the child-table privileges"; UAC-r2's session27 note said
"fails closed until UAC C10 lands". C10 is the team's identity plus re-owning documents at provisioning, not these
privileges. The owner assigned the gap to this project on 2026-09-30, and UAC-r2 agreed the split
(`uac-r2-findings-2026-09-30.md` §9).

## 2. What changed

| Where | Change |
|---|---|
| Live role (dev) | `+ prvReadsprk_Todo`, `+ prvReadsprk_Communication`, `+ prvReadsprk_Event`, `+ prvReadsprk_Memo`, all at Basic. 36 → 40, **0 removed** |
| `config/secure-record-owner-role.json` | **The one list**: 8 tables (3 root, 5 child), each with its reason and the quoted refusal that forces it. Read by the guide, the script and UAC-r2's NFR-05 census clause (theirs to add) |
| `scripts/Set-SecureRecordOwnerRolePrivileges.ps1` | Dry run by default; `-Apply` adds only and reads back; `-Verify` exits 1 naming each gap. Privilege ids come from metadata, and a name mismatch is refused |
| `docs/guides/SECURE-PROJECT-ENVIRONMENT-SETUP.md` | Header, §1, §5.1 (the list is the JSON; root and child kinds; the "nothing assigns children" row corrected), §5.3 (the script), §5.4 (`$keep` reads the JSON, with a ⚠️ about the drift), §7 items 1 and 2b, §9 |

Not changed, by agreement with UAC-r2: `SecureBuRoleDepthAssertion*`. Their task 144 rewrites that file, and they
add the codified-set clause.

## 3. Evidence

**Negative controls**, before the grant. Each is a probe row with `ownerid@odata.bind → /teams(daec0b6f…)`:

| Table | Result |
|---|---|
| `sprk_todo` (3 polls, 20 s apart) | REFUSED ×3: *"Read Privilege Check For Owner failed … Principal team (Id=daec0b6f-…, type=9, teamType=0, privilegeCount=36, …) is missing prvReadsprk_Todo privilege … context.Caller=1d02f31c-…"* |
| `sprk_communication` | REFUSED: *"… privilegeCount=36 … is missing prvReadsprk_Communication privilege"* |
| `sprk_event` | REFUSED: *"… privilegeCount=36 … is missing prvReadsprk_Event privilege"* |
| `sprk_memo` | REFUSED: *"… privilegeCount=36 … is missing prvReadsprk_Memo privilege"* |

- `privilegeCount=36` equals the role's real count, so these results are current, not cached (setup guide §7).
- The caller was an administrator, which shows the check is on the **owner**, not the caller.
- No custom processing steps are registered on any of the four tables, so the probes had no side effects.

**`-Verify`, seeded both ways:**

| Run | Exit | Output |
|---|---|---|
| Before the grant (real config) | **1** | `VERIFY FAIL: … cannot own rows of: sprk_todo, sprk_communication, sprk_event, sprk_memo` |
| After the grant (real config) | **0** | `VERIFY PASS: the role holds Read at Basic on all 8 tables.` |
| Seeded config with a table the role lacks (`sprk_invoice`) | **1** | `VERIFY FAIL: … sprk_invoice` |
| Seeded config, wrong casing (`prvReadsprk_todo`) | **1** | refused: metadata names it `prvReadsprk_Todo` |
| Seeded config, `depth: Deep` | **1** | refused: only Read at Basic is supported, by design |

**Apply:** `ADDED at Basic and read back: prvReadsprk_Todo, prvReadsprk_Communication, prvReadsprk_Event,
prvReadsprk_Memo`, and the role went 36 → 40. No unrequested privilege was injected. Re-running `-Apply` reports
"Nothing to add". The before/after diff is exactly 4 ADDED and 0 REMOVED.

**Positive probe**, `sprk_todo` owned by the team, 20 s apart:

| Poll | Result |
|---|---|
| 1 | REFUSED with `privilegeCount=36`. This was **stale**: the role already held 40. It is the cache lag the guide warns about |
| 2 | CREATED `44b10a19-…`; `owningteam` = `daec0b6f…`, `owningbusinessunit` = `d9ec0b6f…` (the Secure BU); deleted, read-back 404 |
| 3 | CREATED `affbbf2d-…`; same result; deleted, read-back 404 |
| 4 | CREATED `88e49040-…`; same result; deleted, read-back 404 |

That is three consecutive successes, and a final check found **0** remaining probe rows.

Communication, event and memo were not positively probed. The mechanism and the privilege are identical, the
privilege is present, and `-Verify` confirms it; creating real rows adds no information.

**The guide's §5.4 strip, simulated read-only against the live role:**
- the new `$keep` (from the JSON) keeps all 8 and would remove the 32 drift privileges;
- the OLD literal three-name `$keep` would have removed 5 privileges in the set, **including `prvReadsprk_Document`**.

**UAC-r2's NFR-05 census, live:**
- Run with `SPAARKE_NFR05_DATAVERSE_URL`, `SPAARKE_NFR05_REQUIRED=true` and `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`.
  A plain `DefaultAzureCredential` reached only `EnvironmentCredential` in this shell.
- Clause 2 (0 human members) **PASS**. Clause 3 (the role held by the team alone) **PASS**.
- **Clause 1 FAILS on 2 PRE-EXISTING findings** outside this task's inputs:
  - The hotmail `#EXT#` guest `Ralph Schroeder` in the root BU holds `Spaarke Basic User` Read on project and
    matter at a depth that reaches the Secure BU, so that account can read secure projects and matters.
  - The census covers only project and matter Read on all roles. 082 added only todo, communication, event and
    memo Read on this role, so the census input is unchanged by 082.
  - Reported to UAC-r2, whose check it is.

## 4. 🔔 Owner decision: the drift

The live role holds **32 privileges outside the file**:
- Create, Write, Delete, Assign, Share, Append and AppendTo (at Basic) on `sprk_project`, `sprk_matter`,
  `sprk_workassignment` and `sprk_document`: 28;
- the SharePoint four at Global.

The guide's design is Read only (§5.1: *"Everything else was tested and is NOT required"*). UAC-r2 did not add them,
their design specified Read only, their only change to this role was the 2026-09-29 rename, and nothing in UAC-r2
depends on them. The role-privilege GUIDs suggest one later batch; its origin is unrecorded.

**Why it matters, and why it is not urgent:**
- The team has no members, and Basic reaches only what the team owns, so today these privileges confer nothing on
  any person.
- They become real the moment anyone joins the team. UAC-r2's task 144 introduces a NAMED owner team, and membership
  is exactly the hazard NFR-05 clause 2 guards.

**Recommendation: remove them, returning to Read only** (setup guide §5.4, whose `$keep` now keeps the 8). This is
the documented design, and nothing depends on the extra privileges. It is not done here; it waits for the owner's
answer.

### 4.1 ✅ Decided and done, 2026-10-01

**The owner said:** *"yes can remove them if not needed"*. This also answers UAC-r2's question F1, which was asked once,
jointly with 082. UAC-r2 has been told.

**Done in `spaarkedev1`, by guide §5.4 as written.** `RemovePrivilegeRole` was run for each privilege outside `$keep`,
with `$keep` read from the JSON. **32 removed; the role went 40 → 8**, all Read at Basic, exactly the file.

**The before-snapshot** (all 40 privileges, with ids and depths) is
[`082-role-before-strip-2026-10-01.json`](082-role-before-strip-2026-10-01.json). To reverse, re-add those rows
with `AddPrivilegesRole`.

**"If not needed" was proven, not assumed:**

| Check | Result |
|---|---|
| Who holds the role | Only the `Secure Record` team (`daec0b6f…`), with **0 members**. There is one copy of the role, in the Secure BU |
| Side effects of a probe create on the 4 changed tables | No custom plugin steps (only `Microsoft.Crm.ObjectModel` and `Microsoft.CDS.DataArchival.Plugins`), no callback registrations, no active workflows |
| **Positive probes**: 4 polls about 25 s apart; in each poll, a Secure-team-owned create on `sprk_project`, `sprk_matter`, `sprk_workassignment` and `sprk_document` | **16 of 16 CREATED.** Every row had `owningteam` = `daec0b6f…` and `owningbusinessunit` = the Secure BU, was deleted, and read back as 404. **0 leftover probe rows** |
| **Control in the same polls**: a `sprk_invoice` create (the role has no invoice privilege) | **REFUSED in all 4 polls with `privilegeCount=8`**. So the positive probes ran against the new role, not a cached 40-privilege copy (the cache lag recorded in §3) |
| Script dry run / `-Verify` | "Held now: 8 privileges … Nothing to add" / **`VERIFY PASS`, exit 0** |

The four child tables (todo, communication, event, memo) only ever held Read, so the §3 `sprk_todo` probe still stands
for them.

**⚠️ For whoever extends the set next** (UAC-r2's 145/146): `AddPrivilegesRole` **re-injects the SharePoint four**
(guide §5.4). After any `-Apply`, run the §5.4 strip again, or this drift returns.

## 5. Quality gates (task-execute Step 9.5)

**Code review: 0 critical.**
- **W1** A role created in an ancestor business unit matches by name as a replica. The script now reads
  `_parentrootroleid_value` and refuses a replica.
- **W2** An entry missing `reason` or `evidence` was not refused. Fixed, and seeded: an entry with empty
  `evidence` is refused, naming the table.
- **S1** The `sprk_document` evidence now states plainly why no refusal could be recorded, and `howToExtend` names
  it as the one exception.
- **S2** The §5.2 role description now covers children.
- **S3** The JSON-reading snippets say to run them from the repository root.
- **S4** `-Verify` does not check role holders. That is NFR-05 clause 3, UAC-r2's, so no change.

After the fixes, `-Verify` still passes against the live role.

**ADR check: 0 violations.**
- ADR-002: native security configuration is permitted, and no plugin was added.
- ADR-028/A4: no secrets; the script runs under the operator's `az` identity.
- ADR-044: GUIDs are lowercase at boundaries, and none is hard-coded.

One accepted warning under ADR-038: there is no KEEP-path test of our own. The CI-runnable standing check is
UAC-r2's census clause (their task 145), which reads our file. It lives in their file, which their task 144 owns,
by written agreement, so two projects do not edit one test file at once.

## 6. Placement

- **ADR-002:** native Dataverse security configuration, which is permitted; it adds no plugin and no BFF code.
- **CLAUDE.md §10 (BFF hygiene):** not applicable. No BFF code, package or registration changed, and there is no
  publish-size delta.
- **CLAUDE.md §11:** the justification is in the POML. The JSON file replaces three hand-maintained copies (the guide
  table, the §5.3 list and the §5.4 list); the script replaces the guide's hand-run snippets and adds `-Verify`.
