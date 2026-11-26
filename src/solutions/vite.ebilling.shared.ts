/**
 * Shared Vite config for the e-billing code pages (ApExport / InvoiceFiles / InvoiceReview / RecordProcedures / RuleReview), #2965.
 *
 * Each page's `vite.config.ts`:
 *
 *   import path from "path";
 *   import { fileURLToPath } from "url";
 *   import { createEbillingViteConfig } from "../vite.ebilling.shared";
 *   export default createEbillingViteConfig(path.dirname(fileURLToPath(import.meta.url)));
 *
 * It is the wizard pages' factory (`../vite.wizard.shared.ts`) plus what the five e-billing configs had that the wizards did not: the
 * single-copy dedupe of React, Fluent and `scheduler`, and the CommonJS mixed-module transform. Output shape is unchanged from the five
 * per-page copies this replaced: a single-file HTML (`dist/index.html`, JS and CSS inlined) that the page's `build` script renames to its
 * web resource name.
 *
 * `_ebilling-shared/` is listed as an extra source folder so the files in it resolve their bare imports (`react`, `@fluentui/*`) from the
 * PAGE's `node_modules`, the same way the shared libraries do, and so their JSX is transformed by the React plugin.
 *
 * @param pageDir absolute path of the page folder (its `node_modules` and `src` are used).
 */
import path from "path";
import { createWizardViteConfig } from "./vite.wizard.shared";

export function createEbillingViteConfig(pageDir: string) {
  return createWizardViteConfig(pageDir, {
    extraSourceDirs: [path.resolve(__dirname, "_ebilling-shared")],
    dedupe: ["react", "react-dom", "scheduler", "@fluentui/react-components", "@fluentui/react-icons"],
    commonjsMixedEsModules: true,
  });
}
