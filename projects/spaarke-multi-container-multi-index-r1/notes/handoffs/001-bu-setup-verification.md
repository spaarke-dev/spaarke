# Task 001 — Operator BU Value Setup — Verification Handoff

> **Date**: 2026-06-07
> **Task**: `001-operator-bu-value-setup.poml`
> **Status**: ✅ COMPLETE
> **Executor**: Main session (Claude Code), 2 MCP writes
> **Verified by**: MCP read-after-write

---

## Pre-Write State (INV-5 gate)

Pre-write MCP read showed **all 4 BUs had `sprk_searchindexname = NULL`** — INV-5 satisfied (no overwrites possible).

```
businessunitid                          name              sprk_searchindexname
9271b764-952f-f111-88b5-7c1e520aa4df    Spaarke Demo      (null)
7bdb15ee-e39e-f011-bbd3-7c1e5215b8b5    Spaarke Dev 1     (null)
1a75377a-e29e-f011-bbd3-7c1e5217cd7c    Spaarke Test 1    (null)
06fbf21c-1872-f011-b4cb-7c1e52671ad0    Spaarke           (null)
```

## Writes Performed

| BU | businessunitid | New value |
|---|---|---|
| Spaarke Demo | `9271b764-952f-f111-88b5-7c1e520aa4df` | `spaarke-knowledge-index-v2` |
| Spaarke | `06fbf21c-1872-f011-b4cb-7c1e52671ad0` | `spaarke-file-index` |

Spaarke Dev 1 and Spaarke Test 1 intentionally left NULL per spec FR-OPS-01 (operator-determined, tenant default applies).

## Post-Write Verification (MCP)

```json
[
  {
    "businessunitid": "9271b764-952f-f111-88b5-7c1e520aa4df",
    "name": "Spaarke Demo",
    "sprk_searchindexname": "spaarke-knowledge-index-v2"
  },
  {
    "businessunitid": "06fbf21c-1872-f011-b4cb-7c1e52671ad0",
    "name": "Spaarke",
    "sprk_searchindexname": "spaarke-file-index"
  }
]
```

## Acceptance Criteria

- [x] **MCP read-query of Spaarke Demo BU returns `sprk_searchindexname = "spaarke-knowledge-index-v2"`** — verified above
- [x] **MCP read-query of Spaarke BU returns `sprk_searchindexname = "spaarke-file-index"`** — verified above
- [x] **INV-5 honored** — pre-write read confirmed both BUs were NULL
- [x] **Verification file at expected path** — this file

## Unblocks

- Phase A wizard tasks (020-029) — wizards can now cascade BU values onto create payloads
- Phase F backfill scripts (050-053) — can use BU values as fallback when child-document evidence is missing
- Phase H UAT task 071 — has expected values to verify against
