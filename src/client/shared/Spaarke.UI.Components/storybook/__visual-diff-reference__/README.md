# `__visual-diff-reference__` — MDA Visual Parity Capture Directory

> **Status**: Empty placeholder — populated by the human reviewer per Phase A acceptance gate (task 009).
> **Created by**: Phase A acceptance-gate sub-agent on 2026-06-01
> **Acceptance gate**: NFR-01 (MDA pixel-parity vs. Events sub-grid, light + dark + 75/100/125/150% zoom)

---

## Purpose

This directory holds the side-by-side screenshot pairs used to verify the
framework `<DataGrid />` visually matches the canonical MDA Events sub-grid.
The capture protocol is documented in:
`projects/spaarke-datagrid-framework-r1/notes/phase-a-acceptance-gate.md` §7.

## Expected layout

```
__visual-diff-reference__/
├── README.md                 ← this file
├── mda-events-subgrid/       ← reference captures from live MDA
│   ├── light-zoom75.png
│   ├── light-zoom100.png
│   ├── light-zoom125.png
│   ├── light-zoom150.png
│   ├── dark-zoom75.png
│   ├── dark-zoom100.png
│   ├── dark-zoom125.png
│   └── dark-zoom150.png
├── framework-datagrid/       ← matching captures from Storybook story DataGrid/Core → DefaultSprkEvent
│   ├── light-zoom75.png
│   ├── light-zoom100.png
│   ├── light-zoom125.png
│   ├── light-zoom150.png
│   ├── dark-zoom75.png
│   ├── dark-zoom100.png
│   ├── dark-zoom125.png
│   └── dark-zoom150.png
└── diff-notes.md             ← human-authored side-by-side observations
```

## Capture rules

- **Naming convention**: `{theme}-zoom{percent}.png` (e.g., `dark-zoom125.png`).
- **MDA reference**: full viewport screenshot of the Events sub-grid on a
  Matter form. Set the OS / browser theme + browser zoom to match the filename
  before capture.
- **Framework**: same viewport from the Storybook story
  `DataGrid/Core → DefaultSprkEvent`, configured via the `theme` argType +
  `parameters.viewport` preset to match the MDA capture's theme/zoom.
- **`diff-notes.md`**: per-condition human review notes. Capture pixel-level
  regressions (with screenshots highlighting the area), any deliberate
  framework deviations (e.g., richer empty-state copy), and a final accept /
  fix decision per condition.

## Gitignore note

PNG files in this directory are **deliberately committed** (they are the
acceptance-gate evidence). Do NOT add a `.gitignore` exclusion.

---

*Populated by human reviewer at Phase A close. Sub-agent leaves it empty.*
