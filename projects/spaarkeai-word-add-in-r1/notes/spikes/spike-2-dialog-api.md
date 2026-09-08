# Spike-2: Office Dialog API for Opening a Record

> **Task**: `003-spike-2-office-dialog-api-record-open.poml`
> **Rigor**: STANDARD (desk research + decision record; no code produced)
> **Date**: 2026-09-08
> **Gates**: FR-10 (open record / open Document record); weakens or holds FR-13's "finish editing in the record" premise

---

## Questions

Per spec.md Dependencies/Spikes table (line 238) and Owner Clarifications "Record open" (line 257):

- **(a) Framing** — can `Office.context.ui.displayDialogAsync` host an MDA record-form URL without a framing refusal, separately for Word desktop and Word on the web, or must Spaarke host its own code page?
- **(b) Auth context** — can the dialog obtain a usable token from the pane (and under what origin constraint), or must it sign in independently? Does either route need a new Entra SPA redirect URI beyond the two NFR-09 already specifies?
- **(c) Usability at dialog size** — does the record form remain operable at the maximum practical dialog size, specifically: saving, opening a lookup picker, and using a subgrid — as three separate observations?
- **(d) Propagation** — can an edit made in the dialog be reported back to the pane via `messageParent`, under what origin constraint, and if not, what mechanism instead satisfies "edits made there are reflected in the pane on return"?

---

## In-repo ground truth

- **Token acquisition today**: `shared/services/AuthService.ts:63-94` (`AuthService.initialize`) constructs `OfficeNaaStrategy` (`src/client/shared/Spaarke.Auth/src/strategies/OfficeNaaStrategy.ts:185-478`) and wraps it in `SpaarkeAuthProvider`. `bffApiClientId` defaults to `1e40baad-e065-4aea-a8d4-4b7ab273458c` (`AuthService.ts:68`), matching `outlook/manifest.json:287` (`webApplicationInfo.resource: "api://1e40baad-e065-4aea-a8d4-4b7ab273458c"`).
- **NAA redirect URI is host-derived, not hardcoded**: `OfficeNaaStrategy.ts:29-32` (`naaRedirectUri()`) builds `brk-multihub://${window.location.hostname}`. The web fallback (non-NAA) uses `${window.location.origin}/auth-callback.html` (`AuthService.ts:79`, `OfficeNaaStrategy.ts:440-442`). These are exactly the two URIs NFR-09 names and the only two registered per environment (spec.md line 106; `office-outlook-teams-integration-architecture.md:34-43`).
- **NAA is platform-gated**: `OfficeNaaStrategy.ts:86-149` (`detectNaaSupport`) — Office-on-the-web deliberately does **not** use NAA (`:116-125`, empirically found unreliable, "Word/Outlook on the web ... NAA broker does not actually engage"); desktop Windows build ≥13530 and Mac ≥16.44 do use NAA (`:127-141`).
- **Token cache location is `sessionStorage`**, not `localStorage`, for both the NAA and fallback MSAL configs (`OfficeNaaStrategy.ts:424-427`, `:451-454`) — "Office Add-ins live in an iframe and `localStorage` is unavailable/unstable across host suspensions." This is a per-top-level-browsing-context cache, which matters for question (b) — see below.
- **No dialog code exists today.** `Grep` for `displayDialogAsync|messageParent|messageChild|openBrowserWindow` across `src/client/**` returns hits **only** in test scaffolding: `shared/__mocks__/office-js.ts:49,180,383,458-459`, `jest.setup.js:34-35`, `outlook/taskpane/taskpane-test.html:145-146`. Zero production usage. There is nothing to reconcile or delete — this spike is greenfield.
- **`staticwebapp.config.json`** (`src/client/office-addins/staticwebapp.config.json`) sets only CORS/cache headers on the add-in's own static assets; it carries no CSP `frame-ancestors` directive relevant to this spike (that header lives on the Dataverse side, cited below from the repo's own standard).
- **`docs/standards/MODAL-DECISION-CRITERIA.md`** anti-pattern §4 (lines 268-276) is binding repo guidance: iframe-embedding an OOB `main.aspx` inside a proprietary shell is a documented Microsoft **unsupported-territory** statement ("Displaying a form within an IFrame embedded in another form is not supported", MS Learn revision 2025-05-07) plus a Dataverse CSP fact ("`frame-ancestors 'self' https://*.powerapps.com`" by default, revision 2026-02-10). This spike treats that citation as inherited from the repo doc (not independently re-fetched this session) and notes below why it does not automatically apply to the Office Dialog API's default behavior.

