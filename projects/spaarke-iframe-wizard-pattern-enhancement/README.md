# Spaarke Iframe-Wizard Pattern Enhancement

> **Status**: Design (pre-planning)
> **Created**: 2026-05-27
> **Source**: Discovered during R4 task 043 (W-5 Context → Workspace mount source)
> **Predecessor**: [spaarke-ai-platform-unification-r4](../spaarke-ai-platform-unification-r4/)
> **Quick links**: [design.md](design.md)

---

## Overview

SpaarkeAi runs as a React 19 code page inside a Dynamics iframe. Many features need to mount a workspace tab from **outside** the SpaarkeAi React tree — wizards running in their own iframes, model-driven-app main forms, background job completions, external SPAs, Office Add-ins. None of these can call `useDispatchPaneEvent()` because the React context isn't available outside the `<PaneEventBusProvider>` scope.

R4 task 043 (W-5) discovered this when wiring `CreateProjectWizard` as a mount source and had to pivot to the in-process `SemanticSearchCriteriaTool`. The broader pattern problem was deferred to this project.

## Problem Statement

How does code **outside** the SpaarkeAi React tree tell SpaarkeAi to mount a workspace tab, given the core-product constraint that **no Power Automate and no Dataverse plugins** may participate in the solution?

## Critical Constraints

This is a **core product** project. The following are explicitly OUT OF SCOPE as solution mechanisms:

- **Power Automate** — not part of the core product
- **Dataverse plugins** — not part of the core product
- **Anything plugin-triggered** (ServiceBus pushes from plugin post-actions, plugin pipelines, etc.)

Allowed mechanisms are limited to:

- Web platform: `postMessage`, `BroadcastChannel`, `CustomEvent`, `sessionStorage` events
- BFF API: HTTP polling, WebSockets, SSE, signed callback URLs
- Dataverse Web API (client-side via `Xrm.WebApi`)
- React composition (in-process components within the provider tree)

## Scope

See [design.md](design.md) for the full write-up — 5 use case surfaces, options evaluation, recommended architecture, implementation phases.

## Status

Pre-planning. Design document captured during R4 close-out. Project execution will follow the normal `/design-to-spec` → `/project-pipeline` pipeline once design is approved.

## Graduation Criteria (placeholder)

To be finalized during `/design-to-spec`:

- [ ] postMessage bridge implemented in SpaarkeAi
- [ ] At least one wizard demonstrates "Add to Workspace" flow end-to-end
- [ ] Protocol documented (event types, validation, security)
- [ ] No Power Automate or plugin code introduced
- [ ] All 5 surfaces have a documented integration path

## Changelog

| Date | Entry |
|---|---|
| 2026-05-27 | Project folder created; design.md drafted. Captured during R4 close-out (R4 task 043 discovery + operator decision 2026-05-27 to scope as separate project rather than R4 add-on). |
