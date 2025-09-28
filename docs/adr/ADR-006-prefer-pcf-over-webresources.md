# ADR-006: Prefer PCF controls over legacy JavaScript webresources
Status: Accepted
Date: 2025-09-27
Authors: Spaarke Engineering

## Context
Legacy webresources (JS/HTML) are harder to package, test, and lifecycle with modern Power Platform. PCF controls provide typed, testable components and better performance characteristics.

## Decision
- Build custom UI using PCF controls (TypeScript) for model‑driven apps and custom pages.
- Avoid new legacy webresources; use React/SPA where appropriate for external surfaces (Power Pages, add‑ins).

## Consequences
Positive:
- Better lifecycle management, packaging, and testability.
- Access to modern UI patterns and performance improvements.
Negative:
- Learning curve for PCF; initial scaffolding effort.

## Alternatives considered
- Continue with webresources. Rejected due to maintainability and lifecycle limitations.

## Operationalization
- PCF project templates; shared UI libraries; CI build steps for PCF.
- Coding standards favor PCF for embedded UI and React for SPAs.

## Exceptions
Small, static tweaks may use existing webresources if already deployed and low‑risk; no new ones should be created without explicit approval.

## Success metrics
- Reduced UI regressions; faster delivery of embedded UI features; improved performance metrics.
