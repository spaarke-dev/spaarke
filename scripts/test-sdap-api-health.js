// Test SDAP API Health and Configuration
// Run this in browser console on Dataverse page

(async function testSdapApi() {
    console.log("=".repeat(60));
    console.log("SDAP BFF API Health Check");
    console.log("=".repeat(60));

    const apiUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";

    // Test 1: API Health
    console.log("\n[Test 1] Checking API health...");
    try {
        const healthResponse = await fetch(`${apiUrl}/api/health`, {
            method: 'GET'
        });
        console.log("Health Status:", healthResponse.status);
        if (healthResponse.ok) {
            const health = await healthResponse.json();
            console.log("✅ API is running:", health);
        } else {
            console.log("❌ Health check failed:", healthResponse.statusText);
        }
    } catch (error) {
        console.error("❌ Cannot reach API:", error.message);
    }

    // Test 2: Get User Token
    console.log("\n[Test 2] Getting user token...");
    try {
        // Get current user's access token from Dataverse context
        const userToken = await new Promise((resolve, reject) => {
            Xrm.Utility.getGlobalContext().userSettings.userId; // Just to verify Xrm is available
            // Note: In PCF, we use MSAL to get token. Here we'll just log that it's needed.
            console.log("ℹ️  User context available. PCF will use MSAL to get token for API.");
            console.log("ℹ️  Token audience should be: api://1e40baad-e065-4aea-a8d4-4b7ab273458c");
            resolve("Token would be obtained via MSAL in PCF");
        });
    } catch (error) {
        console.error("❌ Cannot get user context:", error);
    }

    // Test 3: Check CORS
    console.log("\n[Test 3] Checking CORS configuration...");
    try {
        const corsResponse = await fetch(`${apiUrl}/api/health`, {
            method: 'OPTIONS'
        });
        console.log("CORS Status:", corsResponse.status);
        console.log("CORS Headers:");
        console.log("  Access-Control-Allow-Origin:", corsResponse.headers.get('Access-Control-Allow-Origin'));
        console.log("  Access-Control-Allow-Methods:", corsResponse.headers.get('Access-Control-Allow-Methods'));
        console.log("  Access-Control-Allow-Headers:", corsResponse.headers.get('Access-Control-Allow-Headers'));
    } catch (error) {
        console.error("❌ CORS check failed:", error);
    }

    // Test 4: Check API version/info
    console.log("\n[Test 4] Getting API info...");
    try {
        const infoResponse = await fetch(`${apiUrl}/api/info`, {
            method: 'GET'
        });
        if (infoResponse.ok) {
            const info = await infoResponse.json();
            console.log("✅ API Info:", info);
        } else {
            console.log("⚠️  /api/info endpoint not available (this is OK)");
        }
    } catch (error) {
        console.log("⚠️  /api/info endpoint not available (this is OK)");
    }

    console.log("\n" + "=".repeat(60));
    console.log("Health Check Complete");
    console.log("=".repeat(60));

    console.log("\n📋 Summary:");
    console.log("If you see errors above, the API may not be deployed or configured correctly.");
    console.log("\nNext steps:");
    console.log("1. Check Azure App Service logs for the 500 error details");
    console.log("2. Verify API is deployed: Azure Portal → App Service → spe-api-dev-67e2xz");
    console.log("3. Check API configuration: appsettings.json has correct Azure AD settings");
    console.log("4. Verify Container Type registration is complete");
})();
