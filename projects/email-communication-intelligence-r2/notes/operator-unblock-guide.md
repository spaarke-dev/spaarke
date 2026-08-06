# Operator unblock guide — Key Vault (task 010) + Dataverse schema (016 / 020 / 023 / 034)

> **Purpose**: the operator-side prerequisites that unblock the gated tasks. Created 2026-08-05.
> Environment: dev = `spaarkedev1`; Key Vault dev = `spe-kv-dev-67e2xz`.

---

## 1. Key Vault — unblocks task 010 (HMAC footer/token signer) → then 012/013

**What the code needs**: the HMAC signing **key material** lives in Key Vault (ADR-028 / NFR-07). The config
carries only the secret **NAME** (`Communication:TrackingFooter:SigningKeySecretName`, already shipped in task
011's `TrackingFooterOptions`); task 010's signer resolves the key from Key Vault via `DefaultAzureCredential`.

**Your steps (dev):**

1. **Generate a strong HMAC key** (256-bit, base64) — e.g.:
   ```bash
   openssl rand -base64 32
   ```
2. **Store it as a Key Vault secret** in `spe-kv-dev-67e2xz` under a name you choose (suggest `footer-hmac-key`):
   ```bash
   az keyvault secret set --vault-name spe-kv-dev-67e2xz --name footer-hmac-key --value "<base64-from-step-1>"
   ```
3. **Confirm the BFF managed identity can read it.** The BFF App Service MI already holds **`Key Vault Secrets
   User`** on `spe-kv-dev-67e2xz` (per docs/architecture/auth-azure-resources.md), so **no new grant is needed**
   as long as the signer reads from THIS vault. (If task 010 is pointed at a different vault, grant
   `Key Vault Secrets User` there.)
4. **Set the app settings** (App Service configuration — no redeploy needed, ADR-018):
   - `Communication__TrackingFooter__SigningKeySecretName = footer-hmac-key`
   - `Communication__TrackingFooter__Enabled = true`  *(leave `false` until you actually want outbound mail stamped)*

**That's the whole KV prerequisite.** Nothing else is required from you for 010. When 010 runs, its signer
reads `footer-hmac-key` from `spe-kv-dev-67e2xz` via MI and signs/verifies the tracking token. Rotation later =
`az keyvault secret set` a new version (the signer reads current).

> Note: prod uses a different vault; repeat steps 1–4 there at deploy time with a prod-specific key value.

---

## 2. Dataverse schema — create via `dataverse-create-schema` (Web API + PowerShell), packed to the managed solution (ADR-027)

> PAC CLI has **no** key/column create path — use the Web API recipe. All of these must land in the **managed
> solution** so they deploy to every subscription.

### Task 020 — UNIQUE alternate key on `sprk_communication` (READY — create now)
- **Entity**: `sprk_communication`
- **Alternate key (EntityKey)**: **UNIQUE**, over the **single** attribute `sprk_internetmessageid`.
- **Suggested key schema name**: `sprk_communication_internetmessageid_key`
- **Pre-flight (REQUIRED before activation)**: confirm **no existing active `sprk_communication` rows share a
  duplicate non-null `sprk_internetmessageid`**. Null-valued rows are fine (a key doesn't apply to nulls), but
  **many blank-string `""` values will collide** — check for those too. If duplicates/blanks block activation,
  **STOP** (don't delete/mutate production rows) — tell me and we escalate.
- **Why unique (not just an index)**: NFR-02 requires the platform to *reject* a duplicate insert structurally;
  task 021 catches that rejection for race-proof dedup. Record the exact duplicate-key error class for 021.

### Task 023 — indexed column on `sprk_document` (READY — create now)
- **Entity**: `sprk_document`
- **New column**: `sprk_canonicalhash`
  - **Type**: Single Line of Text (string)
  - **Length**: **100** (quickXorHash base64 ≈ 28 chars; 100 is ample and won't truncate)
  - **Indexed**: **yes** (equality-lookup at scale by the task-024 detector; a non-indexed column degrades it)
- **Holds**: `quickXorHash` from `driveItem.file.hashes` (NOT `sha256Hash` — deprecated on SPE).
- **Backfill**: **forward-only** (owner decision 2026-08-05) — do **not** run a mass historical backfill.

### Task 016 — new `sprk_affinity` table (READY to create; opus-tier CODE follows later)
Per-tenant deterministic learning store (ADR-040 Path A — distinct from the session ledger + the ADR-048
participant index). Create the table + these columns:

| Column (logical) | Type | Notes |
|---|---|---|
| `sprk_name` | Primary Name (string) | Auto primary column; a human label (e.g. "sender:jane@acme.com→MAT-123") |
| `sprk_signaltype` | Choice (option set) | Values: **Sender** (100000000), **SenderDomain** (100000001), **SubjectKeyword** (100000002), **ParticipantSet** (100000003) |
| `sprk_signalvalue` | Single Line Text, len **850**, **indexed** | The sender email / domain / keyword / participant-set hash (the lookup key) |
| `sprk_targetentity` | Single Line Text, len **128** | Target record logical name (e.g. `sprk_matter`) — polymorphic, so string+id not a typed lookup |
| `sprk_targetid` | Single Line Text, len **64** | Target record GUID |
| `sprk_confirmationcount` | Whole Number (int) | Incremented on each human confirmation |
| `sprk_lastconfirmed` | Date and Time | Last confirmation timestamp (UTC) |
| `sprk_tenantkey` | Single Line Text, len **128**, **indexed** | Opaque per-tenant key (the store is per-tenant + inspectable) |
| *(alternate key, recommended)* | EntityKey | Over (`sprk_tenantkey`, `sprk_signaltype`, `sprk_signalvalue`, `sprk_targetentity`, `sprk_targetid`) for **idempotent increment-on-confirmation** (upsert) |
- **Ownership**: user/team-owned table (standard). No ML fields — deterministic counting only.

### Task 034 — two new task-entity fields — ⚠️ CONFIRM TARGET ENTITY FIRST (do NOT create blind)
- **Fields to add**: `sprk_basedate` (Date and Time) + `sprk_finalduedate` (Date and Time).
- **⚠️ Open question — which entity?** The current write core (`ActionSeam.CreateTaskAsync` →
  `TaskActionCore.CreateAsync`) creates the **OOB `task` activity** (`new Entity("task")`), but the 034 POML
  assumes a task entity that **already has `status` + `completed-date`** and elsewhere the codebase references a
  **custom `sprk_task`**. These don't line up. Before creating fields:
  - If Job C tasks should be the **OOB `task`** activity → add `sprk_basedate` + `sprk_finalduedate` as custom
    columns on `task` (status = `statecode`/`statuscode`; "completed-date" ≈ `actualend`).
  - If Job C tasks should be **`sprk_task`** (custom) → add them there (and `TaskActionCore` must be pointed at
    `sprk_task` in the 034 code work).
  - **Recommendation**: **hold 034 schema** until we confirm the target entity at 034 execution (I'll resolve it
    then). Creating on the wrong entity is worse than waiting. 016/020/023 have no such ambiguity — create those now.

---

## Suggested order
1. **Key Vault** (steps 1–4) → unblocks 010 → then the footer chain 012/013.
2. **020 + 023** (schema-c, parallel, no ambiguity) → unblock the Pillar C dedup foundation (021/024).
3. **016** table → unblocks the AffinityRung code (opus).
4. **034** — confirm entity target with me first, then create the two date fields.
