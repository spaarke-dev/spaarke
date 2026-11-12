# Task 191g — Office edit through an item-level permission (live-test plan, results, undo)

> Branch `task/uac-191g`. Rigor FULL, opus @ xhigh. Owner decision Q2 = (b) (2026-11-11): a flagged-external caller never
> gets a role on a secure container; Office edit is a just-in-time write permission on the one file. Design §3.12, AC-8,
> §3.19 step 1. Ids in this note are truncated; full ids live only in the operator's scratch folder.

## 1. Live-test plan (design §3.19 step 1) — RUN BY THE OWNER 2026-11-12 (Route A); results in §2

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

## 2. Results (run by the owner, Route A, dev BFF build 6dbe69b9e2, 2026-11-12)

| Step | Check | Result |
|---|---|---|
| phase0 | snapshot | PASS — test user held no container role, no item permission; `Sprk…` property baseline recorded |
| 1 | C1 app-only item invite (`write`, no mail, sign-in required, by object id) on a secure container | **PASS** — one permission (`aTowIy5m…`), read back as `write` for the test user |
| 2 | C2 not a container role | **PASS** |
| 2 | C3 sibling not shared | **PASS** |
| 3 | C6 71-character `SprkItm` key, value `itemId\|permissionId` | **FAIL — PATCH answered 400** (Graph's error text was not captured) |
| browser | C4 Word for the web edits the test file as the flagged B2B guest | **PASS** — typed `191g`, Word showed "Saved" |
| browser | C5 sibling refused | **PASS** — access denied |
| 5 | C7 removal | **PASS** — DELETE 204, read-back 404 at 14:16:38Z |
| 5 | C7 a new private window as the test user is refused | **PASS** — on the first re-check, within 10 minutes |
| undo | §1.6 | **UNDO-VERIFIED** (item permissions, container roles, sibling and `Sprk…` properties equal the step-0 snapshot) |

**Left behind (accepted, test record):** one content edit — the text `191g` — on the dev test file (`3ff21ca8…`); the
owner did not delete it (§1.5 step 4 asked for it to be deleted). Nothing else.

**Findings** (ids only):
- **F-1 Marker 400 (fixed in code).** The documented API states no rule the 71-character key or the `|` value broke, so
  the cause is not known. Decision (main session, 2026-11-12): **option B** (§3) — the marker copies the live-proven
  `SprkStd`/`SprkJit` profile, is read back after writing and fails closed; the 191i gate proves it and runs the §4 probe
  if it fails.
- **K6 Guest MFA on first sign-in.** The B2B guest had to register MFA in the Spaarke directory on first sign-in. Every
  flagged B2B user meets this on their first Office open — an onboarding note, not a defect.
- **K7 No Spaarke add-in for the guest in Word for the web.** Expected: the add-in is deployed to directory members only.

## 3. Code (behind a setting, default off)

**Setting:** `ExternalAccess:FlaggedOfficeEdit:Enabled` (app setting `ExternalAccess__FlaggedOfficeEdit__Enabled`).
Absent, empty or anything but `true` = off. It stays off until the 191i gate turns it on.

| Path | Setting off (default) | Setting on |
|---|---|---|
| Office edit-open, flagged caller (`sprk_isexternal = true`) | view only; never a container role (unconditional) | Write + not Restricted + document externally Visible (191h's public seam `AppOnlyExternalVisibility`; Visible from a faulted decision never counts) → one item write permission (`item-writer-jit`); anything else view only; a grant that cannot be made, read back, recorded, or whose record does not read back → undone, 503 |
| Office edit-open, caller whose flag cannot be read | 503 `edit_access_unavailable`, no grant of any kind | same |
| Office edit-open, internal caller | unchanged (container JIT writer, task 171) | unchanged |
| Sync: marked item grants (`SprkItm…`) | **all removed** (kill switch: the open answers view only, so no item grant may stand) | removed when the holder lost Write OR the document is no longer Visible (Hidden, or structurally undecidable); an answer that may change (a faulted decision, 191c's transient reasons such as a row not found this run) keeps the grant and reports it (round 70) |
| Sync: a flagged user's container role on a secure container | untouched | removed with `Prefer: onlyRemoveContainerScopedPermission` (item grants stay), whoever granted it — a deliberate exception to "only remove what we made" (owner Q2 = b) |

**Marker (option B, main session 2026-11-12).** The Graph docs for container custom properties state no limit on a
property's name length, characters, value length or count, and no reason for a 400 beyond "names must be valid property
names" (the Graph dictionary pattern). So the marker copies the live-proven `SprkStd`/`SprkJit` profile:
- key `SprkItm` + the first 128 bits of SHA-256 over (systemuserid, documentid) as 32 lowercase hex — **39 characters,
  letters and digits only**, the profile of the live keys;
- value `{systemuserid:N}.{documentid:N}.{itemId}` — letters, digits and `.` only, no `|`, no permission id; **about 100
  characters, which is LONGER than the live `SprkJit` values** (a container permission id; no record in the repo states
  their exact length) — the 191i gate proves it (K8);
- a reader acts on a marker only when its key is the key the value's ids derive; a hand-edited or mismatched marker is
  reported and left alone;
- the marker is READ BACK after it is written; if the write is refused (the live 400) or does not stick, the item
  permission is deleted and the open answers 503 — no grant stands without its record. Add answers 200 or 201, delete
  204 or 200, everywhere.

**Removal (option B).** The sync reads the holder's Entra object id (systemuser) and lists the item's permissions; it
deletes ONLY the holder's plain write — direct (not inherited), not a sharing link, held by that object id, roles exactly
`write` — and nothing when the list cannot be read to its end. Never deleted: a link, an inherited permission, another
holder's permission, a changed role (downgraded, owner, a role added). An unreadable object id keeps the grant and reports
it. **Inherent limit:** a plain `write` a person hand-grants the same user on the same file is indistinguishable from
ours and is removed with the marker; and an open finds such a hand grant first and reports `HeldByOther`, so it is not
marked in the first place unless ours was made first.

**Cancellation:** once the ownership and permission checks pass, the invite, read-back and marker write ignore the
request's cancellation, so an aborted request never leaves an unrecorded write permission (the same fix is applied to
the task-171 container grant, which had the same gap).

**Known limits** (K-class; none holds a confirmed defect on a real path):
- K1 — Two concurrent first opens of the same file by the same flagged user can both invite; Graph returns the same
  identity-derived permission and the marker is deterministic, so the second is idempotent, but this is not live-proven.
- K2 — Deploying this build with the setting off is a user-visible change: flagged users get view only (no Office edit)
  until the setting is on. A flagged user who still holds a container role from before 191g edits through it until the
  setting is on and the sweep runs.
- K3 — The item grant follows the document's CURRENT file; when the file is replaced, the old item's grant is undone at
  the next open (stale-marker path) or by the sync once Write or visibility goes.
- K4 — If Graph's own timeout fires after it created the item permission but before answering the invite, the permission
  id is never known: Failed is reported and the permission is unrecorded (logged). Reachable only by a Graph-side
  timeout, not by a request cancel.
- K5 — One custom property per user × document; markers go with their grants, but a container's custom-property
  capacity is not documented.
- K6, K7 — guest MFA on first sign-in; no Spaarke add-in for guests in Word for the web (§2).
- K8 — The marker value (about 100 characters) is longer than any live-proven marker value; proven at the 191i gate.
- K9 — A systemuser whose Entra object id cannot be read (deleted or disabled user) keeps its item grant; reported every
  run until an operator removes it.

**Deviations:** the code was built while the live test waited (main session's explicit allowance); the task-171
cancellation fix is in scope (same gap); `ExternalRowVisibility.MayAnswerLater` is added to 191h's answer type (the sync
needs "not visible for now" vs "not visible", and the transient set already lives in 191c).

## 4. 191i marker gate and probe (under the 191i owner go, BEFORE the setting goes on)

**Gate (191i step 10, first half):** in the dev BFF's SSH console, write ONE marker in the option-B shape (39-character `SprkItm…`
key, a ~100-character `hex.hex.ITEMID` value, `isSearchable: false`) to the test container, read it back, then set it to
`null` and verify it is gone. Pass = 200/201 on the write, the value reads back exactly, 204/200 on the delete. Pass → the
setting on (191i step 10, second half).

**If the gate fails, run the probe before the setting goes on** — five throwaway `SprkPrb…` properties on the same test
container, each written, read back and removed, printing the HTTP status and Graph's error code and message (no ids),
then a final `UNDO-VERIFIED` line (no `SprkPrb` property remains). The paste template is in the operator's scratch folder
(`c191g/probe-paste.txt`), not in the repo.

| Case | Key | Value | Answers |
|---|---|---|---|
| P1 control | 39 characters (`SprkPrb` + 32 hex) | `x1` | the proven profile still works |
| P2 | 71 characters | `x1` | is key length the cause? |
| P3 | 39 characters | ~130 characters with a `\|` (the 191g live value shape) | is the old value the cause? |
| P4 | 71 characters | ~130 characters with a `\|` | the 191g live failure itself |
| P5 | 39 characters | ~100 characters `hex.hex.ITEMID` (option B) | the shape this code writes |

The first failing case names the cause; the code's key/value shape then moves to the largest passing profile (a code
change and a new PR, not a setting), with the read-back (§3) as the safety net either way. The setting stays off until
P5 or its replacement passes.
