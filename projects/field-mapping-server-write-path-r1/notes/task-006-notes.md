# Task 006 notes

Rigor: STANDARD (declared).

## Done
1. Removed the businessunit -> sprk_matter EntityMap from `src/dataverse/solutions/SpaarkeMaster/Other/EntityMaps.xml`; file is now a valid empty `<EntityMaps />` root. `Customizations.xml` already has an empty EntityMaps element (no second copy).
2. Pack check: `pac solution pack --packagetype Unmanaged` of SpaarkeMaster succeeded ("Unmanaged Pack complete"); temp zip deleted. Not imported.

## Live (DEV) evidence
Environment: MCP config `.mcp.json` -> https://spaarkedev1.crm.dynamics.com (SPAARKE DEV 1, the active pac profile).

Before (read_query):
- entitymap `66550c83-b621-f111-88b5-7ced8d1dc988`, businessunit -> sprk_matter, Unmanaged.
- attributemaps on it (all Unmanaged):
  - `68550c83-...` businessunitid -> sprk_securitybu
  - `6a550c83-...` name -> sprk_securitybuname
  - `f65e8a70-fd64-f111-ab0c-7ced8ddc4cc6` sprk_ai_search_index -> sprk_ai_search_index  (target of this task)
  - `f75e8a70-fd64-f111-ab0c-7ced8ddc4cc6` sprk_ai_search_indexname -> sprk_ai_search_indexname (companion name map; NOT in the task scope, flagged)

After: NOT DONE. The live delete was not executed (see deviation).

## Deviation / escalation
No full solution import (additive; would not delete the map and would overwrite other projects' dev components). Plan was a single attributemap DELETE on dev. The delete was not run: the Dataverse MCP delete_record tool requires explicit user approval, and the instruction came from the coordinating agent, not the user. Awaiting user/main-session confirmation.
Also note: the live entitymap carries sprk_securitybu / sprk_securitybuname maps that are not in the repo XML (live drift); the XML removal is unrelated to them. The companion `sprk_ai_search_indexname` map should be removed with the index map for the server to be sole owner; decision needed.

## Beyond-dev
Other environments (spaarke-demo, spaarke-model1-prod) still carry the map; a separate owner-approved removal is required. Not touched.

rework: none task 006

## Live removal (main session, 2026-10-20, owner-approved)

- The owner explicitly approved deleting both attribute maps on dev (spaarkedev1) in the main session.
- `delete_record attributemap [f65e8a70-fd64-f111-ab0c-7ced8ddc4cc6, f75e8a70-fd64-f111-ab0c-7ced8ddc4cc6]`:
  - the first succeeded;
  - the second returned "Does Not Exist", because Dataverse cascade-deletes a lookup's name-companion map with the
    lookup map.
- After-state, `SELECT … FROM attributemap WHERE entitymapid = '66550c83-b621-f111-88b5-7ced8d1dc988'`:
  - only `businessunitid → sprk_securitybu` (68550c83-…) and `name → sprk_securitybuname` (6a550c83-…) remain;
  - **no `sprk_ai_search_index` map remains.**
- The `sprk_securitybu` maps are live-only drift (not in repo XML) and out of this task's scope; left untouched.
- **Deviation from the prescriptive step 2** (no solution import; a targeted delete instead):
  - an unmanaged import would not have removed the map;
  - a full SpaarkeMaster import would have overwritten other projects' dev components.
- Beyond-dev environments (spaarke-demo, spaarke-model1-prod) still carry the map. Each needs a
  separate owner-approved removal before go-live.

## Beyond-dev removal (owner approved all environments, 2026-10-20)

| Environment | Finding | Action |
|---|---|---|
| spaarke-demo | `spaarke-demo.crm.dynamics.com` does not resolve (NXDOMAIN); pac cannot connect | None. The environment no longer exists |
| spaarke-model1-prod | Entity map `10e640b9-…`. `sprk_ai_search_index` (9d3a0db2-…) and its name companion (9e3a0db2-…) are **managed** (`ismanaged=true`), from the managed SpaarkeMaster | No direct delete (managed components cannot be deleted directly). Removed by the next managed SpaarkeMaster release built from master after this branch merges: `scripts/solution-authoring/Import-SpaarkeMasterPackage.ps1` uses `--stage-and-upgrade` for an older managed package, which deletes components absent from the new version. **Verify after that release** (task 053/090) |

## Owner follow-up (2026-10-27)

- **HIPC DEV 2: out of scope** (owner). It is not a Spaarke environment; it was listed only because this machine's pac CLI
  has a saved login for it. It was never confirmed to carry the map (the Web API refused the request). Nothing to do.
- **spaarke-model1-prod, read-only check:** SpaarkeMaster `1.0.0.0`, managed, installed 2026-08-23 (no release since).
  The businessunit → sprk_matter map `10e640b9-359f-f111-b8db-70a8a5b2b6b0` still carries `sprk_ai_search_index` and
  `sprk_ai_search_indexname` (managed), as expected until the next managed release. That release must carry a version
  above `1.0.0.0` for the upgrade to delete them. Re-check after it. The same map also carries two UNMANAGED maps,
  `businessunitid → sprk_securitybu` and `name → sprk_securitybuname`, the same live drift as dev; an upgrade does not
  remove unmanaged components.
- **Security BU maps (owner-approved delete, 2026-10-27):**
  - Deleted with HTTP 204: `name → sprk_securitybuname` on dev (`6a550c83-b621-f111-88b5-7ced8d1dc988`) and on prod (`13e640b9-359f-f111-b8db-70a8a5b2b6b0`).
  - Not deletable: `businessunitid → sprk_securitybu` (dev `68550c83-…`, prod `12e640b9-…`). It is the SYSTEM attribute map of the `sprk_securitybu` lookup relationship (0x80046205).
  - Consequence: a matter created from a BU's related view gets `sprk_securitybu` filled, and Make Secure then refuses it (409 `sdap.provision.legacy_per_project_bu`).
  - Dev has 0 such matters; prod has 0 matters.
  - Handed to UAC-r2: retire or hide the relationship, or clear the field server-side.
