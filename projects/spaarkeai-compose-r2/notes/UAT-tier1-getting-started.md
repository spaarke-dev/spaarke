# Tier-1 UAT — Compose Getting-Started Vertical (no AI-catalog / no webhook needed)

> **Created**: 2026-07-10 after Phase-9 E2E remediation complete (100/102/104/101/103).
> **Branch**: `work/spaarkeai-compose-r2` @ `ad5d0aed3` (merged with master, 0 behind). **Gate: GREEN** (BFF build 0 err · compose-surface 468/468 · SpaarkeAi jest 410/410 · publish 45.24 MB compressed · no new CVE).
> **Purpose**: the minimum deployable slice that demonstrates real user value end-to-end. Deliberately excludes anything that needs the owner-only catalog deploy (047) or webhook config (056).

---

## What Tier-1 proves (and why it's the right first UAT)

Everything below is verified **in-process** by WebApplicationFactory / real-bus forcing tests — but those mock SPE / Dataverse / auth at the module boundary. **This deploy is the first time the code meets real Xrm.WebApi BU→container resolution, real SPE container writes, real OBO auth, real CORS.** That is exactly the environment-specific "other half" no in-process test can cover. **Expect Tier-1 UAT to surface 1–3 environment gaps** (auth scope, real container resolution, a config/CORS mismatch) — that is the process working, not a regression.

Tier-1 needs **only**: BFF deployed + SpaarkeAi code page deployed. **No `sprk_analysisaction`/`sprk_playbookconsumer` rows (047). No `Compose:Webhook:*` secrets (056).**

---

## Deploy steps (OWNER — live-env)

1. **Deploy BFF** (task 017) to the sandbox App Service from this branch build.
   - Artifact already produced + verified this session: `deploy/api-publish/` (Release, 45.24 MB compressed).
   - After deploy, hit `GET /healthz`. **Expected: Degraded/Unhealthy is OK here** — the `RoutingConsumerTypeHealthCheck` is Unhealthy on AI envs until 047 seeds the catalog rows. That does NOT block Tier-1 (create-on-save / memory / three-pane do not dispatch AI actions). Confirm the app is *up* and other checks are green.
2. **Deploy the SpaarkeAi code page** to the sandbox.
3. Confirm the user's Business Unit has a `sprk_containerid` set (Tier-1 create-on-save resolves the container from `businessunit.sprk_containerid` client-side via Xrm.WebApi — if the BU has no container, Save shows the honest "no container" banner by design).

---

## Tier-1 test cases

### TC-1 — Browse/Upload → Save persists a new document (task 100, the headline)
1. Open the Compose workspace (three-pane shell, `composeMode=editor`).
2. Choose **Browse / open file** (or **Upload**) and pick a `.docx`.
3. **Expect**: the document renders in the editor; the **Save** button is **enabled** (transient draft).
4. Click **Save**.
5. **Expect**: a new `sprk_document` is created in the user's BU SPE container; a byte-correct `.docx` lands in that container; the session rebinds to the new document id.
6. **Verify** (maker): the new `sprk_document` row exists; the SPE item exists in the BU drive.
- **Watch for**: container resolution failing (→ "no container" banner or a 400) = real BU→container env gap; OBO auth 401 on the SPE write; the Save button staying disabled (transient-draft gate regression).

### TC-2 — Search → open existing document → replace-save
1. Choose **Search for Document**, pick an existing indexed doc.
2. **Expect**: the real document loads (`documentRecordId` populated, distinct from a transient mount).
3. Edit, **Save**. **Expect**: replace-save updates the existing SPE item (no new row).
- **Watch for**: the second Save re-breaking (task 100 gap 1.7 — the client must carry the server-minted `documentSpeId`).

### TC-3 — Reopen restores the session + annotations (task 102, the linchpin)
1. In an open compose doc, add an anchored annotation / defined term.
2. Close the workspace, then **reopen the same document** (same matter).
3. **Expect**: the **same session resumes** (not a new empty one); the annotations, defined terms, and action history are **restored** and rendered.
- **Watch for**: a fresh empty session on reopen = the linchpin regressed (Load must send + honor `sessionId`+`matterId`); annotations missing after a Redis-TTL-crossing reopen = the Cosmos warm-tier path.

### TC-4 — Three-pane coordination reacts (task 104)
1. With the three-pane shell open (Workspace + Context + Assistant), perform a selection in the editor.
2. **Expect**: the **Context** and **Assistant** panes react on their own controllers (selection surfaced / offered) — not a silent no-op.
3. Exercise a Context→Workspace insert and an Assistant→Workspace insert.
- **Watch for**: a pane not reacting = a receiver not mounted in the real host (the forcing test proves mount+reachability in jest, but real render order can still differ).

### TC-5 — Push-to-Word + poll return (task 103, poll path only)
1. Click **Push to Word** in the toolbar. **Expect**: annotations pushed to the Word/SPE copy (push-annotations route).
2. Make a change in Word, save; return to Compose and **focus the tab**.
3. **Expect**: poll-on-focus `check-changes` → pull → reanchor banner appears with re-anchored changes.
- **Note**: the **webhook** (instant push) path is **not** in Tier-1 — it needs the 056 Key Vault config. The **poll-on-focus** path above needs no secrets and is the Tier-1 scope.

---

## After Tier-1 passes → the tiered follow-on

| Next | Unlocks | Prereq (owner) |
|---|---|---|
| **Tier-2** | The 5 AI toolbar actions light up + dispatch | Run **task 047** (deploy 5 Action + 5 Binding rows → spaarkedev1). Also flips `/healthz` green. Then the host registers the seeded GUIDs via `registerComposeAiToolbarAction`. |
| **Tier-3** | Instant (webhook) Word return-from-Word | Provision **task 056** — `Compose:Webhook:{SigningKey,ClientState,NotificationUrl}` to Key Vault (DEF-03). |
| **Merge to master** | — | After UAT green: PR + auto-merge this branch. Coordinate order with core (redesign-r2) — see `HANDOFF-to-core-shared-surface-heads-up.md` (core-first recommended). |

---

## Known non-Tier-1 items (named, not blocking)
- **101 gap 2.2** (toolbar activation) — ✅◐ E2E-pending on 047 (per-env bindingId GUID minted at catalog seed).
- **103 webhook-delivery leg** — ✅◐ E2E-pending on 056; poll path is live.
- **034** (undo/replace durable scope) — OPEN owner decision, independent of Tier-1.
- **014-split** — the parent-association *prompt UI* (selection surface) in the Tier-2c dialog; the `associate()` wire itself is landed (task 100).
