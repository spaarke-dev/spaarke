# wwwroot Asset Inventory (Task 016)

> **Source**: `deploy/api-publish/wwwroot/` after `dotnet publish`
> **Captured**: 2026-05-24
> **Status**: 4 sourcemap files confirmed (matches pipeline pre-flight)

---

## Summary

- **Total wwwroot size**: ~9.5 MB (9,586,516 bytes)
- **Sourcemap files**: 4 (`.js.map` — all in `playbook-builder/assets/`)
- **Top-level directories**: `playbook-builder/`

---

## Sourcemap files (FR-A2 target — exclude from publish)

| File | Location |
|---|---|
| `flow-vendor-BHHmI87s.js.map` | `wwwroot/playbook-builder/assets/` |
| `fluent-vendor-CmJVTK5h.js.map` | `wwwroot/playbook-builder/assets/` |
| `index-BWeOj5bW.js.map` | `wwwroot/playbook-builder/assets/` |
| `react-vendor-BWFb42Va.js.map` | `wwwroot/playbook-builder/assets/` |

**Estimated savings from FR-A2 exclusion**: a few MB (4 files; rough size proportional to JS bundle sizes — actual ~2-7 MB combined per spec hints).

---

## Other wwwroot contents

- `playbook-builder/` — Vite-built playbook builder UI (assets + index.html)
- No README, LICENSE, or other non-shipping files detected

---

## FR-A2 implementation guidance

Phase 4 task 041 should add to `Sprk.Bff.Api.csproj`:

```xml
<ItemGroup>
  <Content Update="wwwroot\**\*.js.map" CopyToPublishDirectory="Never" />
</ItemGroup>
```

Verification post-deploy: `find deploy/api-publish/wwwroot -name "*.map"` returns 0 entries.
