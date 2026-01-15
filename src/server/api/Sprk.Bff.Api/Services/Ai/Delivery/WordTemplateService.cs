using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace Sprk.Bff.Api.Services.Ai.Delivery;

/// <summary>
/// Service for generating Word documents from templates with placeholder replacement.
/// Uses OpenXML SDK to manipulate Word documents directly.
/// </summary>
/// <remarks>
/// <para>
/// Supports placeholder syntax: {{variableName}} or {{nested.property.value}}
/// Uses ITemplateEngine for consistent template rendering across all delivery types.
/// </para>
/// <para>
/// Templates can be stored in Dataverse as attachments or provided directly.
/// This service handles the Word document manipulation layer.
/// </para>
/// </remarks>
public sealed partial class WordTemplateService : IWordTemplateService
{
    private readonly ITemplateEngine _templateEngine;
    private readonly ILogger<WordTemplateService> _logger;

    private const string ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public WordTemplateService(
        ITemplateEngine templateEngine,
        ILogger<WordTemplateService> logger)
    {
        _templateEngine = templateEngine;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WordTemplateResult> GenerateFromTemplateAsync(
        byte[] templateBytes,
        Dictionary<string, object?> variables,
        string outputFileName,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Generating Word document from template with {VariableCount} variables", variables.Count);

        try
        {
            // Create a copy of the template to work with
            using var memoryStream = new MemoryStream();
            memoryStream.Write(templateBytes, 0, templateBytes.Length);
            memoryStream.Position = 0;

            // Process the document
            using (var document = WordprocessingDocument.Open(memoryStream, true))
            {
                var mainPart = document.MainDocumentPart;
                if (mainPart?.Document?.Body == null)
                {
                    return WordTemplateResult.Fail("Invalid Word template: missing document body");
                }

                // Replace placeholders in all text elements
                var replacementCount = ReplaceAllPlaceholders(mainPart.Document.Body, variables);

                // Also process headers and footers
                if (mainPart.HeaderParts != null)
                {
                    foreach (var headerPart in mainPart.HeaderParts)
                    {
                        if (headerPart.Header != null)
                        {
                            replacementCount += ReplaceAllPlaceholders(headerPart.Header, variables);
                        }
                    }
                }

                if (mainPart.FooterParts != null)
                {
                    foreach (var footerPart in mainPart.FooterParts)
                    {
                        if (footerPart.Footer != null)
                        {
                            replacementCount += ReplaceAllPlaceholders(footerPart.Footer, variables);
                        }
                    }
                }

                mainPart.Document.Save();

                _logger.LogInformation(
                    "Word document generated: {ReplacementCount} placeholders replaced",
                    replacementCount);
            }

            // Get the result bytes
            var resultBytes = memoryStream.ToArray();

            return await Task.FromResult(WordTemplateResult.Ok(
                resultBytes,
                ContentType,
                outputFileName,
                new Dictionary<string, object?>
                {
                    ["size"] = resultBytes.Length,
                    ["generatedAt"] = DateTimeOffset.UtcNow
                }));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Word document from template");
            return WordTemplateResult.Fail($"Template processing failed: {ex.Message}");
        }
    }

    /// <inheritdoc />
    public async Task<WordTemplateResult> GenerateFromTemplateAsync(
        Stream templateStream,
        Dictionary<string, object?> variables,
        string outputFileName,
        CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await templateStream.CopyToAsync(memoryStream, cancellationToken);
        return await GenerateFromTemplateAsync(
            memoryStream.ToArray(),
            variables,
            outputFileName,
            cancellationToken);
    }

    /// <summary>
    /// Replaces all {{placeholder}} patterns in the document element.
    /// </summary>
    private int ReplaceAllPlaceholders(OpenXmlElement element, Dictionary<string, object?> variables)
    {
        var replacementCount = 0;

        // Find all Text elements
        var textElements = element.Descendants<Text>().ToList();

        foreach (var text in textElements)
        {
            if (string.IsNullOrEmpty(text.Text)) continue;

            // Check if text contains any placeholders
            if (PlaceholderPattern().IsMatch(text.Text))
            {
                // Use template engine to render the text
                var rendered = _templateEngine.Render(text.Text, variables);

                if (rendered != text.Text)
                {
                    text.Text = rendered;
                    replacementCount++;
                }
            }
        }

        // Handle case where placeholder spans multiple runs (common in Word)
        // This happens when Word splits text across runs for formatting
        replacementCount += ConsolidateAndReplaceSplitPlaceholders(element, variables);

        return replacementCount;
    }

    /// <summary>
    /// Handles placeholders that are split across multiple Run elements.
    /// Word often splits text for internal tracking, breaking {{placeholder}} into separate runs.
    /// </summary>
    private int ConsolidateAndReplaceSplitPlaceholders(OpenXmlElement element, Dictionary<string, object?> variables)
    {
        var replacementCount = 0;
        var paragraphs = element.Descendants<Paragraph>().ToList();

        foreach (var paragraph in paragraphs)
        {
            var runs = paragraph.Elements<Run>().ToList();
            if (runs.Count <= 1) continue;

            // Concatenate all text from runs
            var fullText = string.Join("", runs.SelectMany(r => r.Elements<Text>().Select(t => t.Text)));

            // Check for placeholder pattern
            if (PlaceholderPattern().IsMatch(fullText))
            {
                // Render the full text
                var rendered = _templateEngine.Render(fullText, variables);

                if (rendered != fullText)
                {
                    // Replace content: put all text in first run, clear others
                    var firstTextElement = runs.SelectMany(r => r.Elements<Text>()).FirstOrDefault();
                    if (firstTextElement != null)
                    {
                        firstTextElement.Text = rendered;
                        replacementCount++;

                        // Clear text from subsequent runs
                        var isFirst = true;
                        foreach (var run in runs)
                        {
                            foreach (var text in run.Elements<Text>())
                            {
                                if (isFirst)
                                {
                                    isFirst = false;
                                    continue;
                                }
                                text.Text = "";
                            }
                        }
                    }
                }
            }
        }

        return replacementCount;
    }

    [GeneratedRegex(@"\{\{[^}]+\}\}")]
    private static partial Regex PlaceholderPattern();
}

/// <summary>
/// Interface for Word template generation service.
/// </summary>
public interface IWordTemplateService
{
    /// <summary>
    /// Generates a Word document from a template with placeholder replacement.
    /// </summary>
    /// <param name="templateBytes">Template document bytes.</param>
    /// <param name="variables">Variables to substitute into placeholders.</param>
    /// <param name="outputFileName">Output filename for the generated document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Result containing the generated document or error.</returns>
    Task<WordTemplateResult> GenerateFromTemplateAsync(
        byte[] templateBytes,
        Dictionary<string, object?> variables,
        string outputFileName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Generates a Word document from a template stream with placeholder replacement.
    /// </summary>
    Task<WordTemplateResult> GenerateFromTemplateAsync(
        Stream templateStream,
        Dictionary<string, object?> variables,
        string outputFileName,
        CancellationToken cancellationToken);
}

/// <summary>
/// Result of Word template generation.
/// </summary>
public record WordTemplateResult
{
    /// <summary>Whether generation succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Generated document bytes.</summary>
    public byte[]? FileBytes { get; init; }

    /// <summary>MIME content type.</summary>
    public string? ContentType { get; init; }

    /// <summary>Suggested filename.</summary>
    public string? FileName { get; init; }

    /// <summary>Error message if failed.</summary>
    public string? Error { get; init; }

    /// <summary>Generation metadata.</summary>
    public Dictionary<string, object?>? Metadata { get; init; }

    public static WordTemplateResult Ok(byte[] bytes, string contentType, string fileName, Dictionary<string, object?>? metadata = null) => new()
    {
        Success = true,
        FileBytes = bytes,
        ContentType = contentType,
        FileName = fileName,
        Metadata = metadata
    };

    public static WordTemplateResult Fail(string error) => new()
    {
        Success = false,
        Error = error
    };
}
