/**
 * Page Object for Word Add-in Task Pane
 *
 * Provides reusable methods for interacting with the Spaarke
 * task pane in Word for document save operations.
 *
 * @see spec.md - Word save flow requirements (FR-09, FR-10)
 * @see SaveView.tsx - Component implementation
 * @see WordHostAdapter.ts - Word-specific host adapter
 */

import { Page, Locator, expect } from '@playwright/test';

export interface WordTaskPaneConfig {
  /** Base URL for the add-in task pane */
  taskPaneUrl: string;
  /** Timeout for add-in initialization */
  initTimeout: number;
  /** API base URL for mocking */
  apiBaseUrl: string;
}

export interface EntitySearchResult {
  id: string;
  entityType: 'Matter' | 'Project' | 'Invoice' | 'Account' | 'Contact';
  logicalName: string;
  name: string;
  displayInfo: string;
}

export interface SaveJobResponse {
  jobId: string;
  documentId: string;
  statusUrl: string;
  streamUrl: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed';
  duplicate: boolean;
  correlationId: string;
  message?: string;
}

export interface JobStatusResponse {
  jobId: string;
  status: 'Queued' | 'Running' | 'Completed' | 'Failed';
  stages: Array<{
    name: string;
    status: 'Pending' | 'Running' | 'Completed' | 'Skipped' | 'Failed';
  }>;
  documentId?: string;
  documentUrl?: string;
  errorCode?: string;
  errorMessage?: string;
}

/**
 * Page Object for Word Task Pane interactions
 */
export class WordTaskPanePage {
  readonly page: Page;
  readonly config: WordTaskPaneConfig;

  // Navigation elements
  readonly navSaveButton: Locator;
  readonly navShareButton: Locator;

  // Entity picker section
  readonly entitySearchInput: Locator;
  readonly entitySearchButton: Locator;
  readonly entityTypeDropdown: Locator;
  readonly entityResults: Locator;
  readonly entityItems: Locator;
  readonly quickCreateButton: Locator;

  // Document info section
  readonly documentNameDisplay: Locator;
  readonly documentSizeDisplay: Locator;

  // Processing options section
  readonly profileSummaryToggle: Locator;
  readonly ragIndexToggle: Locator;
  readonly deepAnalysisToggle: Locator;

  // Save action section
  readonly saveButton: Locator;
  readonly cancelButton: Locator;

  // Status elements
  readonly loadingSpinner: Locator;
  readonly errorMessage: Locator;
  readonly successMessage: Locator;
  readonly progressContainer: Locator;
  readonly stageIndicators: Locator;

  // Job status section
  readonly jobStatusContainer: Locator;
  readonly viewDocumentButton: Locator;

  constructor(page: Page, config: WordTaskPaneConfig) {
    this.page = page;
    this.config = config;

    // Navigation
    this.navSaveButton = page.getByRole('tab', { name: /save/i });
    this.navShareButton = page.getByRole('tab', { name: /share/i });

    // Entity picker
    this.entitySearchInput = page.getByPlaceholder(/search for/i);
    this.entitySearchButton = page.getByRole('button', { name: /search/i });
    this.entityTypeDropdown = page.getByRole('combobox', { name: /entity type/i });
    this.entityResults = page.locator('[data-testid="entity-results"]');
    this.entityItems = page.locator('[data-testid="entity-item"]');
    this.quickCreateButton = page.getByRole('button', { name: /create new/i });

    // Document info
    this.documentNameDisplay = page.locator('[data-testid="document-name"]');
    this.documentSizeDisplay = page.locator('[data-testid="document-size"]');

    // Processing options
    this.profileSummaryToggle = page.getByRole('switch', { name: /profile summary/i });
    this.ragIndexToggle = page.getByRole('switch', { name: /rag index/i });
    this.deepAnalysisToggle = page.getByRole('switch', { name: /deep analysis/i });

    // Save action
    this.saveButton = page.getByRole('button', { name: /save to spaarke/i });
    this.cancelButton = page.getByRole('button', { name: /cancel/i });

    // Status
    this.loadingSpinner = page.locator('[role="progressbar"]');
    this.errorMessage = page.locator('[data-testid="error-message"]');
    this.successMessage = page.locator('[data-testid="success-message"]');
    this.progressContainer = page.locator('[data-testid="progress-container"]');
    this.stageIndicators = page.locator('[data-testid="stage-indicator"]');

    // Job status
    this.jobStatusContainer = page.locator('[data-testid="job-status"]');
    this.viewDocumentButton = page.getByRole('button', { name: /view document/i });
  }

