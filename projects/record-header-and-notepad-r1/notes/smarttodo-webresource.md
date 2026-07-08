# SmartTodo webresource verification (Task 020)

**Verified**: 2026-07-02
**Assumed name**: `sprk_smarttodo_page`
**Actual name**: `sprk_smarttodo` (**DIFFERS from spec assumption**)
**Source**: Dataverse MCP (`read_query` against `webresource`); cross-checked against `scripts/Deploy-SmartTodo.ps1` + `src/solutions/SmartTodo/README.md`

## Findings

### Empirical confirmation (Dataverse MCP)

Query executed:

```sql
SELECT TOP 20 name, displayname, webresourcetype FROM webresource WHERE name LIKE 'sprk_smart%'
```

Result — single row:

| Field | Value |
|---|---|
| `name` | `sprk_smarttodo` |
| `displayname` | `Smart To Do` |
| `webresourcetype` | 1 (Webpage / HTML) |
| `webresourceid` | `f85a1884-962b-f111-88b5-7ced8d1dc988` |

**No `sprk_smarttodo_page` webresource exists in the dev environment.**

### Cross-check (deployment source of truth)

`scripts/Deploy-SmartTodo.ps1` line 66:

```powershell
$wrName = 'sprk_smarttodo'
```

`src/solutions/SmartTodo/README.md` "Web Resource" section:

| Property | Value |
|----------|-------|
| Name | `sprk_smarttodo` |

`src/solutions/SmartTodo/package.json` build script renames the Vite output to `smarttodo.html` (single-file HTML), which is then uploaded as the `sprk_smarttodo` webresource.

Three independent sources agree: the deployed webresource name is `sprk_smarttodo` (no `_page` suffix).

### Root cause of the spec assumption

The R4 spec (`spec.md` FR-09, line 98) and the plan/README/CLAUDE.md all assumed a `_page` suffix by parallel with `sprk_notepad_page`. The Notepad code page has not shipped yet (this project deploys it as `sprk_notepad_page` per task 039), so the pattern was templated onto SmartTodo without empirical verification. SmartTodo shipped in R3 as `sprk_smarttodo` before that naming convention was retrofitted.

This is exactly the risk the spec's Assumptions section (line 287) called out — the task 020 verification is doing its job.

## Impact on Phase 1

- **`toolbarLaunchDefaults.ts` (updated in this task)**: `SMARTTODO_WEBRESOURCE_NAME` constant changed from `'sprk_smarttodo_page'` → `'sprk_smarttodo'`. Docblock updated to cite Dataverse MCP verification + deployment source.
- **`useRecordHeaderToolbarActions` (task 012)**: Consumes `SMARTTODO_WEBRESOURCE_NAME` — no code change needed there; the constant flip propagates automatically.
- **Task 025 (Phase 2 QA)**: When the checkmark action is smoke-tested end-to-end, the launch call `Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_smarttodo' })` will resolve to the actual deployed webresource. If task 025 still fails at launch, the failure mode is NOT this constant.

## Follow-ups filed / required

### Test update — outside task 020 write scope

`src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/toolbarLaunchDefaults.test.ts` line 47 still asserts the OLD name:

```ts
expect(SMARTTODO_WEBRESOURCE_NAME).toBe('sprk_smarttodo_page');
```

This test will now FAIL. Task 020's write boundary explicitly limited to `toolbarLaunchDefaults.ts` (source) — the test lives at `__tests__/toolbarLaunchDefaults.test.ts` and is outside that boundary. **Phase 2 task 012 (implement `useRecordHeaderToolbarActions`) or task 013 (update shared-lib exports) MUST update this test assertion to `'sprk_smarttodo'`** — trivial one-line change, but must land in Phase 2 to keep the test suite green.

Owner action: neither task 012 nor 013 currently mentions this — recommend the executor of the first Phase 2 task update the assertion at that time. Not filing a formal DEF-{NNN} because the required change is a single-line trivially-scoped test tweak; noting it here for traceability.

### Doc drift — spec / plan / README / CLAUDE.md still reference `sprk_smarttodo_page`

These references are informational (spec FR-09 narrative + assumption; plan risk table; README consumer table; CLAUDE.md implementation note). They should be corrected at project wrap-up (Phase 5 wrap-up task, or add a small doc-fix pass to task 025's follow-through) to prevent future readers from mis-typing the constant.

Not blocking Phase 2. Documenting for the wrap-up pass.

## Notes

- No SmartTodo-related deployment gap found. The webresource exists and is discoverable via `Xrm.Navigation.navigateTo` in the dev environment.
- Dataverse MCP was authoritative here — the deployment-script value + README value + MCP query value all match, giving three-way confirmation.
- The `_page` naming convention DOES apply to Notepad (this project ships `sprk_notepad_page`); it just wasn't applied to SmartTodo when it shipped in R3.
