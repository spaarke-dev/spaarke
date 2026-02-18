/**
 * Jest Test Setup for DueDatesWidget
 *
 * Configures testing environment with:
 * - @testing-library/jest-dom matchers
 * - Mock for Xrm global object
 * - Mock for PCF context
 * - Mock for WebAPI
 */

import '@testing-library/jest-dom';

// ─────────────────────────────────────────────────────────────────────────────
// Xrm Global Mock
// ─────────────────────────────────────────────────────────────────────────────

interface MockSidePane {
    paneId: string;
    title?: string;
    close: jest.Mock;
    select: jest.Mock;
    navigate: jest.Mock;
}

interface MockSidePanes {
    state: 0 | 1;
    createPane: jest.Mock<Promise<MockSidePane>>;
    getPane: jest.Mock<MockSidePane | undefined>;
    getAllPanes: jest.Mock<MockSidePane[]>;
}

interface MockFormTab {
    getName: jest.Mock<string>;
    setFocus: jest.Mock;
    setVisible: jest.Mock;
    getVisible: jest.Mock<boolean>;
}

interface MockFormUi {
    tabs: {
        get: jest.Mock;
        getLength: jest.Mock<number>;
        forEach: jest.Mock;
    };
}

interface MockXrm {
    App: {
        sidePanes: MockSidePanes;
    };
    Page: {
        ui: MockFormUi;
    };
    Navigation: {
        navigateTo: jest.Mock;
    };
}

// Create default mock side pane
export function createMockSidePane(paneId: string = 'testPane'): MockSidePane {
    return {
        paneId,
        title: 'Test Pane',
        close: jest.fn(),
        select: jest.fn(),
        navigate: jest.fn().mockResolvedValue(undefined)
    };
}

// Create default mock form tab
export function createMockFormTab(name: string = 'events'): MockFormTab {
    return {
        getName: jest.fn().mockReturnValue(name),
        setFocus: jest.fn(),
        setVisible: jest.fn(),
        getVisible: jest.fn().mockReturnValue(true)
    };
}

// Initialize Xrm mock
export function createMockXrm(): MockXrm {
    const mockPane = createMockSidePane('eventDetailPane');
    const mockTab = createMockFormTab('events');

    return {
        App: {
            sidePanes: {
                state: 1,
                createPane: jest.fn().mockResolvedValue(mockPane),
                getPane: jest.fn().mockReturnValue(undefined),
                getAllPanes: jest.fn().mockReturnValue([])
            }
        },
        Page: {
            ui: {
                tabs: {
                    get: jest.fn().mockImplementation((name?: string) => {
                        if (name === 'events' || name === 'Events') {
                            return mockTab;
                        }
                        return undefined;
                    }),
                    getLength: jest.fn().mockReturnValue(3),
                    forEach: jest.fn()
                }
            }
        },
        Navigation: {
            navigateTo: jest.fn().mockResolvedValue(undefined)
        }
    };
}

// Set global Xrm
(global as unknown as { Xrm: MockXrm }).Xrm = createMockXrm();

// ─────────────────────────────────────────────────────────────────────────────
// WebAPI Mock
// ─────────────────────────────────────────────────────────────────────────────

export interface MockWebApiOptions {
    entities?: unknown[];
    error?: Error;
}

export function createMockWebApi(options: MockWebApiOptions = {}): ComponentFramework.WebApi {
    const { entities = [], error } = options;

    return {
        retrieveMultipleRecords: jest.fn().mockImplementation(() => {
            if (error) {
                return Promise.reject(error);
            }
            return Promise.resolve({ entities });
        }),
        retrieveRecord: jest.fn().mockResolvedValue({}),
        createRecord: jest.fn().mockResolvedValue({ id: 'new-id' }),
        updateRecord: jest.fn().mockResolvedValue({}),
        deleteRecord: jest.fn().mockResolvedValue({})
    } as unknown as ComponentFramework.WebApi;
}

// ─────────────────────────────────────────────────────────────────────────────
// PCF Context Mock
// ─────────────────────────────────────────────────────────────────────────────

export interface MockContextOptions {
    webApi?: ComponentFramework.WebApi;
    parentRecordId?: string;
    daysAhead?: number;
    maxItems?: number;
}

