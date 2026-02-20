# Decision: PCF LegalWorkspace Directory Removed

> **Date**: 2026-02-18
> **Decision By**: Developer (Ralph)
> **Status**: Implemented

---

## Summary

The `src/client/pcf/LegalWorkspace/` directory has been **permanently removed** from the
repository. The Legal Operations Workspace uses the Vite-built standalone HTML web resource
at `src/solutions/LegalWorkspace/` as its sole production artifact.

---

## Background

The Legal Operations Workspace was initially scaffolded as a PCF (PowerApps Component Framework)
control in `src/client/pcf/LegalWorkspace/`. During deployment, it was discovered that:

1. **React version conflict**: The PCF `ControlManifest.Input.xml` declared
   `<platform-library name="React" version="16.14.0" />`, but the application code used
   React 18 APIs (`createRoot` from `react-dom/client`). This produced a runtime error:
   `TypeError: createRoot is not a function`.

2. **Architecture decision (ADR-026)**: Full-page surfaces should use standalone HTML web
   resources, not PCF controls. The PCF framework injects React 16 regardless of Custom Page
   context, making it incompatible with React 18 code.

3. **Migration to solutions directory**: The workspace was successfully migrated to
   `src/solutions/LegalWorkspace/`, which uses Vite + `vite-plugin-singlefile` to produce
   a single `corporateworkspace.html` file with React 18 bundled directly.

4. **Dual directory confusion**: After migration, both directories contained near-identical
   code. The PCF directory used `ComponentFramework.WebApi` types while the solutions directory
   used `IWebApi` (a local type alias for the same `Xrm.WebApi` interface). Maintaining both
   created unnecessary work and risk of divergence.

---

## What Was Removed

```
src/client/pcf/LegalWorkspace/          (entire directory tree)
├── ControlManifest.Input.xml           React 16 manifest — contradicts React 18 code
├── index.ts                            PCF lifecycle (init/updateView/destroy)
├── Solution/                           Dataverse solution XML packaging
├── components/                         Duplicate of solutions/LegalWorkspace/src/components/
├── hooks/                              Duplicate of solutions/LegalWorkspace/src/hooks/
├── services/                           Duplicate of solutions/LegalWorkspace/src/services/
├── types/                              Duplicate of solutions/LegalWorkspace/src/types/
├── utils/                              Duplicate of solutions/LegalWorkspace/src/utils/
└── contexts/                           Duplicate of solutions/LegalWorkspace/src/contexts/
```

---

## Production Architecture

```
Source:     src/solutions/LegalWorkspace/
Build:      npm run build (Vite + vite-plugin-singlefile)
Output:     dist/corporateworkspace.html (~800 KB single file)
Deploy:     pac webresource push → Dataverse web resource
Hosting:    Power Apps Custom Page iframe
Runtime:    React 18 (bundled), Fluent UI v9 (bundled)
```

---

## Impact

- **Build**: No impact — the PCF directory had no build integration with the solutions directory
- **Deployment**: No impact — deployment uses `pac webresource push` with the Vite-built HTML
- **Future work**: All modifications go to `src/solutions/LegalWorkspace/` only
- **Documentation**: Updated `custom-page-definition.md`, `custom-page-registration.md`,
  `deployment-verification.md`, and project `CLAUDE.md` to remove PCF references

---

## References

- ADR-026: Full-page Custom Page standard (`.claude/adr/ADR-026.md`)
- Pattern: Full-page custom page template (`.claude/patterns/webresource/full-page-custom-page.md`)
- Build config: `src/solutions/LegalWorkspace/vite.config.ts`

---

*This decision is permanent. Do not recreate a PCF scaffold for the Legal Operations Workspace.*
