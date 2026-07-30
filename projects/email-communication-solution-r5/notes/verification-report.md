# Task 050 — Verification Sweep Report

> **Task**: `050-verification-sweep.poml` (Phase 5 integration gate)
> **Run date**: 2026-07-28
> **Scope note**: Nothing is deployed yet (deploy = task 051, gated on owner env). This report covers **all automatable (code/build/test-level) verification**. The live-browser portions (SC 2/3/4/5/6, live dark-mode, live OOB-form manual regression) are compiled into `notes/task-050-uat-checklist.md` for the owner to run **post-deploy** — no live MDA/Chrome session was driven because no deployed surface exists.
> **Result**: ✅ PASS (automatable scope). No security or regression failure found. No escalation.

---

## Summary of test totals

| Suite | Command | Result |
|---|---|---|
| BFF `.eml`-render (Eml* filter) | `dotnet test --filter FullyQualifiedName~Eml` | **68 passed / 0 failed** |
| `@spaarke/communication-components` | `npm run build` + `npx jest --ci` | build OK · **97 passed / 0 failed** (14 suites) |
| `@spaarke/ui-components` (sanitizer + tracking) | `npm run build` + `jest sanitizeEmailHtml TrackingFieldTrio` | build OK · **23 passed / 0 failed** (2 suites) |
| PCF `CommunicationConnections` | `npm run build:prod` + `jest` | build:prod **Succeeded** · **41 passed / 0 failed** |
| PCF `CommunicationAttachments` | `npm run build:prod` + `jest` | build:prod **Succeeded** · **40 passed / 0 failed** |
| PCF `CommunicationActions` | `npm run build:prod` + `jest` | build:prod **Succeeded** · **22 passed / 0 failed** |
| PCF `TrackingFieldTrio` | `npm run build:prod` | build:prod **Succeeded** (bundle.js emitted; jest tests live in `@spaarke/ui-components`) |

**Aggregate**: BFF 68 · comm-components 97 · ui-components 23 · 3 PCF jest suites 103 (41+40+22) · 4/4 PCFs build in prod mode.

---

## 1. XSS / NFR-03 (Success Criterion 7) — ✅ VERIFIED

**Finding: the security cases anticipated by this POML ("extend endpoint tests for server-side sanitize + unauthorized-fails-closed") already exist and pass — written by tasks 010/001/033. No new test authoring was required; this sweep verifies them green.**

### Server path (`.eml`, server-sanitized) — `tests/unit/Sprk.Bff.Api.Tests`
- `EmlToHtmlRendererTests.cs` — the authoritative server-side XSS boundary. Confirms neutralization of all four payload classes plus more, over **real MimeKit `.eml` fixtures** (ADR-038: no `Mock<HttpMessageHandler>`):
  - `<script>` stripped (`NotContain("<script")`)
  - `onerror=` inline handler stripped (`NotContain("onerror")`)
  - `javascript:` scheme neutralized (`NotContain("javascript:")`, `NotContain("alert(")`)
  - `<iframe>`/`<object>`/`<embed>` removed; `data:text/html` smuggling dropped (only image `data:` allowed); plain-text angle brackets encoded not injected; allowed `https`/`mailto` preserved; `cid:` inline images resolved to `data:` and unresolved `cid:` dropped.
- `EmlRenderResponseTests.cs` — HTTP shaping + **fail-closed**:
  - null `.eml` stream ⇒ **404 `file_not_found`, NO HTML body produced** (fail-closed on missing file).
  - valid `.eml` ⇒ 200 `text/html`, sanitized (`NotContain("<script")`), benign content preserved.
  - long-lived immutable cache header (NFR-01).
  - Fail-closed **authorization** documented as covered structurally: unauthorized callers rejected by `DocumentAuthorizationFilter` + group `RequireAuthorization()` BEFORE the handler runs (EndpointGroupingTests); a missing doc 404s before any HTML is produced.