---

## Method and provenance policy

Every claim below is labeled:
- **DESK-RESEARCHED** — cites a Microsoft Learn URL + its shown last-updated date, an `@types/office-js` declaration, or an MDN citation with its last-modified date.
- **EMPIRICALLY VERIFIED** — states host, host build, platform, and what was observed. **None of the platform-behavior claims in this report are empirically verified** — this session has no live Word desktop or Dataverse tenant. The Operator Verification Recipe section names exactly what must be run to convert AMBER items to GREEN.
- A prior `.claude/agent-memory/researcher/MEMORY.md` entry dated 2026-09-08 ("Office Dialog API for Dataverse/MDA record open") was treated strictly as a **lead**, not evidence, per this task's instructions. It was independently re-verified this session by fetching the same and additional Microsoft Learn pages directly; several of its conclusions are **revised** below (notably on framing) because the earlier note did not distinguish the Dialog API's default non-iframe window behavior from `displayInIframe: true`.

Pages fetched directly this session:
- **DESK-RESEARCHED**: [Use the Office dialog API in your Office Add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/dialog-api-in-office-add-ins) — `ms.date: 2026-06-23`, page `updated_at: 2026-06-24`.
- **DESK-RESEARCHED**: [Office.UI interface](https://learn.microsoft.com/en-us/javascript/api/office/office.ui?view=word-js-preview) — `updated_at: 2026-08-31`.
- **DESK-RESEARCHED**: [frame-ancestors CSP directive — MDN](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Content-Security-Policy/frame-ancestors) — last-modified 2026-08-27.
- **DESK-RESEARCHED** (search corroboration, not independently fetched in full): [Dialog Origin requirement sets](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/dialog-origin-requirement-sets) and the Microsoft 365 Developer Blog cross-domain-messaging breaking-change post (mid-2021 change; still current per the fetched `dialog-api-in-office-add-ins` page's own restatement of the same rule).

---

## (a) Framing, per platform

**Verdict: No framing refusal is expected on either Word desktop or Word on the web, in the Dialog API's default (non-iframe) mode.**

**DESK-RESEARCHED** ([dialog-api-in-office-add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/dialog-api-in-office-add-ins), 2026-06-24): "The dialog box always opens in the center of the screen. The user can move and resize it. The window is *nonmodal*." This describes an independent window, not an inline embed. The doc's `displayInIframe` option makes the non-iframe default explicit: "When you set this property to `true` **and the add-in runs in a document opened in Office on the web**, the dialog box opens as a floating iframe rather than an independent window ... **If the add-in isn't running in Office on the web, the `displayInIframe` property is ignored**." Two consequences, per platform:

| Platform | Default dialog mechanism | Is it a `<iframe>`? |
|---|---|---|
| **Word desktop** (Windows/Mac) | Independent native window (WebView2-hosted top-level browsing context) | **No** — `displayInIframe` is silently ignored outside Office on the web |
| **Word on the web** | Independent floating window by default; becomes a floating `<iframe>` **only** if the caller explicitly passes `displayInIframe: true` | **No**, unless we opt in |

**DESK-RESEARCHED** (MDN [`frame-ancestors`](https://developer.mozilla.org/en-US/docs/Web/HTTP/Headers/Content-Security-Policy/frame-ancestors), 2026-08-27): `frame-ancestors` "specifies valid parents that may embed a page using `<frame>`, `<iframe>`, `<object>`, or `<embed>`" — it restricts embedding only, not a top-level navigation (`window.open()`, a redirect, or direct URL load). Because the Dialog API's default mode is a genuine top-level browsing context, **Dataverse's CSP `frame-ancestors 'self' https://*.powerapps.com`** (cited in `docs/standards/MODAL-DECISION-CRITERIA.md` line 274, MS Learn revision 2026-02-10) has no bearing on it — that header only fires when *something else* tries to iframe the MDA page, which is not what `displayDialogAsync` does by default.

**This revises the researcher-memory lead**, which framed the question mainly around iframe/CSP risk without distinguishing default-mode from `displayInIframe: true`. Under this spike's recommended design (below), `displayInIframe` is never set to `true` for MDA content — which also matches Microsoft's own explicit warning in the same doc: "Don't use `displayInIframe: true` if the dialog box ever redirects to a page that can't be opened in an iframe. For example, the sign in pages of many popular web services ... can't be opened in an iframe" — a warning that plausibly extends to a Dataverse/Entra sign-in redirect chain.

**Residual open item (AMBER)**: whether the WebView2-hosted window used on Word desktop enforces any *additional* restriction beyond standard CSP (e.g., a WebView2-specific navigation allow-list) is **not documented** in the fetched pages and is not something desk research can settle — see Operator Verification Recipe.

---

## (b) Auth context

**Verdict: No new Entra SPA redirect URI is required for any of the three options. The dialog cannot receive the pane's cached MSAL token by shared storage, and an MDA form's own sign-in is a separate concern from `@spaarke/auth`'s BFF-scoped token entirely.**

Two separate auth questions are folded into "(b)" and must be answered separately, because they are genuinely different systems:

1. **Can the Office dialog window share the pane's `@spaarke/auth` session (the BFF-scoped token)?**
   **DESK-RESEARCHED** + in-repo: the NAA/fallback MSAL cache location is `sessionStorage` (`OfficeNaaStrategy.ts:424-427`, `:451-454`), which is scoped per top-level browsing context. A dialog opened via `displayDialogAsync` is a **separate** top-level browsing context from the pane's iframe. Whether that new context inherits a copy of the opener's `sessionStorage` (as `window.open()` popups do for same-origin pages, per general browser behavior) is **not verified for the Office Dialog API specifically** — the Dialog API is documented as an abstraction over `window.open()`-like behavior on web and a native WebView2 window on desktop, and neither fetched page states whether session storage is cloned. **AMBER — untested.** The robust, storage-independent design (used by the recommendation below) is instead: the pane calls `dialog.messageChild(...)` (same-domain, no `Dialog Origin 1.1` requirement — DESK-RESEARCHED, [`office.ui`](https://learn.microsoft.com/en-us/javascript/api/office/office.ui?view=word-js-preview), `messageChild` requires only `DialogApi 1.2`) to hand identity context to the dialog once it signals ready, rather than relying on shared storage.

2. **Does an MDA record-form URL, if loaded (even briefly, before any redirect) inside the dialog, need its own sign-in?**
   Yes — and this is **independent of `@spaarke/auth` and NFR-09 entirely**. A Dataverse `main.aspx` URL authenticates against its own Entra app registration (the Power Platform / Dataverse client), using the browser/WebView2 profile's existing AAD session cookie if present, or an interactive sign-in if not. The add-in's BFF-scoped `bffApiScope` token (`api://1e40baad-.../user_impersonation`, `AuthService.ts:72`) is **not a Dataverse token** and cannot be handed to MDA to skip its sign-in. This is architecturally identical to what already happens when a user opens a Dataverse record in a plain browser tab (option 3's mechanism) — it is not new exposure and needs no new app registration.

**NFR-09 consequence — none.** Both the interstitial redirect page used in option (1)'s design and any Spaarke-hosted code page in option (2) are served from the existing SWA origin, so they use the **already-registered** `brk-multihub://<swa-host>` and `https://<swa-host>/auth-callback.html` URIs. MDA's own sign-in uses Dataverse's pre-existing app registration, never Spaarke's. No escalation trigger fires here.

**AMBER — untested**: whether the WebView2 profile behind a Word-desktop Office dialog carries the same AAD session cookie as the user's default browser (silent SSO) or forces an interactive prompt is a genuine platform unknown that only a live probe can answer.

---

## (c) Usability at dialog size, per interaction

**Verdict: UNVERIFIED — AMBER on all three interactions.** No fetched Microsoft documentation states or denies that a Dataverse record form functions correctly inside a Dialog-API window at any given size; the dialog is architecturally just a browser window at a stated percentage of screen size, so there is no *documented* restriction, but there is no *positive* confirmation either.

**DESK-RESEARCHED** ([dialog-api-in-office-add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/dialog-api-in-office-add-ins), 2026-06-24): "By default, the dialog box occupies 80% of the height and width of the device screen ... Set both values to 100% to get what is effectively a full screen experience. The effective maximum is 99.5%, and the window is still moveable and resizable." At a typical 1920×1080 display, 80%×80% ≈ 1536×864px — comparable to, or larger than, the 85%×85% Layout-1 `navigateTo` dialog size Spaarke already uses successfully for MDA record editing everywhere else (`docs/standards/MODAL-DECISION-CRITERIA.md` line 15). That existing precedent is suggestive but **not transferable evidence**: Layout 1 renders MDA-hosted-inside-MDA (same-origin iframe inside the model-driven app shell), not MDA loaded as a top-level document inside an Office host's dialog window — a different rendering context that could plausibly behave identically, but has not been observed to.

Per interaction, reported separately as the acceptance criteria require, all at the **default 80%×80% dimension** (no smaller size is proposed):

| Interaction | Verdict | Basis |
|---|---|---|
| **Saving the record** | UNVERIFIED (AMBER) | No Learn page documents MDA form-save behavior inside a Dialog-API window. Plausible (top-level document, same as a browser tab) but not observed. |
| **Opening a lookup picker** (which itself opens a nested overlay inside an already-constrained window) | UNVERIFIED (AMBER) | Highest concrete risk of the three — a lookup picker overlay inside an 80%-of-screen window has less headroom than a full browser tab; whether MDA's lookup control degrades gracefully at this size is unknown. |
| **Scrolling/using a subgrid** | UNVERIFIED (AMBER) | Same reasoning; subgrids are the most layout-sensitive MDA control. |

No source claims "the form renders" as a substitute for these three — this table intentionally reports them separately per the task's constraint.

---

## (d) Propagation

**Verdict: `messageParent` is architecturally available cross-domain (Dialog Origin 1.1), but an unmodified Dataverse `main.aspx` page will never call it — Microsoft does not instrument MDA to participate in Office Dialog messaging. Propagation must therefore use a different, close/focus-triggered refetch mechanism, for every one of the three options, not just the ones that avoid MDA.**

**DESK-RESEARCHED** ([dialog-api-in-office-add-ins](https://learn.microsoft.com/en-us/office/dev/add-ins/develop/dialog-api-in-office-add-ins), 2026-06-24, section "Cross-domain messaging to the host runtime"): "After the dialog opens, either the dialog or the parent runtime can navigate away from the add-in's domain. If either of these things happens, a call to `messageParent` fails unless your code specifies the domain of the parent runtime" via `DialogMessageOptions.targetOrigin`. This "**requires the [Dialog Origin 1.1 requirement set](https://learn.microsoft.com/en-us/javascript/api/requirement-sets/common/dialog-origin-requirement-sets)**. Older versions of Office that don't support the requirement set ignore the `DialogMessageOptions` parameter." So the origin constraint is real but **surmountable** by API design — cross-domain messaging is a supported, documented capability, not a hard wall.

The wall is elsewhere. `messageParent` is one of only two JS APIs callable from *inside* the dialog page's own script (the doc states this explicitly: "`messageParent` ... is one of *only* two Office JS APIs that you can call in the dialog box"), and it must be called **by the page loaded in the dialog**. An unmodified Dataverse `main.aspx` is a Microsoft-authored page that has no knowledge of, and does not load, the Office JavaScript API — there is no documented extensibility hook (form script, web resource, ribbon command) through which a Dataverse form can call `Office.context.ui.messageParent(...)`. This is not an origin-policy failure; it is that **the participant on the other end never opts in**. So even if we solve (a) framing and (b) sign-in for a raw MDA form, (d) still fails for that content specifically — MDA cannot report anything back to the pane.

**The named alternative** (as the task anticipates in step 5): the pane listens for the dialog's close (`Office.EventType.DialogEventReceived`, available for anything opened via `displayDialogAsync`) or, for a plain browser tab opened via `Office.context.ui.openBrowserWindow()` (which has **no** close/return event at all — DESK-RESEARCHED, the [`Office.UI` reference](https://learn.microsoft.com/en-us/javascript/api/office/office.ui?view=word-js-preview) lists no event for `openBrowserWindow`), a `window`/`document` `focus`/`visibilitychange` listener in the pane that re-fetches the record/profile fields when the user returns to Word. This is a weaker signal than an explicit message (it fires on "the user came back," not "the user actually saved a change"), but it satisfies FR-10's acceptance intent without requiring any cooperation from the far end, and it is the **same mechanism regardless of which of the three options is chosen** — it is not a differentiator between them.

---

## Option comparison

| | (1) MDA form directly in dialog | (2) Spaarke-hosted code page in dialog | (3) Read-only detail in-pane + browser-tab escape hatch |
|---|---|---|---|
| **API surface** | `displayDialogAsync` → add-in-domain interstitial → redirect to MDA URL (satisfies the documented same-domain-for-`startAddress` rule) | `displayDialogAsync` → add-in-domain page for the dialog's entire lifetime | `Office.context.ui.openBrowserWindow(url)` — no Dialog API involved at all |
| **(a) Framing** | No refusal expected (default non-iframe mode; §a) | No refusal — never leaves the add-in's own origin | No refusal — a plain browser tab, same mechanism users already use today |
| **(b) Auth** | No new Entra reg; MDA authenticates itself, independent of `@spaarke/auth` (§b, item 2) | No new Entra reg; dialog can reuse `OfficeNaaStrategy` config or receive identity via `messageChild` | No new Entra reg; browser tab authenticates exactly as today's MDA usage does |
| **(c) Usability** | UNVERIFIED (AMBER) — full MDA fidelity IF it works, but save/lookup/subgrid at dialog size are unconfirmed | N/A for full-form edit — a hand-built Fluent form cannot replicate business rules/subgrids/ribbon (`docs/standards/MODAL-DECISION-CRITERIA.md` "Edit fidelity" dimension: "Full-form edit ... Nothing else can match" `navigateTo`) | N/A — no in-Word editing; full usability guaranteed because it's identical to normal browser MDA usage |
| **(d) Propagation** | Close-event refetch only (MDA cannot call `messageParent`) | Full control — native `messageParent`/`messageChild`, same-domain, no Dialog Origin 1.1 needed | Focus/visibility-based refetch only (no close event exists for `openBrowserWindow`) |
| **New component cost** | None — zero new Spaarke UI; one small interstitial redirect page | New, ongoing-maintenance surface: a hand-authored Fluent v9 form (ADR-050 Path C + ADR-021) that must track whichever fields/entities the pane needs, forever drifting from the maker-authored MDA form it doesn't replicate | None — the "read-only detail" piece is a small in-pane card (already implied by FR-09), not a dialog at all |
| **Satisfies FR-13 "finish editing in the record"** | Potentially — full fidelity IF (c) resolves favorably | No — by construction, a proprietary form is light-edit at best, which is exactly the fidelity gap FR-13's premise exists to avoid | No — editing happens outside Word entirely |
| **Residual unresolved risk after this spike** | Two genuine platform unknowns ((c) form usability, part of (b) sign-in silence) that only a live probe resolves | One structural, not platform, cost: rebuilding form fidelity is expensive and never fully matches the maker-authored form | None — every mechanism it depends on is either already proven elsewhere in Spaarke or is the status quo |

---

## Recommendation

**Recommendation: Option (3) — read-only detail in-pane with a browser-tab escape hatch (`Office.context.ui.openBrowserWindow`).**

This is the owner's documented fallback (spec.md Owner Clarifications line 257), and the task's own note pre-authorizes recommending it "without hedging if the evidence supports it." The evidence supports it specifically because it is the only one of the three options with **zero remaining platform risk that only a live probe could resolve** — every mechanism it depends on (browser-tab MDA sign-in, no dialog/messaging machinery at all) is either already how Spaarke users interact with Dataverse today, or a documented, unconditional Office.js API (`OpenBrowserWindowApi 1.1`) with no origin constraints to verify.

This is a closer call than the researcher-memory lead suggested, and the reasoning matters for anyone revisiting it: desk research materially **de-risks** option (1) beyond what that earlier note found — there is no documented framing refusal in the Dialog API's default mode, and no new Entra registration is needed for any option. But two genuinely platform-specific facts remain unresolved by any Microsoft documentation found this session — whether MDA's save/lookup-picker/subgrid interactions remain operable at ~80% dialog dimensions (§c), and whether the WebView2 window on Word desktop carries an existing AAD session silently (§b) — and this task's own guardrail (a prior agent on this project already over-built ahead of a spike's verification and had to revert) argues against committing task 027 to build full MDA-dialog integration on those two unconfirmed assumptions. Recommending option (3) is the choice that does not require that confidence.

**Cost of option (1) (not chosen)**: forgoes full-fidelity in-Word record editing, which is the ideal outcome for FR-13's "finish editing in the record" premise. If a future project wants to revisit it, the Operator Verification Recipe below is exactly the two checks (§c, §b-2) that would need to pass; nothing else in this report blocks it.

**Cost of option (2) (not chosen)**: forgoes any Spaarke-owned edit surface inside the dialog. This is a smaller loss than it first appears, because option (2)'s own ceiling is light-edit at best (per `docs/standards/MODAL-DECISION-CRITERIA.md`'s "Edit fidelity" dimension, a hand-authored Fluent form structurally cannot match `navigateTo`'s full-form fidelity — business rules, subgrids, ribbon). Building it would mean taking on a new, field-mapping-dependent, ongoing-maintenance component (per root CLAUDE.md §11, this would need its own three-question justification) for a capability that, at best, only partially satisfies FR-13's premise. If a future project wants a **read/light-edit browse-in-context** surface, `docs/standards/MODAL-DECISION-CRITERIA.md`'s Family 3 (`RecordNavigationModalShell` + a `FormModal`/`PreviewModal` preset, ADR-050) is the correct starting point, not this spike's Dialog-API path — but that surface, if built, would be a Spaarke Code Page or PCF context per that standard, not inside an Office Dialog.

---

## Consequences for FR-10 and FR-13

- **FR-10's acceptance criterion** ("the record opens and is usable; edits made there are reflected in the pane on return") is satisfied by option (3) with one adjustment: "reflected in the pane on return" is implemented as a **focus/visibility-triggered refetch** in the pane (re-fetch identity/profile/record-card fields when the pane regains focus after `openBrowserWindow` was used), not a push notification from the record. This is the named alternative mechanism the task requires when messaging is unavailable (§d) — it is weaker (fires on "user returned," not "user saved") but is the same limitation every option in this report carries, since MDA never calls `messageParent` regardless of hosting mechanism.
- **FR-13's "finish editing in the record" premise weakens**, exactly as spec.md's Unresolved Questions (line 282) anticipated: the user leaves Word entirely to edit the full record. The deferred "+More fields on create" request (design.md §4.2, spec.md line 37) stays deferred and unaddressed by this project — this spike does not reopen it, but confirms the spec's own prediction that a Dialog-API negative-or-uncertain result would leave it open. Nothing in this report treats that as new scope; it is recorded here as the consequence the spec asked this spike to surface.
- No escalation trigger fires. FR-10's acceptance criterion is satisfiable by option (3) (so the first `<escalation>` trigger — "the record cannot be opened in a usable form by any mechanism" — does not apply), and no path in this report requires a new Entra app registration, redirect URI class, or admin-consent-requiring permission (so the second trigger does not apply). The third trigger (an anti-pattern under `docs/standards/MODAL-DECISION-CRITERIA.md`) also does not apply: the recommended design never iframe-embeds `main.aspx`.

---

## Operator verification recipe

**Nothing in this section is to be committed to `src/`.** These are paste-ready probes to run from a scratch/dev build, not production code. They exist to convert the AMBER items above to a confirmed verdict if a future task revisits option (1) or (2), or simply to sanity-check option (3)'s assumptions.

### Probe 1 — confirm option (3)'s browser-tab mechanism (low risk, quick)

From any dev build of the add-in (Word desktop or web), in a temporary button handler — do not leave this wired into `App.tsx`:

```typescript
// TEMPORARY PROBE — remove before commit. Paste into a throwaway button handler.
Office.context.ui.openBrowserWindow(
  "https://<your-dev-org>.crm.dynamics.com/main.aspx?etn=sprk_document&id=<a-real-record-guid>&pagetype=entityrecord"
);
```
Observe: does the tab open with an already-signed-in Dataverse session (no interactive prompt), assuming the operator is already signed into that tenant in the same browser profile? This is expected to work identically to opening the URL by hand — confirm it, don't assume it.

### Probe 2 — confirm/deny option (1)'s two AMBER items, if ever revisited

```typescript
// TEMPORARY PROBE — remove before commit.
// Step 1: host an add-in-domain interstitial (any existing add-in page works for this probe)
// that immediately does:
window.location.href = "https://<your-dev-org>.crm.dynamics.com/main.aspx?etn=sprk_document&id=<a-real-record-guid>&pagetype=entityrecord";

// Step 2: invoke the dialog against that interstitial from the pane:
Office.context.ui.displayDialogAsync(
  "https://<your-swa-host>/probe-redirect.html", // same-origin as the add-in — required
  { height: 80, width: 80 },
  (asyncResult) => {
    const dialog = asyncResult.value;
    dialog.addEventHandler(Office.EventType.DialogEventReceived, (arg) => {
      console.log("[Probe] dialog closed/errored:", arg);
    });
  }
);
```
Observe and record, **separately**, on both Word desktop and Word on the web:
1. Does the redirect to the MDA URL complete without any framing-refusal error in the console (there should be none per §a, but confirm)?
2. Does the MDA form load already signed in, or does it prompt for interactive sign-in? (§b-2 — the genuinely unknown item)
3. At the dialog's rendered size, does **Save** on the form complete normally?
4. Does opening a **lookup picker** field render and function correctly, including its own nested overlay?
5. Does a **subgrid** on the form scroll and render its rows correctly?

Report each of 3–5 as a separate pass/fail, not a single "the form works" observation, per this spike's own method.

### Probe 3 — confirm the `messageChild` same-domain path (only relevant to option (2), if ever revisited)

```typescript
// TEMPORARY PROBE — remove before commit.
Office.context.ui.displayDialogAsync(
  "https://<your-swa-host>/probe-messagechild.html",
  { height: 40, width: 30 },
  (asyncResult) => {
    const dialog = asyncResult.value;
    dialog.messageChild(JSON.stringify({ probe: true, token: "example-not-a-real-token" }));
  }
);
// probe-messagechild.html:
Office.onReady(() => {
  Office.context.ui.addHandlerAsync(Office.EventType.DialogParentMessageReceived, (arg) => {
    console.log("[Probe] received from parent:", arg.message);
  });
});
```
Confirms `messageChild` delivery same-domain without needing Dialog Origin 1.1 (per §b, item 1's DESK-RESEARCHED claim).

---

## Open questions remaining

- **§b, item 1**: whether a dialog's top-level browsing context inherits a clone of the pane's `sessionStorage` (same-origin) is unresolved by desk research and was not tested — Probe 3 above sidesteps it by using `messageChild` instead, so it does not block the recommendation, but it remains genuinely open for anyone who later wants to rely on shared storage.
- **§b, item 2**: whether the WebView2 profile behind a Word-desktop Office dialog carries the operator's existing AAD session (silent) is unresolved — Probe 2, step 2.
- **§c, all three interactions**: save / lookup picker / subgrid usability at ~80% dialog dimensions on a live tenant is unresolved — Probe 2, steps 3–5.
- **FR-13's "+More fields on create" deferred request** stays open per design.md §4.2 — this spike confirms the spec's own prediction (line 282) that a non-full-fidelity Spike-2 outcome leaves it unaddressed; it is not reopened by this task.

No project source or task tracker file was modified during the desk-research portion of this task except this report and the two files listed in the notes below.
