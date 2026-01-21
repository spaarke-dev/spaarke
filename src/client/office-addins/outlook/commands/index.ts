/**
 * Outlook add-in commands (function file).
 *
 * These functions are invoked from ribbon buttons and keyboard shortcuts.
 * They run in a separate context from the taskpane.
 */

// Register global functions for Office to call
declare global {
  interface Window {
    showTaskPane: (event: Office.AddinCommands.Event) => void;
    quickSave: (event: Office.AddinCommands.Event) => void;
  }
}

/**
 * Opens the taskpane.
 */
function showTaskPane(event: Office.AddinCommands.Event): void {
  // The taskpane will be shown automatically by Office
  // This function just signals completion
  event.completed();
}

/**
 * Quick save the current email to Spaarke DMS.
 * This is a placeholder - actual implementation will be added in later tasks.
 */
async function quickSave(event: Office.AddinCommands.Event): Promise<void> {
  try {
    // TODO: Implement quick save functionality
    Office.context.mailbox.item?.notificationMessages.addAsync('spaarke_save', {
      type: Office.MailboxEnums.ItemNotificationMessageType.InformationalMessage,
      message: 'Saving to Spaarke...',
      icon: 'Icon.16x16',
      persistent: false,
    });

    // Placeholder for actual save logic
    // Will be implemented in task 030-implement-save-email-endpoint

  } catch (error) {
    console.error('Quick save failed:', error);
    Office.context.mailbox.item?.notificationMessages.addAsync('spaarke_error', {
      type: Office.MailboxEnums.ItemNotificationMessageType.ErrorMessage,
      message: 'Failed to save email. Please try again.',
    });
  } finally {
    event.completed();
  }
}

// Initialize and register commands
Office.onReady(() => {
  // Register global functions
  window.showTaskPane = showTaskPane;
  window.quickSave = quickSave;
});

// Export for module systems
export { showTaskPane, quickSave };
