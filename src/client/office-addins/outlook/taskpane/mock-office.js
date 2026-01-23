/**
 * Mock Office.js for browser testing
 * This simulates the Outlook environment so we can test the add-in UI without Outlook.
 *
 * Usage: Load this script BEFORE the main app bundle.
 */

// Mock email data
const mockEmail = {
  itemId: 'AAMkAGI2TG93AAA=',
  subject: 'Test Email - Project Update for Q1 2026',
  sender: {
    emailAddress: 'john.smith@contoso.com',
    displayName: 'John Smith'
  },
  dateTimeCreated: new Date('2026-01-20T10:30:00Z'),
  body: {
    contentType: 'HTML',
    content: '<html><body><p>This is a test email body for browser testing.</p></body></html>'
  },
  attachments: [
    {
      id: 'att1',
      name: 'Document.pdf',
      contentType: 'application/pdf',
      size: 102400,
      isInline: false
    },
    {
      id: 'att2',
      name: 'Image.png',
      contentType: 'image/png',
      size: 51200,
      isInline: false
    }
  ]
};

// Create mock Office namespace
window.Office = {
  context: {
    mailbox: {
      item: {
        itemId: mockEmail.itemId,
        itemType: 'message',
        subject: mockEmail.subject,
        from: mockEmail.sender,
        dateTimeCreated: mockEmail.dateTimeCreated,
        dateTimeModified: mockEmail.dateTimeCreated,
        internetMessageId: '<mock-message-id-12345@contoso.com>',
        conversationId: 'AAQkAGI2TG93AAA=',
        importance: 'normal',
        to: [
          { emailAddress: 'recipient@spaarke.com', displayName: 'Recipient User' }
        ],
        cc: [],
        bcc: [],
        body: {
          getAsync: function(coercionType, callback) {
            setTimeout(() => {
              callback({
                status: 'succeeded',
                value: mockEmail.body.content
              });
            }, 100);
          }
        },
        attachments: mockEmail.attachments,
        getAttachmentContentAsync: function(attachmentId, callback) {
          setTimeout(() => {
            callback({
              status: 'succeeded',
              value: {
                format: 'base64',
                content: 'SGVsbG8gV29ybGQh' // "Hello World!" in base64
              }
            });
          }, 100);
        }
      },
      userProfile: {
        emailAddress: 'user@spaarke.com',
        displayName: 'Test User'
      },
      diagnostics: {
        hostName: 'Outlook',
        hostVersion: '16.0.0.0'
      }
    },
    requirements: {
      isSetSupported: function(setName, version) {
        return true;
      }
    },
    host: 'Outlook',
    platform: 'PC'
  },

  onReady: function(callback) {
    // Simulate async Office.js initialization
    setTimeout(() => {
      console.log('[Mock Office.js] Office.onReady called - simulating Outlook host');
      if (callback) {
        callback({
          host: 'Outlook',
          platform: 'PC'
        });
      }
    }, 50);

    // Return a promise for async/await usage
    return new Promise((resolve) => {
      setTimeout(() => {
        resolve({
          host: 'Outlook',
          platform: 'PC'
        });
      }, 50);
    });
  },

  HostType: {
    Word: 'Word',
    Excel: 'Excel',
    PowerPoint: 'PowerPoint',
    Outlook: 'Outlook',
    OneNote: 'OneNote',
    Project: 'Project',
    Access: 'Access'
  },

  CoercionType: {
    Html: 'html',
    Text: 'text'
  },

  AsyncResultStatus: {
    Succeeded: 'succeeded',
    Failed: 'failed'
  },

  MailboxEnums: {
    Importance: {
      Low: 'low',
      Normal: 'normal',
      High: 'high'
    }
  }
};

// Also set on globalThis for module contexts
globalThis.Office = window.Office;

console.log('[Mock Office.js] Loaded - Browser testing mode enabled');
console.log('[Mock Office.js] Mock email subject:', mockEmail.subject);
console.log('[Mock Office.js] Mock attachments:', mockEmail.attachments.length);
