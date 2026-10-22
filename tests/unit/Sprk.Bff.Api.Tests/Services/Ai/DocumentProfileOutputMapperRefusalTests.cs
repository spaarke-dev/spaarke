// #2237 (uac-r2): the document-profile output mapper never maps a column the app-only refusal refuses on a document.
//
// DocumentProfileOutputMapper.BuildFields turns every property of the AI structured output into a sprk_document column,
// and every consumer (the upload wizard, the app-only profile, the Compose OBO facade) writes its output through
// IDocumentDataverseService.UpdateDocumentFieldsAsync. The output is built by a model and a configuration, not a person,
// so a key naming a document's Access Permission, its own-narrowing record or a D7 link column is dropped
// (InheritedAccessPermission.RefuseAppOnlyWrite), in any form, with one Warning naming ids and codes only, and the rest of
// the profile is still mapped.
//
// No doubles beyond a capturing ILogger: the mapper is a pure function of the JSON output, and the refusal it asks is the
// real one.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Dataverse;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public sealed class DocumentProfileOutputMapperRefusalTests
{
    private const string FileName = "engagement-letter.pdf";

    // The ordinary profile a Document Profiler output carries; each must still be mapped beside a refused key.
    private const string ProfileProperties =
        "\"sprk_filesummary\":\"A summary\",\"sprk_filetldr\":\"tldr\",\"sprk_documenttype\":\"contract\"," +
        "\"sprk_filekeywords\":\"engagement, fees\"";

    private static JsonElement Output(string json) => JsonDocument.Parse(json).RootElement.Clone();

    public static TheoryData<string, string, string, string> RefusedKeys => new()
    {
        // key named by the model, JSON value, the normalized column and the reason code the Warning reports
        { "sprk_accesspermission", "1", "sprk_accesspermission", InheritedAccessPermission.AppOnlyDocumentWriteReasonCode },
        { "sprk_accessinheritance", "\"{\\\"own\\\":null}\"", "sprk_accessinheritance", InheritedAccessPermission.AppOnlyDocumentWriteReasonCode },
        { "sprk_relatedcommunication", "\"5b1e7a4c-0f6d-4a52-9d1b-3f0c2e8a7d11\"", "sprk_relatedcommunication", InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode },
        { "sprk_parentdocument", "\"5b1e7a4c-0f6d-4a52-9d1b-3f0c2e8a7d11\"", "sprk_parentdocument", InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode },
        // a case variant
        { "SPRK_AccessPermission", "\"1\"", "sprk_accesspermission", InheritedAccessPermission.AppOnlyDocumentWriteReasonCode },
        // the @odata.bind and _x_value forms
        { "sprk_RelatedCommunication@odata.bind", "\"/sprk_communications(5b1e7a4c-0f6d-4a52-9d1b-3f0c2e8a7d11)\"", "sprk_relatedcommunication", InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode },
        { "_sprk_parentdocument_value", "\"5b1e7a4c-0f6d-4a52-9d1b-3f0c2e8a7d11\"", "sprk_parentdocument", InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode },
        // an object value (mapped as a JSON blob when not refused)
        { "sprk_accessinheritance", "{\"own\":0}", "sprk_accessinheritance", InheritedAccessPermission.AppOnlyDocumentWriteReasonCode },
    };

    [Theory]
    [MemberData(nameof(RefusedKeys))]
    public void BuildFields_RefusedKey_IsNotMapped_AndTheRestOfTheProfileIs(
        string key, string jsonValue, string column, string reasonCode)
    {
        var documentId = Guid.NewGuid();
        var logger = new CapturingLogger();

        var fields = DocumentProfileOutputMapper.BuildFields(
            documentId, Output($"{{{ProfileProperties},\"{key}\":{jsonValue}}}"), FileName, parentEntity: null, logger);

        // Not mapped in any form.
        fields.Keys.Should().NotContain(k => Sprk.Bff.Api.Services.Access.SecureRootInheritance.NormalizeColumn(k) == column);
        InheritedAccessPermission.RefuseAppOnlyWrite("sprk_document", fields.Keys).Should().BeNull();

        // The rest of the profile is mapped.
        fields.Should().ContainKeys("sprk_filesummary", "sprk_filetldr", "sprk_documenttype", "sprk_filekeywords",
            "sprk_filetype");
        fields["sprk_filesummary"].Should().Be("A summary");
        fields["sprk_documenttype"].Should().BeOfType<Microsoft.Xrm.Sdk.OptionSetValue>();
        fields["sprk_filetype"].Should().Be("PDF");

        // One Warning: the document id, the column and the reason code; never the value.
        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should().Contain(documentId.ToString()).And.Contain(column).And.Contain(reasonCode);
        warning.Message.Should().NotContain("5b1e7a4c");
    }

    [Fact]
    public void BuildFields_EveryRefusedColumnAtOnce_OneWarningNamingEachColumnAndCode()
    {
        var documentId = Guid.NewGuid();
        var logger = new CapturingLogger();

        var fields = DocumentProfileOutputMapper.BuildFields(
            documentId,
            Output($"{{{ProfileProperties},\"sprk_accesspermission\":1,\"sprk_AccessInheritance\":\"x\"," +
                   "\"sprk_relatedcommunication@odata.bind\":\"/sprk_communications(5b1e7a4c-0f6d-4a52-9d1b-3f0c2e8a7d11)\"," +
                   "\"_SPRK_PARENTDOCUMENT_VALUE\":\"5b1e7a4c-0f6d-4a52-9d1b-3f0c2e8a7d11\"}"),
            FileName, parentEntity: null, logger);

        fields.Keys.Should().BeEquivalentTo(
            "sprk_filesummary", "sprk_filetldr", "sprk_documenttype", "sprk_filekeywords", "sprk_filetype");

        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should()
            .Contain(documentId.ToString())
            .And.Contain("sprk_accesspermission").And.Contain("sprk_accessinheritance")
            .And.Contain("sprk_relatedcommunication").And.Contain("sprk_parentdocument")
            .And.Contain(InheritedAccessPermission.AppOnlyDocumentWriteReasonCode)
            .And.Contain(InheritedAccessPermission.AppOnlyDocumentLinkWriteReasonCode);
        warning.Message.Should().NotContain("5b1e7a4c");
    }

    [Fact]
    public void BuildFields_NoRefusedKey_MapsEveryPropertyAndLogsNoWarning()
    {
        var logger = new CapturingLogger();

        // Must not fire: neighbouring names that are not refused columns stay mapped (sprk_accesspermissionnote is not
        // sprk_accesspermission; sprk_parentdocumentname is not the link).
        var fields = DocumentProfileOutputMapper.BuildFields(
            Guid.NewGuid(),
            Output($"{{{ProfileProperties},\"sprk_accesspermissionnote\":\"n\",\"sprk_parentdocumentname\":\"p\"}}"),
            FileName, parentEntity: null, logger);

        fields.Should().ContainKeys("sprk_filesummary", "sprk_filetldr", "sprk_documenttype", "sprk_filekeywords",
            "sprk_accesspermissionnote", "sprk_parentdocumentname", "sprk_filetype");
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
