/**
 * Page Object for SpeFileViewer PCF Control
 *
 * Provides methods for interacting with the file viewer component
 * in E2E tests, including loading states, preview, and Edit button.
 */

import { Page, Locator } from '@playwright/test';
import { BasePCFPage, PCFControlConfig } from '../BasePCFPage';

/**
 * FileViewer component states
 */
export enum FileViewerState {
  Loading = 'loading',
  Ready = 'ready',
  Error = 'error'
}

export class SpeFileViewerPage extends BasePCFPage {
  // Loading overlay elements
  readonly loadingOverlay: Locator;
  readonly loadingSpinner: Locator;
  readonly loadingText: Locator;

  // Preview elements
  readonly previewContainer: Locator;
  readonly previewIframe: Locator;

  // Action buttons
  readonly editInDesktopButton: Locator;
  readonly downloadButton: Locator;
  readonly refreshButton: Locator;

  // Error state elements
  readonly errorContainer: Locator;
  readonly errorMessage: Locator;
  readonly retryButton: Locator;

  constructor(page: Page, config: PCFControlConfig) {
    super(page, config);

    // Loading state elements
    this.loadingOverlay = this.controlRoot.locator('.spe-file-viewer-loading-overlay');
    this.loadingSpinner = this.controlRoot.locator('.spe-file-viewer-loading-spinner');
    this.loadingText = this.controlRoot.locator('.spe-file-viewer-loading-text');

    // Preview elements
    this.previewContainer = this.controlRoot.locator('.spe-file-viewer__container');
    this.previewIframe = this.controlRoot.locator('iframe.spe-file-viewer__iframe');

    // Action buttons
    this.editInDesktopButton = this.controlRoot.locator('[data-testid="edit-in-desktop-btn"]');
    this.downloadButton = this.controlRoot.locator('[data-testid="download-btn"]');
    this.refreshButton = this.controlRoot.locator('[data-testid="refresh-btn"]');

    // Error state
    this.errorContainer = this.controlRoot.locator('.spe-file-viewer__error-container');
    this.errorMessage = this.controlRoot.locator('.spe-file-viewer__error-message');
    this.retryButton = this.controlRoot.locator('[data-testid="retry-btn"]');
  }

  /**
   * Get current state of the file viewer
   */
  async getState(): Promise<FileViewerState> {
    if (await this.loadingOverlay.isVisible()) {
      return FileViewerState.Loading;
    }
    if (await this.errorContainer.isVisible()) {
      return FileViewerState.Error;
    }
    return FileViewerState.Ready;
  }

  /**
   * Wait for loading state to appear
   * @param timeout Maximum time to wait in milliseconds
   */
  async waitForLoadingState(timeout = 500): Promise<void> {
    await this.loadingOverlay.waitFor({ state: 'visible', timeout });
  }

  /**
   * Wait for preview to be ready (loading complete)
   * @param timeout Maximum time to wait in milliseconds
   */
  async waitForPreviewReady(timeout = 10000): Promise<void> {
    // Wait for loading overlay to disappear
    await this.loadingOverlay.waitFor({ state: 'hidden', timeout });

    // Wait for preview container to be visible
    await this.previewContainer.waitFor({ state: 'visible', timeout });
  }

  /**
   * Wait for error state to appear
   * @param timeout Maximum time to wait in milliseconds
   */
  async waitForErrorState(timeout = 10000): Promise<void> {
    await this.errorContainer.waitFor({ state: 'visible', timeout });
  }

  /**
   * Click the "Edit in Desktop" button
   * Note: This triggers a protocol URL (ms-word:, ms-excel:, etc.)
   * which may be blocked by the browser. Use interceptProtocolUrl() to verify.
   */
  async clickEditInDesktop(): Promise<void> {
    await this.editInDesktopButton.click();
  }

  /**
   * Click the "Edit in Desktop" button and capture the protocol URL
   * @returns The protocol URL that would be triggered
   */
  async clickEditInDesktopAndCaptureUrl(): Promise<string | null> {
    let capturedUrl: string | null = null;

    // Intercept window.location.href assignment
    await this.page.evaluate(() => {
      const originalDescriptor = Object.getOwnPropertyDescriptor(window, 'location');
      (window as any).__originalLocation = originalDescriptor;
      (window as any).__capturedProtocolUrl = null;

      Object.defineProperty(window, 'location', {
        get: () => originalDescriptor?.get?.call(window),
        set: (url: string) => {
          if (url.startsWith('ms-word:') || url.startsWith('ms-excel:') || url.startsWith('ms-powerpoint:')) {
            (window as any).__capturedProtocolUrl = url;
            return;
          }
          if (originalDescriptor?.set) {
            originalDescriptor.set.call(window, url);
          }
        },
        configurable: true
      });
    });

    // Click the button
    await this.editInDesktopButton.click();

    // Wait for potential async operation
    await this.page.waitForTimeout(1000);

    // Capture the URL
    capturedUrl = await this.page.evaluate(() => {
      const url = (window as any).__capturedProtocolUrl;
      // Restore original location
      const original = (window as any).__originalLocation;
      if (original) {
        Object.defineProperty(window, 'location', original);
      }
      return url;
    });

    return capturedUrl;
  }

  /**
   * Click download button
   */
  async clickDownload(): Promise<void> {
    await this.downloadButton.click();
  }

  /**
   * Click retry button (in error state)
   */
  async clickRetry(): Promise<void> {
    await this.retryButton.click();
    await this.waitForUpdate();
  }

  /**
   * Check if Edit button is visible and enabled
   */
  async isEditButtonEnabled(): Promise<boolean> {
    const isVisible = await this.editInDesktopButton.isVisible();
    if (!isVisible) return false;

    const isDisabled = await this.editInDesktopButton.isDisabled();
    return !isDisabled;
  }

  /**
   * Check if Edit button shows loading state
   */
  async isEditButtonLoading(): Promise<boolean> {
    const loadingClass = await this.editInDesktopButton.getAttribute('class');
    return loadingClass?.includes('loading') ?? false;
  }

  /**
   * Get error message text
   */
  async getErrorMessage(): Promise<string> {
    return await this.errorMessage.textContent() ?? '';
  }

  /**
   * Get iframe src URL
   */
  async getPreviewUrl(): Promise<string | null> {
    const src = await this.previewIframe.getAttribute('src');
    return src;
  }

  /**
   * Check accessibility attributes on loading overlay
   */
  async verifyLoadingAccessibility(): Promise<{
    role: string | null;
    ariaBusy: string | null;
    ariaLabel: string | null;
  }> {
    return {
      role: await this.loadingOverlay.getAttribute('role'),
      ariaBusy: await this.loadingOverlay.getAttribute('aria-busy'),
      ariaLabel: await this.loadingOverlay.getAttribute('aria-label')
    };
  }

  /**
   * Check accessibility attributes on action buttons
   */
  async verifyButtonAccessibility(): Promise<{
    editAriaLabel: string | null;
    downloadAriaLabel: string | null;
  }> {
    return {
      editAriaLabel: await this.editInDesktopButton.getAttribute('aria-label'),
      downloadAriaLabel: await this.downloadButton.getAttribute('aria-label')
    };
  }
}
