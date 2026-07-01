# Research Evidence — Iframe-embedding OOB Dataverse `main.aspx` (2026-07-01)

> **Source**: `researcher` subagent invocation on 2026-07-01, in the context of the AI SpaarkeAi Workspace UI R2 project design conversation.
> **Purpose**: Freeze the evidence trail supporting the R2 decision to retire the iframe-hosted OOB `main.aspx` pattern (SmartTodoModal) and standardize on Layout 1 (OOB `Xrm.Navigation.navigateTo`) for entity records.
> **Question researched**: Does Microsoft officially support (or explicitly discourage, or silently tolerate) iframe-embedding of the Dataverse OOB main form page — `main.aspx?pagetype=entityrecord&etn=<entity>&id=<guid>` — as a rendered surface INSIDE a custom React application (Custom Pages / Code Sites / Code Pages) rendered as an `<iframe>` child?
> **Referenced by**: [`../design.md`](../design.md) §3.3 and §7.1.

---

## 1. Direct answer

**DISCOURAGED and progressively BLOCKED.** Microsoft has never documented `main.aspx?pagetype=entityrecord&…` as an embeddable surface. As of the 2026-02-10 revision of the model-driven CSP admin doc, the default `Content-Security-Policy: frame-ancestors 'self' https://*.powerapps.com` is sent for Dataverse environments — third-party origins are blocked unless an admin explicitly allow-lists them. Separately, the model-driven iframe-and-web-resource doc (updated 2025-05-07) states verbatim:

> **"Displaying a form within an IFrame embedded in another form is not supported."**

The strongest single source is the CSP admin doc itself, which shows the default and enumerates that only `frame-ancestors` is admin-customizable.

---

## 2. Evidence table

