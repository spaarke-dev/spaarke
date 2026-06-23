# Task 092 — Seed-TypedHandlers DEV Seed Verification (MVP-cut)

**Project**: spaarke-ai-platform-chat-routing-redesign-r1
**Phase**: 4 (WP5 — 6-Tier Memory Subsystem, sub-WP 4d)
**Task**: 092 — Update `scripts/Seed-TypedHandlers.ps1` to register new chat retrieval handlers as `sprk_analysistool` rows
**Status**: Code change complete; **DEV deployment DEFERRED** per task 026/054 owner pattern (managed deploy window)
**Authored**: 2026-06-23
**Owner**: Main session (will dispatch deploy at next managed deploy window)

---

## 1. MVP-cut scope reframing

The POML for task 092 was authored before the Q5b MVP scope cut. The original POML assumes **8 new tool handlers** (tasks 083–090) shipped with corresponding `infra/dataverse/sprk_analysistool-*-row.json` files. Per Q5b MVP cut, **only task 085 (`recall_session_file`) is shipping in MVP**. Therefore task 092 in MVP mode reduces to:

1. Register the **1 NEW** handler row (`RECALL-SESSION-FILE`) in `scripts/Seed-TypedHandlers.ps1`
2. Defer the seven other rows (tasks 083, 084, 086, 087, 088, 089, 090)
3. **Do not deploy** — managed deploy window owns DEV seeding

The change in this task is a **single hashtable-entry addition** to the script's hardcoded `$RowFiles` enumeration. The script's upsert mechanism (POST/PATCH against `sprk_analysistools` via Web API, keyed on `sprk_handlerclass` + `sprk_toolcode` with `sprk_name LIKE 'SYS-%'` safety filter) is unchanged.

---

## 2. Script discovery mechanism

**Hardcoded enumeration** — `scripts/Seed-TypedHandlers.ps1` uses a `$RowFiles` hashtable that maps a unique key (typically the `sprk_toolcode`) to its source JSON path. The script does **NOT** auto-discover JSON files by glob pattern. New rows must be added explicitly to the `$RowFiles` map, which is the change applied in this task.

Evidence: `scripts/Seed-TypedHandlers.ps1` lines 95–190 (post-edit: lines 95–204). The script then iterates `foreach ($rowKey in $RowFiles.Keys)`.

---

## 3. What was applied (code change)

**File**: `scripts/Seed-TypedHandlers.ps1`
**Change**: Added one entry to the `$RowFiles` hashtable immediately after the `MANAGE-PINNED-CONTEXT` entry:

```powershell
"RECALL-SESSION-FILE"              = "$RepoRoot/infra/dataverse/sprk_analysistool-recall-session-file-row.json"
```

Preceded by an 11-line provenance comment block documenting:

- Project / phase / task linkage (chat-routing-redesign-r1, Phase 4 WP5, task 085)
- Handler purpose (T2+T5 retrieval; legal-domain trust framing; load-bearing for citation requirement)
- Index binding (spaarke-session-files ONLY; ADR-014 tenant + session AND-clause)
- Authorization semantics (`sprk_requiredcapability = null`, always-on per architecture §8.2)
- Invocation context (`sprk_availableincontexts = 100000001` = Chat only)
- MVP-cut note listing the seven deferred sibling tasks (083, 084, 086, 087, 088, 089, 090)

---

## 4. Row JSON validation result

**`infra/dataverse/sprk_analysistool-recall-session-file-row.json` validated against:**

(a) The script's expected payload-field surface (`Get-PayloadFromRowJson` + `Test-AnalysisToolSchemaValid` calls), and
(b) The peer template `infra/dataverse/sprk_analysistool-update-workspace-tab-row.json` (task 055 — same chat-only single-row pattern).

| Field | Required | Present in `recall-session-file-row.json` | Comment |
|---|---|---|---|
| `sprk_name` | Yes (display + safety filter) | `"SYS-Recall Session File"` | Conforms to `SYS-%` prefix safety convention |
| `sprk_toolcode` | Yes (upsert key) | `"RECALL-SESSION-FILE"` | Unique; matches `$RowFiles` map key |
| `sprk_handlerclass` | Yes (upsert key; runtime routing per `nameof(handler)`) | `"RecallSessionFileHandler"` | Matches C# class name (ADR-010 auto-discovery) |
| `sprk_description` | Load-bearing (NFR-12 LLM tool-selection) | Present (long-form, explicitly states "summary is NOT authoritative" + `requireCitations: true` semantic) | OK |
| `sprk_availableincontexts` | Yes | `100000001` (Chat) | Matches chat-only intent |
| `sprk_jsonschema` | Yes (Draft 2020-12 schema validated by `Test-AnalysisToolSchemaValid`) | Present; `type: object`, `required: [fileId, purpose, query]`, closed enums on `purpose` + `scope`, `additionalProperties: false` | OK; mirrors peer schema style |
| `sprk_configuration` | Optional (serialized as JSON string) | Present (empty `_comment` only — no method-discriminator dispatch since single LLM-facing function) | OK |
| `sprk_requiredcapability` | Optional | Omitted → defaults to null | Intentional per architecture §8.2; consistent with peer `update-workspace-tab-row.json` (also omits the field) |

