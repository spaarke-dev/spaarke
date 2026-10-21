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

## DI-004 — Mapping reads the source app-only; field-secured source columns are copied

| Field | Value |
|---|---|
| **Found** | 2026-10-21, task 014 review K-4 (task 003 residual) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2188 |
| **Class** | F1 once callers are re-pointed (017/018); dormant on master today (the service has no caller) |
| **Owner task** | **Blocks 017**: fix before any caller is re-pointed. Option (a): read the source columns as the caller. Option (b): check the caller's field-security read per mapped column and drop with a warning. Either way, timing-equalised and fail closed |
| **Failure** | Record-level Read probe, then an app-only read of the columns, so a column the caller cannot read is copied into a row they can read |
| **Status (2026-10-21, task 014 fix round 3)** | **Mapping half: pending the shared caller-mask helper (Office hotfix first).** Fix round 3 was briefed to read the source as the caller, then descoped by the coordinator. `IDataverseUserClient` returns Web API JSON, `CreateTimeFieldMapping` needs a typed SDK `Entity`, and there is no OBO SDK client. The agreed design is a caller mask: the app-only typed read stays, one caller read through `IDataverseUserClient` fetches the mapped source columns, any column non-null for the app but null for the caller is dropped before the engine runs (like an empty column), and a fault in the caller read makes the source `Unavailable`. It lands first on master as a shared helper under `Services/Dataverse/FieldMapping/` (the Office half); 014's step 3 reuses it afterwards. Step 3's source read is unchanged on `task/fm-014-r3`. Still **blocks 017** |
