/**
 * Test Script: Custom Page Navigation
 *
 * Purpose: Manually test Custom Page parameter passing before updating ribbon buttons
 *
 * Instructions:
 * 1. Open a Matter record in Dataverse (SPAARKE DEV 1)
 * 2. Open browser DevTools (F12)
 * 3. Go to Console tab
 * 4. Paste this entire script
 * 5. Press Enter to execute
 * 6. Custom Page dialog should open
 * 7. Verify PCF control renders and receives parameters
 *
 * Expected Results:
 * - Dialog opens centered on screen
 * - PCF control visible inside dialog
 * - No console errors (except expected MSAL/API calls)
 * - Parameters logged to console
 * - Can close dialog (X button or Cancel)
 *
 * @version 1.0.0
 * @task Task 1, Step 1.4
 */

(async function testCustomPageNavigation() {
    console.log('='.repeat(80));
    console.log('CUSTOM PAGE NAVIGATION TEST - v1.0.0');
    console.log('='.repeat(80));

    try {
        // Get form context
        const formContext = Xrm.Page;
        if (!formContext) {
            console.error('❌ ERROR: Form context not available. Make sure you run this on a form.');
            return;
        }

        // Get entity information
        const entityName = formContext.data.entity.getEntityName();
        const recordId = formContext.data.entity.getId().replace(/[{}]/g, '');

        console.log('\n📋 FORM INFORMATION:');
        console.log(`   Entity: ${entityName}`);
        console.log(`   Record ID: ${recordId}`);

        // Get container ID
        const containerIdAttr = formContext.getAttribute('sprk_containerid');
        if (!containerIdAttr) {
            console.error('❌ ERROR: sprk_containerid field not found on form');
            console.error('   Add the Container ID field to this entity\'s form');
            return;
        }

        const containerId = containerIdAttr.getValue();
        if (!containerId) {
            console.error('❌ ERROR: Container ID is empty');
            console.error('   This record needs a SharePoint Embedded container ID');
            console.error('   Add a container ID to sprk_containerid field first');
            return;
        }

        console.log(`   Container ID: ${containerId}`);

        // Get display name (entity-specific field)
        let displayNameField;
        switch (entityName) {
            case 'sprk_matter':
                displayNameField = 'sprk_matternumber';
                break;
            case 'sprk_project':
                displayNameField = 'sprk_projectname';
                break;
            case 'sprk_invoice':
                displayNameField = 'sprk_invoicenumber';
                break;
            case 'account':
                displayNameField = 'name';
                break;
            case 'contact':
                displayNameField = 'fullname';
                break;
            default:
                displayNameField = 'name'; // fallback
        }

        const displayNameAttr = formContext.getAttribute(displayNameField);
        const displayName = displayNameAttr ? displayNameAttr.getValue() : 'Test Record';

        console.log(`   Display Name: ${displayName}`);

        // Build navigation parameters
        const navigationParams = {
            parentEntityName: entityName,
            parentRecordId: recordId,
            containerId: containerId,
            parentDisplayName: displayName
        };

        console.log('\n🚀 NAVIGATION PARAMETERS:');
        console.log(JSON.stringify(navigationParams, null, 2));

        // Custom Page input
        const pageInput = {
            pageType: 'custom',
            name: 'sprk_documentuploaddialog_e52db',  // Actual name with Dataverse-generated suffix
            data: navigationParams
        };

        // Dialog options
        const navigationOptions = {
            target: 2,      // Dialog (2 = modal dialog)
            position: 1,    // Center (1 = center of screen)
            width: { value: 800, unit: 'px' },
            height: { value: 600, unit: 'px' }
        };

        console.log('\n📱 DIALOG OPTIONS:');
        console.log(`   Target: Dialog (modal)`);
        console.log(`   Position: Center`);
        console.log(`   Width: 800px`);
        console.log(`   Height: 600px`);

        console.log('\n⏳ Opening Custom Page dialog...');
        console.log('   (This may take 3-5 seconds)');

        // Navigate to Custom Page
        const result = await Xrm.Navigation.navigateTo(pageInput, navigationOptions);

        // Dialog closed
        console.log('\n✅ DIALOG CLOSED');
        console.log('   Result:', result);

        console.log('\n🎉 TEST COMPLETE');
        console.log('='.repeat(80));

    } catch (error) {
        console.error('\n❌ NAVIGATION FAILED');
        console.error('   Error:', error.message);
        console.error('   Full error:', error);

        console.log('\n🔍 TROUBLESHOOTING:');
        console.log('   1. Check Custom Page name is correct: sprk_documentuploaddialog');
        console.log('   2. Verify Custom Page is published');
        console.log('   3. Verify PCF control is deployed');
        console.log('   4. Check browser console for additional errors');
        console.log('   5. Try refreshing page and running test again');

        console.log('\n='.repeat(80));
    }
})();
