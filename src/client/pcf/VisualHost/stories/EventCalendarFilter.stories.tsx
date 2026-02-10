/**
 * EventCalendarFilter Stories
 *
 * Storybook stories for visual testing of the EventCalendarFilter PCF control.
 * Task 009: Added stories for all states (empty, with events, selected, range)
 *
 * @see projects/events-workspace-apps-UX-r1/tasks/009-storybook-and-deploy-phase1.poml
 */

import * as React from "react";
import type { Meta, StoryObj } from "@storybook/react";
import { action } from "@storybook/addon-actions";
import {
    FluentProvider,
    webLightTheme,
    webDarkTheme
} from "@fluentui/react-components";
import { EventCalendarFilterRoot, IEventCalendarFilterRootProps } from "../../EventCalendarFilter/control/components/EventCalendarFilterRoot";
import { IInputs } from "../../EventCalendarFilter/control/generated/ManifestTypes";

/**
 * Mock PCF context for Storybook
 * Provides minimal implementation of ComponentFramework.Context
 */
const createMockContext = (
    width: number = 280,
    height: number = 450
): ComponentFramework.Context<IInputs> => ({
    mode: {
        allocatedWidth: width,
        allocatedHeight: height,
        isControlDisabled: false,
        isVisible: true,
        label: "Event Calendar Filter",
        setControlState: () => {},
        setFullScreen: () => {},
        trackContainerResize: () => {},
    } as unknown as ComponentFramework.Mode,
    parameters: {} as IInputs,
    resources: {
        getString: (key: string) => key,
        getResource: () => "",
    } as unknown as ComponentFramework.Resources,
    updatedProperties: [],
    userSettings: {
        dateFormattingInfo: {
            amDesignator: "AM",
            pmDesignator: "PM",
            abbreviatedDayNames: ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"],
            abbreviatedMonthNames: ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"],
            dayNames: ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"],
            monthNames: ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"],
            firstDayOfWeek: 0,
            longDatePattern: "dddd, MMMM d, yyyy",
            shortDatePattern: "M/d/yyyy",
            shortTimePattern: "h:mm tt",
            longTimePattern: "h:mm:ss tt",
            timeSeparator: ":",
            dateSeparator: "/",
            calendar: { minSupportedDateTime: new Date(1900, 0, 1), maxSupportedDateTime: new Date(2099, 11, 31) }
        } as unknown as ComponentFramework.UserSettings["dateFormattingInfo"],
        isRTL: false,
        languageId: 1033,
        numberFormattingInfo: {} as ComponentFramework.UserSettings["numberFormattingInfo"],
        securityRoles: [],
        userId: "mock-user-id",
        userName: "Mock User",
    },
    client: {
        disableScroll: false,
        getFormFactor: () => 1,
        getClient: () => "Web",
        isOffline: () => false,
        isNetworkAvailable: () => true,
    } as unknown as ComponentFramework.Client,
    device: {} as ComponentFramework.Device,
    factory: {} as unknown as ComponentFramework.Factory,
    formatting: {} as unknown as ComponentFramework.Formatting,
    navigation: {} as unknown as ComponentFramework.Navigation,
    utils: {} as unknown as ComponentFramework.Utility,
    webAPI: {} as unknown as ComponentFramework.WebApi,
});

/**
 * Wrapper component for Storybook that provides FluentProvider
 */
interface StoryWrapperProps extends Omit<IEventCalendarFilterRootProps, "context"> {
    isDarkMode?: boolean;
    width?: number;
    height?: number;
}

const StoryWrapper: React.FC<StoryWrapperProps> = ({
    isDarkMode = false,
    width = 280,
    height = 450,
    ...props
}) => {
    const context = React.useMemo(
        () => createMockContext(width, height),
        [width, height]
    );

    return (
        <FluentProvider theme={isDarkMode ? webDarkTheme : webLightTheme}>
            <div
                style={{
                    width,
                    height,
                    padding: "16px",
                    backgroundColor: isDarkMode ? "#1f1f1f" : "#ffffff",
                }}
            >
                <EventCalendarFilterRoot
                    context={context}
                    {...props}
                />
            </div>
        </FluentProvider>
    );
};

const meta: Meta<typeof StoryWrapper> = {
    title: "PCF/EventCalendarFilter",
    component: StoryWrapper,
    parameters: {
        layout: "centered",
        docs: {
            description: {
                component: `
EventCalendarFilter is a multi-month calendar control for filtering events by date.

**Features:**
- Multi-month vertical stack (3 months visible)
- Single date selection with click
- Range selection with Shift+click
- Event indicators (dots on dates with events)
- Dark mode support via Fluent UI tokens
                `,
            },
        },
    },
    tags: ["autodocs"],
    argTypes: {
        isDarkMode: {
            control: "boolean",
            description: "Toggle dark mode theme",
            defaultValue: false,
        },
        width: {
            control: { type: "number", min: 200, max: 400 },
            description: "Control width in pixels",
            defaultValue: 280,
        },
        height: {
            control: { type: "number", min: 300, max: 600 },
            description: "Control height in pixels",
            defaultValue: 450,
        },
        displayMode: {
            control: "select",
            options: ["month", "multiMonth"],
            description: "Display mode (single month or multi-month)",
            defaultValue: "multiMonth",
        },
    },
};

