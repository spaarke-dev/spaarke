/**
 * Page Object for Outlook Add-in Task Pane
 *
 * Provides reusable methods for interacting with the Spaarke
 * task pane in Outlook compose and read modes.
 *
 * @see spec.md - Share flow requirements
 * @see ShareView.tsx - Component implementation
 */

import { Page, Locator, expect } from '@playwright/test';

export interface OutlookTaskPaneConfig {
  /** Base URL for the add-in task pane */
  taskPaneUrl: string;
  /** Timeout for add-in initialization */
  initTimeout: number;
  /** API base URL for mocking */
  apiBaseUrl: string;
}

export interface DocumentSearchResult {
  id: string;
  name: string;
  path: string;
  modifiedDate?: string;
}

export interface ShareLinkResponse {
  links: Array<{
    documentId: string;
    url: string;
    title: string;
  }>;
  invitations?: Array<{
    email: string;
    status: string;
    invitationId: string;
  }>;
}

export interface AttachmentResponse {
  attachments: Array<{
    documentId: string;
    filename: string;
    contentType: string;
    size: number;
    downloadUrl: string;
    urlExpiry: string;
  }>;
}

/**
 * Page Object for Outlook Task Pane interactions
 */
export class OutlookTaskPanePage {
  readonly page: Page;
  readonly config: OutlookTaskPaneConfig;

  // Navigation elements
  readonly navShareButton: Locator;
  readonly navSaveButton: Locator;

  // Search section
  readonly searchInput: Locator;
  readonly searchButton: Locator;
  readonly searchResults: Locator;
  readonly documentItems: Locator;

  // Share section
  readonly permissionDropdown: Locator;
  readonly generateLinkButton: Locator;
  readonly generatedLinkInput: Locator;
  readonly copyLinkButton: Locator;
  readonly insertLinkButton: Locator;
  readonly shareAsAttachmentButton: Locator;

  // Status elements
  readonly loadingSpinner: Locator;
  readonly errorMessage: Locator;
  readonly successMessage: Locator;

  // Compose mode elements
  readonly composeBody: Locator;

  constructor(page: Page, config: OutlookTaskPaneConfig) {
    this.page = page;
    this.config = config;

    // Navigation
    this.navShareButton = page.getByRole('tab', { name: /share/i });
    this.navSaveButton = page.getByRole('tab', { name: /save/i });

    // Search
    this.searchInput = page.getByPlaceholder('Search by name or path...');
    this.searchButton = page.getByRole('button', { name: /search/i });
    this.searchResults = page.locator('[data-testid="search-results"]');
    this.documentItems = page.locator('[data-testid="document-item"]');

    // Share
    this.permissionDropdown = page.getByRole('combobox');
    this.generateLinkButton = page.getByRole('button', { name: /generate link/i });
    this.generatedLinkInput = page.locator('input[readonly]');
    this.copyLinkButton = page.getByRole('button', { name: /copy/i });
    this.insertLinkButton = page.getByRole('button', { name: /insert link/i });
    this.shareAsAttachmentButton = page.getByRole('button', { name: /attach/i });

    // Status
    this.loadingSpinner = page.locator('[role="progressbar"]');
    this.errorMessage = page.locator('[data-testid="error-message"]');
    this.successMessage = page.locator('[data-testid="success-message"]');

    // Compose mode
    this.composeBody = page.locator('[data-testid="compose-body"]');
  }

  /**
   * Navigate to the task pane in share mode
   */
  async navigateToShareMode(): Promise<void> {
    await this.page.goto(this.config.taskPaneUrl);
    await this.waitForTaskPaneLoad();

    // Click share navigation if available
    if (await this.navShareButton.isVisible()) {
      await this.navShareButton.click();
    }
  }

  /**
   * Wait for task pane to fully load
   */
  async waitForTaskPaneLoad(timeout?: number): Promise<void> {
    await this.page.waitForLoadState('domcontentloaded', {
      timeout: timeout || this.config.initTimeout,
    });

    // Wait for Fluent UI provider to initialize
    await this.page.waitForFunction(() => {
      return document.querySelector('[data-fui-focus-visible]') !== null;
    }, { timeout: timeout || this.config.initTimeout });
  }

  /**
   * Search for documents with the given query
   */
  async searchDocuments(query: string): Promise<void> {
    await this.searchInput.fill(query);
    await this.searchButton.click();
    await this.waitForSearchResults();
  }

  /**
   * Wait for search results to appear
   */
  async waitForSearchResults(timeout = 5000): Promise<void> {
    await this.page.waitForFunction(
      () => {
        const spinner = document.querySelector('[role="progressbar"]');
        return !spinner || spinner.getAttribute('aria-hidden') === 'true';
      },
      { timeout }
    );
  }

  /**
   * Select a document from search results by name
   */
  async selectDocument(documentName: string): Promise<void> {
    const documentItem = this.page.locator(`text="${documentName}"`).first();
    await documentItem.click();

    // Wait for selection to register
    await this.page.waitForSelector('text="Generate Sharing Link"', { timeout: 3000 });
  }

  /**
   * Select multiple documents from search results
   */
  async selectMultipleDocuments(documentNames: string[]): Promise<void> {
    for (const name of documentNames) {
      const documentItem = this.page.locator(`text="${name}"`).first();
      await documentItem.click({ modifiers: ['Control'] }); // Ctrl+click for multi-select
    }
  }

