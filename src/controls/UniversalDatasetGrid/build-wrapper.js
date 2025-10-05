#!/usr/bin/env node
/**
 * Build wrapper for PCF deployment
 *
 * Problem: pac pcf push always uses --buildMode development
 * Solution: This wrapper intercepts the build command and forces production mode
 *
 * When MSBuild calls: npm run build -- --buildMode development
 * This wrapper changes it to: pcf-scripts build --buildMode production
 */

const { spawn } = require('child_process');

// Get all args after 'node build-wrapper.js'
const args = process.argv.slice(2);

// Check if this is being called from MSBuild (has --buildSource MSBuild)
const isMSBuild = args.includes('--buildSource') && args.includes('MSBuild');

// Filter out buildMode args and force production if from MSBuild
const filteredArgs = args.filter(arg => !arg.startsWith('--buildMode'));

// Force production mode for MSBuild (deployment), otherwise use provided mode
const buildMode = isMSBuild ? 'production' :
                  (args.find(arg => arg.startsWith('--buildMode='))?.split('=')[1] || 'development');

console.log(`[Build Wrapper] Source: ${isMSBuild ? 'MSBuild (deployment)' : 'Manual'}`);
console.log(`[Build Wrapper] Build mode: ${buildMode}`);

// Build final args
const finalArgs = ['build', `--buildMode`, buildMode, ...filteredArgs];

// Run pcf-scripts
const child = spawn('npx', ['pcf-scripts', ...finalArgs], {
    stdio: 'inherit',
    shell: true
});

child.on('exit', (code) => {
    process.exit(code);
});
