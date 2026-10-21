# Deferred work and found issues — field-mapping-server-write-path-r1

Two-write rule: every entry here has a GitHub issue, and every issue has an entry.

## DI-001 — Engine writes the wrong CLR type for Default→Money and expression→non-text targets

| Field | Value |
|---|---|
| **Found** | 2026-10-20, task 001 (characterization tests) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2124 |
| **Class** | F1, a real path once such a rule is configured |
| **Owner task** | 002 fixed the expression→non-text half (2026-10-20); the Default→Money half needs `moneyAttributes` from metadata, so **014** closes #2124 |
| **Failure** | Default→Money writes a `decimal`, not a `Money`. Concat/Template into number, option-set, date or boolean writes a string. Either way the SDK create throws. |
| **Pinned by** | `Default_MoneyTarget_WritesAPlainDecimalNotAMoney`, `Expression_NonTextNonLookupTarget_WritesTheResolvedStringWithoutConversion` (002 updates both) |

## DI-002 — Read checks leak record existence via timing; duplicate Read-check copies

| Field | Value |
|---|---|
| **Found** | 2026-10-20, task 003 (pre-existing) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2137 |
| **Class** | F1 (existence oracle) + F2 (duplicate mechanism) |
| **Owner task** | Push site: 040 (reuse `MappingSourceReadProbe.CheckCallerReadAsync`). Quick-create filter sites (source + Assigned To) and `TodoSourceAccessFilter`: **operator decision**; small, but outside a planned task |
| **Failure** | `GetCallerRightsAsync` (not timing-equalised) makes a missing id ~1.6 s slower than a denied one, with identical text |

## DI-003 — Seeder creates Copy rules with no `sprk_syncmode`; `/push` skips them in new environments

| Field | Value |
|---|---|
| **Found** | 2026-10-21, task 005 (design gap exposed by FR-05d / FR-23) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2181 |
| **Class** | F2: a real path in every newly provisioned environment once 040 lands |
| **Owner task** | **040**: the seeder sets `sprk_syncmode` = Manual Refresh on seeded Copy rules; the deployment guide runs `Set-FieldMappingRuleSyncMode.ps1` after an upgrade |
| **Failure** | Seeded Copy rules have an empty mode (One-time), so `/push`'s Manual Refresh selection ignores them until the script is run. On dev, 20 of 25 rules are empty |
