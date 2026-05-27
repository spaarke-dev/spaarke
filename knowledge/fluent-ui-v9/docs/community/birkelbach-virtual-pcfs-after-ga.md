---
source: https://dianabirkelbach.wordpress.com/2024/12/06/virtual-pcfs-with-fluent-ui-9-after-ga/
fetched: 2026-05-26
author: Diana Birkelbach (Dianamics PCF Lady)
published: 2024-12-06
summary: Virtual PCF GA. Baseline React 16.14 + Fluent 9.46.2. Still no Power Pages support. Bundle-size figures (production ~234 KB).
loadWhen: Power Pages compatibility question OR validating virtual PCF baseline versions.
notes: WebFetch capture; verify against live post before quoting verbatim.
---

# Virtual PCFs with Fluent UI 9 after GA — Diana Birkelbach

## Overview

Virtual Components for Power Apps have reached **general availability** (after being in preview since April 2022). These components allow developers to leverage React and Fluent UI from platform libraries without bundling them separately.

## Key constraints

**Virtual components are still NOT supported in Power Pages** — a significant constraint for portal-based implementations.

## Library version evolution

| Stage | React | Fluent |
|---|---|---|
| Initial preview | 16.8.6 | 8.29.0 |
| **GA (current)** | **16.14.0** | **9.46.2** |

The docs previously showed Fluent 9 could work with React 16.8.6, but developers sometimes needed workarounds for components requiring newer React versions. With GA, React 16.14.0 alongside Fluent 9.46.2 is the supported pair. React 16.8.6 no longer appears in official docs, but Microsoft confirms existing projects continue functioning.

## New project configuration

When creating a fresh virtual PCF using `pac pcf init -fw react` with PAC CLI v1.37.4, projects automatically include:

- **Manifest**: Fluent 9.46.2 and React 16.14.0
- **package.json**: Matching versions
- **Updated PCF typings**

## Migrating existing projects

1. Run `npm update` to update packages
2. Rebuild projects with the new versions
3. No immediate action required — compatibility maintained

**Troubleshooting**: If build errors occur after version updates, delete `package-lock.json` and `node_modules`, close VS Code, reopen, then `npm install`.

## Bundle size results (example project)

| Mode | Size |
|------|------|
| Debug `bundle.js` | 7,666 KB |
| Debug solution | 1,289 KB |
| **Production `bundle.js`** | **234 KB** |
| **Production solution** | **68 KB** |

The significant production-size reduction makes uploading via `pac pcf push` feasible.

## Development experience

Fluent UI 9 improves development speed compared to Fluent 8. Birkelbach particularly appreciates how the theming (provided through the PCF context) can be applied.

## Community Q&A highlights

Standard (non-virtual) controls can wrap custom React and Fluent UI implementations. David Rivard published a solution addressing DOM ID collision issues using **`IdPrefixProvider`**.
