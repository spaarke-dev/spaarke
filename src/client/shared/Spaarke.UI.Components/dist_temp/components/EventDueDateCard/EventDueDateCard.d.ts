/**
 * EventDueDateCard - Displays an event due date card with color-coded urgency
 * Standards: ADR-012 (shared components), ADR-021 (Fluent v9 design tokens)
 */
import * as React from 'react';
export interface IEventDueDateCardProps {
    eventId: string;
    eventName: string;
    eventTypeName: string;
    dueDate: Date;
    daysUntilDue: number;
    isOverdue: boolean;
    eventTypeColor?: string;
    description?: string;
    assignedTo?: string;
    onClick?: (eventId: string) => void;
    isNavigating?: boolean;
}
export declare const EventDueDateCard: React.FC<IEventDueDateCardProps>;
//# sourceMappingURL=EventDueDateCard.d.ts.map