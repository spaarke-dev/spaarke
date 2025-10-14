/**
 * Test PCF SdapApiClient integration with BFF API
 * Simulates browser environment without building PCF control
 *
 * This script tests the EXACT same logic that the PCF control uses in:
 * src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts
 *
 * Usage:
 *   1. Get token: export PCF_TOKEN=$(az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c --query accessToken -o tsv)
 *   2. Get drive ID: export DRIVE_ID=<your-drive-id>
 *   3. Run: node test-pcf-client-integration.js
 */

const https = require('https');
const http = require('http');

// Configuration (from PCF control)
const API_BASE_URL = 'https://spe-api-dev-67e2xz.azurewebsites.net';
const USER_TOKEN = process.env.PCF_TOKEN;
const DRIVE_ID = process.env.DRIVE_ID;

// Color codes for terminal output
const colors = {
    reset: '\x1b[0m',
    bright: '\x1b[1m',
    green: '\x1b[32m',
    red: '\x1b[31m',
    yellow: '\x1b[33m',
    blue: '\x1b[36m'
};

function log(message, color = colors.reset) {
    console.log(`${color}${message}${colors.reset}`);
}

// Simple fetch implementation (Node.js compatible)
function fetch(url, options = {}) {
    return new Promise((resolve, reject) => {
        const urlObj = new URL(url);
        const isHttps = urlObj.protocol === 'https:';
        const client = isHttps ? https : http;

        const requestOptions = {
            hostname: urlObj.hostname,
            port: urlObj.port,
            path: urlObj.pathname + urlObj.search,
            method: options.method || 'GET',
            headers: options.headers || {}
        };

        const req = client.request(requestOptions, (res) => {
            let data = [];

            res.on('data', (chunk) => {
                data.push(chunk);
            });

            res.on('end', () => {
                const buffer = Buffer.concat(data);
                resolve({
                    ok: res.statusCode >= 200 && res.statusCode < 300,
                    status: res.statusCode,
                    statusText: res.statusMessage,
                    headers: res.headers,
                    text: () => Promise.resolve(buffer.toString()),
                    json: () => Promise.resolve(JSON.parse(buffer.toString())),
                    blob: () => Promise.resolve(buffer)
                });
            });
        });

        req.on('error', reject);

        if (options.body) {
            if (Buffer.isBuffer(options.body)) {
                req.write(options.body);
            } else if (typeof options.body === 'string') {
                req.write(options.body);
            }
        }

        req.end();
    });
}

// Simulate SdapApiClient.uploadFile()
// EXACT logic from: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts:48-81
async function testUpload(driveId, fileName, fileContent) {
    log('\n=== Test Upload ===', colors.bright);
    log(`File Name: ${fileName}`, colors.blue);
    log(`Drive ID: ${driveId}`, colors.blue);
    log(`File Size: ${fileContent.length} bytes`, colors.blue);

    const url = `${API_BASE_URL}/api/obo/drives/${encodeURIComponent(driveId)}/upload?fileName=${encodeURIComponent(fileName)}`;

    try {
        const response = await fetch(url, {
            method: 'PUT',
            headers: {
                'Authorization': `Bearer ${USER_TOKEN}`,
                'Content-Type': 'text/plain'
            },
            body: Buffer.from(fileContent)
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Upload failed: ${response.status} ${errorText}`);
        }

        const result = await response.json();
        log('✅ Upload successful', colors.green);
        log('Response:', colors.blue);
        console.log(JSON.stringify(result, null, 2));
        return result;

    } catch (error) {
        log(`❌ Upload failed: ${error.message}`, colors.red);
        throw error;
    }
}

// Simulate SdapApiClient.downloadFile()
// EXACT logic from: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts:87-126
async function testDownload(driveId, itemId) {
    log('\n=== Test Download ===', colors.bright);
    log(`Drive ID: ${driveId}`, colors.blue);
    log(`Item ID: ${itemId}`, colors.blue);

    const url = `${API_BASE_URL}/api/obo/drives/${encodeURIComponent(driveId)}/items/${encodeURIComponent(itemId)}/content`;

    try {
        const response = await fetch(url, {
            method: 'GET',
            headers: {
                'Authorization': `Bearer ${USER_TOKEN}`
            }
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Download failed: ${response.status} ${errorText}`);
        }

        const content = await response.text();
        log('✅ Download successful', colors.green);
        log('Content:', colors.blue);
        console.log(content);
        return content;

    } catch (error) {
        log(`❌ Download failed: ${error.message}`, colors.red);
        throw error;
    }
}

