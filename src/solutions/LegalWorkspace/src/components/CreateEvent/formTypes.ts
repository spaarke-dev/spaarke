/**
 * formTypes.ts
 * Form state types for Create New Event wizard.
 */

export interface ICreateEventFormState {
  /** Event Name — free text (required). Maps to sprk_eventname. */
  eventName: string;
  /** sprk_eventtype_ref lookup — GUID of the selected record. */
  eventTypeId: string;
  /** Display name of the selected event type. */
  eventTypeName: string;
  /** Due date — ISO date string (optional). Maps to sprk_duedate. */
  dueDate: string;
  /** Priority — Dataverse option set value (100000000=Low, 100000001=Normal, 100000002=High, 100000003=Urgent). */
  priority: number;
  /** Description — free text, multi-line (optional). Maps to sprk_description. */
  description: string;
  /** Regarding Record — GUID of the related matter/project. */
  regardingRecordId: string;
  /** Regarding Record display name. */
  regardingRecordName: string;
}

export const EMPTY_EVENT_FORM: ICreateEventFormState = {
  eventName: '',
  eventTypeId: '',
  eventTypeName: '',
  dueDate: '',
  priority: 100000001, // Normal
  description: '',
  regardingRecordId: '',
  regardingRecordName: '',
};
