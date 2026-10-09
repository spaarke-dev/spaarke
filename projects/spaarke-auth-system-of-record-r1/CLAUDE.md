# spaarke-auth-system-of-record-r1 — project instructions

Code-level authentication and authorization system of record for every Spaarke surface and user type, and the
canonical auth architecture derived from it. Read `current-task.md` first.

## Key files

| File | What it is |
|---|---|
| `auth-system-of-record.md` | Verified record (draft for owner review): facts with provenance, matrix, ADR-028 reconciliation, defects, live results |
| `live/live-test-plan.md` | 41 live tests; run log at the top |
| `working/a01…a09` | Area evidence (traced from code, independently verified, refreshed to `8a9ecaac1`) |
| `working/x01` | Cross-area contradictions — its "truth" column overrides the area files |
| `working/x02` | Verdicts on 210 existing auth documents |
| `working/x03` | User × surface × operation matrix, ADR-028 rule-by-rule, consolidated live list, defects |
| `working/x04…x07` | Live read-only results (Entra, App Service, Key Vault, dev Dataverse) — dev only |
| `live/*.sh` | Read-only live scripts (default-deny masking) |

## Standing directives & gotchas

- **Code on master is the only authority** (owner, 2026-10-08). Documents — ADR-028 included — are leads. Every claim cites `path:line` or a live read-out; say "needs live verification" when code cannot prove it. Never present an assumption as fact.
- **Live reads are read-only, and the exact commands are shown to the owner before they run** (owner, 2026-10-08). Every live change needs owner approval and a stated rollback.
- **Mask by default** in any live script: print values only for identifiers (client/tenant ids), URLs and booleans. A deny-list once leaked `Compose__Webhook__ClientState` (2026-10-09).
- **Never print secret values** in evidence files, commits or chat.
- Workflows and Fable-tier agents at high effort are approved for this project (owner, 2026-10-08).
- **Gotcha:** while workflows run, the machine is heavily loaded and shell commands time out — use `run_in_background` and read output files.
- **Gotcha:** workflow `journal.jsonl` holds full agent results; don't regex it — parse the `.output` JSON with Python.
- **Gotcha:** Dataverse MCP `read_query` results over the token limit are saved to a file; parse it with Python.
- **Gotcha (live facts, dev):** `spaarke-bff-dev` has **no deployment slots**; `rg-spaarke-demo` no longer exists; the control-plane hosts are **not** in the "Spaarke Devlopment Environment" subscription; the App Insights component `sprkspaarkedev-aif-insights` is not the BFF's own telemetry.
- Retiring old docs, editing `.claude/adr/`, `.claude/patterns/`, `.claude/constraints/` or root CLAUDE.md each needs owner approval (sub-agents cannot write `.claude/`; the main session applies those edits).
