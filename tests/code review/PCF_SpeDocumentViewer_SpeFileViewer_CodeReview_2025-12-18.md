# PCF Code Review: SpeDocumentViewer + SpeFileViewer (2025-12-18)

## Scope
Reviewed the following control projects for PCF best practices and MVP streamlining:
- `src/client/pcf/SpeDocumentViewer`
- `src/client/pcf/SpeFileViewer`

Goals:
- Align with PCF control lifecycle best practices (init/updateView/destroy)
- Reduce risk in Power Apps / model-driven runtime (dependency compatibility, performance)
- Streamline to an MVP-ready, maintainable shape

No code changes were made as part of this review.

---

## Executive Summary (Top Priority)

### 1) React/Fluent “platform-library” mismatch and bundling risk (High)
**Why it matters:** PCF can either **bundle** your UI libraries or use **host-provided** platform libraries. Mixing these approaches can cause **multiple React copies**, runtime crashes, subtle UI bugs, and larger bundles.

**Where:**
- SpeDocumentViewer declares host libraries:
  - `src/client/pcf/SpeDocumentViewer/control/ControlManifest.Input.xml` has `<platform-library name="React" version="16.14.0" />` and `<platform-library name="Fluent" version="9.46.2" />`.
- SpeDocumentViewer runtime uses React 18+ API:
  - `src/client/pcf/SpeDocumentViewer/control/index.ts` uses `createRoot` (React 18+).
- SpeFileViewer bundles React 19 (riskier than React 18 for PCF):
  - `src/client/pcf/SpeFileViewer/package.json` depends on `react@^19.2.0` + `react-dom@^19.2.0`.

**Proposed solution options (pick ONE strategy per control):**
- **Option A (recommended for reliability/MVP): bundle React 18 + Fluent v9**
  - Remove platform-library declarations in SpeDocumentViewer manifest (or don’t rely on them).
  - Standardize to React 18.2.x for both controls.
  - Ensure only one Fluent UI major (prefer v9 only).
- **Option B (use host platform libraries):**
  - Keep platform-library declarations, BUT then ensure:
    - React/ReactDOM usage matches (React 16.14 => legacy ReactDOM.render/unmount patterns)
    - Fluent dependencies match the host-provided versions exactly
    - Avoid bundling React/Fluent at all.

---

### 2) `notifyOutputChanged` is being used as a render trigger (High)
**Why it matters:** `notifyOutputChanged()` is intended to notify the framework that *outputs changed*. These controls return `{}` from `getOutputs()`, so triggering output changes can cause unnecessary update cycles and performance overhead.

**Where:**
- SpeDocumentViewer: `src/client/pcf/SpeDocumentViewer/control/index.ts` `transitionTo()` calls `this._notifyOutputChanged?.()`
- SpeFileViewer: `src/client/pcf/SpeFileViewer/control/index.ts` `transitionTo()` calls `this._notifyOutputChanged?.()`

**Proposed solution:**
- Only call `notifyOutputChanged()` when you actually return a changed value from `getOutputs()`.
- For UI state changes, render directly (React render) without involving PCF output notification.

---

### 3) Hard-coded MSAL redirect URI (High)
**Why it matters:** This blocks multi-environment deployments and is fragile for tenants/org URLs. It also increases support burden when moving between dev/test/prod.

**Where:**
- `src/client/pcf/SpeDocumentViewer/control/AuthService.ts`
- `src/client/pcf/SpeFileViewer/control/AuthService.ts`

**Proposed solution:**
- Add an input property for `redirectUri` OR derive from a supported PCF API (e.g., org URL / client URL) if allowed.
- Document the required Azure AD registration redirect(s) and how this property should be configured per environment.

---

### 4) External service usage flagged as disabled despite calling BFF (Governance/Compliance risk)
**Why it matters:** Many environments use governance policies around external calls. If the control calls an external domain, the manifest should reflect external usage.

**Where:**
- `src/client/pcf/SpeDocumentViewer/control/ControlManifest.Input.xml` has `<external-service-usage enabled="false">`
- `src/client/pcf/SpeFileViewer/control/ControlManifest.Input.xml` has `<external-service-usage enabled="false">`

**Proposed solution:**
- Set external-service-usage appropriately for a control calling a BFF.
- If the manifest supports declaring domains, declare allowed domains.

---

## High-Impact Streamlining Opportunities (MVP)

### A) Reduce dependency bloat (size + risk)
**Where:**
- `src/client/pcf/SpeFileViewer/package.json` includes both `@fluentui/react` (v8) and `@fluentui/react-components` (v9).

**Why it matters:**
- Carrying both v8 and v9 tends to bloat the bundle and can introduce styling/theme inconsistencies.

**Proposed solution:**
- Standardize on Fluent UI v9 only if v8 is not required.
- Consider moving React + Fluent to devDependencies only if you rely on host platform libraries (otherwise keep as dependencies).