**Validation outcome**: No missing required fields. No malformed JSON. The row is **ready to seed**.

---

## 5. Rows to be seeded once deploy runs

| Handler class | Tool code | Display name | Capability gate | Context mask |
|---|---|---|---|---|
| `RecallSessionFileHandler` | `RECALL-SESSION-FILE` | `SYS-Recall Session File` | null (always-on) | 100000001 (Chat) |

**GUID assignment**: The `sprk_analysistoolid` will be auto-assigned by Dataverse at POST time (first-run only). On subsequent runs the existing ID is reused via PATCH.

**Parameter schema summary**: Required `{ fileId: string, purpose: enum, query: string }`; optional `{ scope: enum, maxTokens: int, requireCitations: bool=true }`. Closed enums prevent open-ended LLM hallucinated values.

---

## 6. Deferred rows (NOT seeded in MVP)

Per Q5b MVP cut, the following handler tasks are **DEFERRED** — no JSON file was authored, and no entry exists in the `$RowFiles` map. These will be added in a later wave when the corresponding handlers ship:

| Task | Tool name | Status | Expected `sprk_toolcode` |
|---|---|---|---|
| 083 | `list_session_files` | DEFERRED | `LIST-SESSION-FILES` |
| 084 | `get_file_manifest` | DEFERRED | `GET-FILE-MANIFEST` |
| 086 | `write_session_memory` | DEFERRED | `WRITE-SESSION-MEMORY` |
| 087 | `retrieve_matter_memory` | DEFERRED | `RETRIEVE-MATTER-MEMORY` |
| 088 | `promote_to_matter_memory` | DEFERRED | `PROMOTE-TO-MATTER-MEMORY` |
| 089 | `get_user_preferences` | DEFERRED | `GET-USER-PREFERENCES` |
| 090 | `get_org_templates` | DEFERRED | `GET-ORG-TEMPLATES` |

When these handlers ship, each task will:
1. Author its `infra/dataverse/sprk_analysistool-{name}-row.json` file
2. Add one entry to `$RowFiles` in `scripts/Seed-TypedHandlers.ps1`
3. File a follow-up handoff note pointing to this one's structure

---

## 7. Ready-to-apply checklist for the managed deploy window

### 7.1 Pre-conditions

- [ ] BFF DEV instance reachable (`https://spaarkedev1.crm.dynamics.com` per script default)
- [ ] DEV Dataverse environment authenticated (`az login` complete; `az account show` returns the Spaarke DEV-eligible identity)
- [ ] `Test-AnalysisToolSchemaValid.ps1` (in `scripts/`) loadable — it is dot-sourced at top of `Seed-TypedHandlers.ps1`
- [ ] The new row JSON file present at the registered path: `infra/dataverse/sprk_analysistool-recall-session-file-row.json` ✓ (verified at code time)
- [ ] Working directory checked out to the branch containing this task's commit (`work/spaarke-ai-platform-chat-routing-redesign-r1` or its merge target)

### 7.2 Preview run (recommended before live)

```powershell
pwsh scripts/Seed-TypedHandlers.ps1 -OnlyHandler RECALL-SESSION-FILE -WhatIf
```

Expected output: `[WhatIf] Would UPSERT row from .../sprk_analysistool-recall-session-file-row.json` with the three identifying fields echoed (`sprk_name`, `sprk_handlerclass`, `sprk_toolcode`).

### 7.3 Live run

```powershell
# All currently-registered rows (idempotent; will PATCH if drift, POST if missing):
pwsh scripts/Seed-TypedHandlers.ps1

# Or restrict to just the new task-085 row:
pwsh scripts/Seed-TypedHandlers.ps1 -OnlyHandler RECALL-SESSION-FILE
```

The `-OnlyHandler` switch accepts the map key — for this task: `RECALL-SESSION-FILE`.

### 7.4 Post-verification

After the script reports `Done.`:

