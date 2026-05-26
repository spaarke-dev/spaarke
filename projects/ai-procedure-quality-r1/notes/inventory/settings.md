# Settings Files Inventory — Phase 0 Wave 0-A (Task 004)

**Date**: 2026-05-14
**Task**: `tasks/004-inventory-settings.poml`
**Mode**: Read-only validation + inventory
**Files audited**: 3 (settings.json, settings.local.json, .mcp.json)

---

## Summary Table

| File | Lines | Parse | Schema | $schema declared | Notes |
|---|---|---|---|---|---|
| `.claude/settings.json` | 45 | ✅ OK | ⚠️ 5 violations | yes | Hooks block in correct nested format |
| `.claude/settings.local.json` | 413 | ✅ OK | ⚠️ 43 violations | no | User-local accumulated allow-list with many unparseable Bash entries |
| `.mcp.json` | 14 | ✅ OK | n/a (no schema) | no | Single `dataverse` stdio server entry |

**Validation procedure** (reproducible):
- Tooling: Python 3.13 + `jsonschema` 4.26.0 (installed via `pip install jsonschema`)
- Schema source: `https://json.schemastore.org/claude-code-settings.json` (redirects to `https://www.schemastore.org/claude-code-settings.json`); fetched ~111 KB
- Parse verification: `python -m json.tool <file>` (settings files require UTF-8 explicit encoding due to non-ASCII content)
- Schema validation: `jsonschema.Draft7Validator(schema).iter_errors(data)`

---

## 1. `.claude/settings.json` (project-checked-in)

- **Path**: `c:\code_files\spaarke\.claude\settings.json`
- **Lines**: 45
- **JSON parse**: ✅ OK
- **Schema validation**: ⚠️ **5 violations** (all in `permissions.allow`)
- **`$schema` field**: present (`https://json.schemastore.org/claude-code-settings.json`)

### Top-level key inventory

| Key | Type | Purpose |
|---|---|---|
| `$schema` | string | Schema URL pointer (claude-code-settings) |
| `model` | string | Default model: `"opus"` |
| `env` | object | Environment overrides (1 entry: `CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS=1`) |
| `hooks` | object | Hook event bindings (2 events: `PostToolUse`, `Stop`) |
| `permissions` | object | `defaultMode=acceptEdits`, 10 allow-rules, 0 deny-rules |

### Hooks Health — Nested Format Confirmed ✅

The 2026-03 → 2026-05 hook-schema bug (flat `{matcher, command}` format) is **fixed**. Current state uses the schema-required nested format:

```
PostToolUse[0]: keys=['hooks', 'matcher']  matcher='Edit'  format=NESTED-OK
  .hooks[0]: type=command  command=bash scripts/quality/post-edit-lint.sh
Stop[0]:        keys=['hooks']             matcher=(none)  format=NESTED-OK
  .hooks[0]: type=command  command=bash scripts/quality/task-quality-gate.sh
```

Both event entries are `{matcher?, hooks: [{type, command}]}` — matches the published schema's `hookEntry` definition. No flat-format entries remain.

### Permissions

- `defaultMode`: `acceptEdits`
- `allow` length: **10**
- `deny` length: **0**
- `additionalDirectories`: not present

### Schema violations (5, all low-severity)

All five failures are in `permissions.allow` and stem from the schema's strict regex for tool names. The regex permits `Agent | Bash | Edit | ExitPlanMode | Glob | Grep | KillShell | LSP | Monitor | NotebookEdit | PowerShell | Read | Skill | TaskCreate | TaskGet | TaskList | TaskOutput | TaskStop | TaskUpdate | TodoWrite | ToolSearch | WebFetch | WebSearch | Write | mcp__*` — and importantly does **not** include bare `Task`.

| Index | Rule | Reason |
|---|---|---|
| 0 | `Read(**)` | Glob `**` not allowed as scope arg (schema requires `[^)*?]` after open paren) |
| 1 | `Glob(**)` | Same — `**` matches the disallowed regex |
| 2 | `Grep(**)` | Same |
| 3 | `Skill(*)` | Bare `*` not allowed (regex forbids `*?` chars in argument) |
| 4 | `Task(*)` | `Task` not in the tool whitelist; bare `*` also forbidden |

These were intentional broad permissions in the project's checked-in settings. They work at runtime (Claude Code accepts them in practice) but fail strict schema validation. **Severity: LOW** — schema is more restrictive than the runtime parser.

---

## 2. `.claude/settings.local.json` (user-local, gitignored)

- **Path**: `c:\code_files\spaarke\.claude\settings.local.json`
- **Lines**: 413
- **JSON parse**: ✅ OK (file is UTF-8 with non-ASCII characters — emoji, smart quotes from commit messages)
- **Schema validation**: ⚠️ **43 violations** (all in `permissions.allow`)
- **`$schema` field**: NOT declared

### Top-level key inventory

| Key | Type | Purpose |
|---|---|---|
| `permissions` | object | User-local accumulated allow-list (`additionalDirectories: ["c:\\code_files\\spaarke"]`, 404 allow rules, 0 deny rules) |

This file has accumulated **404 allow rules** through the user accepting prompts during many sessions. There is no `defaultMode`, no `env`, no `hooks` — it strictly augments the project's settings.json.

### Permissions

- `allow` length: **404**
- `deny` length: **0**
- `additionalDirectories`: `["c:\\code_files\\spaarke"]`

### Schema violations (43, predominantly pattern issues)

The 43 schema-invalid rules fall into 3 categories:

