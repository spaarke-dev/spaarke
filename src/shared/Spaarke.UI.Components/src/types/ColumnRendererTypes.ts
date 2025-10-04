/**
 * Column renderer types for different Dataverse attribute types
 */

import * as React from "react";
import { IDatasetColumn, IDatasetRecord } from "./DatasetTypes";

/**
 * Column renderer function signature
 */
export type ColumnRenderer = (
  value: any,
  record: IDatasetRecord,
  column: IDatasetColumn
) => React.ReactElement | string | null;

/**
 * Dataverse attribute type mapping
 * Matches ComponentFramework.PropertyHelper.DataSetApi.DataType
 */
export enum DataverseAttributeType {
  // String types
  SingleLineText = "SingleLine.Text",
  MultipleLineText = "Multiple",
  Email = "SingleLine.Email",
  Phone = "SingleLine.Phone",
  Url = "SingleLine.URL",
  TickerSymbol = "SingleLine.Ticker",

  // Number types
  WholeNumber = "Whole.None",
  DecimalNumber = "Decimal.Number",
  FloatingPoint = "FP",
  Money = "Currency",

  // Date/Time
  DateAndTime = "DateAndTime.DateAndTime",
  DateOnly = "DateAndTime.DateOnly",

  // Choice types
  TwoOptions = "TwoOptions",
  OptionSet = "OptionSet",
  MultiSelectOptionSet = "MultiSelectOptionSet",

  // Lookup
  Lookup = "Lookup.Simple",
  Customer = "Lookup.Customer",
  Owner = "Lookup.Owner",
  PartyList = "Lookup.PartyList",
  Regarding = "Lookup.Regarding",

  // Other
  Boolean = "Boolean",
  Image = "Image",
  File = "File"
}

/**
 * Choice option metadata
 */
export interface IChoiceOption {
  value: number;
  label: string;
  color?: string;
}

/**
 * Lookup reference metadata
 */
export interface ILookupReference {
  id: string;
  name: string;
  entityType: string;
}
