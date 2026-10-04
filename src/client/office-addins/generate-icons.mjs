// Generate the Office add-in icon set from the Spaarke logo.
//
// Source: shared/assets/spaarke-logo.svg (owner-provided black Spaarke mark). Outputs into
// shared/assets/, which the webpack CopyWebpackPlugin copies to dist/assets/ at build time (the
// manifests reference assets/*.png). The mark is non-square (1412×1618 viewBox).
//
// Style (email-communication-intelligence-r2 UAT 2026-09-03): a **WHITE mark on a BLACK tile** — the
// black-mark-on-transparent version read too much like the Claude logo. The black background makes the
// Spaarke starburst distinct and intentional (cf. the Harvey black-tile style).
//
// Produces:
//   icon-16/32/64/80/128.png  — taskpane/ribbon + Word HighResolutionIconUrl (WHITE on BLACK)
//   icon-color.png (128)       — Outlook unified-manifest `icons.color`  (WHITE on BLACK)
//   icon-outline.png (32)      — Outlook unified-manifest `icons.outline` (monochrome/transparent —
//                                 Office recolors it; NOT used by the XML manifest we register today)
//
// Requires `sharp` (native). Run: `npm install --no-save sharp && node generate-icons.mjs`.
import sharp from 'sharp';
import { readFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));
const assetsDir = join(__dirname, 'shared/assets');
const svg = readFileSync(join(assetsDir, 'spaarke-logo.svg'));

const TRANSPARENT = { r: 0, g: 0, b: 0, alpha: 0 };
const BLACK = { r: 0, g: 0, b: 0, alpha: 1 };

/** Render the WHITE mark centered on a BLACK `size`×`size` tile with padding. */
async function renderColor(size, outName, padRatio = 0.16) {
  const inner = Math.max(1, Math.round(size * (1 - padRatio * 2)));
  // Rasterize the (black) mark on transparent, then negate RGB so the mark becomes white
  // (alpha preserved → transparent stays transparent; the small white center-dot becomes black and
  // disappears against the black tile).
  const whiteMark = await sharp(svg, { density: 512 })
    .resize(inner, inner, { fit: 'contain', background: TRANSPARENT })
    .negate({ alpha: false })
    .png()
    .toBuffer();

  await sharp({ create: { width: size, height: size, channels: 4, background: BLACK } })
    .composite([{ input: whiteMark, gravity: 'center' }])
    .png()
    .toFile(join(assetsDir, outName));

  console.log(`wrote ${outName} (${size}×${size}, white-on-black)`);
}

/** Render a monochrome mark on transparent (Office tints outline icons; kept for the unified manifest). */
async function renderOutline(size, outName, padRatio = 0.04) {
  const inner = Math.max(1, Math.round(size * (1 - padRatio * 2)));
  const mark = await sharp(svg, { density: 512 })
    .resize(inner, inner, { fit: 'contain', background: TRANSPARENT })
    .png()
    .toBuffer();

  await sharp({ create: { width: size, height: size, channels: 4, background: TRANSPARENT } })
    .composite([{ input: mark, gravity: 'center' }])
    .png()
    .toFile(join(assetsDir, outName));

  console.log(`wrote ${outName} (${size}×${size}, outline)`);
}

/**
 * Every output this script can produce. `icon-color-192.png` (spaarkeai-word-add-in-r1 task 078) is the
 * COLOR icon for the combined Outlook + Word app PACKAGE: the Microsoft 365 app-package validation expects
 * the color icon at 192×192 (outline 32×32). The 128px `icon-color.png` is left untouched — the standalone
 * Outlook manifest still references it until the owner cuts over to the combined package.
 */
const TARGETS = {
  'icon-16.png': () => renderColor(16, 'icon-16.png'),
  'icon-32.png': () => renderColor(32, 'icon-32.png'),
  'icon-64.png': () => renderColor(64, 'icon-64.png'),
  'icon-80.png': () => renderColor(80, 'icon-80.png'),
  'icon-128.png': () => renderColor(128, 'icon-128.png'),
  'icon-color.png': () => renderColor(128, 'icon-color.png'),
  'icon-color-192.png': () => renderColor(192, 'icon-color-192.png'),
  'icon-outline.png': () => renderOutline(32, 'icon-outline.png'),
};

/**
 * `node generate-icons.mjs`                     → render every target
 * `node generate-icons.mjs icon-color-192.png`  → render only the named target(s)
 *
 * Rendering only what changed matters: re-running the full set re-encodes icons the LIVE add-in already
 * serves, producing byte churn (and a different `sharp` version can shift pixels) with no visual intent.
 */
async function main() {
  const requested = process.argv.slice(2);
  const unknown = requested.filter(name => !(name in TARGETS));
  if (unknown.length > 0) {
    throw new Error(`Unknown icon target(s): ${unknown.join(', ')}. Known: ${Object.keys(TARGETS).join(', ')}`);
  }
  for (const name of requested.length > 0 ? requested : Object.keys(TARGETS)) {
    await TARGETS[name]();
  }
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