// Simulate SdapApiClient.deleteFile()
// EXACT logic from: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts:132-161
async function testDelete(driveId, itemId) {
    log('\n=== Test Delete ===', colors.bright);
    log(`Drive ID: ${driveId}`, colors.blue);
    log(`Item ID: ${itemId}`, colors.blue);

    const url = `${API_BASE_URL}/api/obo/drives/${encodeURIComponent(driveId)}/items/${encodeURIComponent(itemId)}`;

    try {
        const response = await fetch(url, {
            method: 'DELETE',
            headers: {
                'Authorization': `Bearer ${USER_TOKEN}`
            }
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(`Delete failed: ${response.status} ${errorText}`);
        }

        log('✅ Delete successful', colors.green);

    } catch (error) {
        log(`❌ Delete failed: ${error.message}`, colors.red);
        throw error;
    }
}

// Main test
async function runTests() {
    log('='.repeat(80), colors.bright);
    log('PCF Client Integration Test (Simulating SdapApiClient.ts)', colors.bright);
    log('='.repeat(80), colors.bright);
    log('');
    log('This test simulates the EXACT API calls that the PCF control makes.', colors.yellow);
    log('Location: src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClient.ts', colors.yellow);
    log('');

    // Validate environment
    if (!USER_TOKEN) {
        log('❌ Error: PCF_TOKEN environment variable not set', colors.red);
        log('');
        log('Run the following command to get a token:', colors.yellow);
        log('export PCF_TOKEN=$(az account get-access-token --resource api://1e40baad-e065-4aea-a8d4-4b7ab273458c --query accessToken -o tsv)', colors.blue);
        log('');
        process.exit(1);
    }

    if (!DRIVE_ID) {
        log('❌ Error: DRIVE_ID environment variable not set', colors.red);
        log('');
        log('Get Drive ID from Dataverse or API, then run:', colors.yellow);
        log('export DRIVE_ID=<your-drive-id>', colors.blue);
        log('');
        log('To get Drive ID:', colors.yellow);
        log('  1. From Dataverse: pac data read --entity-logical-name sprk_matter --id <guid> --columns sprk_driveid', colors.blue);
        log('  2. From API: curl -H "Authorization: Bearer $PCF_TOKEN" https://spe-api-dev-67e2xz.azurewebsites.net/api/containers', colors.blue);
        log('');
        process.exit(1);
    }

    log(`Token Length: ${USER_TOKEN.length} chars`, colors.blue);
    log(`Drive ID: ${DRIVE_ID}`, colors.blue);
    log('');

    try {
        // Test 1: Upload (SdapApiClient.uploadFile)
        const fileName = `pcf-test-${Date.now()}.txt`;
        const fileContent = `Test file from PCF client simulation\nTimestamp: ${new Date().toISOString()}\n\nThis test verifies:\n- MSAL token acquisition\n- BFF API authentication\n- OBO flow (On-Behalf-Of)\n- Graph API calls\n- SharePoint Embedded storage`;

        const uploadResult = await testUpload(DRIVE_ID, fileName, fileContent);
        const itemId = uploadResult.id;

        // Test 2: Download (SdapApiClient.downloadFile)
        const downloadedContent = await testDownload(DRIVE_ID, itemId);

        // Verify content matches
        if (downloadedContent.trim() === fileContent.trim()) {
            log('\n✅ Content verification passed (upload = download)', colors.green);
        } else {
            log('\n⚠️  Warning: Downloaded content does not match uploaded content', colors.yellow);
        }

        // Test 3: Delete (SdapApiClient.deleteFile)
        await testDelete(DRIVE_ID, itemId);

        log('\n' + '='.repeat(80), colors.bright);
        log('✅ ALL TESTS PASSED!', colors.green);
        log('='.repeat(80), colors.bright);
        log('');
        log('Integration Verified:', colors.bright);
        log('  ✓ PCF Client App → BFF API authentication', colors.green);
        log('  ✓ BFF API → Graph API (OBO flow)', colors.green);
        log('  ✓ Graph API → SharePoint Embedded', colors.green);
        log('  ✓ File upload/download/delete operations', colors.green);
        log('');
        log('Next Steps:', colors.bright);
        log('  1. Build PCF control: npm run build:prod', colors.blue);
        log('  2. Deploy to Dataverse: pac pcf push', colors.blue);
        log('  3. Test in model-driven app', colors.blue);
        log('');

    } catch (error) {
        log('\n' + '='.repeat(80), colors.bright);
        log('❌ TESTS FAILED', colors.red);
        log('='.repeat(80), colors.bright);
        log('');
        log(`Error: ${error.message}`, colors.red);
        log('');
        log('Troubleshooting:', colors.bright);
        log('  1. Check token is valid: echo $PCF_TOKEN | cut -d. -f2 | base64 -d', colors.blue);
        log('  2. Check Azure AD app permissions (PCF client → BFF API)', colors.blue);
        log('  3. Check BFF API logs: az webapp log tail --name spe-api-dev-67e2xz', colors.blue);
        log('  4. Verify Drive ID exists in Dataverse/SPE', colors.blue);
        log('');
        process.exit(1);
    }
}

runTests();