### Client path (`sprk_body`, client-sanitized) — `@spaarke/ui-components`
- `sanitizeEmailHtml.test.ts` (23 assertions in the sweep's 2-suite run) — neutralizes `<script>`, `on*` handlers (`onerror`/`onclick`/`onload`/`onmouseover`), `javascript:` href, `data:text/html` href, `data:` image src; strips `<iframe>`/`<object>/<embed>/<form>`; forces `rel="noopener noreferrer" target="_blank"` on benign anchors; preserves legitimate formatting + inline `https` images + style/class; no global DOMPurify hook leak.

### Sandboxed iframe (defense-in-depth) — `@spaarke/communication-components`
- `EmailBodyView.tsx` renders the server HTML via `srcDoc` inside `<iframe sandbox="" referrerPolicy="no-referrer">` — **NOT** `dangerouslySetInnerHTML`.
- `EmailBodyView.test.tsx` (in the 97-test comm-components run) asserts: `sandbox` attribute present and **empty**; `sandbox` excludes `allow-scripts` **and** `allow-same-origin`; a sanitizer-bypass `<script>` payload leaves `window.__evil` undefined (does not execute); malicious `sprk_body` (script/onerror/javascript:) leaves `__xss`/`__xss2`/`__xss3` undefined; degradation + error chrome render clean under `webDarkTheme` with no console errors (ADR-021).

**8-combination closed set (4 payloads × 2 paths)**: covered — `<script>`, `onerror=`, `javascript:` link on both server (`EmlToHtmlRendererTests`) and client (`sanitizeEmailHtml` + `EmailBodyView`) paths; the tracking-pixel / remote-image + `data:` vector on both (server drops non-image `data:`; client drops `data:` image src). Remote `https` images are permitted HTML and execute no script — the remote-image *privacy* gate is an explicitly deferred item (project CLAUDE.md), out of NFR-03 scope which is script-execution.

---

## 2. Dual-mount parity (NFR-06 / SC 1) — ✅ VERIFIED (structural)

Single source of truth confirmed by grep: exactly **one** `EmailWorkspace` component definition exists —
`src/client/shared/Spaarke.Communication.Components/src/components/EmailWorkspace/EmailWorkspace.tsx`. No forked copy anywhere in `src/`.

All mounts render that same component, differing only in host-adapter resolution:
- **Mount #1 — SpaarkeAi direct widget**: `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/EmailWorkspaceWidget.tsx` `import { EmailWorkspace } from '@spaarke/communication-components'` → renders it with Xrm-backed adapters + `useAiSession()` `authenticatedFetch`/`bffBaseUrl`.
- **Mount #2 — standalone code page**: `src/solutions/EmailPage/src/main.tsx` `import { EmailWorkspace } from '@spaarke/communication-components'` → renders it with Xrm-backed adapters + code-page `authenticatedFetch`/`bffBaseUrl`; auth/theme bootstrap mirrors DailyBriefing.
- **Mount #3 — LegalWorkspace section** (Pattern D third mount): `src/solutions/LegalWorkspace/src/sections/email.registration.ts` `import { EmailWorkspace } from '@spaarke/communication-components'` → same render via `React.createElement`.

Structural parity holds (identical component, identical prop contract). **Pixel/behavioral parity in both themes is a live-env UAT item** (deferred to checklist — requires the deployed surface).

---

## 3. OOB regression (NFR-04 / SC 8) — ✅ VERIFIED (build + unit)

All four Communication PCFs build in **production mode** (`npm run build:prod`) and pass their jest suites **after the Layer-1 extractions (020–023)**:
- `CommunicationConnections` — build:prod Succeeded · 41/41 (incl. `ConnectionsWriteHandler`, `provenance`, `grouping`, `CommunicationConnectionsApp`).
- `CommunicationAttachments` — build:prod Succeeded · 40/40.
- `CommunicationActions` — build:prod Succeeded · 22/22.
- `TrackingFieldTrio` — build:prod Succeeded (bundle emitted); its jest lives in `@spaarke/ui-components` (`TrackingFieldTrio.test.tsx`, passing in the 23-test run).

No PCF view code was modified during this sweep (constraint honored). **Live OOB `sprk_communication` main-form read+write exercise of each PCF is a UAT item** (requires deployed form).

---

## 4. Package health — ✅ VERIFIED

- `@spaarke/communication-components`: `tsc` build clean · jest **97/97** (14 suites — matches the ~97 expectation).
- `@spaarke/ui-components`: `tsc` build clean · sanitizer + tracking suites **23/23**.

---

## 5. Layer-1 React-agnostic (NFR-05 / SC 10) — ✅ VERIFIED

Grep of the extracted Layer-1 cores `src/client/shared/Spaarke.Communication.Components/src/logic/**` for `from 'react'` / `import * as React` / `from 'react-dom'` / `as React.ComponentType` → **zero matches**. The cores (`connections/{provenance,ConnectionsWriteHandler}`, `attachments/{CommunicationAttachmentsService,AttachmentApiService,types}`, `actions/{composerPrefill,launchCreate,attachmentsSource}`) are React-agnostic per the two-layer split. `TrackingFieldTrio` was lifted as a generalized **component** (view) to `@spaarke/ui-components`, not a logic core — its React usage is expected and correct (ADR-022 slim-first; no `as React.ComponentType` cast).

---

## Deploy-path caveat (recorded, NOT fixed — pre-existing, out of scope)

The **LegalWorkspace vite build is blocked** and this predates/does not involve email-r5. Reproduced:

```
[vite]: Rollup failed to resolve import "@spaarke/document-operations"
  from ".../Spaarke.Compose.Components/src/widgets/ComposeToolbar.tsx"
Build failed in 8.96s
```

- **Root cause**: a `compose-r4` `@spaarke/document-operations` dependency imported by `ComposeToolbar.tsx` (Compose.Components), unresolved in the LegalWorkspace bundle. `document-operations` appears in email-r5 code **nowhere** — only in LegalWorkspace's `package-lock.json`. The email section (`email.registration.ts` → `EmailWorkspace`) is not implicated.
- **Impact**: affects only the **LegalWorkspace section mount (#3)** of the Email surface, because that mount ships inside the LegalWorkspace vite bundle. The two email-r5 delivery paths are **unaffected**: the SpaarkeAi **direct widget** (`Spaarke.AI.Widgets`, tsc-built) and the **standalone code page** (`src/solutions/EmailPage`, its own vite bundle) do not route through the LegalWorkspace bundle.
- **Action**: deploy task 051 should either resolve the `@spaarke/document-operations` workspace linkage independently of email-r5 or land the Email surface via the direct-widget + code-page paths. Escalate the LegalWorkspace bundle fix to the compose-r4 owner; do NOT block email-r5 on it.

---

## Live-env verification deferred to UAT

Success Criteria **2, 3, 4, 5, 6**, live dark-mode dual-mount parity, and the live OOB-form manual read+write regression require the deployed surface and are compiled in `notes/task-050-uat-checklist.md` for the owner to run after task 051 deploy. This is a scope split, not a gap: the automatable security + regression + parity-structural proofs are complete and green here.
