/**
 * Copy the single self-contained Vite output (`out/index.html`) to the
 * Dataverse web-resource file name (`out/sprk_communicationpage.html`).
 *
 * ADR-026 requires web resources be named `sprk_{pagename}`. viteSingleFile
 * always emits `index.html`; this step gives it the deployable name without
 * introducing a separate inline step (the whole bundle is already inlined).
 */
import { copyFileSync, existsSync, statSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const src = resolve(here, '..', 'out', 'index.html');
const dest = resolve(here, '..', 'out', 'sprk_communicationpage.html');

if (!existsSync(src)) {
  console.error(`[name-artifact] Expected Vite output not found: ${src}. Run 'vite build' first.`);
  process.exit(1);
}

copyFileSync(src, dest);
const kb = Math.round(statSync(dest).size / 1024);
console.log(`[name-artifact] Created ${dest} (${kb} KB)`);
console.log('[name-artifact] Deploy: upload as Dataverse web resource "sprk_communicationpage" (type: Webpage (HTML)).');
