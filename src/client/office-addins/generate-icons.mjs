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

async function main() {
  for (const size of [16, 32, 64, 80, 128]) {
    await renderColor(size, `icon-${size}.png`);
  }
  await renderColor(128, 'icon-color.png');
  await renderOutline(32, 'icon-outline.png');
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
