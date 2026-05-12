/**
 * Column renderer types for different Dataverse attribute types
 */
/**
 * Dataverse attribute type mapping
 * Matches ComponentFramework.PropertyHelper.DataSetApi.DataType
 */
export var DataverseAttributeType;
(function (DataverseAttributeType) {
    // String types
    DataverseAttributeType["SingleLineText"] = "SingleLine.Text";
    DataverseAttributeType["MultipleLineText"] = "Multiple";
    DataverseAttributeType["Email"] = "SingleLine.Email";
    DataverseAttributeType["Phone"] = "SingleLine.Phone";
    DataverseAttributeType["Url"] = "SingleLine.URL";
    DataverseAttributeType["TickerSymbol"] = "SingleLine.Ticker";
    // Number types
    DataverseAttributeType["WholeNumber"] = "Whole.None";
    DataverseAttributeType["DecimalNumber"] = "Decimal.Number";
    DataverseAttributeType["FloatingPoint"] = "FP";
    DataverseAttributeType["Money"] = "Currency";
    // Date/Time
    DataverseAttributeType["DateAndTime"] = "DateAndTime.DateAndTime";
    DataverseAttributeType["DateOnly"] = "DateAndTime.DateOnly";
    // Choice types
    DataverseAttributeType["TwoOptions"] = "TwoOptions";
    DataverseAttributeType["OptionSet"] = "OptionSet";
    DataverseAttributeType["MultiSelectOptionSet"] = "MultiSelectOptionSet";
    // Lookup
    DataverseAttributeType["Lookup"] = "Lookup.Simple";
    DataverseAttributeType["Customer"] = "Lookup.Customer";
    DataverseAttributeType["Owner"] = "Lookup.Owner";
    DataverseAttributeType["PartyList"] = "Lookup.PartyList";
    DataverseAttributeType["Regarding"] = "Lookup.Regarding";
    // Other
    DataverseAttributeType["Boolean"] = "Boolean";
    DataverseAttributeType["Image"] = "Image";
    DataverseAttributeType["File"] = "File";
})(DataverseAttributeType || (DataverseAttributeType = {}));
//# sourceMappingURL=ColumnRendererTypes.js.map