  /**
   * Navigate to the task pane in save mode
   */
  async navigateToSaveMode(): Promise<void> {
    await this.page.goto(this.config.taskPaneUrl);
    await this.waitForTaskPaneLoad();

    // Click save navigation if available
    if (await this.navSaveButton.isVisible()) {
      await this.navSaveButton.click();
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
    await this.page.waitForFunction(
      () => {
        return document.querySelector('[data-fui-focus-visible]') !== null;
      },
      { timeout: timeout || this.config.initTimeout }
    );
  }

  /**
   * Wait for document context to load
   */
  async waitForDocumentContext(timeout = 5000): Promise<void> {
    await this.page.waitForFunction(
      () => {
        const spinner = document.querySelector('[role="progressbar"]');
        return !spinner || spinner.getAttribute('aria-hidden') === 'true';
      },
      { timeout }
    );
  }

  /**
   * Get document name displayed in task pane
   */
  async getDocumentName(): Promise<string> {
    return (await this.documentNameDisplay.textContent()) || '';
  }

  /**
   * Search for entities with the given query
   */
  async searchEntities(query: string): Promise<void> {
    await this.entitySearchInput.fill(query);
    await this.entitySearchButton.click();
    await this.waitForEntityResults();
  }

  /**
   * Wait for entity search results
   */
  async waitForEntityResults(timeout = 5000): Promise<void> {
    await this.page.waitForFunction(
      () => {
        const spinner = document.querySelector('[role="progressbar"]');
        return !spinner || spinner.getAttribute('aria-hidden') === 'true';
      },
      { timeout }
    );
  }

  /**
   * Select an entity from search results by name
   */
  async selectEntity(entityName: string): Promise<void> {
    const entityItem = this.page.locator(`text="${entityName}"`).first();
    await entityItem.click();
  }

  /**
   * Filter entities by type
   */
  async filterByEntityType(
    entityType: 'Matter' | 'Project' | 'Invoice' | 'Account' | 'Contact' | 'All'
  ): Promise<void> {
    await this.entityTypeDropdown.click();
    await this.page.getByRole('option', { name: new RegExp(entityType, 'i') }).click();
  }

  /**
   * Get count of entity search results
   */
  async getEntityResultCount(): Promise<number> {
    const items = await this.entityItems.all();
    return items.length;
  }

  /**
   * Toggle processing option
   */
  async setProcessingOption(
    option: 'profileSummary' | 'ragIndex' | 'deepAnalysis',
    enabled: boolean
  ): Promise<void> {
    let toggle: Locator;
    switch (option) {
      case 'profileSummary':
        toggle = this.profileSummaryToggle;
        break;
      case 'ragIndex':
        toggle = this.ragIndexToggle;
        break;
      case 'deepAnalysis':
        toggle = this.deepAnalysisToggle;
        break;
    }

    const isChecked = await toggle.isChecked();
    if (isChecked !== enabled) {
      await toggle.click();
    }
  }

  /**
   * Click the save button
   */
  async clickSave(): Promise<void> {
    await this.saveButton.click();
  }

  /**
   * Wait for save operation to complete
   */
  async waitForSaveComplete(timeout = 30000): Promise<void> {
    // Wait for job status to appear
    await this.jobStatusContainer.waitFor({ state: 'visible', timeout });

    // Wait for success or error state
    await this.page.waitForFunction(
      () => {
        const success = document.querySelector('[data-testid="success-message"]');
        const error = document.querySelector('[data-testid="error-message"]');
        return success !== null || error !== null;
      },
      { timeout }
    );
  }

  /**
   * Wait for job stages to update via SSE
   */
  async waitForStageUpdate(stageName: string, timeout = 10000): Promise<void> {
    await this.page.waitForFunction(
      (stage) => {
        const stageElement = document.querySelector(`[data-stage="${stage}"]`);
        return stageElement && stageElement.getAttribute('data-status') !== 'Pending';
      },
      stageName,
      { timeout }
    );
  }

  /**
   * Check if save was successful
   */
  async isSaveSuccessful(): Promise<boolean> {
    return await this.successMessage.isVisible().catch(() => false);
  }

  /**
   * Check if error is displayed
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
   * Get job stage statuses
   */
  async getStageStatuses(): Promise<
    Array<{ name: string; status: string }>
  > {
    const stages = await this.stageIndicators.all();
    const statuses: Array<{ name: string; status: string }> = [];

    for (const stage of stages) {
      const name = (await stage.getAttribute('data-stage')) || '';
      const status = (await stage.getAttribute('data-status')) || '';
      statuses.push({ name, status });
    }

    return statuses;
  }

  /**
   * Click Quick Create button to create new entity
   */
  async openQuickCreate(): Promise<void> {
    await this.quickCreateButton.click();
    // Wait for dialog to open
    await this.page.waitForSelector('[role="dialog"]', { timeout: 5000 });
  }

  /**
   * Fill Quick Create form and submit
   */
  async createNewEntity(
    entityType: 'Matter' | 'Project' | 'Account' | 'Contact',
    name: string,
    additionalFields?: Record<string, string>
  ): Promise<void> {
    // Wait for form to load
    await this.page.waitForSelector('[data-testid="quickcreate-form"]', { timeout: 5000 });

    // Fill name field
    const nameInput = this.page.getByLabel(/name/i).first();
    await nameInput.fill(name);

    // Fill additional fields if provided
    if (additionalFields) {
      for (const [field, value] of Object.entries(additionalFields)) {
        const input = this.page.getByLabel(new RegExp(field, 'i'));
        if (await input.isVisible()) {
          await input.fill(value);
        }
      }
    }

    // Submit form
    const submitButton = this.page.getByRole('button', { name: /create/i }).last();
    await submitButton.click();

    // Wait for dialog to close
    await this.page.waitForFunction(
      () => document.querySelector('[role="dialog"]') === null,
      { timeout: 10000 }
    );
  }

  /**
   * View the created document
   */
  async viewDocument(): Promise<void> {
    await this.viewDocumentButton.click();
  }

  // ============================================
  // Mock Methods for Testing
  // ============================================

  /**
   * Mock Office.js Word environment
   */
  async mockWordEnvironment(
    documentTitle = 'Test Document',
    documentSize = 1024 * 50 // 50KB
  ): Promise<void> {
    await this.page.addInitScript(
      ({ title, size }) => {
        const mockOoxmlContent =
          'PD94bWwgdmVyc2lvbj0iMS4wIj8+CjxkdGQ+...'; // Mock base64 OOXML

        (window as any).Word = {
          run: async (callback: (context: any) => Promise<any>) => {
            const context = {
              document: {
                body: {
                  getOoxml: () => ({ value: mockOoxmlContent }),
                  getHtml: () => ({ value: '<p>Test content</p>' }),
                  text: 'Test content',
                  load: () => {},
                },
                properties: {
                  title: title,
                  author: 'Test Author',
                  creationDate: new Date(),
                  lastSaveTime: new Date(),
                  load: () => {},
                },
              },
              sync: async () => {},
            };
            return await callback(context);
          },
        };

        (window as any).Office = {
          context: {
            requirements: {
              isSetSupported: (name: string, version: string) => {
                if (name === 'WordApi' && parseFloat(version) <= 1.3) {
                  return true;
                }
                return true;
              },
            },
          },
          onReady: (callback: (info: any) => void) => {
            callback({ host: 'Word', platform: 'PC' });
          },
          HostType: {
            Word: 'Word',
            Outlook: 'Outlook',
          },
        };

        // Mock document size
        (window as any).__mockDocumentSize = size;
      },
      { title: documentTitle, size: documentSize }
    );
  }

  /**
   * Mock Word environment with large document (exceeds limit)
   */
  async mockLargeDocument(sizeInMB: number): Promise<void> {
    const sizeInBytes = sizeInMB * 1024 * 1024;
    await this.mockWordEnvironment(`Large Document (${sizeInMB}MB)`, sizeInBytes);
  }

  /**
   * Mock entity search API response
   */
  async mockEntitySearchApi(results: EntitySearchResult[]): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/search/entities*`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({ results, totalCount: results.length, hasMore: false }),
      });
    });
  }

  /**
   * Mock save API response
   */
  async mockSaveApi(response: SaveJobResponse): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/save`, (route) => {
      route.fulfill({
        status: response.duplicate ? 200 : 202,
        contentType: 'application/json',
        body: JSON.stringify(response),
      });
    });
  }

  /**
   * Mock job status API response
   */
  async mockJobStatusApi(jobId: string, response: JobStatusResponse): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/jobs/${jobId}`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify(response),
      });
    });
  }

  /**
   * Mock SSE stream for job status updates
   */
  async mockJobStatusSSE(
    jobId: string,
    events: Array<{ event: string; data: any; delay?: number }>
  ): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/jobs/${jobId}/stream`, async (route) => {
      const headers = {
        'Content-Type': 'text/event-stream',
        'Cache-Control': 'no-cache',
        Connection: 'keep-alive',
      };

      // Build SSE body
      let body = '';
      for (const e of events) {
        body += `event: ${e.event}\n`;
        body += `data: ${JSON.stringify(e.data)}\n\n`;
      }

      await route.fulfill({
        status: 200,
        headers,
        body,
      });
    });
  }

  /**
   * Mock Quick Create API response
   */
  async mockQuickCreateApi(
    entityType: string,
    response: { id: string; name: string; url: string }
  ): Promise<void> {
    await this.page.route(
      `${this.config.apiBaseUrl}/office/quickcreate/${entityType.toLowerCase()}`,
      (route) => {
        route.fulfill({
          status: 201,
          contentType: 'application/json',
          body: JSON.stringify({
            id: response.id,
            entityType,
            logicalName: `sprk_${entityType.toLowerCase()}`,
            name: response.name,
            url: response.url,
          }),
        });
      }
    );
  }

  /**
   * Mock API error response
   */
  async mockApiError(
    endpoint: string,
    status: number,
    errorCode: string,
    message: string
  ): Promise<void> {
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
   * Mock recent associations API response
   */
  async mockRecentApi(
    recentAssociations: EntitySearchResult[],
    recentDocuments: Array<{ id: string; name: string }> = []
  ): Promise<void> {
    await this.page.route(`${this.config.apiBaseUrl}/office/recent*`, (route) => {
      route.fulfill({
        status: 200,
        contentType: 'application/json',
        body: JSON.stringify({
          recentAssociations,
          recentDocuments,
          favorites: [],
        }),
      });
    });
  }
}

export default WordTaskPanePage;