export default meta;
type Story = StoryObj<typeof StoryWrapper>;

const handleFilterOutput = action("onFilterOutputChange");
const handleSelectedDate = action("onSelectedDateChange");

/**
 * Generate sample event dates for the current and next months
 */
const generateEventDatesJson = (): string => {
    const today = new Date();
    const year = today.getFullYear();
    const month = today.getMonth();

    const events = [
        // Current month
        { date: `${year}-${String(month + 1).padStart(2, "0")}-03`, count: 2 },
        { date: `${year}-${String(month + 1).padStart(2, "0")}-07`, count: 5 },
        { date: `${year}-${String(month + 1).padStart(2, "0")}-12`, count: 1 },
        { date: `${year}-${String(month + 1).padStart(2, "0")}-15`, count: 3 },
        { date: `${year}-${String(month + 1).padStart(2, "0")}-22`, count: 8 },
        { date: `${year}-${String(month + 1).padStart(2, "0")}-28`, count: 2 },
        // Next month
        { date: `${year}-${String(month + 2).padStart(2, "0")}-05`, count: 4 },
        { date: `${year}-${String(month + 2).padStart(2, "0")}-10`, count: 1 },
        { date: `${year}-${String(month + 2).padStart(2, "0")}-18`, count: 6 },
        { date: `${year}-${String(month + 2).padStart(2, "0")}-25`, count: 3 },
    ];

    return JSON.stringify(events);
};

/**
 * Default state - Empty calendar with no events
 */
export const Default: Story = {
    args: {
        displayMode: "multiMonth",
        onFilterOutputChange: handleFilterOutput,
        onSelectedDateChange: handleSelectedDate,
    },
};

/**
 * Calendar with event indicators
 */
export const WithEvents: Story = {
    args: {
        displayMode: "multiMonth",
        eventDatesJson: generateEventDatesJson(),
        onFilterOutputChange: handleFilterOutput,
        onSelectedDateChange: handleSelectedDate,
    },
};

/**
 * Single month display mode
 */
export const SingleMonth: Story = {
    args: {
        displayMode: "month",
        eventDatesJson: generateEventDatesJson(),
        onFilterOutputChange: handleFilterOutput,
        onSelectedDateChange: handleSelectedDate,
    },
};

/**
 * High density - Many events across dates
 */
export const HighDensity: Story = {
    args: {
        displayMode: "multiMonth",
        eventDatesJson: (() => {
            const today = new Date();
            const year = today.getFullYear();
            const month = today.getMonth();
            const events = [];
            for (let i = 1; i <= 28; i++) {
                if (i % 2 === 0) {
                    events.push({
                        date: `${year}-${String(month + 1).padStart(2, "0")}-${String(i).padStart(2, "0")}`,
                        count: Math.floor(Math.random() * 10) + 1,
                    });
                }
            }
            return JSON.stringify(events);
        })(),
        onFilterOutputChange: handleFilterOutput,
        onSelectedDateChange: handleSelectedDate,
    },
};

/**
 * Dark mode theme
 */
export const DarkMode: Story = {
    args: {
        isDarkMode: true,
        displayMode: "multiMonth",
        eventDatesJson: generateEventDatesJson(),
        onFilterOutputChange: handleFilterOutput,
        onSelectedDateChange: handleSelectedDate,
    },
    parameters: {
        backgrounds: {
            default: "dark",
        },
    },
};

/**
 * Dark mode - Empty calendar
 */
export const DarkModeEmpty: Story = {
    args: {
        isDarkMode: true,
        displayMode: "multiMonth",
        onFilterOutputChange: handleFilterOutput,
        onSelectedDateChange: handleSelectedDate,
    },
    parameters: {
        backgrounds: {
            default: "dark",
        },
    },
};

/**
 * Narrow width (sidebar scenario)
 */
export const NarrowWidth: Story = {
    args: {
        width: 220,
        height: 400,
        displayMode: "multiMonth",
        eventDatesJson: generateEventDatesJson(),
        onFilterOutputChange: handleFilterOutput,
        onSelectedDateChange: handleSelectedDate,
    },
};

/**
 * Wide width (panel scenario)
 */
export const WideWidth: Story = {
    args: {
        width: 350,
        height: 500,
        displayMode: "multiMonth",
        eventDatesJson: generateEventDatesJson(),
        onFilterOutputChange: handleFilterOutput,
        onSelectedDateChange: handleSelectedDate,
    },
};
