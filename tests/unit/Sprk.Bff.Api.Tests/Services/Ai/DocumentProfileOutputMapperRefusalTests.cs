// #2237 (uac-r2): the document-profile output mapper maps only the named profile columns (fail closed).
//
// DocumentProfileOutputMapper.BuildFields turns properties of the AI structured output into sprk_document columns, and
// every consumer (the upload wizard, the app-only profile, the Compose OBO facade) writes its output through
// IDocumentDataverseService.UpdateDocumentFieldsAsync as the BFF identity. The output keys come from a configuration (the
// Action's sprk_outputschemajson), not a person, so only DocumentProfileOutputMapper.ProfileColumns, in their plain form,
// are mapped. A key naming a document's Access Permission, its own-narrowing record or a D7 link column is dropped by the
// app-only refusal (InheritedAccessPermission.RefuseAppOnlyWrite) with its reason code; every other key that is not a
// listed profile column (a pointer, relocation or link-source column, the owner, an unknown name, an @odata.bind or
// _x_value form of a listed column) is dropped by the list. One Warning names the document id, the keys and the codes
// only, and the rest of the profile is still mapped.
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

    // Not a listed profile column, or not in its plain form: dropped by the list (fail closed), whatever it names.
    public static TheoryData<string, string> NotProfileKeys => new()
    {
        // key named by the configured schema, JSON value
        { "sprk_graphitemid", "\"01ABCDEFPOINTERITEM\"" },
        { "sprk_graphdriveid", "\"b!POINTERDRIVE\"" },
        { "sprk_graphitemidbound", "\"01ABCDEFPOINTERITEM\"" },
        { "sprk_relocationpending", "\"{\\\"to\\\":\\\"POINTER\\\"}\"" },
        { "sprk_filepath", "\"/POINTER/path.pdf\"" },
        { "sprk_emailmessageid", "\"<POINTER@example.com>\"" },
        { "sprk_isemailarchive", "true" },
        { "ownerid", "\"5b1e7a4c-0f6d-4a52-9d1b-3f0c2e8a7d11\"" },
        { "sprk_containerid", "\"b!POINTERCONTAINER\"" },
        { "sprk_whatever", "\"POINTER\"" },
        // neighbouring names of refused columns are not profile columns either
        { "sprk_accesspermissionnote", "\"POINTER\"" },
        { "sprk_parentdocumentname", "\"POINTER\"" },
        // a case variant of a listed column in a non-plain form: the writer would use the raw key
        { "SPRK_FileSummary@odata.bind", "\"/sprk_POINTER(5b1e7a4c-0f6d-4a52-9d1b-3f0c2e8a7d11)\"" },
        { "_sprk_filetldr_value", "\"POINTER\"" },
        // the two columns this mapper computes itself are never taken from the output
        { "sprk_filetype", "\"POINTER Agreement\"" },
        { "sprk_searchprofile", "\"POINTER\"" },
    };

    [Theory]
    [MemberData(nameof(NotProfileKeys))]
    public void BuildFields_NotAProfileColumn_IsNotMapped_AndIsLoggedWithoutItsValue(string key, string jsonValue)
    {
        var documentId = Guid.NewGuid();
        var logger = new CapturingLogger();

        var fields = DocumentProfileOutputMapper.BuildFields(
            documentId, Output($"{{{ProfileProperties},\"{key}\":{jsonValue}}}"), FileName, parentEntity: null, logger);

        // Only the profile and the computed file type (no search profile: one part only), which keeps its computed value.
        fields.Keys.Should().BeEquivalentTo(
            "sprk_filesummary", "sprk_filetldr", "sprk_documenttype", "sprk_filekeywords", "sprk_filetype");
        fields.Values.OfType<string>().Should().NotContain(v => v.Contains("POINTER"));
        fields["sprk_filetype"].Should().Be("PDF");

        var warning = logger.Entries.Should().ContainSingle(e => e.Level == LogLevel.Warning).Subject;
        warning.Message.Should()
            .Contain(documentId.ToString())
            .And.Contain(key)
            .And.Contain(DocumentProfileOutputMapper.NotProfileColumnReasonCode);
        warning.Message.Should().NotContain("POINTER").And.NotContain("5b1e7a4c");
    }

    [Fact]
    public void BuildFields_ModelFileTypeWithNoExtension_IsStillNotWritten()
    {
        var fields = DocumentProfileOutputMapper.BuildFields(
            Guid.NewGuid(), Output($"{{{ProfileProperties},\"sprk_filetype\":\"Legal Agreement\"}}"), "no-extension",
            parentEntity: null, new CapturingLogger());

        fields.Keys.Should().NotContain("sprk_filetype");
    }

    [Fact]
    public void BuildFields_EveryProfileColumn_IsWrittenWithItsValueAndType_AndLogsNoWarning()
    {
        var logger = new CapturingLogger();

        var fields = DocumentProfileOutputMapper.BuildFields(
            Guid.NewGuid(),
            Output("{\"sprk_filesummary\":\"A summary\",\"sprk_filetldr\":\"tldr\",\"sprk_filekeywords\":\"engagement, fees\"," +
                   "\"sprk_documenttype\":\"contract\",\"sprk_extractorganization\":\"Alpha Inc., Beta LLC\"," +
                   "\"sprk_extractpeople\":\"Jane Doe, John Smith\"}"),
            FileName, parentEntity: null, logger);

        fields["sprk_filesummary"].Should().BeOfType<string>().Which.Should().Be("A summary");
        fields["sprk_filetldr"].Should().BeOfType<string>().Which.Should().Be("tldr");
        fields["sprk_filekeywords"].Should().BeOfType<string>().Which.Should().Be("engagement, fees");
        fields["sprk_extractorganization"].Should().BeOfType<string>().Which.Should().Be("Alpha Inc., Beta LLC");
        fields["sprk_extractpeople"].Should().BeOfType<string>().Which.Should().Be("Jane Doe, John Smith");
        fields["sprk_documenttype"].Should().BeOfType<Microsoft.Xrm.Sdk.OptionSetValue>()
            .Which.Value.Should().Be(DocumentTypeMapper.ToDataverseValue("contract")!.Value);
        fields.Keys.Should().BeEquivalentTo(DocumentProfileOutputMapper.ProfileColumns.Append("sprk_filetype"));
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void BuildFields_MixedCaseProfileColumn_IsWrittenUnderItsListedName()
    {
        var logger = new CapturingLogger();

        var fields = DocumentProfileOutputMapper.BuildFields(
            Guid.NewGuid(),
            Output("{\"SPRK_FileSummary\":\"A summary\",\" sprk_FileTldr \":\"tldr\",\"Sprk_DocumentType\":\"contract\"}"),
            FileName, parentEntity: null, logger);

        // The dictionary ignores case, so the stored key names are what the writer will send.
        fields.Keys.Should().Contain("sprk_filesummary").And.Contain("sprk_filetldr").And.Contain("sprk_documenttype");
        fields.Keys.Should().NotContain(k => k != k.ToLowerInvariant() || k != k.Trim());
        fields["sprk_filesummary"].Should().Be("A summary");
        fields["sprk_filetldr"].Should().Be("tldr");
        fields["sprk_documenttype"].Should().BeOfType<Microsoft.Xrm.Sdk.OptionSetValue>();
        logger.Entries.Should().NotContain(e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public void ProfileColumns_IsExactlyTheShippedDocumentProfilerSchema()
    {
        // Pinned: widening this list widens what a configuration can make the BFF identity write to a document, so it
        // is a reviewed diff. Source: the live ACT-011 sprk_outputschemajson and the jps-action-create document-profiler
        // example, minus sprk_filetype (computed from the file extension).
        DocumentProfileOutputMapper.ProfileColumns.Should().BeEquivalentTo(
            "sprk_filesummary", "sprk_filetldr", "sprk_filekeywords", "sprk_documenttype",
            "sprk_extractorganization", "sprk_extractpeople");
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