1. **Dataverse Web API verify** (or `pac data list`):

   ```powershell
   # Replace TOKEN via: az account get-access-token --resource https://spaarkedev1.crm.dynamics.com -o tsv --query accessToken
   $TOKEN = az account get-access-token --resource "https://spaarkedev1.crm.dynamics.com" -o tsv --query accessToken
   $headers = @{ Authorization = "Bearer $TOKEN"; "OData-Version" = "4.0"; Accept = "application/json" }
   $url = "https://spaarkedev1.crm.dynamics.com/api/data/v9.2/sprk_analysistools?`$filter=sprk_toolcode eq 'RECALL-SESSION-FILE'&`$select=sprk_analysistoolid,sprk_name,sprk_handlerclass,sprk_toolcode,sprk_availableincontexts"
   (Invoke-RestMethod -Uri $url -Headers $headers).value
   ```

   Expected: 1 row returned with `sprk_handlerclass = "RecallSessionFileHandler"`, `sprk_availableincontexts = 100000001`, `sprk_name = "SYS-Recall Session File"`.

2. **BFF restart** — restart the DEV BFF App Service (or local instance) so the chat agent re-loads its tool catalog from Dataverse.

3. **BFF chat-tool smoke test** — issue a request to `/api/ai/chat/playbooks` (or whichever endpoint surfaces the resolved tool list per the chat-startup flow). Assert that `recall_session_file` appears in the tool list for a chat session that has at least one uploaded file in `ChatSession.UploadedFiles`.

### 7.5 Rollback

The script is **idempotent** — re-runs are safe. Upsert semantics:
- If the row exists (matched by `sprk_handlerclass` + `sprk_toolcode` with `sprk_name LIKE 'SYS-%'`): PATCH with the JSON file's contents (drift-correcting).
- If the row does not exist: POST a new row.

There are **no destructive deletes** in this script. Rollback strategy is therefore "do nothing" — the row is harmless if the BFF handler is not yet deployed (the chat agent simply won't dispatch to it because `RecallSessionFileHandler` won't be in the assembly's auto-discovered handler set). If a hard removal is ever required, it must be done manually via Dataverse make.powerapps.com → Tables → Analysis Tools → delete the row.

---

## 8. Compliance notes

- **ADR-013 (BFF placement)**: this task is data-only seeding; no new BFF code, no new DI line, no new package, no publish-size impact. Out of `bff-extensions.md` scope.
- **ADR-014 (tenant isolation)**: enforced inside the handler (task 085's C# code), not at row level — row config is index-agnostic.
- **ADR-015 (telemetry hygiene)**: enforced inside the handler at runtime, not at row level.
- **ADR-027 (sprk_ prefix)**: every field in the row JSON uses the `sprk_` prefix. ✓
- **ADR-029 (BFF size)**: data-only — no publish-size delta.
- **ADR-010 (auto-discovery)**: the handler is auto-registered in C# via `AddToolHandlersFromAssembly`; this row is what makes the chat agent EXPOSE the handler to the LLM.
- **R6 audit item 1 (write-time schema validation)**: handled automatically by `Test-AnalysisToolSchemaValid` inside the seed loop.

---

## 9. Out-of-scope (explicitly NOT done here)

- The PowerShell script was **NOT executed**. Deploy is deferred to the managed deploy window per the task 026/054 owner pattern.
- The seven deferred sibling rows (tasks 083, 084, 086–090) were **NOT synthesized**. Their JSON files do not exist.
- Task 085's row JSON (`infra/dataverse/sprk_analysistool-recall-session-file-row.json`) was **NOT modified**. It is treated as canonical.
- No `dotnet build` / `dotnet test` / `git commit` run (out of MVP-cut scope; main session handles git).
- No `code-review` / `adr-check` invoked (STANDARD rigor + script-only change; quality gates not required per task-execute Step 0.5 STANDARD tier).

---

## 10. Owner handoff

**Main session** should at the next managed deploy window:

1. Confirm the `Test-AnalysisToolSchemaValid.ps1` helper still loads (no breaking change since task 085).
2. Run `pwsh scripts/Seed-TypedHandlers.ps1 -OnlyHandler RECALL-SESSION-FILE -WhatIf` and check output matches §7.2 expectations.
3. Run the live seed per §7.3.
4. Run the §7.4 post-verification.
5. Update `TASK-INDEX.md`: 092 → ✅ once §7.4 step 3 (BFF smoke test) is green.
6. Append a `Result` section to this file with the actual `sprk_analysistoolid` GUID returned + post-verification timestamps.
