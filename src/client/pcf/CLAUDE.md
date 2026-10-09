<!--
Maintainer notes (stripped before Claude reads this file):
- Loads whenever Claude reads or edits a file under src/client/pcf/. Keep it to orientation + load-bearing rules.
- Size target: about 8 KB — a target, not a cap; exceed it when the content is load-bearing and say why in the PR. No generic TypeScript/React samples; point to .claude/patterns/pcf/ and real controls.
- Never restate the deploy procedure or version-bump file list here — they drifted once (this file said 4
  locations, pcf-deploy says 5). Point to the pcf-deploy skill.
- Previous full version: .claude/archive/2026-10-07/modules/pcf.CLAUDE.md
-->
# PCF controls — module notes

TypeScript/React PCF controls for the Dataverse model-driven app. Each control is its own folder with its own `package.json` (list them: `src/client/pcf/*/**/ControlManifest.Input.xml`). Lifecycle exemplar: `VisualHost/control/index.ts` — see [`.claude/patterns/pcf/control-initialization.md`](../../../.claude/patterns/pcf/control-initialization.md) and the rest of `.claude/patterns/pcf/`.

**Retired — do not use as patterns or references:**
- **UniversalDatasetGrid** (deleted). For lists use the DataGrid framework: `<DataGrid configId=… />` from `@spaarke/ui-components` + a `sprk_gridconfiguration` record ([`docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md`](../../../docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md)).
- **AssociationResolver** (SRFR-045, 2026-07) — folded into **RegardingResolver**, the polymorphic set-regarding picker on child forms. It writes the denormalized `sprk_regarding*` fields with subgrid auto-detect (Xrm.WebApi, no BFF) and supplies the parent the Field Mapping Framework inherits from ([`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../../../docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md)).
- **EmailProcessingMonitor** (deleted 2026-09-25) — its folder holds only a stray `package-lock.json`.

**Not retired:** **SpeDocumentViewer** (document main-form preview via BFF `/view-url`) — source restored 2026-10-08 (task 124) after it was wrongly deleted as an orphan; it is form-bound and ships in SpaarkeMaster.

## Binding rules

- **ADR-006** — custom UI on a form is a PCF control; no new JS web resources for custom UI.
- **ADR-022** — React and Fluent come from the platform libraries declared in `ControlManifest.Input.xml` (React **16.14.0**, Fluent 9). Code written for React 18/19 can fail here.
- **Shared library from a PCF (ADR-012 "PCF Import Pattern"):** import compiled deep paths — `@spaarke/ui-components/dist/components/…`, `dist/utils/…`, or the React 16-safe barrel `dist/pcf-safe`. Never a `src/` path (ADR-022: TS2786). A control importing `dist/` needs the `prebuild`/`prebuild:prod` `ensure-dist-fresh` wiring (`/pcf-deploy`). The bare main barrel (`'@spaarke/ui-components'`) drags `SprkChat`'s `pdfjs-dist`/`mammoth` into PCF's single-chunk bundle and crashes the build; nine controls (the Communication* family, ScopeConfigEditor, UpdateRelatedButton, VisualHost) still import it and work only because each has a webpack stub for those two packages (task 092 — see `CommunicationActions/webpack.config.js`). New code: use deep `dist/` paths; if a control must import the bare barrel, it needs the same stub. Type drift between React versions: [`.claude/patterns/ui/fluent-v9-react-version-boundaries.md`](../../../.claude/patterns/ui/fluent-v9-react-version-boundaries.md).
- **ADR-012 / ADR-021** — reuse `@spaarke/ui-components` rather than duplicating; Fluent UI v9 only (`@fluentui/react-components`), never v8 `@fluentui/react`; design tokens only, never hard-coded hex/px, so light and dark themes both resolve.
- **Section headers and list rows** follow [`docs/standards/UI-DESIGN-STANDARDS.md`](../../../docs/standards/UI-DESIGN-STANDARDS.md): header = `fontSizeBase300` + `fontWeightSemibold` + `colorNeutralForeground1`; row = 20px min-height + `spacingVerticalXS` top/bottom. Reference: `CommunicationAttachments/`.

## Auth — use `@spaarke/auth` ([ADR-028](../../../.claude/adr/ADR-028-spaarke-auth-architecture.md))

Never instantiate `PublicClientApplication` in a PCF. Every Spaarke surface shares one auth provider, which is what keeps SSO silent (`localStorage` cache, cookie state, tenant-specific authority); a private MSAL instance means an isolated cache and a popup on every tab. Bootstrap in the React host component's `useEffect`, not in the PCF class `init()`:

```typescript
import { initAuth, authenticatedFetch } from '@spaarke/auth';

await initAuth({
    clientId: clientAppId,
    // authority omitted — @spaarke/auth resolves the tenant-specific authority (INV-6)
    redirectUri: dataverseUrl,                          // Xrm.Utility.getGlobalContext().getClientUrl()
    bffApiScope: `api://${bffAppId}/user_impersonation`, // .claude/constraints/auth.md
    bffBaseUrl: bffApiUrl,                              // await getApiBaseUrl(context.webAPI) — HOST ONLY
    proactiveRefresh: true,
});
const response = await authenticatedFetch('/ai/search/...'); // relative path; token, refresh and 401 retry handled
```

Copy the real file rather than this sketch: `SemanticSearchControl/SemanticSearchControl/authInit.ts`. **The BFF base URL is host only** — take it from `getApiBaseUrl()` in `shared/utils/environmentVariables.ts` (strips `/api`) and pass relative paths to `authenticatedFetch`; hand-concatenating URLs has caused repeated `/api/api` 404s in production. Canonical: [`.claude/patterns/auth/spaarke-sso-binding.md`](../../../.claude/patterns/auth/spaarke-sso-binding.md) (INV-1..INV-8).

**⛔ Never (ADR-028 violations):**
- pass `accessToken: string` or `getAccessToken: () => Promise<string>` as a prop or constructor argument (API clients included);
- reference `window.__SPAARKE_BFF_TOKEN__`, `tokenBridge`, `BridgeStrategy`, `XrmStrategy`, `MsalSilentStrategy` (retired);
- write `fetch(url, { headers: { Authorization: \`Bearer ${token}\` } })` — use `authenticatedFetch`;
- instantiate `PublicClientApplication` outside `@spaarke/auth`, or clear its cache by hand (bypasses the `exp` validation).

## Build and deploy

- **Production build: `npm run build:prod`** in the control's folder — not `npm run build`, not `npm run build -- --mode production` (FAILURE-MODES AP-1). `npm run start` runs the test harness.
- **Deploy: `/pcf-deploy`** (build:prod → version bump → pack → solution-ZIP import). `pac pcf push` is a dev-loop convenience only, never a release path. The version-bump file list, Custom Page republish and cache-refresh steps are in the skill and [`docs/guides/PCF-DEPLOYMENT-GUIDE.md`](../../../docs/guides/PCF-DEPLOYMENT-GUIDE.md).

## Version footer (MANDATORY)

Every PCF control displays its version in the UI footer, e.g. `v3.2.4 • Built 2025-12-09`. It lets users and developers confirm which build is running without dev tools, verify a deployment after a hard refresh, and know the version when an issue is reported. Bump it with the other version locations (`/pcf-deploy`).

## Do / don't

| ✅ Do | ❌ Don't |
|---|---|
| Type props and state | Use `any` |
| Handle loading and error states; show user-friendly messages | Show raw errors to users |
| Use the logger (`createLogger` from `@spaarke/ui-components/dist/pcf-safe`) | Leave `console.log` in production code |
| Clean up in `destroy()` | Leave event listeners attached |