| Source | Date | Position | URL |
|---|---|---|---|
| MS Learn — CSP admin doc | 2026-02-10 | Default `frame-ancestors 'self' https://*.powerapps.com`; strict-mode also locks `base-uri 'none'` and `frame-src 'self' blob: <platform>` | [content-security-policy](https://learn.microsoft.com/en-us/power-platform/admin/content-security-policy) |
| MS Learn — model-driven iframe/web resource | 2025-05-07 | "Displaying a form within an IFrame embedded in another form is not supported" | [use-iframe-and-web-resource-controls-on-a-form](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/use-iframe-and-web-resource-controls-on-a-form) |
| MS Learn — code apps embed-iframe how-to | 2026-03-06 | Documents ONLY the reverse (code app hosted BY a model-driven host). Does not document `main.aspx` as embeddable content. | [embed-iframe](https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/embed-iframe) |
| MS Learn — `Xrm.Navigation.navigateTo` reference | 2026-04-09 | Modal `target: 2` opens `entityrecord` as dialog. No prev/next collection option. `generative` pageType added recently. | [navigateto](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto) |
| 2tolead — Power Apps CSP 2026 setup guide | 2026 | Strict CSP enforcement for code apps tightens late January 2026; third-party sources blocked unless allow-listed | [Power Apps CSP 2026 Setup Guide](https://www.2tolead.com/insights/power-apps-content-security-policy-2026-setup-guide) |
| Andrew Butenko blog | 2019 | Canonical `navigateTo` gotchas post; still cited by community for modal-dialog work | [butenko.pro navigateTo gotchas](https://butenko.pro/2019/12/23/xrm-navigation-navigateto-gotchas-tricks-and-limitations/) |
| Carl de Souza blog | c. 2020 | Modal-dialog `openForm`/`navigateTo` pattern; iframe not recommended | [carldesouza.com](https://carldesouza.com/using-the-new-modal-dialog-to-open-forms-in-dynamics-365-using-xrm-navigation-navigateto/) |

**Community consensus check**: No post from Scott Durow, Kylie Kiser, Ryan Redmond, Natraj Yegnaraman, Elaiza Benitez, or Mark Carrington in 2025–2026 was found endorsing iframe-embedding of `main.aspx`. Community consensus tracks Microsoft's `navigateTo` guidance.

---

## 3. What's changed in the last 12 months

- **CSP tightening in code apps**: strict-mode CSP enforcement rolled out late January 2026 for code apps (2tolead guide) — third-party CDNs, APIs, and iframes must be allow-listed or they are blocked. This does NOT retroactively break `main.aspx` for same-origin embedding, but it signals the direction.
- **`navigateTo` gained `generative` pageType** (April 2026 doc revision) — first-class support for generative pages as modal dialogs. No new record-collection/browse option.
- **Code apps embed-iframe doc published** (2026-03-06) — Microsoft's only new "embedding" guidance covers embedding code apps INTO a model-driven host, not the reverse.
- **No 2025 wave 2 or 2026 wave 1 release-plan item** relaxing or documenting `main.aspx` iframe embedding.

The direction of platform change is **tighter**, not looser.

---

## 4. `formmode=readonly` — separate answer

No Microsoft documentation distinguishes `formmode=readonly` from the editable variant for iframe-embedding. Same CSP applies; same "not supported" statement applies. There is no evidence Microsoft treats the readonly URL as a documented embedding surface. Loading the OOB main form via `main.aspx?…&formmode=readonly` inside an iframe carries the same support-contract risk as the editable variant.

---

## 5. Same-origin nuance

Same-origin embedding (Code Page at `<org>.crm.dynamics.com` iframing `main.aspx` at the same `<org>.crm.dynamics.com`) **passes** the default `frame-ancestors 'self'` check — the CSP header will not block it. But two independent constraints still apply:

1. The 2025-05-07 iframe/web-resource doc's "not supported" statement is a **support-contract statement, not a CSP one**. It applies regardless of origin.
2. Modern UI reserves the right to change internal `main.aspx` behavior at any time. The Unified Interface uses `main.aspx` as its top-level shell; treating it as a stable child-iframe target is betting on an internal contract.

**So**: same-origin works today, is not blocked, and is still contractually unsupported.

This is the specific posture SmartTodoModal has been in — working, not blocked, unsupported.

---

## 6. What Microsoft recommends for cross-record modal browse

**`Xrm.Navigation.navigateTo` with `target: 2`** opens an entity record as a modal dialog (returns a Promise that resolves on dialog close). It has **no** prev/next collection option. The Microsoft-idiomatic pattern for "browse across records without close/reopen" is:

- **Custom Page** (`pageType: 'custom'`) hosting the browse chrome (prev/next chevrons, "1 of N", the shared React shell)
- Inside the Custom Page, either render a proprietary form OR call `navigateTo`/`openForm` per record to launch the OOB form as a nested modal.

There is no documented Microsoft "record navigator" API, no `Xrm.Navigation.navigateToNext`, no built-in collection cursor.

This is exactly the Spaarke Layout 2 architecture (`RecordNavigationModalShell` + proprietary content). Layout 2 is aligned with Microsoft's own idiom.

---

## 7. Confidence level

**HIGH.** Four dated Microsoft Learn sources from 2025-05-07 through 2026-04-09 converge; no MVP counter-signal in the last 12 months; CSP admin doc explicit about defaults; `navigateTo` reference explicit about supported pageTypes and dialog options. The only residual uncertainty is the same-origin edge case (§5), which Microsoft docs do not explicitly address — but the "not supported" statement is not scoped to cross-origin.

---

## 8. Implications for Spaarke — summarized

- **Layout 1** (OOB `navigateTo` + OOB main form): fully aligned with Microsoft-supported path. This is the canonical default for R2.
- **Layout 2** (`RecordNavigationModalShell` + proprietary content): fully aligned with Microsoft's own idiom for cross-record browse (Custom Page + proprietary chrome + navigateTo escalation for edit).
- **Layout 3 candidate** (iframe of OOB `main.aspx` in the shell — SmartTodoModal today): contractually unsupported, works via same-origin CSP passthrough, at risk under platform change. Retired in R2.
- **`formmode=readonly` variant**: same support-contract issue. Not a mitigation.

---

## 9. Sources (full URLs)

- [Use IFRAME and web resource controls on a form (model-driven apps)](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/use-iframe-and-web-resource-controls-on-a-form) — updated 2025-05-07
- [Content security policy (Power Platform admin)](https://learn.microsoft.com/en-us/power-platform/admin/content-security-policy) — updated 2026-02-10
- [How to: Embed a Code App in an Iframe](https://learn.microsoft.com/en-us/power-apps/developer/code-apps/how-to/embed-iframe) — updated 2026-03-06
- [Xrm.Navigation.navigateTo (Client API reference)](https://learn.microsoft.com/en-us/power-apps/developer/model-driven-apps/clientapi/reference/xrm-navigation/navigateto) — updated 2026-04-09
- [Power Apps Content Security Policy: 2026 Setup Guide (2tolead)](https://www.2tolead.com/insights/power-apps-content-security-policy-2026-setup-guide)
- [Xrm.Navigation.navigateTo gotchas (Andrew Butenko)](https://butenko.pro/2019/12/23/xrm-navigation-navigateto-gotchas-tricks-and-limitations/)
- [Using the New Modal Dialog to Open Forms in Dynamics 365 (Carl de Souza)](https://carldesouza.com/using-the-new-modal-dialog-to-open-forms-in-dynamics-365-using-xrm-navigation-navigateto/)

---

## 10. Provenance

- **Researcher subagent invocation**: 2026-07-01
- **Confidence rating assigned by researcher**: HIGH
- **Follow-up review recommended**: 6-month cadence, or on any of the following events:
  - Microsoft publishes a Power Platform release-plan item about main.aspx embedding
  - CSP strict-mode enforcement extends beyond code apps to broader Dataverse pages
  - An MVP publishes a documented reversal of the community consensus
  - Spaarke encounters a `main.aspx` behavior change in any environment
