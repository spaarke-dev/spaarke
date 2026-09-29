# Fluent version pinning — why PCFs pin to 9.68.0 and Code Pages do not

> **Applied**: 2026-08-26, main session, after the owner asked why we were on an old Fluent
> **Scope**: all 19 PCF controls + the 4 shared libs PCFs consume. **Not** the Vite solutions.

---

## The rule

| Surface | Fluent comes from | Pin to 9.68.0? |
|---|---|---|
| **PCF controls** (`src/client/pcf/*`) | the **host**, via `<platform-library name="Fluent">` — externalized, not bundled | ✅ **yes, exactly** |
| **Shared libs consumed by PCFs** (`ui-components`, `communication-components`, `visuals`, `auth`) | their compiled `dist/` runs *inside* a PCF bundle, so the host's copy | ✅ **yes** (`devDependencies`) |
| **Code Pages / SPAs** (`src/solutions/*`, `code-pages/*`, `external-spa`, `office-addins`) | **bundled into their own output** — never touches a platform global | ❌ **no** — pinning would constrain them for no reason |

## Why exact, not caret

`@fluentui/react-components` is **externalized** in a PCF build: we do not ship it, we call into the
host's copy. So any API added after the host's version typechecks, bundles, and is `undefined` at
runtime. A caret range makes that gap widen silently on every `npm install`.

Measured before the fix:

| package | declared | actually installed | host serves |
|---|---|---|---|
| `Spaarke.UI.Components` | `^9.73.2` | 9.73.2 | **9.68.0** |
| `RecordHeader` | `^9.46.2` | **9.74.7** | **9.68.0** |
| `RegardingResolver` | `^9.46.0` | **9.74.1** | **9.68.0** |

Two PCFs had drifted six minor versions past the runtime.

## Why 9.68.0 specifically — it is not our choice

`pcf-scripts/PlatformLibraryVersions.json` has exactly **one** Fluent v9 entry:

```
min 9.0.0   max 9.68.0   platformVersion 9.68.0   alias FluentUIReactv940
```

9.68.0 is the newest Fluent the Power Platform serves, and `pcf-scripts@1.51.1` is already the latest
on npm. The ceiling is Microsoft's, not ours. When Microsoft ships a newer platform library, bump
`pcf-scripts` and re-pin — that is the only path forward.

## The manifest version is a red herring

Every PCF manifest declares `<platform-library name="Fluent" version="9.46.2" />`. That number has
**no runtime effect**: `getSupportedVersion` resolves *any* 9.x to the single entry above, yielding
the same `FluentUIReactv940` alias and the same host build. Changing it to 9.68.0 would be cosmetic.
The real lever is the npm dependency — which is what is now pinned.

## Peer ranges stay wide

`Spaarke.Communication.Components` and `Spaarke.Visuals` declare Fluent in **both**
`devDependencies` and `peerDependencies`. Only the dev dependency is pinned — that is what the lib
*compiles* against. The peer range stays `>=9.46.0 <10`, because a peer range should **permit** a
consumer's version rather than dictate one exact build: Code Pages bundle their own Fluent and may
legitimately run newer.

## Verification

- `Spaarke.UI.Components` compiles clean against 9.68.0 → we were **not** already depending on
  post-9.68.0 APIs. This closed an exposure, it did not fix a live outage.
- 17 R2-owned suites 601/601; full shared-lib suite unchanged (same 9 known-red).
- `MatterHeader` 61,471 B, 7/7 — byte-identical to pre-pin.
- `RecordHeader` 210,765 B, 35/35, date picker intact.

## Gotcha worth remembering

Pinning changed the dependency tree and npm's prune dropped **`scheduler`** (a `react-dom@16.14`
transitive). Jest then failed to resolve `@fluentui/react-context-selector` with a misleading error.
It is a tree inconsistency, not a version conflict — `rm -rf node_modules package-lock.json` and
reinstall. Expect this on any PCF where the pin changes the resolved tree.
