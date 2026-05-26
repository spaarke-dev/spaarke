---
source: https://www.ariclevin.com/powerapps/post/creating-a-new-virtual-pcf-with-fluent-ui-version-9/
fetched: 2026-05-26
author: Aric Levin
summary: PCF Builder VS Code walkthrough for creating a virtual PCF with React + Fluent UI (configure namespace, control name, template, manifest changes).
loadWhen: bootstrapping a brand-new PCF project. For ongoing PCF work, prefer the Microsoft docs + Birkelbach references.
notes: |
  WebFetch capture (the live blog post is short / image-heavy; the curated capture here is light).
  Companion posts under https://www.ariclevin.com/category/powerapps/pcf/.
---

# Creating a new Virtual PCF with Fluent UI version 9 — Aric Levin

This guide demonstrates how to create a Power Apps PCF control using Fluent UI v9 components in Visual Studio Code.

## Step 1 — Create the VSCode project

1. Open VS Code and navigate to your project folder (e.g., `C:\Repos\MessageBar`)
2. `Ctrl+Shift+P` → command palette
3. Type "PCF" to see PCF Builder commands
4. **"PCF Builder: Initialize Component (Simple)"**

## Step 2 — Configuration prompts

| Prompt | Value |
|---|---|
| Namespace | `PCFControls.Virtual` |
| Control Name | `MessageBar` |
| Template | `field` |
| Additional libraries | `React + Fluent UI` |

The system runs `pac pcf init` and `npm install` automatically.

## Step 3 — Modify `ControlManifest.input.xml`

Key changes:

- Change control type from `standard` to `virtual`
- Add resources for virtual controls for v9
- Include any required properties for your control

## Step 4 — Create the FluentMessageBar component

Folder structure: `MessageBar > components > FluentMessageBar.tsx`. Add React component code using Fluent UI v9.

## Takeaway

The process combines PCF Builder commands with React + Fluent UI to streamline virtual control development for Power Platform.
