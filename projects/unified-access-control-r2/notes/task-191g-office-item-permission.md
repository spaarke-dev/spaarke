# Task 191g — Office edit through an item-level permission (live-test plan, results, undo)

> Branch `task/uac-191g`. Rigor FULL, opus @ xhigh. Owner decision Q2 = (b) (2026-11-11): a flagged-external caller never
> gets a role on a secure container; Office edit is a just-in-time write permission on the one file. Design §3.12, AC-8,
> §3.19 step 1. Ids in this note are truncated; full ids live only in the operator's scratch folder.

## 1. Live-test plan (design §3.19 step 1) — GO GIVEN (Route A), RUN BY THE OWNER

**Status:** the owner gave the go for Route A (relayed by the uac-r2 main session, 2026-11-12). The owner pastes the
script into the dev BFF's SSH console after the coordinator's dev deploy and does the browser checks; the agent runs
nothing live. The script prints PASS/FAIL per step and a final `UNDO-VERIFIED` / `UNDO-NOT-VERIFIED` line, with every
printed id truncated to 8 characters.

### 1.1 What the test must prove

| # | Check | Pass | Fail → |
|---|---|---|---|
| C1 | App-only `POST /drives/{d}/items/{i}/invite` (roles `write`, `requireSignIn: true`, `sendInvitation: false`, recipient by Entra object id) on a SECURE container, from the BFF identity | 200, one permission id; read back: role `write`, holder = the test user's object id; no mail sent | STOP (trigger 2) |
| C2 | The item permission is not a container role | the test user is absent from the container's permission list before AND after the invite | STOP |
| C3 | The sibling file is not shared | the sibling item's permission list has no entry for the test user | STOP |
| C4 | Office for the web edits the test file as the flagged user | the owner, signed in as the test user, opens the file in Word for the web in edit mode and a change saves | STOP |
| C5 | The sibling is refused | the same browser session opening the sibling file's URL gets "access denied" / "request access" | STOP |
| C6 | The marker key the code will write is accepted | `PATCH …/containers/{id}/customProperties` with a 71-character key (`SprkItm` + systemuserid + documentid) answers 200 and reads back | not a STOP: the code shortens the key (hash), re-tested in the deploy gate |
| C7 | Removal takes access away | `DELETE /drives/{d}/items/{i}/permissions/{p}` answers 204, read-back 404; a NEW private window as the test user is refused within 10 min (Word for the web re-checks at least every 5 min, task 171 row b′) | STOP |

The restrictive-vs-open sharing model of the dev container type is not readable app-only (task 171 row a′); C1 answers
the question that matters (whether an app-only item invite is accepted on this type).

### 1.2 Fixtures (read-only discovery on dev, 2026-11-11)

| Role | Id | Facts checked |
|---|---|---|
| Flagged test user | systemuser `e44317a0…` | the only enabled `sprk_isexternal = true` user on dev; a B2B guest; licensed; has an Entra object id; the S1–S4 spike user (design A.4) |
| Secure record | matter `9137406e…` | `sprk_issecure = true`, own container, `sprk_accesspermission` blank (not Restricted), owned by the Secure Record BU; the test user has **no** Dataverse rights on it (RetrievePrincipalAccess: None); not used by any other gate note |
| Test file | document `3ff21ca8…` | .docx, item in the matter's own container, `sprk_graphitemidbound` = `sprk_graphitemid` |
| Sibling file | document `5c29b0fe…` | .docx, same container, same pointer check |

**No Dataverse write at all.** The test proves SharePoint Embedded behaviour only; the Dataverse conditions (Write,
Restricted, visibility) are the code's job and are unit-tested. The test user is not shared on the matter, not moved and
not changed.

### 1.3 Identities and settings

- **Graph caller:** the identity the running dev BFF uses for app-only Graph — its user-assigned managed identity
  (`mi-bff-api-dev`, appId `5967251e…`), which already holds `full` on the dev container type (topology doc §6B, verified
  2026-10-04). No new app registration, permission, consent, sharing-policy or container-type change. Never the `az` CLI
  Graph token (403, spike S4).
- **Setting NAMES read inside the console (values never printed or stored):** `Graph__ManagedIdentity__ClientId`
  (fallback `ManagedIdentity__ClientId`), and the platform's `IDENTITY_ENDPOINT` / `IDENTITY_HEADER`. **No setting is
  changed**; the BFF is not restarted or redeployed.
- **Browser identity:** the flagged test user, signed in by the owner in a private window (agents never enter passwords).
- **Tokens:** held in shell variables inside the console only; never echoed, logged or copied out.

### 1.4 Where it runs

The managed identity answers only on its own Azure compute (spike S4), so the script runs **inside the dev BFF's App
Service container** (Linux):

- **Route A (preferred):** the operator opens *spaarke-bff-dev → SSH* in the Azure portal and pastes the prepared script
  (`c191g/owner-paste.txt` in the agent's scratch folder: one heredoc that writes `/tmp/191g.sh`, then `phase0` …
  `phase3`; bash + curl; prints truncated ids, status codes and PASS/FAIL only, plus the two file URLs for the browser
  steps). The agent prepares it and reads the output back; the agent cannot drive an interactive console or enter the
  platform's SSH password.