1. **Multi-line `Bash(git commit -m "$(cat <<EOF ...)")` entries** (~20 entries). The HEREDOC pattern with literal newlines inside the rule string fails the regex `[^)]+` (which is single-line by definition).
2. **Quoted MSBuild paths**: `Bash("/c/Program Files (x86)/Microsoft Visual Studio/...")`. The space + double-quote + parentheses in the path break the `[^)]+` regex.
3. **PowerShell expressions with literal backticks and embedded parens**: e.g. `PowerShell(& ... -Path "...?$filter=contains(LogicalName,'scope')...")` — same root cause: unescaped `)` inside the arg.

These rules work at runtime (Claude Code's runtime matcher is more permissive than the published schema's regex), but cannot be validated by strict schema check. **Severity: LOW** — these are gitignored user-local entries; cleanup is the user's choice.

### Missing `$schema` field

settings.local.json does not declare `$schema`. This is **expected** — the file is user-local/transient. Tooling can still validate it against the same `claude-code-settings.json` schema by passing the schema URL externally, which is what this audit did.

---

## 3. `.mcp.json` (MCP server config)

- **Path**: `c:\code_files\spaarke\.mcp.json`
- **Lines**: 14
- **JSON parse**: ✅ OK
- **Schema validation**: SKIPPED — no public JSON schema is published at `schemastore.org` for `.mcp.json`. The format is documented in MCP / Claude Code docs but no machine-readable schema URL exists at this time.
- **`$schema` field**: NOT declared

### Top-level key inventory

| Key | Type | Purpose |
|---|---|---|
| `mcpServers` | object | Map of server-name → server-config. One entry: `dataverse` |

### `mcpServers.dataverse`

```
type:    stdio
command: npx
args:    ["-y", "@microsoft/dataverse", "mcp", "https://spaarkedev1.crm.dynamics.com"]
env:     {} (empty)
```

This matches the canonical pattern in CLAUDE.md's "MCP Server Integration" section. Single server, stdio transport, targeting the dev Dataverse environment.

---

## Anomalies / Findings

1. **Schema regex is stricter than runtime** (severity: LOW).
   The published `claude-code-settings.json` schema rejects 5 rules in settings.json and 43 in settings.local.json. All of these work at runtime. This means a CI validator that uses this schema (e.g. future Phase 4a task 060 validator) will produce noise on existing files. The Phase 4a validator design should either:
   - Use a relaxed regex for `permissions.allow` (allow `**` and `*` as scope wildcards, and allow `Task` as a tool name), OR
   - Be applied only to new entries (delta-validation), OR
   - Be applied only to the `hooks` block and other structural elements where the schema is reliable.

2. **`Task` not in schema's tool whitelist** (severity: LOW).
   The schema's tool-name enum lists `TaskCreate, TaskGet, TaskList, TaskOutput, TaskStop, TaskUpdate` but not bare `Task`. The settings.json rule `Task(*)` is therefore schema-invalid. This may be a schema lag (Claude Code's actual `Task` tool exists; the schema hasn't been updated). **No action needed in this project** — flag for upstream report if relevant.

3. **Hooks block format is correct after 2026-05-14 fix** (severity: NONE — this is the desired state).
   Both `PostToolUse` and `Stop` event entries use the nested `{matcher?, hooks: [{type, command}]}` format. The pre-fix flat format (`{matcher, command}` at the entry level) is **not** present. This is the validation baseline the Phase 4a validator must enforce.

4. **No `deny` rules in any file** (severity: NONE — informational).
   Neither settings.json nor settings.local.json declares any `permissions.deny` entries. Phase 4a validator should not assume `deny` will be populated.

5. **settings.local.json contains 404 accumulated allow-rules** (severity: INFO — possible cleanup target outside this project's scope).
   The file has grown organically as the user accepted permission prompts. Many entries are duplicates or near-duplicates (e.g. multiple `git commit -m "$(cat <<EOF..."` rules for slightly different commit messages). This is not within scope of ai-procedure-quality-r1 (the project explicitly excludes user-local settings cleanup), but worth recording for a future hygiene pass.

6. **No `$schema` in .mcp.json — no public schema available** (severity: NONE).
   This is the current state of the MCP server config format. If/when a schema is published, settings.json and .mcp.json should both declare it. No action.

---

## Validation procedure for Phase 4a task 060 (reproducible)

```powershell
# 1. Install jsonschema if not present
pip install jsonschema

# 2. Fetch schema (follow redirect)
curl -sSfL -o /tmp/claude-schema.json https://www.schemastore.org/claude-code-settings.json

# 3. Validate
python -c "
import json, jsonschema
schema = json.load(open('/tmp/claude-schema.json', encoding='utf-8'))
for path in ['.claude/settings.json', '.claude/settings.local.json']:
    data = json.load(open(path, encoding='utf-8'))
    try:
        jsonschema.validate(data, schema)
        print(f'{path}: VALID')
    except jsonschema.ValidationError as e:
        print(f'{path}: INVALID at {list(e.absolute_path)}: {e.message[:200]}')
"
```

**Caveats for the Phase 4a validator**:
- Run via UTF-8 explicit encoding (`encoding='utf-8'`) — Windows default `cp1252` chokes on emoji in commit-message rules.
- The published schema's `permissionRule` regex is **stricter than runtime**. Either relax the validator or whitelist known-good-broad-patterns (`Read(**)`, `Glob(**)`, `Grep(**)`, `Skill(*)`, `Task(*)`).
- Focus structural validation on `hooks` (this is where the 2026-03→05 bug lived). Permission-rule pattern checking should be optional/warnings.

---

## Acceptance Criteria (from task 004 POML)

- [x] All three settings files validated; result documented
- [x] Validation procedure is reproducible (commands documented above)
- [x] Drift found is flagged with severity (see "Anomalies" section)
