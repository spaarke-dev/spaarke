/**
 * DueDatesWidget Stories
 *
 * Storybook stories for visual testing of the DueDatesWidget PCF control components.
 * Task 058: Add Storybook stories and deploy Phase 5
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/058-deploy-phase5.poml
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme
} from "@fluentui/react-components";
import { EventListItem, IEventListItemProps } from "../../DueDatesWidget/control/components/EventListItem";
import { DateColumn, IDateColumnProps } from "../../DueDatesWidget/control/components/DateColumn";
import { EventTypeBadge, IEventTypeBadgeProps } from "../../DueDatesWidget/control/components/EventTypeBadge";
import { DaysUntilDueBadge, IDaysUntilDueBadgeProps } from "../../DueDatesWidget/control/components/DaysUntilDueBadge";
import { WidgetFooter, IWidgetFooterProps } from "../../DueDatesWidget/control/components/WidgetFooter";

// ─────────────────────────────────────────────────────────────────────────────
// Story Wrappers
// ─────────────────────────────────────────────────────────────────────────────

interface StoryWrapperProps {
    isDarkMode?: boolean;
    children: React.ReactNode;
}

const StoryWrapper: React.FC<StoryWrapperProps> = ({ isDarkMode = false, children }) => (
    <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
        <div
            style={{
                padding: "16px",
                backgroundColor: isDarkMode ? "#1f1f1f" : "#ffffff",
                minHeight: "100px"
            }}
        >
            {children}
        </div>
    </FluentProvider>
);

// ─────────────────────────────────────────────────────────────────────────────
// DateColumn Stories
// ─────────────────────────────────────────────────────────────────────────────

const dateColumnMeta: Meta<typeof DateColumn> = {
    title: "PCF/DueDatesWidget/DateColumn",
    component: DateColumn,
    parameters: {
        layout: "centered",
        docs: {
            description: {
                component: `
DateColumn displays the date in a vertical column format with large day number and day abbreviation.

**Features:**
- Large day number (1-31)
- Day abbreviation below (SUN, MON, TUE, etc.)
- Dark mode support via Fluent design tokens
                `,
            },
        },
    },
    tags: ["autodocs"],
    argTypes: {
        date: {
            control: "date",
            description: "The date to display",
        },
    },
};

export default dateColumnMeta;

type DateColumnStory = StoryObj<typeof DateColumn>;

const DateColumnWrapper: React.FC<IDateColumnProps & { isDarkMode?: boolean }> = ({
    isDarkMode = false,
    ...props
}) => (
    <StoryWrapper isDarkMode={isDarkMode}>
        <DateColumn {...props} />
    </StoryWrapper>
);

export const DateColumnDefault: DateColumnStory = {
    render: (args) => <DateColumnWrapper {...args} />,
    args: {
        date: new Date(),
    },
};

export const DateColumnMonday: DateColumnStory = {
    render: (args) => <DateColumnWrapper {...args} />,
    args: {
        date: new Date(2026, 1, 9), // Monday Feb 9, 2026
    },
};

export const DateColumnFriday: DateColumnStory = {
    render: (args) => <DateColumnWrapper {...args} />,
    args: {
        date: new Date(2026, 1, 13), // Friday Feb 13, 2026
    },
};

export const DateColumnDarkMode: DateColumnStory = {
    render: (args) => <DateColumnWrapper isDarkMode {...args} />,
    args: {
        date: new Date(),
    },
    parameters: {
        backgrounds: { default: "dark" },
    },
};

// ─────────────────────────────────────────────────────────────────────────────
// EventTypeBadge Stories
// ─────────────────────────────────────────────────────────────────────────────

export const EventTypeBadgeStories: Meta<typeof EventTypeBadge> = {
    title: "PCF/DueDatesWidget/EventTypeBadge",
    component: EventTypeBadge,
    parameters: {
        layout: "centered",
        docs: {
            description: {
                component: `
EventTypeBadge displays an event type with colored indicator and type name.

**Colors are mapped by keywords:**
- Hearing, Court, Trial = Yellow
- Filing, Patent, Submission = Green
- Regulatory, Review, Compliance = Purple
- Meeting, Conference, Call = Blue
- Deadline, Due, Expiration = Orange
- Urgent, Critical, Emergency = Red
                `,
            },
        },
    },
    tags: ["autodocs"],
};

const EventTypeBadgeWrapper: React.FC<IEventTypeBadgeProps & { isDarkMode?: boolean }> = ({
    isDarkMode = false,
    ...props
}) => (
    <StoryWrapper isDarkMode={isDarkMode}>
        <EventTypeBadge {...props} />
    </StoryWrapper>
);

export const EventTypeBadgeHearing: StoryObj<typeof EventTypeBadge> = {
    render: (args) => <EventTypeBadgeWrapper {...args} />,
    args: {
        typeName: "Hearing",
    },
};

export const EventTypeBadgeFilingDeadline: StoryObj<typeof EventTypeBadge> = {
    render: (args) => <EventTypeBadgeWrapper {...args} />,
    args: {
        typeName: "Filing Deadline",
    },
};

export const EventTypeBadgeRegulatoryReview: StoryObj<typeof EventTypeBadge> = {
    render: (args) => <EventTypeBadgeWrapper {...args} />,
    args: {
        typeName: "Regulatory Review",
    },
};

export const EventTypeBadgeMeeting: StoryObj<typeof EventTypeBadge> = {
    render: (args) => <EventTypeBadgeWrapper {...args} />,
    args: {
        typeName: "Meeting",
    },
};

export const EventTypeBadgeUrgent: StoryObj<typeof EventTypeBadge> = {
    render: (args) => <EventTypeBadgeWrapper {...args} />,
    args: {
        typeName: "Urgent Review",
    },
};

export const EventTypeBadgeIndicatorOnly: StoryObj<typeof EventTypeBadge> = {
    render: (args) => <EventTypeBadgeWrapper {...args} />,
    args: {
        typeName: "Hearing",
        indicatorOnly: true,
    },
};

export const EventTypeBadgeDarkMode: StoryObj<typeof EventTypeBadge> = {
    render: (args) => <EventTypeBadgeWrapper isDarkMode {...args} />,
    args: {
        typeName: "Hearing",
    },
    parameters: {
        backgrounds: { default: "dark" },
    },
};

export const EventTypeBadgeAllTypes: StoryObj<typeof EventTypeBadge> = {
    render: () => (
        <StoryWrapper>
            <div style={{ display: "flex", flexDirection: "column", gap: "12px" }}>
                <EventTypeBadge typeName="Hearing" />
                <EventTypeBadge typeName="Filing Deadline" />
                <EventTypeBadge typeName="Regulatory Review" />
                <EventTypeBadge typeName="Meeting" />
                <EventTypeBadge typeName="Patent Application" />
                <EventTypeBadge typeName="Urgent Review" />
                <EventTypeBadge typeName="Project Milestone" />
                <EventTypeBadge typeName="Unknown Type" />
            </div>
        </StoryWrapper>
    ),
};

// ─────────────────────────────────────────────────────────────────────────────
// DaysUntilDueBadge Stories
// ─────────────────────────────────────────────────────────────────────────────

export const DaysUntilDueBadgeStories: Meta<typeof DaysUntilDueBadge> = {
    title: "PCF/DueDatesWidget/DaysUntilDueBadge",
    component: DaysUntilDueBadge,
    parameters: {
        layout: "centered",
        docs: {
            description: {
                component: `
DaysUntilDueBadge displays a circular badge showing days until due with urgency-based coloring.

**Urgency Levels:**
- Overdue (negative days): Deep red
- Critical (0-1 days): Red
- Urgent (2-3 days): Dark orange
- Warning (4-7 days): Orange/marigold
- Normal (8+ days): Neutral gray
                `,
            },
        },
    },
    tags: ["autodocs"],
};

const DaysUntilDueBadgeWrapper: React.FC<IDaysUntilDueBadgeProps & { isDarkMode?: boolean }> = ({
    isDarkMode = false,
    ...props
}) => (
    <StoryWrapper isDarkMode={isDarkMode}>
        <DaysUntilDueBadge {...props} />
    </StoryWrapper>
);

export const DaysUntilDueBadgeToday: StoryObj<typeof DaysUntilDueBadge> = {
    render: (args) => <DaysUntilDueBadgeWrapper {...args} />,
    args: {
        daysUntilDue: 0,
        isOverdue: false,
    },
};

export const DaysUntilDueBadgeTomorrow: StoryObj<typeof DaysUntilDueBadge> = {
    render: (args) => <DaysUntilDueBadgeWrapper {...args} />,
    args: {
        daysUntilDue: 1,
        isOverdue: false,
    },
};

export const DaysUntilDueBadgeUrgent: StoryObj<typeof DaysUntilDueBadge> = {
    render: (args) => <DaysUntilDueBadgeWrapper {...args} />,
    args: {
        daysUntilDue: 3,
        isOverdue: false,
    },
};

export const DaysUntilDueBadgeWarning: StoryObj<typeof DaysUntilDueBadge> = {
    render: (args) => <DaysUntilDueBadgeWrapper {...args} />,
    args: {
        daysUntilDue: 5,
        isOverdue: false,
    },
};

export const DaysUntilDueBadgeNormal: StoryObj<typeof DaysUntilDueBadge> = {
    render: (args) => <DaysUntilDueBadgeWrapper {...args} />,
    args: {
        daysUntilDue: 14,
        isOverdue: false,
    },
};

export const DaysUntilDueBadgeOverdue: StoryObj<typeof DaysUntilDueBadge> = {
    render: (args) => <DaysUntilDueBadgeWrapper {...args} />,
    args: {
        daysUntilDue: 2,
        isOverdue: true,
    },
};

export const DaysUntilDueBadgeSmall: StoryObj<typeof DaysUntilDueBadge> = {
    render: (args) => <DaysUntilDueBadgeWrapper {...args} />,
    args: {
        daysUntilDue: 3,
        isOverdue: false,
        size: "small",
    },
};

export const DaysUntilDueBadgeDarkMode: StoryObj<typeof DaysUntilDueBadge> = {
    render: (args) => <DaysUntilDueBadgeWrapper isDarkMode {...args} />,
    args: {
        daysUntilDue: 3,
        isOverdue: false,
    },
    parameters: {
        backgrounds: { default: "dark" },
    },
};

export const DaysUntilDueBadgeAllUrgencyLevels: StoryObj<typeof DaysUntilDueBadge> = {
    render: () => (
        <StoryWrapper>
            <div style={{ display: "flex", flexDirection: "column", gap: "12px", alignItems: "flex-start" }}>
                <div style={{ display: "flex", alignItems: "center", gap: "16px" }}>
                    <DaysUntilDueBadge daysUntilDue={2} isOverdue={true} />
                    <span>Overdue (2 days ago)</span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "16px" }}>
                    <DaysUntilDueBadge daysUntilDue={0} isOverdue={false} />
                    <span>Due Today (critical)</span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "16px" }}>
                    <DaysUntilDueBadge daysUntilDue={1} isOverdue={false} />
                    <span>Due Tomorrow (critical)</span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "16px" }}>
                    <DaysUntilDueBadge daysUntilDue={3} isOverdue={false} />
                    <span>3 days (urgent)</span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "16px" }}>
                    <DaysUntilDueBadge daysUntilDue={5} isOverdue={false} />
                    <span>5 days (warning)</span>
                </div>
                <div style={{ display: "flex", alignItems: "center", gap: "16px" }}>
                    <DaysUntilDueBadge daysUntilDue={14} isOverdue={false} />
                    <span>14 days (normal)</span>
                </div>
            </div>
        </StoryWrapper>
    ),
};

// ─────────────────────────────────────────────────────────────────────────────
// EventListItem Stories
// ─────────────────────────────────────────────────────────────────────────────

export const EventListItemStories: Meta<typeof EventListItem> = {
    title: "PCF/DueDatesWidget/EventListItem",
    component: EventListItem,
    parameters: {
        layout: "centered",
        docs: {
            description: {
                component: `
EventListItem displays a single event row in the list layout with:
- Date column (day number + abbreviation)
- Event type badge (colored indicator + type name)
- Event name
- Description (optional, truncated)
- Days-until-due badge

Supports click action and keyboard navigation.
                `,
            },
        },
    },
    tags: ["autodocs"],
};

const handleClick = action("onClick");

const EventListItemWrapper: React.FC<IEventListItemProps & { isDarkMode?: boolean }> = ({
    isDarkMode = false,
    ...props
}) => (
    <StoryWrapper isDarkMode={isDarkMode}>
        <div style={{ width: "400px" }}>
            <EventListItem {...props} />
        </div>
    </StoryWrapper>
);

// Helper to create future/past dates
const addDays = (days: number): Date => {
    const date = new Date();
    date.setDate(date.getDate() + days);
    return date;
};

export const EventListItemDefault: StoryObj<typeof EventListItem> = {
    render: (args) => <EventListItemWrapper {...args} />,
    args: {
        id: "event-1",
        name: "Quarterly Filing Deadline",
        dueDate: addDays(5),
        eventType: "filing-deadline",
        eventTypeName: "Filing Deadline",
        description: "Submit quarterly compliance report to regulatory board",
        daysUntilDue: 5,
        isOverdue: false,
        onClick: handleClick,
    },
};

export const EventListItemHearing: StoryObj<typeof EventListItem> = {
    render: (args) => <EventListItemWrapper {...args} />,
    args: {
        id: "event-2",
        name: "Motion Hearing",
        dueDate: addDays(3),
        eventType: "hearing",
        eventTypeName: "Hearing",
        description: "Hearing on motion to dismiss",
        daysUntilDue: 3,
        isOverdue: false,
        onClick: handleClick,
    },
};

export const EventListItemOverdue: StoryObj<typeof EventListItem> = {
    render: (args) => <EventListItemWrapper {...args} />,
    args: {
        id: "event-3",
        name: "Contract Review",
        dueDate: addDays(-2),
        eventType: "review",
        eventTypeName: "Regulatory Review",
        description: "Review vendor contract for compliance",
        daysUntilDue: 2,
        isOverdue: true,
        onClick: handleClick,
    },
};

export const EventListItemDueToday: StoryObj<typeof EventListItem> = {
    render: (args) => <EventListItemWrapper {...args} />,
    args: {
        id: "event-4",
        name: "Client Meeting",
        dueDate: new Date(),
        eventType: "meeting",
        eventTypeName: "Meeting",
        description: "Discuss project timeline with client",
        daysUntilDue: 0,
        isOverdue: false,
        onClick: handleClick,
    },
};

export const EventListItemNoDescription: StoryObj<typeof EventListItem> = {
    render: (args) => <EventListItemWrapper {...args} />,
    args: {
        id: "event-5",
        name: "Patent Application Due",
        dueDate: addDays(10),
        eventType: "patent",
        eventTypeName: "Patent Application",
        daysUntilDue: 10,
        isOverdue: false,
        onClick: handleClick,
    },
};

export const EventListItemLongDescription: StoryObj<typeof EventListItem> = {
    render: (args) => <EventListItemWrapper {...args} />,
    args: {
        id: "event-6",
        name: "Annual Compliance Audit",
        dueDate: addDays(7),
        eventType: "audit",
        eventTypeName: "Compliance Audit",
        description: "This is a very long description that should be truncated when it exceeds the available width of the container. The description provides detailed context about the event.",
        daysUntilDue: 7,
        isOverdue: false,
        onClick: handleClick,
    },
};

export const EventListItemNavigating: StoryObj<typeof EventListItem> = {
    render: (args) => <EventListItemWrapper {...args} />,
    args: {
        id: "event-7",
        name: "Opening Event...",
        dueDate: addDays(3),
        eventType: "meeting",
        eventTypeName: "Meeting",
        description: "This item shows the loading state",
        daysUntilDue: 3,
        isOverdue: false,
        onClick: handleClick,
        isNavigating: true,
    },
};

export const EventListItemDarkMode: StoryObj<typeof EventListItem> = {
    render: (args) => <EventListItemWrapper isDarkMode {...args} />,
    args: {
        id: "event-8",
        name: "Quarterly Filing Deadline",
        dueDate: addDays(5),
        eventType: "filing-deadline",
        eventTypeName: "Filing Deadline",
        description: "Submit quarterly compliance report",
        daysUntilDue: 5,
        isOverdue: false,
        onClick: handleClick,
    },
    parameters: {
        backgrounds: { default: "dark" },
    },
};

export const EventListItemList: StoryObj<typeof EventListItem> = {
    render: () => (
        <StoryWrapper>
            <div style={{ width: "400px", display: "flex", flexDirection: "column" }}>
                <EventListItem
                    id="e1"
                    name="Motion Hearing"
                    dueDate={addDays(1)}
                    eventType="hearing"
                    eventTypeName="Hearing"
                    daysUntilDue={1}
                    isOverdue={false}
                    onClick={handleClick}
                />
                <EventListItem
                    id="e2"
                    name="Contract Review (Overdue)"
                    dueDate={addDays(-2)}
                    eventType="review"
                    eventTypeName="Review"
                    daysUntilDue={2}
                    isOverdue={true}
                    onClick={handleClick}
                />
                <EventListItem
                    id="e3"
                    name="Filing Deadline"
                    dueDate={addDays(5)}
                    eventType="filing"
                    eventTypeName="Filing Deadline"
                    description="Q1 regulatory filing"
                    daysUntilDue={5}
                    isOverdue={false}
                    onClick={handleClick}
                />
                <EventListItem
                    id="e4"
                    name="Client Meeting"
                    dueDate={addDays(14)}
                    eventType="meeting"
                    eventTypeName="Meeting"
                    description="Quarterly business review"
                    daysUntilDue={14}
                    isOverdue={false}
                    onClick={handleClick}
                />
            </div>
        </StoryWrapper>
    ),
};

// ─────────────────────────────────────────────────────────────────────────────
// WidgetFooter Stories
// ─────────────────────────────────────────────────────────────────────────────

export const WidgetFooterStories: Meta<typeof WidgetFooter> = {
    title: "PCF/DueDatesWidget/WidgetFooter",
    component: WidgetFooter,
    parameters: {
        layout: "centered",
        docs: {
            description: {
                component: `
WidgetFooter displays the "All Events" link with optional count badge.
Shows badge when total events exceed displayed count and threshold.
                `,
            },
        },
    },
    tags: ["autodocs"],
};

const handleViewAll = action("onViewAllClick");

const WidgetFooterWrapper: React.FC<IWidgetFooterProps & { isDarkMode?: boolean }> = ({
    isDarkMode = false,
    ...props
}) => (
    <StoryWrapper isDarkMode={isDarkMode}>
        <div style={{ width: "350px", border: "1px solid #ccc", borderRadius: "4px" }}>
            <WidgetFooter {...props} />
        </div>
    </StoryWrapper>
);

export const WidgetFooterDefault: StoryObj<typeof WidgetFooter> = {
    render: (args) => <WidgetFooterWrapper {...args} />,
    args: {
        totalEventCount: 5,
        displayedCount: 5,
        onViewAllClick: handleViewAll,
    },
};

export const WidgetFooterWithBadge: StoryObj<typeof WidgetFooter> = {
    render: (args) => <WidgetFooterWrapper {...args} />,
    args: {
        totalEventCount: 25,
        displayedCount: 5,
        onViewAllClick: handleViewAll,
    },
};

export const WidgetFooterManyMore: StoryObj<typeof WidgetFooter> = {
    render: (args) => <WidgetFooterWrapper {...args} />,
    args: {
        totalEventCount: 100,
        displayedCount: 5,
        onViewAllClick: handleViewAll,
    },
};

export const WidgetFooterDarkMode: StoryObj<typeof WidgetFooter> = {
    render: (args) => <WidgetFooterWrapper isDarkMode {...args} />,
    args: {
        totalEventCount: 25,
        displayedCount: 5,
        onViewAllClick: handleViewAll,
    },
    parameters: {
        backgrounds: { default: "dark" },
    },
};
