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

## DI-011 — An unregistered owned table is refused only after the chat user confirms

| Field | Value |
|---|---|
| **Found** | 2026-10-30, task 017 adversarial review (finding 2, the part the round-1 fix left) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2485 |
| **Class** | Deferred: it fails closed (nothing is created); the fix needs new interface surface beyond 017's scope |
| **Owner task** | Follow-up (chat / AIR2-034 owner) |
| **Failure** | `ValidateChat` is sync with no I/O, and only a metadata read tells an Owned table from an org-owned (RunAsUser) one, so `table_unsupported` comes after confirmation. Fix: an async pre-suspend hook on `IToolHandler` that reads `OwnershipType`, or a tool description that names the creatable tables |

## DI-012 — `rag/index-file` lets a client choose a document's search index

| Field | Value |
|---|---|
| **Found** | 2026-11-01, task 039 adversarial review (finding 10) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2502 |
| **Class** | Out of scope, pre-existing: 039 covers record create/update payloads; search trims by live Read (design §13.3b), so routing, not disclosure |
| **Owner task** | Task 041 (FR-40). **Closed 2026-11-03** by PR #2541 (249809ce0b): index-file ignores `searchIndexName`; the index follows the content type |
| **Failure** | `POST /api/ai/rag/index-file` passes a client, allow-listed `searchIndexName` to `FileIndexingService` (`:140`, `:224`, `:272`), so the chunks land in the index the client named, not the record's server-owned one |

## DI-013 — The multi-container backfill scripts contradict FR-40

| Field | Value |
|---|---|
| **Found** | 2026-11-03, task 041 implementer (backfill inventory in `task-041-notes.md`) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2538 |
| **Class** | Out of scope, pre-existing: the scripts are run by hand and have not been run under 041 |
| **Owner task** | Unassigned; the runbook note goes in task 062 |
| **Failure** | `Backfill-MultiContainerMultiIndex-Documents.ps1` stamps the literal `spaarke-files-index`, which is wrong wherever `KnowledgeIndexName` differs. `…-ParentRecords.ps1` writes record index columns that FR-40 says have no writer |

## DI-014 — The DeliverToIndex playbook node reports an index it never uses

| Field | Value |
|---|---|
| **Found** | 2026-11-03, task 041 adversarial review (finding 8) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2539 |
| **Class** | Out of scope, pre-existing (F1, user-visible text): outside 041's diff, and it touches the JPS node config contract |
| **Owner task** | Unassigned (AI playbook owners) |
| **Failure** | `DeliverToIndexNodeExecutor` requires `indexName` and reports it in its output, but the enqueued payload carries no index. Under FR-40 the file always goes to `KnowledgeIndexName`, so the output names the wrong index |

## DI-015 — Ribbon-only BFF create tables are not in the client set

| Field | Value |
|---|---|
| **Found** | 2026-11-03, task 020 implementer |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2540 |
| **Class** | Out of scope: changing the client routing of three tables needs its own decision, and billingevent is in ebilling-r1's area |
| **Owner task** | Unassigned; ebilling-r1 must agree to the billingevent part |
| **Failure** | `CreateTables` has sprk_budget, sprk_kpiassessment and sprk_billingevent; `BFF_CHILD_CREATE_TABLES` does not, so a wrapped client create of these tables bypasses the BFF. Pinned as `RibbonOnlyCreateTables` in `ChildRecordCreateTablesAgreementTests` |

## DI-016 — Dev BFF logger provider throws BadImageFormatException

| Field | Value |
|---|---|
| **Found** | 2026-11-03, main session, post-deploy check of d763dc120e (in passing) |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2549 |
| **Class** | Out of scope, pre-existing: seen 2026-10-28 before this project's #2541 deployed; not this project's code |
| **Owner task** | Unassigned (BFF platform / deploy owners) |
| **Failure** | `Logger.ThrowLoggingError` → BadImageFormatException at instance startup (4× 2026-11-03 22:26:37Z) and in a 2026-10-28 burst (14×). Log entries in those windows are lost; no 5xx followed |

## DI-017 — The server to-do regarding map lacks sprk_reportcard, which the client writes

| Field | Value |
|---|---|
| **Found** | 2026-11-03, task 042b implementer (Q1, proposed DI-P1) and correctness review (finding 5), in passing |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2556 |
| **Class** | Out of scope, pre-existing: fixing it changes `TodoRegardingBuilder`, outside `OwnerQuestionOf` / `ForRow`. Needs an owner decision |
| **Owner task** | Unassigned; owner decision, then UAC-r2 review |
| **Failure** | A to-do with a report-card pair is owned by the matter's team, not the report card's (create and reconciler agree; 042b AC3's second half cannot hold). `AllRegardingLookups` does not clear a set report-card lookup |

## DI-018 — A communication's thread filing is ignored by chat create and the service's OwnershipException check

| Field | Value |
|---|---|
| **Found** | 2026-11-03, task 042b security review (finding 4) and correctness review (findings 2, 6), in passing |
| **GitHub Issue** | https://github.com/spaarke-dev/spaarke/issues/2557 |
| **Class** | Out of scope, pre-existing: the chat path (`OwnedChildWrite.PathFor`, `DataverseCreateRecordHandler`) is outside 042b's two files, and the service check is an ownership refusal (NFR-07) |
| **Owner task** | Unassigned; UAC-r2 review |
| **Failure** | Under a Secure matter's thread, a thread-only communication is either created caller-owned (chat; the reconciler moves it 2–4 min later) or refused 409 `ownership_exception` (service). A missing thread gives a retryable-looking 503 |
