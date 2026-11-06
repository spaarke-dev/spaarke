namespace Sprk.Bff.Api.Services.Finance.Models;

/// <summary>
/// JSON schema constants for OpenAI structured output.
/// Used with ChatResponseFormat.CreateJsonSchemaFormat for constrained decoding.
/// </summary>
public static class FinanceJsonSchemas
{
    public static readonly BinaryData ExtractionResultSchema = BinaryData.FromString("""
    {
      "type": "object",
      "properties": {
        "header": {
          "type": "object",
          "properties": {
            "invoiceNumber": { "type": "string" },
            "invoiceDate": { "type": "string" },
            "totalAmount": { "type": "number" },
            "currency": { "type": "string" },
            "vendorName": { "type": "string" },
            "vendorAddress": { "type": ["string", "null"] },
            "paymentTerms": { "type": ["string", "null"] }
          },
          "required": ["invoiceNumber", "invoiceDate", "totalAmount", "currency", "vendorName", "vendorAddress", "paymentTerms"],
          "additionalProperties": false
        },
        "lineItems": {
          "type": "array",
          "items": {
            "type": "object",
            "properties": {
              "lineNumber": { "type": "integer" },
              "description": { "type": "string" },
              "costType": { "type": "string" },
              "amount": { "type": "number" },
              "currency": { "type": "string" },
              "eventDate": { "type": ["string", "null"] },
              "roleClass": { "type": ["string", "null"] },
              "hours": { "type": ["number", "null"] },
              "rate": { "type": ["number", "null"] }
            },
            "required": ["lineNumber", "description", "costType", "amount", "currency", "eventDate", "roleClass", "hours", "rate"],
            "additionalProperties": false
          }
        },
        "extractionConfidence": { "type": "number" },
        "notes": { "type": ["string", "null"] }
      },
      "required": ["header", "lineItems", "extractionConfidence", "notes"],
      "additionalProperties": false
    }
    """);
}
