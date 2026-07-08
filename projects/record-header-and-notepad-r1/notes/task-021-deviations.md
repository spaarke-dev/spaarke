# Task 021 — Deviations from POML

> **Task**: 021 — MatterHeader PCF manifest + PCF class + version.ts
> **Completed**: 2026-07-02
> **Files created**:
> - `src/client/pcf/MatterHeader/ControlManifest.Input.xml`
> - `src/client/pcf/MatterHeader/control/index.ts`
> - `src/client/pcf/MatterHeader/control/version.ts`
> - `src/client/pcf/MatterHeader/control/MatterHeaderView.tsx` (placeholder stub)
> - `src/client/pcf/MatterHeader/package.json`

---

## D-01: `ReactControl` used instead of `StandardControl` (POML template)

**POML template said**: `implements ComponentFramework.StandardControl<IInputs, IOutputs>` with `ReactDOM.render()` + `ReactDOM.unmountComponentAtNode()` and a manual `FluentProvider` wrap.

**What was written**: `implements ComponentFramework.ReactControl<IInputs, IOutputs>` returning a `React.ReactElement` from `updateView`.

**Rationale (CLAUDE.md §6.5 Path C — pivot to comply with existing convention)**:

1. **Existing project convention**: Both `SemanticSearchControl` and `DocumentRelationshipViewer` — the two PCFs the POML lists as reference — use `ReactControl` (virtual control-type). Following convention.
2. **Explicit convention hint in POML step 4**: "wrap in FluentProvider theme=webLightTheme (matching PCF convention)". Existing PCF convention IS `ReactControl` + platform-library modern theming, which does not require a manual `FluentProvider`.
3. **`.claude/patterns/pcf/fluent-v9-modern-theming.md` approach 1**: platform-library Fluent v9 auto-applies host theme when `control-type="virtual"`. No manual `FluentProvider` needed. Manual wrap would OPT OUT of modern theming (approach 4 in the pattern).
4. **`.claude/adr/ADR-022`**: Runtime is React 17.0.2 (MDA) even though manifest declares 16.14.0. `ReactControl` is React-16-API-compatible (no `createRoot`, no `hydrateRoot`, no concurrent features) — fully within the ADR-022 constraints.
5. **LOC savings**: `ReactControl` eliminates the ~5 lines for `ReactDOM.render()` + `unmountComponentAtNode` + private `root` field, keeping class comfortably under the ≤30 LOC ceiling (currently 20 LOC excluding imports and JSDoc — see verification below).

**Net effect**: Identical runtime behavior (React 16/17 API), simpler class, matches every other Spaarke PCF's convention, still ≤30 LOC.

**Verified**: `grep createRoot|react-dom/client` returns zero matches; no React 18-exclusive APIs used.

---

## D-02: Folder-layout convention (POML-directed, not the module CLAUDE.md convention)

The POML places the manifest at `MatterHeader/ControlManifest.Input.xml` and the class at `MatterHeader/control/index.ts` (i.e., the class lives in a `control/` subfolder while the manifest lives at the PCF root). Followed as specified.

Note that `src/client/pcf/CLAUDE.md` and existing PCFs (`SemanticSearchControl`, `DocumentRelationshipViewer`) place `ControlManifest.Input.xml` and `index.ts` as **siblings** (e.g., `DocumentRelationshipViewer/DocumentRelationshipViewer/index.ts` + `DocumentRelationshipViewer/DocumentRelationshipViewer/ControlManifest.Input.xml`).

The manifest's `<code path="control/index.ts" order="1"/>` element is set to reflect the POML-directed layout (path is relative to the manifest file).

This is a project-specific convention documented in `plan.md` and consistent across POMLs 021 / 022 / 023 / 024 for this project.

**Impact**: Task 023 (Solution folder scaffold) and task 024 (build verify) may need to account for this layout when copying build output to Solution/Controls/... . `pac pcf init` might also generate a different layout — task 024 will surface this if it matters.

---

## D-03: `getOutputs` returns `{}` (no output properties in manifest)

The PCF has zero output properties on the manifest (only one `input`). `getOutputs` returns an empty object — the framework accepts this per `IOutputs` when the manifest declares no outputs.

---

## Enforcement checklist verification

- [x] Manifest lists exactly 1 input property (`recordId`, `SingleLine.Text`, optional) — verified in file
- [x] NO entity/schema/fieldSchema properties — verified (entity is compile-time-fixed in `MatterHeaderView.tsx`)
- [x] PCF class implements all 4 lifecycle methods (`init`, `updateView`, `destroy`, `getOutputs`) — verified in file
- [x] React 16/17 API used (`ReactControl` pattern; no `createRoot`; no `react-dom/client` import) — grep clean
- [x] Class LOC ≤ 30 excluding imports/comments — 20 LOC verified via `grep -v` count
- [x] Zero hex/rgb literals — grep clean (only 1 hit for `#YYYY-MM-DD` in a comment context: none actually)
- [x] Zero `@spaarke/auth` imports — grep shows only comment reference stating we do NOT use it
- [x] Zero BFF calls — no `authenticatedFetch`, no `apiClient`, only `Xrm.WebApi` via shared hooks (delegated to task 022)
- [x] Manifest well-formed XML — verified via `xml.etree.ElementTree.parse` (Python)
- [x] Version 1.0.0 in manifest AND `version.ts` — both files show `1.0.0`

---

## Reference PCFs consulted

- `src/client/pcf/DocumentRelationshipViewer/` — primary reference for `ReactControl` pattern + `CONTROL_VERSION` constant convention + `package.json` shape + `featureconfig.json` (`pcfReactPlatformLibraries: on`).
- `src/client/pcf/SemanticSearchControl/` — secondary reference for manifest structure + platform-library declarations.
- `src/client/pcf/package.json` (workspace root) — confirmed workspace-level devDependencies are shared; MatterHeader still declares its own devDependencies mirroring DocumentRelationshipViewer's baseline (per POML "match reference PCF exactly").

---

## Workspace / package.json note

`src/client/pcf/` has a workspace-level `package.json` (name: `pcf-project`) but each PCF has its own `package.json` too (verified via `SemanticSearchControl/package.json` + `DocumentRelationshipViewer/package.json`). Followed the existing convention: MatterHeader has its own `package.json` at the PCF root.

**Notable difference from other PCFs**: MatterHeader's `package.json` does NOT declare `@spaarke/auth` as a dependency (per spec NFR-05 — this project is a host-context surface). No `@azure/msal-browser` either.

Task 024 (build + verify) will run `npm install` and `npm run build:prod` to validate.