### B) React root mounting pattern could be safer
**Where:**
- Both controls call `createRoot(this.container)` after populating the container with a “loading overlay” DOM.

**Why it matters:**
- React expects a stable, dedicated mount node. Rendering into a container that you mutate directly can create warnings and edge-case issues.

**Proposed solution:**
- Create a dedicated child element for the React mount (e.g., `this.reactHost`) and keep it separate from raw DOM overlays, OR render loading/error via React as well.

### C) AbortController is created but not consistently wired (SpeFileViewer)
**Where:**
- `src/client/pcf/SpeFileViewer/control/index.ts` creates/aborts an AbortController, but `BffClient` doesn’t accept a `signal`.

**Why it matters:**
- You think you are cancelling in-flight work, but fetch calls keep running.

**Proposed solution:**
- Update BffClient to accept `AbortSignal` (optional) and pass it through to fetch.
- In React components/hooks, cancel requests on unmount or when documentId changes.

---

## Correctness / Reliability Findings

### 1) `useCheckoutFlow` mutates refs during render (React anti-pattern)
**Where:**
- `src/client/pcf/SpeDocumentViewer/control/hooks/useCheckoutFlow.ts` checks `bffClient.current['baseUrl']` and reassigns the ref outside a `useEffect`.

**Why it matters:**
- Side effects during render can behave unpredictably in Strict Mode and can cause subtle bugs.

**Proposed solution:**
- Use a `useEffect([bffApiUrl])` to update the client.
- Avoid accessing private properties via bracket indexing; expose a getter or store baseUrl separately.

### 2) DocumentViewerApp’s `bffClient` ref does not react to URL changes
**Where:**
- `src/client/pcf/SpeDocumentViewer/control/SpeDocumentViewer.tsx` uses `useRef(new BffClient(bffApiUrl))` but doesn’t update it when `bffApiUrl` changes.

**Proposed solution:**
- Mirror the approach in `useDocumentPreview` (useEffect to rebuild client on URL changes).

---

## Security / Privacy

### Excessive console logging (Medium)
**Where:** many files, including both controls’ `index.ts`, both `AuthService.ts`, and both `BffClient.ts`.

**Why it matters:**
- Console logs are visible to end users and can leak operational details.

**Proposed solution:**
- Gate logs behind a `debug` input property or compile-time flag.
- Avoid logging URLs and potentially sensitive metadata unless needed.

---

## UX / Accessibility

### Good: loading overlays include ARIA attributes
- SpeFileViewer loading overlay uses `role="status"`, `aria-busy`, and label.
- SpeDocumentViewer loading overlay uses `role="status"`, `aria-busy`.

### Improve: unify error UI into Fluent components
- Both controls use `innerHTML` error blocks with inline styles during init errors.

**Proposed solution:**
- Render errors via React + Fluent components for consistent accessibility and theming.

---

## Recommended MVP Target Shape

### Keep the MVP surface area small
For MVP:
- SpeFileViewer: preview + refresh + (optional) open links.
- SpeDocumentViewer: preview + checkout/checkin/discard (consider deferring delete/download unless required).

### Consolidate shared functionality
Create a shared internal module used by both controls:
- Theme detection + listeners
- AuthService (configurable redirect URI)
- BffClient with consistent headers + AbortSignal support

---

## Quick Win Checklist
- Decide platform-library vs bundling strategy (then align React/Fluent versions).
- Remove `notifyOutputChanged()` calls unless outputs actually change.
- Make MSAL redirectUri environment-configurable.
- Enable/declare external service usage if calling a BFF.
- Remove Fluent v8 dependency if unused.
- Make request cancellation real (wire AbortSignal to fetch).
- Move ref mutation logic in hooks into `useEffect`.

---

## Files Reviewed (primary)
- `src/client/pcf/SpeDocumentViewer/control/index.ts`
- `src/client/pcf/SpeDocumentViewer/control/ControlManifest.Input.xml`
- `src/client/pcf/SpeDocumentViewer/control/AuthService.ts`
- `src/client/pcf/SpeDocumentViewer/control/BffClient.ts`
- `src/client/pcf/SpeDocumentViewer/control/SpeDocumentViewer.tsx`
- `src/client/pcf/SpeDocumentViewer/control/hooks/useDocumentPreview.ts`
- `src/client/pcf/SpeDocumentViewer/control/hooks/useCheckoutFlow.ts`
- `src/client/pcf/SpeDocumentViewer/control/components/*`
- `src/client/pcf/SpeFileViewer/control/index.ts`
- `src/client/pcf/SpeFileViewer/control/ControlManifest.Input.xml`
- `src/client/pcf/SpeFileViewer/control/AuthService.ts`
- `src/client/pcf/SpeFileViewer/control/BffClient.ts`
- `src/client/pcf/SpeFileViewer/control/FilePreview.tsx`
- `src/client/pcf/SpeFileViewer/control/css/SpeFileViewer.css`
- `src/client/pcf/SpeFileViewer/package.json`
