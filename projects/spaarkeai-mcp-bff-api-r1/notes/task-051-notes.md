# Task 051 notes — MCP skills rewritten (2026-11-25, main session)

**Rigor:** STANDARD (POML). Main session (`.claude/` edits).

## Done
- `.claude/skills/mcp-tool-handler/SKILL.md` rewritten from `Api/Mcp/**`, `McpModule.cs`, `RateLimitingModule.cs`, the MCP tests and the CI conformance job. The old text described the `IAiToolHandler` chat-tool pattern with kebab-case ids.
- `.claude/skills/widget-design/SKILL.md`: "Spaarke conventions (implemented)" section (URI, folder, BFF embedding, size gate with the measured Phase 0 init and paint times, empty CSP, `callServerTool` shims returning `Response`, host theme, no storage, host differences); checklist and conventions updated.
- `.claude/skills/declarative-agent/SKILL.md`: "Spaarke's Copilot packages today" section with the schema versions in use (Spaarke AI: manifest 1.30, DA v1.8, plugin v2.4; plugin `spaarke`: manifest 1.30 `agentSkills`, skills only), the register-once rule, OAuth not SSO, the Cowork switch-on. Package location and MCP OAuth wiring corrected.
- `.claude/CHANGELOG.md` entry 2026-11-25.

## Checks
- Path check: every repo path named in the three skills exists (script over backticked paths, 2026-11-25). The three docs from task 052 exist as of the same commit.
- Negative: no "Spaarke Copilot" as a name; no camelCase tool convention.
- The POML's "DA v1.9" applied only if 043 was built; 043 is cancelled, so the skill states DA v1.8 (what `src/solutions/CopilotAgent/declarativeAgent.json` uses).

## Deviations
- Ran before 042 closed and before 050 (the owner said "I will test in parallel and continue with next tasks", 2026-11-25). The CHANGELOG ordering reason for "after 050" does not apply: both are edited in this one session, sequentially.
