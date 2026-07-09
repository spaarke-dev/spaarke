# ⛔ DEPRECATED — Field Mapping Administrator Guide (February 2026)

> **Status**: DEPRECATED as of 2026-07-09. Do not follow this document.
> **Superseded by**:
> - **Maker guide** → [`docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`](../guides/FIELD-MAPPING-ADMIN-GUIDE.md)
> - **Architecture** → [`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md)

---

The original content of this file described a **superseded model** and would actively mislead a maker or Claude Code. It was preserved only as a tombstone. The framework was rebuilt from scratch in project `set-regarding-and-field-mapping-resolver-r2` (2026-07); the shipped behavior is materially different.

## What this old guide got wrong (do NOT rely on any of it)

| Old guide claimed | Actual shipped behavior |
|---|---|
| **Three "Sync Modes"** (One-time / Manual Refresh / Update Related) chosen per profile | There is **no surfaced sync-mode field**. Creation-time apply is automatic and unconditional; update-time refresh is a separate **manual ribbon push** only. |
| A **"Refresh from Parent"** button on the child form | **Does not exist.** There is no pull-from-parent button on child forms. |
| A **"Mapping Direction"** field (Parent-to-Child / bidirectional) | Not part of the authoring model; the engine is one-directional (source → target) by construction. |
| Admin navigation at **`Settings > Administration > Field Mapping Profiles`** | That subarea **does not exist**. Find the tables via the Power Apps maker portal → **Tables** → `sprk_fieldmappingprofile` (see the current guide). |
| Rules are just **field-to-field Copy** with a type-compatibility matrix | There are now **four mapping types** — `sprk_mapping_type`: Copy / Default / Concat / Template — including a `sprk_expression` format-string seam for Concat/Template. |
| Type-compatibility is enforced/blocking at save time | The client engine **never throws**; incompatible or unresolvable rules **warn and skip** at apply time, they do not block. |

## Where to go instead

- **"How do I set up a profile and rules?"** → [`docs/guides/FIELD-MAPPING-ADMIN-GUIDE.md`](../guides/FIELD-MAPPING-ADMIN-GUIDE.md) (includes the four mapping types, `sprk_expression` syntax, the option-set integer values, and a Web-API seeding recipe).
- **"How does it work / what are the components (code + PCF)?"** → [`docs/architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md`](../architecture/SPAARKE-FIELD-MAPPING-FRAMEWORK.md).

*This stub replaces the February 1, 2026 content. It may be safely deleted once no inbound links remain.*
