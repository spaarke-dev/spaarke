// Generate the Office add-in icon set from the Spaarke logo.
//
// Source: shared/assets/spaarke-logo.svg (owner-provided black Spaarke mark,
// email-communication-intelligence-r2 §B). Outputs into shared/assets/, which the
// webpack CopyWebpackPlugin copies to dist/assets/ at build time (the manifests
// reference assets/*.png). The logo is non-square (1412×1618 viewBox), so each icon
// is the logo fit-contained + centered on a transparent square with padding.
//
// Produces:
//   icon-16/32/64/80/128.png  — taskpane/ribbon + Word HighResolutionIconUrl
//   icon-color.png (128)       — Outlook unified-manifest `icons.color`  (Apps-list tile)
//   icon-outline.png (32)      — Outlook unified-manifest `icons.outline` (Office recolors it)
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

/** Render the logo fit-contained + centered on a transparent `size`×`size` PNG with padding. */
async function render(size, outName, padRatio = 0.14) {
  const inner = Math.max(1, Math.round(size * (1 - padRatio * 2)));
  const logo = await sharp(svg, { density: 512 })
    .resize(inner, inner, { fit: 'contain', background: TRANSPARENT })
    .png()
    .toBuffer();

  await sharp({ create: { width: size, height: size, channels: 4, background: TRANSPARENT } })
    .composite([{ input: logo, gravity: 'center' }])
    .png()
    .toFile(join(assetsDir, outName));

  console.log(`wrote ${outName} (${size}×${size})`);
}

async function main() {
  for (const size of [16, 32, 64, 80, 128]) {
    await render(size, `icon-${size}.png`);
  }
  // Apps-list tile (color) + Office-tinted outline (monochrome — the mark is already single-color).
  await render(128, 'icon-color.png');
  await render(32, 'icon-outline.png');
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});
