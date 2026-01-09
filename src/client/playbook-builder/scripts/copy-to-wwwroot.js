/**
 * Copy build output to App Service wwwroot directory.
 * Used for deployment to Azure App Service.
 *
 * Usage: npm run build:deploy
 */

import { cpSync, existsSync, mkdirSync } from 'fs';
import { resolve, dirname } from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

const sourceDir = resolve(__dirname, '../dist');
const targetDir = resolve(__dirname, '../../../../server/api/Sprk.Bff.Api/wwwroot/playbook-builder');

console.log('Copying build output to wwwroot...');
console.log(`  Source: ${sourceDir}`);
console.log(`  Target: ${targetDir}`);

if (!existsSync(sourceDir)) {
  console.error('Error: Build output not found. Run "npm run build" first.');
  process.exit(1);
}

// Create target directory if it doesn't exist
if (!existsSync(dirname(targetDir))) {
  mkdirSync(dirname(targetDir), { recursive: true });
}

// Copy files
try {
  cpSync(sourceDir, targetDir, { recursive: true });
  console.log('Build output copied successfully.');
} catch (error) {
  console.error('Error copying files:', error);
  process.exit(1);
}