  /**
   * Set the permission type for sharing
   */
  async setPermission(permission: 'view' | 'edit'): Promise<void> {
    await this.permissionDropdown.click();

    const optionText = permission === 'view' ? 'View only' : 'Can edit';
    await this.page.getByRole('option', { name: new RegExp(optionText, 'i') }).click();
  }

  /**
   * Generate a sharing link for the selected document
   */
  async generateLink(): Promise<string> {
    await this.generateLinkButton.click();

    // Wait for link to be generated
    await this.generatedLinkInput.waitFor({ state: 'visible', timeout: 10000 });

    return await this.generatedLinkInput.inputValue();
  }

  /**
   * Copy the generated link to clipboard
   */
  async copyLink(): Promise<void> {
    await this.copyLinkButton.click();

    // Wait for "Copied!" feedback
    await this.page.waitForSelector('text="Copied!"', { timeout: 2000 });
  }

  /**
   * Insert the generated link into the email body
   */
  async insertLink(): Promise<void> {
    await this.insertLinkButton.click();

    // Wait for insertion confirmation
    await this.waitForOperationComplete();
  }

  /**
   * Share document as attachment
   */
  async shareAsAttachment(): Promise<void> {
    await this.shareAsAttachmentButton.click();

    // Wait for attachment to be added
    await this.waitForOperationComplete();
  }

  /**
   * Wait for an async operation to complete
   */
  async waitForOperationComplete(timeout = 10000): Promise<void> {
    // Wait for loading to finish
    await this.page.waitForFunction(
      () => {
        const spinner = document.querySelector('[role="progressbar"]');
        return !spinner || spinner.getAttribute('aria-hidden') === 'true';
      },
      { timeout }
    );
  }

  /**
   * Check if error message is displayed
   */
  async hasError(): Promise<boolean> {
    return await this.errorMessage.isVisible();
  }

  /**
   * Get error message text
   */
  async getErrorMessage(): Promise<string | null> {
    if (await this.hasError()) {
      return await this.errorMessage.textContent();
    }
    return null;
  }

  /**
   * Verify the task pane is in compose mode
   */
  async verifyComposeMode(): Promise<boolean> {
    // Check for compose-specific UI elements
    const shareButton = this.page.getByText('Share from Spaarke');
    return await shareButton.isVisible().catch(() => false);
  }

  /**
   * Get count of search results
   */
  async getSearchResultCount(): Promise<number> {
    const items = await this.page.locator('[data-testid="document-item"]').all();
    return items.length;
  }

  /**
   * Verify document appears in email body (compose mode)
   */
  async verifyLinkInEmailBody(expectedUrl: string): Promise<boolean> {
    // This would interact with Office.js mock or actual email body
    const bodyContent = await this.page.evaluate(() => {
      // Mock: Check if link was inserted
      const body = document.querySelector('[data-testid="email-body-mock"]');
      return body?.innerHTML || '';
    });

    return bodyContent.includes(expectedUrl);
  }

  /**
   * Mock API responses for testing
   */
  async mockSearchApi(results: DocumentSearchResult[]): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/search/documents*`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ results, totalCount: results.length, hasMore: false }),
      });
    });
  }

  /**
   * Mock share links API response
   */
  async mockShareLinksApi(response: ShareLinkResponse): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/share/links`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(response),
      });
    });
  }

  /**
   * Mock share attachments API response
   */
  async mockShareAttachApi(response: AttachmentResponse): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/share/attach`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(response),
      });
    });
  }

  /**
   * Mock API error response
   */
  async mockApiError(endpoint: string, status: number, errorCode: string, message: string): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}${endpoint}*`, (route) => {
      route.fulfill({
        status,
        contentType: 'application/json',
        body: JSON.stringify({
          type: `https://spaarke.com/errors/office/${errorCode.toLowerCase()}`,
          title: message,
          status,
          errorCode,
        }),
      });
    });
  }

  /**
   * Mock Office.js compose mode
   */
  async mockOfficeComposeMode(): Promise<void> {
    await this.page.addInitScript(() => {
      (window as any).Office = {
        context: {
          mailbox: {
            item: {
              itemType: 'message',
              body: {
                setAsync: (content: string, options: any, callback: (result: any) => void) => {
                  // Mock body insertion
                  const mockBody = document.querySelector('[data-testid="email-body-mock"]');
                  if (mockBody) {
                    mockBody.innerHTML += content;
                  }
                  callback({ status: 'succeeded' });
                },
                getAsync: (coercionType: any, callback: (result: any) => void) => {
                  const mockBody = document.querySelector('[data-testid="email-body-mock"]');
                  callback({
                    status: 'succeeded',
                    value: mockBody?.innerHTML || '',
                  });
                },
              },
              addFileAttachmentAsync: (uri: string, name: string, options: any, callback: (result: any) => void) => {
                // Mock attachment addition
                callback({ status: 'succeeded', value: `attachment-${Date.now()}` });
              },
            },
          },
          requirements: {
            isSetSupported: (name: string, version: string) => true,
          },
        },
        CoercionType: {
          Html: 'html',
          Text: 'text',
        },
        AsyncResultStatus: {
          Succeeded: 'succeeded',
          Failed: 'failed',
        },
      };
    });
  }
}

export default OutlookTaskPanePage;
