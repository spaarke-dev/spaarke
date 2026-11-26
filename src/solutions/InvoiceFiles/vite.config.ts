// Shared e-billing Code Page build config - see ../vite.ebilling.shared.ts
import path from "path";
import { fileURLToPath } from "url";
import { createEbillingViteConfig } from "../vite.ebilling.shared";

export default createEbillingViteConfig(path.dirname(fileURLToPath(import.meta.url)));
