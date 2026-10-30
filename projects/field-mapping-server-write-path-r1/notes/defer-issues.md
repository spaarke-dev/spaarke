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
| **Owner task** | **Timing (F1): fixed by PR #2331** (owner-approved 2026-10-27): quick-create source + Assigned To, push and to-do source use the equalised gate call; `CheckCallerReadAsync` lost its non-equalised mode. **Duplicate copies (F2): still open on #2137**; push consolidates in 040. Other non-equalised sites found in review: #2334 |
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
| **Class** | F1. **Live on master** through the Office quick-create (`OfficeRecordCreationService.cs` ~1238-1246 reads the source app-only; UAC-r2 2026-10-21). The unified-service half is dormant until 017 |
| **Owner task** | Office half: hotfix branch `fix/fm-2188-office-source-read` (its own PR to master). Service half: 014 fix round 3. Either way it **blocks 017**. Design D (caller mask, agreed with UAC-r2; notes/decisions.md 2026-10-21) |
| **Failure** | Record-level Read probe, then an app-only read of the columns, so a column the caller cannot read is copied into a row they can read |
| **Status (2026-10-21, task 014 fix round 3)** | **Mapping half: pending the shared caller-mask helper (Office hotfix first).** Fix round 3 was briefed to read the source as the caller, then descoped by the coordinator. `IDataverseUserClient` returns Web API JSON, `CreateTimeFieldMapping` needs a typed SDK `Entity`, and there is no OBO SDK client. The agreed design is a caller mask: the app-only typed read stays, one caller read through `IDataverseUserClient` fetches the mapped source columns, any column non-null for the app but null for the caller is dropped before the engine runs (like an empty column), and a fault in the caller read makes the source `Unavailable`. It lands first on master as a shared helper under `Services/Dataverse/FieldMapping/` (the Office half); 014's step 3 reuses it afterwards. Step 3's source read is unchanged on `task/fm-014-r3`. Still **blocks 017** |

## DI-005 — Field mapping can write a matter's secure flag and access columns (create path)

| Field | Value |
|---|---|
| **Found** | 2026-10-21, UAC-r2 condition (b) review of task 016 (F1-1, the create half) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2220 |
| **Class** | Triage pending (UAC-r2). The code path is live through Office matter quick-create; it is not reachable on dev (0 rules target those columns, 2026-10-21) and needs an admin-authored rule |
| **Owner task** | UAC-r2 triage. The Repair half is fixed in 016 (its own never-map set). If accepted, the fix is the matter's `MappingProtectedColumns` in `RecordTableRules`, which changes live Office behaviour to skip-and-warn and needs condition (b) |
| **Failure** | `ProtectedFromMapping` protects `sprk_issecure` / `sprk_accessinheritance` only for project and work assignment. The matter's registry set lacks `sprk_issecure`, `sprk_accesspermission`, `sprk_accessinheritance` and `sprk_securitybu`, so a mapping rule decides them, not the BFF (task 150) |

## DI-006 — Step-9 server binds are not de-duplicated case-insensitively for system-principal items

| Field | Value |
|---|---|
| **Found** | 2026-10-22, UAC-r2 condition (b) review of task 018a (K-c; existed before 018a) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2238 |
| **Class** | K: no caller sends a case variant today |
| **Owner task** | 018b (optional; criterion added), else a follow-up |
| **Failure** | Step 2 is skipped for system-principal items, so a case-variant `ownerid@odata.bind`, creator-person bind or `sprk_issecure` would sit beside the server's own key. The fix is the K3 approach |

## DI-007 — A system-principal create gets its I-1 stamp only after the create

| Field | Value |
|---|---|
| **Found** | 2026-10-30, task 017 part 2d-1 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2479 |
| **Class** | K: no system-principal creator exists (owner decision 2d) |
| **Owner task** | 033 (first system creator) |
| **Failure** | The caller principal gets the stamp in the create payload (decision 2a). The system principal still gets it from the post-create re-derivation, because binding needs nav-property names only a caller can read. Fix: app-only metadata read or a test-verified nav-property map |

## DI-008 — The repair pass must not re-own task, contact and account

| Field | Value |
|---|---|
| **Found** | 2026-10-30, task 017 part 2d-2 |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2480 |
| **Class** | Deferred: RepairAsync has no production caller yet |
| **Owner task** | 052 |
| **Failure** | Registering the three chat tables (owner, 2026-10-30) makes RepairAsync accept them, so the scheduled pass would re-own user-created rows to a team. Exclude them in 052, or ask the owner |

## DI-009 — The owner decision does not see a secure root reached only through the derived stamp

| Field | Value |
|---|---|
| **Found** | 2026-10-30, task 017 adversarial review (finding 3) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2483 |
| **Class** | K2: the same as the deleted core for the route and chat; email.draft keeps its stamp in the item (a′) |
| **Owner task** | Follow-up after 017; needs UAC-r2 (changes owner decisions) |
| **Failure** | Step 7 decides the owner from the item, and step 10 binds the derived stamp only into the create body. A child of a team-owned invoice in the reconcile window, whose secure matter is reachable only through that stamp, is owned without secure-if-any. Fix: derive the stamp before step 7 and add its root to the owner question |

## DI-010 — Budget inquiry preflight is looser than the create it precedes

| Field | Value |
|---|---|
| **Found** | 2026-10-30, task 017 adversarial review (finding 4) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2484 |
| **Class** | K3: rare (a service request is filed directly under a matter); not a regression |
| **Owner task** | Follow-up (a decide-only mode on RecordCreationService) |
| **Failure** | `PreflightAsync` asks through `OwnedChildWrite.OwnershipOf` (no pair parent, intermediates or stamp). The service can then refuse at step 7 or 10 after the decision is committed, so the decision is recorded with no service request |