export function createMockContext(options: MockContextOptions = {}): ComponentFramework.Context<unknown> {
    const {
        webApi = createMockWebApi(),
        parentRecordId = 'test-parent-id',
        daysAhead = 7,
        maxItems = 10
    } = options;

    return {
        webAPI: webApi,
        parameters: {
            parentRecordId: {
                raw: parentRecordId,
                type: 'SingleLine.Text'
            },
            daysAhead: {
                raw: daysAhead,
                type: 'Whole.None'
            },
            maxItems: {
                raw: maxItems,
                type: 'Whole.None'
            }
        },
        mode: {
            isControlDisabled: false,
            isVisible: true,
            label: 'Due Dates Widget'
        },
        fluentDesignLanguage: {
            tokenToColorMap: {},
            isDarkTheme: false
        },
        formatting: {
            formatDateAsFilterStringInUTC: jest.fn((date: Date) => date.toISOString()),
            formatDateShort: jest.fn((date: Date) => date.toLocaleDateString()),
            formatDateLong: jest.fn((date: Date) => date.toLocaleDateString()),
            formatDateLongAbbreviated: jest.fn((date: Date) => date.toLocaleDateString()),
            formatDateYearMonth: jest.fn((date: Date) => date.toLocaleDateString()),
            formatTime: jest.fn((date: Date) => date.toLocaleTimeString()),
            formatCurrency: jest.fn((value: number) => `$${value.toFixed(2)}`),
            formatDecimal: jest.fn((value: number) => value.toFixed(2)),
            formatInteger: jest.fn((value: number) => value.toString()),
            getWeekOfYear: jest.fn(() => 1),
            parseDateFromInput: jest.fn((input: string) => new Date(input))
        },
        userSettings: {
            userId: 'test-user-id',
            userName: 'Test User',
            languageId: 1033,
            dateFormattingInfo: {
                amDesignator: 'AM',
                pmDesignator: 'PM',
                firstDayOfWeek: 0,
                shortDatePattern: 'M/d/yyyy',
                longDatePattern: 'dddd, MMMM d, yyyy'
            }
        }
    } as unknown as ComponentFramework.Context<unknown>;
}

// ─────────────────────────────────────────────────────────────────────────────
// Test Data Factories
// ─────────────────────────────────────────────────────────────────────────────

export interface MockEventDataOptions {
    id?: string;
    name?: string;
    dueDate?: Date;
    eventType?: string;
    eventTypeName?: string;
    /** Event status value (0-7) from sprk_eventstatus */
    eventStatus?: number;
    /** @deprecated Use eventStatus instead */
    statusCode?: number;
    isOverdue?: boolean;
}

/**
 * Event Status values for test data
 * Matches sprk_eventstatus optionset values
 */
export const TestEventStatus = {
    Draft: 0,
    Open: 1,
    Completed: 2,
    Closed: 3,
    OnHold: 4,
    Cancelled: 5,
    Reassigned: 6,
    Archived: 7
} as const;

/**
 * Event Status labels for test data
 */
const EventStatusLabels: Record<number, string> = {
    [TestEventStatus.Draft]: 'Draft',
    [TestEventStatus.Open]: 'Open',
    [TestEventStatus.Completed]: 'Completed',
    [TestEventStatus.Closed]: 'Closed',
    [TestEventStatus.OnHold]: 'On Hold',
    [TestEventStatus.Cancelled]: 'Cancelled',
    [TestEventStatus.Reassigned]: 'Reassigned',
    [TestEventStatus.Archived]: 'Archived'
};

export function createMockEventData(options: MockEventDataOptions = {}): Record<string, unknown> {
    const {
        id = 'event-id-1',
        name = 'Test Event',
        dueDate = new Date(),
        eventType = 'event-type-1',
        eventTypeName = 'Filing Deadline',
        eventStatus = TestEventStatus.Open, // Default to Open (1)
        statusCode // Deprecated, prefer eventStatus
    } = options;

    // Use eventStatus, fallback to statusCode for backward compatibility
    const status = eventStatus ?? statusCode ?? TestEventStatus.Open;
    const statusLabel = EventStatusLabels[status] ?? 'Unknown';

    return {
        sprk_eventid: id,
        sprk_eventname: name,
        sprk_duedate: dueDate.toISOString(),
        sprk_eventstatus: status,
        'sprk_eventstatus@OData.Community.Display.V1.FormattedValue': statusLabel,
        statecode: status === TestEventStatus.Archived ? 1 : 0, // Inactive only when Archived
        statuscode: statusCode, // Keep for backward compat in tests
        '_sprk_eventtype_ref_value': eventType,
        '_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue': eventTypeName,
        'statuscode@OData.Community.Display.V1.FormattedValue': statusLabel,
        '_ownerid_value': 'owner-id-1',
        '_ownerid_value@OData.Community.Display.V1.FormattedValue': 'Test Owner'
    };
}

export function createMockEventDataArray(count: number = 3): Record<string, unknown>[] {
    const today = new Date();
    const eventTypes = ['Filing Deadline', 'Hearing', 'Meeting', 'Deadline', 'Review'];

    return Array.from({ length: count }, (_, i) => {
        const dueDate = new Date(today);
        dueDate.setDate(today.getDate() + i - 1); // -1, 0, 1, 2... days from today

        return createMockEventData({
            id: `event-id-${i + 1}`,
            name: `Test Event ${i + 1}`,
            dueDate,
            eventTypeName: eventTypes[i % eventTypes.length]
        });
    });
}

// ─────────────────────────────────────────────────────────────────────────────
// Test Utilities
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Reset all Xrm mocks between tests
 */
export function resetXrmMocks(): void {
    (global as unknown as { Xrm: MockXrm }).Xrm = createMockXrm();
}

/**
 * Get the current mock Xrm instance
 */
export function getMockXrm(): MockXrm {
    return (global as unknown as { Xrm: MockXrm }).Xrm;
}

// Reset mocks before each test
beforeEach(() => {
    jest.clearAllMocks();
    resetXrmMocks();
});
