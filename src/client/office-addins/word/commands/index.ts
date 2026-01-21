/**
 * Word add-in commands (function file).
 *
 * These functions are invoked from ribbon buttons and keyboard shortcuts.
 * They run in a separate context from the taskpane.
 */

// Register global functions for Office to call
declare global {
  interface Window {
    showTaskPane: (event: Office.AddinCommands.Event) => void;
    quickSave: (event: Office.AddinCommands.Event) => void;
    shareDocument: (event: Office.AddinCommands.Event) => void;
  }
}

/**
 * Opens the taskpane.
 */
function showTaskPane(event: Office.AddinCommands.Event): void {
  event.completed();
}

/**
 * Quick save the current document to Spaarke DMS.
 * This is a placeholder - actual implementation will be added in later tasks.
 */
async function quickSave(event: Office.AddinCommands.Event): Promise<void> {
  try {
    // TODO: Implement quick save functionality
    // Will be implemented in task 040-implement-save-document-endpoint

    // Show notification
    await Word.run(async (context) => {
      // Placeholder for actual save logic
      console.log('Quick save initiated');
      await context.sync();
    });

  } catch (error) {
    console.error('Quick save failed:', error);
  } finally {
    event.completed();
  }
}

/**
 * Share the current document via Spaarke.
 * This is a placeholder - actual implementation will be added in later tasks.
 */
async function shareDocument(event: Office.AddinCommands.Event): Promise<void> {
  try {
    // TODO: Implement share functionality
    // Will be implemented in task 050-implement-share-links

    console.log('Share document initiated');

  } catch (error) {
    console.error('Share document failed:', error);
  } finally {
    event.completed();
  }
}

// Initialize and register commands
Office.onReady(() => {
  window.showTaskPane = showTaskPane;
  window.quickSave = quickSave;
  window.shareDocument = shareDocument;
});

export { showTaskPane, quickSave, shareDocument };
