/**
 * Event Filter Service
 *
 * Provides filtering logic for actionable events in DueDatesWidget.
 * Builds OData queries for Dataverse WebAPI.
 *
 * ADR Compliance:
 * - Uses WebAPI for Dataverse queries (not SDK)
 * - No hard-coded entity names (configurable)
 *
 * Event Status Field Migration:
 * - Uses custom `sprk_eventstatus` field (values 0-7)
 * - OOB statecode/statuscode are deprecated and only used for Archive functionality
 *
 * @see projects/events-workspace-apps-UX-r1/notes/design/statecode-statuscode-migration.md
 */

// ─────────────────────────────────────────────────────────────────────────────
// Types
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Event entity schema names from spec.md
 */
export const EventSchemaNames = {
    entityName: "sprk_event",
    entitySetName: "sprk_events",
    fields: {
        eventId: "sprk_eventid",
        eventName: "sprk_eventname",
        dueDate: "sprk_duedate",
        baseDate: "sprk_basedate",
        finalDueDate: "sprk_finalduedate",
        /** Custom Event Status field - primary status indicator */
        eventStatus: "sprk_eventstatus",
        /** @deprecated Use eventStatus instead. Kept for Archive detection. */
        stateCode: "statecode",
        /** @deprecated Use eventStatus instead. */
        statusCode: "statuscode",
        priority: "sprk_priority",
        eventType: "_sprk_eventtype_ref_value",
        eventTypeName: "_sprk_eventtype_ref_value",
        owner: "ownerid",
        ownerName: "_ownerid_value",
        regardingRecordType: "sprk_regardingrecordtype",
        regardingRecordName: "sprk_regardingrecordname",
        regardingRecordId: "sprk_regardingrecordid",
        createdOn: "createdon",
        modifiedOn: "modifiedon"
    }
} as const;

/**
 * Event Status values (sprk_eventstatus custom field)
 * Matches Dataverse optionset values
 *
 * Actionable statuses: Draft (0), Open (1), On Hold (4)
 * Terminal statuses: Completed (2), Closed (3), Cancelled (5), Reassigned (6), Archived (7)
 */
