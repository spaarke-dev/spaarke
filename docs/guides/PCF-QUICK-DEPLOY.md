# PCF Quick Dev Deploy

> **Use When**: Iterative PCF development, testing changes quickly
>
> **Time**: ~60 seconds
>
> **Result**: Control updated in Dataverse (temporary solution)

---

## One-Liner (Copy-Paste Ready)

```bash
cd /c/code_files/spaarke/src/client/pcf/{ControlName} && npm run build:prod && mv /c/code_files/spaarke/Directory.Packages.props{,.disabled} && pac pcf push --publisher-prefix sprk; mv /c/code_files/spaarke/Directory.Packages.props{.disabled,}
```

**Replace `{ControlName}`** with: `UniversalQuickCreate`, `AnalysisWorkspace`, `SpeFileViewer`, etc.

---

## Step-by-Step

```bash
# 1. Navigate to control directory
cd /c/code_files/spaarke/src/client/pcf/{ControlName}

# 2. Build for production
npm run build:prod

# 3. Disable Central Package Management (REQUIRED)
mv /c/code_files/spaarke/Directory.Packages.props /c/code_files/spaarke/Directory.Packages.props.disabled

# 4. Deploy to Dataverse
pac pcf push --publisher-prefix sprk

# 5. Restore Central Package Management (REQUIRED)
mv /c/code_files/spaarke/Directory.Packages.props.disabled /c/code_files/spaarke/Directory.Packages.props
```

---

## File Lock Error Workaround

The `pac pcf push` command often fails with:
```
Unable to remove directory "obj\Debug\Metadata"
```

**This is harmless** - the solution IS already packed. Import it directly:

```bash
pac solution import --path obj/PowerAppsToolsTemp_sprk/bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes
```

---

## Important Notes

| Note | Details |
|------|---------|
| **Creates Temporary Solution** | `pac pcf push` creates `PowerAppsToolsTemp_sprk`, NOT your named solution |
| **Does NOT Update Version** | Named solution (e.g., `UniversalQuickCreate`) stays at old version |
| **Browser Cache** | Hard refresh (`Ctrl+Shift+R`) after deployment to see changes |
| **Custom Pages** | If PCF is in Custom Page, you may need to republish - see [PCF-CUSTOM-PAGE-DEPLOY.md](PCF-CUSTOM-PAGE-DEPLOY.md) |

---

## ⚠️ Critical: Development Mode Rebuild

**`pac pcf push` ALWAYS rebuilds your control in development mode**, ignoring your production build.

This means:
- Your `npm run build:prod` optimizations are **discarded**
- Tree-shaking for `@fluentui/react-icons` is **lost**
- Bundle size can increase from 200KB to 8MB+

**If you need production build optimizations**, use the **Manual Pack Fallback** instead:

```bash
# 1. Build production bundle (preserves tree-shaking)
npm run build:prod

# 2. Copy to solution folder (including styles.css!)
mkdir -p obj/PowerAppsToolsTemp_sprk/bin/net462/control
cp out/controls/*/bundle.js obj/PowerAppsToolsTemp_sprk/bin/net462/control/
cp out/controls/*/ControlManifest.xml obj/PowerAppsToolsTemp_sprk/bin/net462/control/
cp control/css/styles.css obj/PowerAppsToolsTemp_sprk/bin/net462/control/

# 3. Build wrapper and import
cd obj/PowerAppsToolsTemp_sprk && dotnet build *.cdsproj --configuration Debug
pac solution import --path bin/Debug/PowerAppsToolsTemp_sprk.zip --publish-changes
```

See [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) Section 4.4 for icon tree-shaking setup.

---

## When NOT to Use This

Use **[PCF-PRODUCTION-RELEASE.md](PCF-PRODUCTION-RELEASE.md)** instead when:
- You need proper version tracking
- This is a production release
- You want `pac solution list` to show the new version

---

## Verify Deployment

```bash
# Check deployment succeeded
pac solution list | grep -i "PowerAppsTools"

# Then hard refresh browser and test
```

---

## Related Guides

- [PCF-PRODUCTION-RELEASE.md](PCF-PRODUCTION-RELEASE.md) - Version management for production
- [PCF-CUSTOM-PAGE-DEPLOY.md](PCF-CUSTOM-PAGE-DEPLOY.md) - Custom Page complexity
- [PCF-TROUBLESHOOTING.md](PCF-TROUBLESHOOTING.md) - Error resolution
- [PCF-V9-PACKAGING.md](PCF-V9-PACKAGING.md) - Bundle size optimization and comprehensive guide
