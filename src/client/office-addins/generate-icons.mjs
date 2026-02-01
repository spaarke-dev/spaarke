import sharp from 'sharp';
import { readFileSync, writeFileSync } from 'fs';
import { join, dirname } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));

// Read the SVG file
const svgPath = join(__dirname, 'dist/assets/star-logo.svg');
const svgContent = readFileSync(svgPath, 'utf8');

// Icon sizes needed for Office Add-ins
const sizes = [16, 32, 64, 80, 128];

async function generateIcons() {
  for (const size of sizes) {
    const outputPath = join(__dirname, `dist/assets/icon-${size}.png`);

    await sharp(Buffer.from(svgContent))
      .resize(size, size)
      .png()
      .toFile(outputPath);

    console.log(`Generated icon-${size}.png`);
  }

  // Also copy to public/assets
  for (const size of sizes) {
    const src = join(__dirname, `dist/assets/icon-${size}.png`);
    const dest = join(__dirname, `public/assets/icon-${size}.png`);
    const content = readFileSync(src);
    writeFileSync(dest, content);
    console.log(`Copied to public/assets/icon-${size}.png`);
  }
}

generateIcons().catch(console.error);