export const EventStatus = {
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
 * @deprecated Use EventStatus instead
 */
export const EventStatusReason = EventStatus;

/**
 * State Code values
 * Only used for Archive functionality (statecode=1 when Archived)
 */
export const EventStateCode = {
    Active: 0,
    Inactive: 1
} as const;

/**
 * Actionable event status values (not terminal)
 * These events require user action
 */
export const ACTIONABLE_EVENT_STATUSES = [
    EventStatus.Draft,
    EventStatus.Open,
    EventStatus.OnHold
] as const;

/**
 * @deprecated Use ACTIONABLE_EVENT_STATUSES instead
 */
export const ACTIONABLE_STATUS_CODES = ACTIONABLE_EVENT_STATUSES;

/**
 * Event data returned from Dataverse
 */
export interface IEventData {
    sprk_eventid: string;
    sprk_eventname: string;
    sprk_duedate: string | null;
    /** Custom Event Status (0-7) - primary status indicator */
    sprk_eventstatus?: number;
    /** @deprecated Use sprk_eventstatus instead. Kept for Archive detection. */
    statecode?: number;
    /** @deprecated Use sprk_eventstatus instead. */
    statuscode?: number;
    sprk_priority?: number;
    "_sprk_eventtype_ref_value"?: string;
    "_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue"?: string;
    /** Formatted value for sprk_eventstatus */
    "sprk_eventstatus@OData.Community.Display.V1.FormattedValue"?: string;
    /** @deprecated Use sprk_eventstatus formatted value instead */
    "statuscode@OData.Community.Display.V1.FormattedValue"?: string;
    "_ownerid_value"?: string;
    "_ownerid_value@OData.Community.Display.V1.FormattedValue"?: string;
    sprk_regardingrecordid?: string;
    sprk_regardingrecordname?: string;
}

/**
 * Processed event item for UI display
 */
export interface IEventItem {
    id: string;
    name: string;
    dueDate: Date;
    eventType: string;
    eventTypeName: string;
    description?: string;
    /** Event Status value (0-7) from sprk_eventstatus */
    eventStatus: number;
    /** Formatted event status name */
    statusName: string;
    priority?: number;
    ownerId?: string;
    ownerName?: string;
    isOverdue: boolean;
    daysUntilDue: number;
}

/**
 * Filter parameters for event queries
 */
export interface IEventFilterParams {
    /** Parent record ID to filter events (Matter/Project ID) */
    parentRecordId: string;
    /** Number of days ahead to include (default 7) */
    daysAhead: number;
    /** Maximum number of items to return (default 10) */
    maxItems: number;
    /** Include overdue events (default true) */
    includeOverdue?: boolean;
}

// ─────────────────────────────────────────────────────────────────────────────
// Date Utilities
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Get today's date at midnight (start of day) in UTC
 */
export function getTodayStart(): Date {
    const today = new Date();
    today.setUTCHours(0, 0, 0, 0);
    return today;
}

/**
 * Get a date N days from today at end of day
 */
export function getDateAhead(days: number): Date {
    const date = getTodayStart();
    date.setUTCDate(date.getUTCDate() + days);
    date.setUTCHours(23, 59, 59, 999);
    return date;
}

/**
 * Format date for OData filter (ISO 8601)
 */
export function formatDateForOData(date: Date): string {
    return date.toISOString();
}

/**
 * Calculate days until due date (negative if overdue)
 */
export function calculateDaysUntilDue(dueDate: Date): number {
    const today = getTodayStart();
    const due = new Date(dueDate);
    due.setUTCHours(0, 0, 0, 0);

    const diffTime = due.getTime() - today.getTime();
    const diffDays = Math.ceil(diffTime / (1000 * 60 * 60 * 24));

    return diffDays;
}

// ─────────────────────────────────────────────────────────────────────────────
// OData Query Builder
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Build OData query URL for upcoming events
 *
 * Query logic:
 * 1. Filter by regarding record ID (context-specific)
 * 2. Filter by actionable event status (sprk_eventstatus: Draft, Open, On Hold)
 * 3. Filter by date: overdue OR due within daysAhead
 * 4. Sort by due date ascending (most urgent first)
 * 5. Limit to maxItems
 */
export function buildUpcomingEventsQuery(params: IEventFilterParams): string {
    const {
        parentRecordId,
        daysAhead,
        maxItems,
        includeOverdue = true
    } = params;

    const schema = EventSchemaNames;
    const today = getTodayStart();
    const endDate = getDateAhead(daysAhead);

    // Build $select - only fields we need
    const selectFields = [
        schema.fields.eventId,
        schema.fields.eventName,
        schema.fields.dueDate,
        schema.fields.eventStatus,  // Primary status field
        schema.fields.stateCode,    // Keep for Archive detection
        schema.fields.priority,
        schema.fields.eventTypeName,
        schema.fields.regardingRecordId,
        schema.fields.regardingRecordName,
        schema.fields.ownerName
    ];
    const $select = selectFields.join(",");

    // Build filter conditions
    const filterConditions: string[] = [];

    // 1. Filter by parent record ID (if provided)
    if (parentRecordId) {
        filterConditions.push(`${schema.fields.regardingRecordId} eq '${parentRecordId}'`);
    }

    // 2. Filter by actionable event statuses (Draft=0, Open=1, On Hold=4)
    // Using 'or' for multiple values on sprk_eventstatus
    const statusFilter = ACTIONABLE_EVENT_STATUSES
        .map(code => `${schema.fields.eventStatus} eq ${code}`)
        .join(" or ");
    filterConditions.push(`(${statusFilter})`);

    // 3. Filter by date range:
    //    - Include overdue events (due date in past) if includeOverdue is true
    //    - Include events due within daysAhead window
    const todayStr = formatDateForOData(today);
    const endDateStr = formatDateForOData(endDate);

    // Due date must exist
    filterConditions.push(`${schema.fields.dueDate} ne null`);

    if (includeOverdue) {
        // Include overdue (before today) OR within window (between today and endDate)
        filterConditions.push(
            `(${schema.fields.dueDate} lt ${todayStr} or ${schema.fields.dueDate} le ${endDateStr})`
        );
    } else {
        // Only within window
        filterConditions.push(`${schema.fields.dueDate} ge ${todayStr}`);
        filterConditions.push(`${schema.fields.dueDate} le ${endDateStr}`);
    }

    const $filter = filterConditions.join(" and ");

    // 4. Order by due date ascending (most urgent first)
    // Overdue items will naturally appear first since they have earlier dates
    const $orderby = `${schema.fields.dueDate} asc`;

    // 5. Limit results
    const $top = maxItems;

    // Build full query string
    const queryParts = [
        `$select=${encodeURIComponent($select)}`,
        `$filter=${encodeURIComponent($filter)}`,
        `$orderby=${encodeURIComponent($orderby)}`,
        `$top=${$top}`
    ];

    return `${schema.entitySetName}?${queryParts.join("&")}`;
}

// ─────────────────────────────────────────────────────────────────────────────
// Data Transformation
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Event Status labels for display (fallback if formatted value not available)
 */
const EVENT_STATUS_LABELS: Record<number, string> = {
    [EventStatus.Draft]: "Draft",
    [EventStatus.Open]: "Open",
    [EventStatus.Completed]: "Completed",
    [EventStatus.Closed]: "Closed",
    [EventStatus.OnHold]: "On Hold",
    [EventStatus.Cancelled]: "Cancelled",
    [EventStatus.Reassigned]: "Reassigned",
    [EventStatus.Archived]: "Archived"
};

/**
 * Get status label for an event status value
 */
export function getEventStatusLabel(status: number): string {
    return EVENT_STATUS_LABELS[status] ?? "Unknown";
}

/**
 * Check if an event status is actionable (not terminal)
 */
export function isEventStatusActionable(status: number): boolean {
    return (ACTIONABLE_EVENT_STATUSES as readonly number[]).includes(status);
}

/**
 * Transform raw Dataverse data to UI-friendly format
 */
export function transformEventData(rawEvent: IEventData): IEventItem | null {
    // Skip events without due date (shouldn't happen due to filter, but be safe)
    if (!rawEvent.sprk_duedate) {
        return null;
    }

    const dueDate = new Date(rawEvent.sprk_duedate);
    const daysUntilDue = calculateDaysUntilDue(dueDate);
    const isOverdue = daysUntilDue < 0;

    // Get event status - prefer sprk_eventstatus, fallback to statuscode for backward compat
    const eventStatus = rawEvent.sprk_eventstatus ?? rawEvent.statuscode ?? EventStatus.Open;

    // Get status name - prefer formatted value, fallback to label lookup
    const statusName =
        rawEvent["sprk_eventstatus@OData.Community.Display.V1.FormattedValue"] ||
        rawEvent["statuscode@OData.Community.Display.V1.FormattedValue"] ||
        getEventStatusLabel(eventStatus);

    return {
        id: rawEvent.sprk_eventid,
        name: rawEvent.sprk_eventname || "(Unnamed Event)",
        dueDate,
        eventType: rawEvent["_sprk_eventtype_ref_value"] || "",
        eventTypeName: rawEvent["_sprk_eventtype_ref_value@OData.Community.Display.V1.FormattedValue"] || "Event",
        eventStatus,
        statusName,
        priority: rawEvent.sprk_priority,
        ownerId: rawEvent["_ownerid_value"],
        ownerName: rawEvent["_ownerid_value@OData.Community.Display.V1.FormattedValue"],
        isOverdue,
        daysUntilDue
    };
}

/**
 * Transform array of raw events, filtering out nulls
 */
export function transformEventDataArray(rawEvents: IEventData[]): IEventItem[] {
    return rawEvents
        .map(transformEventData)
        .filter((event): event is IEventItem => event !== null);
}

// ─────────────────────────────────────────────────────────────────────────────
// WebAPI Service
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Result from fetchUpcomingEventsWithCount
 */
export interface IFetchEventsResult {
    /** List of events (limited by maxItems) */
    events: IEventItem[];
    /** Total count of matching events (before maxItems limit) */
    totalCount: number;
}

/**
 * Build OData query for total count (without $top limit)
 */
export function buildEventCountQuery(params: IEventFilterParams): string {
    const {
        parentRecordId,
        daysAhead,
        includeOverdue = true
    } = params;

    const schema = EventSchemaNames;
    const today = getTodayStart();
    const endDate = getDateAhead(daysAhead);

    // Build filter conditions (same as buildUpcomingEventsQuery but without $top)
    const filterConditions: string[] = [];

    // 1. Filter by parent record ID (if provided)
    if (parentRecordId) {
        filterConditions.push(`${schema.fields.regardingRecordId} eq '${parentRecordId}'`);
    }

    // 2. Filter by actionable event statuses (Draft=0, Open=1, On Hold=4)
    const statusFilter = ACTIONABLE_EVENT_STATUSES
        .map(code => `${schema.fields.eventStatus} eq ${code}`)
        .join(" or ");
    filterConditions.push(`(${statusFilter})`);

    // 3. Filter by date range
    const todayStr = formatDateForOData(today);
    const endDateStr = formatDateForOData(endDate);
    filterConditions.push(`${schema.fields.dueDate} ne null`);

    if (includeOverdue) {
        filterConditions.push(
            `(${schema.fields.dueDate} lt ${todayStr} or ${schema.fields.dueDate} le ${endDateStr})`
        );
    } else {
        filterConditions.push(`${schema.fields.dueDate} ge ${todayStr}`);
        filterConditions.push(`${schema.fields.dueDate} le ${endDateStr}`);
    }

    const $filter = filterConditions.join(" and ");

    // Only select ID for count (minimize data transfer)
    const $select = schema.fields.eventId;

    return `${schema.entitySetName}?$select=${encodeURIComponent($select)}&$filter=${encodeURIComponent($filter)}`;
}

/**
 * Fetch upcoming events from Dataverse WebAPI
 * Uses PCF context.webAPI for authenticated requests
 */
export async function fetchUpcomingEvents(
    webAPI: ComponentFramework.WebApi,
    params: IEventFilterParams
): Promise<IEventItem[]> {
    const queryUrl = buildUpcomingEventsQuery(params);

    try {
        // Use retrieveMultipleRecords which handles auth automatically
        const result = await webAPI.retrieveMultipleRecords(
            EventSchemaNames.entityName,
            `?${queryUrl.split("?")[1]}` // Extract query string portion
        );

        const rawEvents = result.entities as unknown as IEventData[];
        return transformEventDataArray(rawEvents);
    } catch (error) {
        console.error("[EventFilterService] Error fetching events:", error);
        throw error;
    }
}

/**
 * Fetch upcoming events with total count
 * Makes two queries: one for events (with $top) and one for count (without $top)
 */
export async function fetchUpcomingEventsWithCount(
    webAPI: ComponentFramework.WebApi,
    params: IEventFilterParams
): Promise<IFetchEventsResult> {
    const eventsQueryUrl = buildUpcomingEventsQuery(params);
    const countQueryUrl = buildEventCountQuery(params);

    try {
        // Execute both queries in parallel
        const [eventsResult, countResult] = await Promise.all([
            webAPI.retrieveMultipleRecords(
                EventSchemaNames.entityName,
                `?${eventsQueryUrl.split("?")[1]}`
            ),
            webAPI.retrieveMultipleRecords(
                EventSchemaNames.entityName,
                `?${countQueryUrl.split("?")[1]}`
            )
        ]);

        const rawEvents = eventsResult.entities as unknown as IEventData[];
        const events = transformEventDataArray(rawEvents);
        const totalCount = countResult.entities.length;

        return { events, totalCount };
    } catch (error) {
        console.error("[EventFilterService] Error fetching events with count:", error);
        throw error;
    }
}
