/**
 * Page Objects for Office Add-in E2E Testing
 *
 * Export all add-in page objects for easy importing in tests.
 */

export { OutlookTaskPanePage } from './OutlookTaskPanePage';
export type {
  OutlookTaskPaneConfig,
  DocumentSearchResult,
  ShareLinkResponse,
  AttachmentResponse,
} from './OutlookTaskPanePage';

export { WordTaskPanePage } from './WordTaskPanePage';
export type {
  WordTaskPaneConfig,
  EntitySearchResult,
  SaveJobResponse,
  JobStatusResponse,
} from './WordTaskPanePage';