- **Route B (if the owner prefers the agent runs it):** first a read-only probe of the Kudu command endpoint
  (`/api/command`, ARM token) asking only whether `IDENTITY_ENDPOINT` is present (yes/no). If yes, the agent runs the same
  script there; if no, Route A.
- **Rejected:** a throwaway container instance carrying the BFF's managed identity (the 2026-10-03 topology probe pattern)
  — it attaches the BFF identity to new compute, a bigger change than the test needs.

### 1.5 Steps

0. **Snapshot (reads only, BFF identity):** the container exists; its permission list (count; test user absent); both
   items exist; each item's permission list (count; test user absent); the container's custom-property names starting
   `Sprk` (count). If the test user already holds ANY role or item permission here: STOP and report, touch nothing.
1. **Grant (C1):** the invite on the test file. Record the permission id. Read it back.
2. **Not a container role (C2, C3):** re-read the container's and the sibling's permission lists.
3. **Marker probe (C6):** write one `SprkItm…` custom property (value `itemId|permissionId`, `isSearchable: false`); read
   it back.
4. **Owner browser checks (C4, C5):** private window as the test user → test file URL → Edit in Word for the web → type
   `191g` at the end, wait for "Saved", delete it again, wait for "Saved" → sibling file URL → expect refusal. Report each
   result to the agent.
5. **Removal (C7):** delete the item permission; read back (404). Owner opens the test file URL in a NEW private window
   as the test user → expect refusal; retry every 2 min up to 10 min; record the time to refusal.
6. **Undo:** see 1.6. Then the agent records results here (ids, codes, times) and reports to the main session.

### 1.6 Undo (always, pass or fail)

| What the test created | Undo | Verify |
|---|---|---|
| The item permission (step 1) | `DELETE …/items/{i}/permissions/{p}` (step 5; repeated if a failure stopped the run early) | the test item's permission list equals the step-0 snapshot |
| The `SprkItm…` marker (step 3) | `PATCH …/customProperties` with the key set to `null` | the `Sprk…` property count equals the step-0 snapshot |
| Two new versions of the test file (step 4; net content unchanged) | none (version history is accepted on a test record); on request, `restoreVersion` to the step-0 version | file size equals the step-0 size |
| Nothing else | the container role list and the sibling's list are re-read | both equal the step-0 snapshot |

The 5-minute `SpeContainerMembershipSyncJob` in the DEPLOYED dev build (without this branch) neither reads nor changes
item permissions or `SprkItm` properties (it parses only `SprkStd`/`SprkJit` keys), so it cannot confound C7 or remove
the probe early. (This branch's sync does read `SprkItm` keys; it is not deployed during the test.)

## 2. Results

Not run yet.

## 3. Code (built behind a setting while the live test waits — allowed by the main session)

**Setting:** `ExternalAccess:FlaggedOfficeEdit:Enabled` (app setting `ExternalAccess__FlaggedOfficeEdit__Enabled`).
Absent, empty or unparseable = off. It stays off until the live test passes and the owner turns it on.

| Path | Setting off (default) | Setting on |
|---|---|---|
| Office edit-open, flagged caller (`sprk_isexternal = true`) | view only; never a container role (unconditional) | Write + not Restricted + document externally Visible (191c's `ExternalVisibilityDecider`, a faulted decision never counts) → one item write permission (`item-writer-jit`); anything else view only; a grant that cannot be made, read back or recorded → 503 |
| Office edit-open, caller whose flag cannot be read | `(false, "unknown")`, no grant of any kind | same |
| Office edit-open, internal caller | unchanged (container JIT writer, task 171) | unchanged |
| Sync: marked item grants (`SprkItm…`) | removed when the holder lost Write OR the document is no longer Visible (Hidden, or structurally undecidable); an unknown answer keeps the grant and reports it (round 70) | same |
| Sync: a flagged user's container role on a secure container | untouched | removed with `Prefer: onlyRemoveContainerScopedPermission` (item grants stay), whoever granted it |

**Ownership rule** (same as the container grants): only a permission that is still exactly what this code made (direct,
not a link, one user, roles exactly `write`) is ever deleted; a changed one only has its marker cleared. Item permission
ids are identity-derived, so a marker's id can name a permission a person re-made — never deleted.

**Known limits** (K-class; none holds a confirmed defect on a real path):
- K1 — Two concurrent first opens of the same file by the same flagged user can both invite; Graph returns the same
  identity-derived permission id, so both record the same marker value (idempotent), but this is not live-proven.
- K2 — With the setting off, a flagged user who still holds a leftover container role from before 191g edits through it
  (the open reports view only, the role is not removed) until the setting is turned on and the sweep runs.
- K3 — The item grant follows the document's CURRENT file; when a document's file is replaced, the old item's grant is
  undone at the next open of the new one (stale-marker path) or by the sync once Write or visibility goes.

**Deviation:** the code was built before the live test passed (main session's explicit allowance); the PR waits for the
live-test output.
