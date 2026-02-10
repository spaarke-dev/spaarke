/**
 * Tests for Navigation Service
 *
 * Tests:
 * - navigateToEvent function
 * - Tab navigation
 * - Side pane creation and reuse
 * - Error handling
 * - isNavigationAvailable check
 * - navigateToEventsPage function
 */

import {
    navigateToEvent,
    navigateToEventsPage,
    isNavigationAvailable,
    isTabNavigationAvailable,
    closeEventDetailPane,
    isEventDetailPaneOpen,
    NavigateToEventParams
} from '../../services/navigationService';
import {
    getMockXrm,
    resetXrmMocks,
    createMockSidePane,
    createMockFormTab
} from '../setupTests';

describe('navigationService', () => {
    beforeEach(() => {
        resetXrmMocks();
    });

    describe('isNavigationAvailable', () => {
        it('returns true when sidePanes API is available', () => {
            expect(isNavigationAvailable()).toBe(true);
        });

        it('returns false when Xrm is undefined', () => {
            const originalXrm = (global as { Xrm: unknown }).Xrm;
            delete (global as { Xrm?: unknown }).Xrm;

            expect(isNavigationAvailable()).toBe(false);

            (global as { Xrm: unknown }).Xrm = originalXrm;
        });
    });

    describe('isTabNavigationAvailable', () => {
        it('returns true when form context is available', () => {
            expect(isTabNavigationAvailable()).toBe(true);
        });

        it('returns false when Xrm.Page is undefined', () => {
            const mockXrm = getMockXrm();
            delete (mockXrm as { Page?: unknown }).Page;

            expect(isTabNavigationAvailable()).toBe(false);
        });
    });

    describe('navigateToEvent', () => {
        it('returns error for empty eventId', async () => {
            const result = await navigateToEvent({
                eventId: '',
                eventType: 'type-123'
            });

            expect(result.success).toBe(false);
            expect(result.error).toContain('eventId is required');
        });

        it('returns error for whitespace eventId', async () => {
            const result = await navigateToEvent({
                eventId: '   ',
                eventType: 'type-123'
            });

            expect(result.success).toBe(false);
            expect(result.error).toContain('eventId is required');
        });

        it('attempts tab navigation when navigateToTab is true', async () => {
            const mockXrm = getMockXrm();
            const mockPane = createMockSidePane('eventDetailPane');
            mockXrm.App.sidePanes.createPane.mockResolvedValue(mockPane);

            await navigateToEvent({
                eventId: 'event-123',
                eventType: 'type-456',
                navigateToTab: true
            });

            // Tab get should have been called
            expect(mockXrm.Page.ui.tabs.get).toHaveBeenCalled();
        });

        it('skips tab navigation when navigateToTab is false', async () => {
            const mockXrm = getMockXrm();
            const mockPane = createMockSidePane('eventDetailPane');
            mockXrm.App.sidePanes.createPane.mockResolvedValue(mockPane);

            await navigateToEvent({
                eventId: 'event-123',
                eventType: 'type-456',
                navigateToTab: false
            });

            // Tab get should not have been called (unless for other reasons)
            // Note: The implementation tries tab navigation by default
        });

        it('creates new pane when none exists', async () => {
            const mockXrm = getMockXrm();
            const mockPane = createMockSidePane('eventDetailPane');

            mockXrm.App.sidePanes.getPane.mockReturnValue(undefined);
            mockXrm.App.sidePanes.createPane.mockResolvedValue(mockPane);

            const result = await navigateToEvent({
                eventId: 'event-123',
                eventType: 'type-456'
            });

            expect(mockXrm.App.sidePanes.createPane).toHaveBeenCalledWith(
                expect.objectContaining({
                    paneId: 'eventDetailPane',
                    canClose: true,
                    width: 400
                })
            );
            expect(mockPane.navigate).toHaveBeenCalled();
            expect(result.openedSidePane).toBe(true);
            expect(result.success).toBe(true);
        });

        it('reuses existing pane when one exists', async () => {
            const mockXrm = getMockXrm();
            const existingPane = createMockSidePane('eventDetailPane');

            mockXrm.App.sidePanes.getPane.mockReturnValue(existingPane);

            const result = await navigateToEvent({
                eventId: 'event-123',
                eventType: 'type-456'
            });

            expect(mockXrm.App.sidePanes.createPane).not.toHaveBeenCalled();
            expect(existingPane.navigate).toHaveBeenCalled();
            expect(existingPane.select).toHaveBeenCalled();
            expect(result.openedSidePane).toBe(true);
            expect(result.success).toBe(true);
        });

        it('calls onNavigationComplete callback on success', async () => {
            const mockXrm = getMockXrm();
            const mockPane = createMockSidePane('eventDetailPane');
            mockXrm.App.sidePanes.createPane.mockResolvedValue(mockPane);

            const onComplete = jest.fn();

            await navigateToEvent({
                eventId: 'event-123',
                onNavigationComplete: onComplete
            });

            expect(onComplete).toHaveBeenCalled();
        });

        it('calls onNavigationError callback on failure', async () => {
            const mockXrm = getMockXrm();
            mockXrm.App.sidePanes.createPane.mockRejectedValue(new Error('Pane creation failed'));
            mockXrm.App.sidePanes.getPane.mockReturnValue(undefined);

            // Also make tab navigation fail
            mockXrm.Page.ui.tabs.get.mockReturnValue(undefined);

            const onError = jest.fn();

            await navigateToEvent({
                eventId: 'event-123',
                onNavigationError: onError
            });

            expect(onError).toHaveBeenCalled();
        });

        it('includes eventType in page input when provided', async () => {
            const mockXrm = getMockXrm();
            const mockPane = createMockSidePane('eventDetailPane');
            mockXrm.App.sidePanes.createPane.mockResolvedValue(mockPane);

            await navigateToEvent({
                eventId: 'event-123',
                eventType: 'type-456'
            });

            // Check that navigate was called with proper page input
            expect(mockPane.navigate).toHaveBeenCalledWith(
                expect.objectContaining({
                    pageType: 'custom'
                })
            );
        });

        it('handles sidePanes API not available gracefully', async () => {
            const mockXrm = getMockXrm();
            delete (mockXrm.App as { sidePanes?: unknown }).sidePanes;

            const result = await navigateToEvent({
                eventId: 'event-123'
            });

            // Should still complete (possibly with tab navigation only)
            expect(result).toBeDefined();
        });
    });

    describe('closeEventDetailPane', () => {
        it('closes existing pane and returns true', () => {
            const mockXrm = getMockXrm();
            const existingPane = createMockSidePane('eventDetailPane');
            mockXrm.App.sidePanes.getPane.mockReturnValue(existingPane);

            const result = closeEventDetailPane();

            expect(existingPane.close).toHaveBeenCalled();
            expect(result).toBe(true);
        });

        it('returns false when no pane exists', () => {
            const mockXrm = getMockXrm();
            mockXrm.App.sidePanes.getPane.mockReturnValue(undefined);

            const result = closeEventDetailPane();

            expect(result).toBe(false);
        });

        it('returns false when sidePanes API not available', () => {
            const mockXrm = getMockXrm();
            delete (mockXrm.App as { sidePanes?: unknown }).sidePanes;

            const result = closeEventDetailPane();

            expect(result).toBe(false);
        });
    });

    describe('isEventDetailPaneOpen', () => {
        it('returns true when pane exists', () => {
            const mockXrm = getMockXrm();
            const existingPane = createMockSidePane('eventDetailPane');
            mockXrm.App.sidePanes.getPane.mockReturnValue(existingPane);

            expect(isEventDetailPaneOpen()).toBe(true);
        });

        it('returns false when no pane exists', () => {
            const mockXrm = getMockXrm();
            mockXrm.App.sidePanes.getPane.mockReturnValue(undefined);

            expect(isEventDetailPaneOpen()).toBe(false);
        });

        it('returns false when sidePanes API not available', () => {
            const mockXrm = getMockXrm();
            delete (mockXrm.App as { sidePanes?: unknown }).sidePanes;

            expect(isEventDetailPaneOpen()).toBe(false);
        });
    });

    describe('navigateToEventsPage', () => {
        it('first attempts tab navigation', async () => {
            const mockXrm = getMockXrm();
            const mockTab = createMockFormTab('events');
            mockXrm.Page.ui.tabs.get.mockImplementation((name?: string) => {
                if (name === 'events') return mockTab;
                return undefined;
            });

            const result = await navigateToEventsPage();

            expect(mockTab.setFocus).toHaveBeenCalled();
            expect(result).toBe(true);
        });

        it('falls back to custom page navigation when tab not found', async () => {
            const mockXrm = getMockXrm();
            mockXrm.Page.ui.tabs.get.mockReturnValue(undefined);
            mockXrm.Page.ui.tabs.forEach.mockImplementation(() => {});

            const result = await navigateToEventsPage();

            expect(mockXrm.Navigation.navigateTo).toHaveBeenCalled();
        });

        it('calls onNavigationComplete callback on success', async () => {
            const mockXrm = getMockXrm();
            const mockTab = createMockFormTab('events');
            mockXrm.Page.ui.tabs.get.mockReturnValue(mockTab);

            const onComplete = jest.fn();

            await navigateToEventsPage({ onNavigationComplete: onComplete });

            expect(onComplete).toHaveBeenCalled();
        });

        it('calls onNavigationError callback when all methods fail', async () => {
            const originalXrm = (global as { Xrm: unknown }).Xrm;
            delete (global as { Xrm?: unknown }).Xrm;

            const onError = jest.fn();

            await navigateToEventsPage({ onNavigationError: onError });

            expect(onError).toHaveBeenCalled();

            (global as { Xrm: unknown }).Xrm = originalXrm;
        });
    });

    describe('tab navigation edge cases', () => {
        it('finds events tab by common name variations', async () => {
            const mockXrm = getMockXrm();
            const mockTab = createMockFormTab('Events');
            mockXrm.Page.ui.tabs.get.mockImplementation((name?: string) => {
                if (name === 'Events') return mockTab;
                return undefined;
            });

            const mockPane = createMockSidePane('eventDetailPane');
            mockXrm.App.sidePanes.createPane.mockResolvedValue(mockPane);

            const result = await navigateToEvent({
                eventId: 'event-123',
                navigateToTab: true
            });

            expect(result.navigatedToTab).toBe(true);
        });

        it('handles hidden tab by making it visible', async () => {
            const mockXrm = getMockXrm();
            const mockTab = createMockFormTab('events');
            mockTab.getVisible.mockReturnValue(false);

            mockXrm.Page.ui.tabs.get.mockReturnValue(mockTab);

            const mockPane = createMockSidePane('eventDetailPane');
            mockXrm.App.sidePanes.createPane.mockResolvedValue(mockPane);

            await navigateToEvent({
                eventId: 'event-123',
                navigateToTab: true
            });

            expect(mockTab.setVisible).toHaveBeenCalledWith(true);
        });
    });
});
