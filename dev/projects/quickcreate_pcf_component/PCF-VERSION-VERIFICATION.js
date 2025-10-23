/**
 * PCF Version Verification Script
 *
 * Run this in browser console to verify which version of the PCF is loaded
 * and clear all caches if needed.
 *
 * EXPECTED OUTPUT FOR v3.0.1:
 * - Console log: "Initializing PCF control v3.0.1 (IDEMPOTENT UPDATEVIEW FIX)"
 * - Version badge: "✓ V3.0.1 - IDEMPOTENT UPDATEVIEW - NO INIT CALL"
 * - Console log: "Waiting for parameters to hydrate" (NOT "Detected Quick Create Form context")
 */

console.log("========================================");
console.log("PCF VERSION VERIFICATION");
console.log("========================================");

// Step 1: Check PCF bundle version
console.log("\n1. Checking PCF control version...");
console.log("   Expected: v3.0.1 - IDEMPOTENT UPDATEVIEW FIX");
console.log("   Look for log: 'Initializing PCF control v3.0.1'");

// Step 2: Clear all caches
console.log("\n2. Clearing all browser caches...");

// Clear Service Workers
if ('serviceWorker' in navigator) {
    navigator.serviceWorker.getRegistrations().then(registrations => {
        registrations.forEach(registration => {
            registration.unregister();
            console.log("   ✓ Service worker unregistered");
        });
    });
}

// Clear Cache Storage
if ('caches' in window) {
    caches.keys().then(cacheNames => {
        cacheNames.forEach(cacheName => {
            caches.delete(cacheName);
            console.log(`   ✓ Deleted cache: ${cacheName}`);
        });
    });
}

// Clear IndexedDB
if ('indexedDB' in window) {
    indexedDB.databases().then(databases => {
        databases.forEach(db => {
            if (db.name) {
                indexedDB.deleteDatabase(db.name);
                console.log(`   ✓ Deleted IndexedDB: ${db.name}`);
            }
        });
    });
}

// Clear localStorage and sessionStorage
localStorage.clear();
sessionStorage.clear();
console.log("   ✓ Cleared localStorage and sessionStorage");

console.log("\n3. Cache clearing complete!");
console.log("\n========================================");
console.log("NEXT STEPS:");
console.log("1. Close this Custom Page dialog");
console.log("2. Hard refresh the browser (Ctrl+Shift+R)");
console.log("3. Click 'Upload Documents' button again");
console.log("4. Look for the version badge at top of dialog:");
console.log("   Should say: '✓ V3.0.1 - IDEMPOTENT UPDATEVIEW - NO INIT CALL'");
console.log("========================================");
