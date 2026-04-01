using System;
using System.Web;
using FluentAssertions;
using Sprk.Bff.Api.Api.Agent;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Agent;

/// <summary>
/// Unit tests for HandoffUrlBuilder.
/// Validates deep-link URL generation for each Code Page / Wizard target.
/// </summary>
public class HandoffUrlBuilderTests
{
    private const string BaseUrl = "https://spaarkedev1.crm.dynamics.com";
    private readonly HandoffUrlBuilder _builder = new(BaseUrl);

    #region Constructor Tests

    [Fact]
    public void Constructor_TrimsTrailingSlash()
    {
        var builder = new HandoffUrlBuilder("https://example.crm.dynamics.com/");

        var url = builder.BuildPlaybookLibraryUrl();

        url.Should().StartWith("https://example.crm.dynamics.com/main.aspx");
    }

    [Fact]
    public void Constructor_HandlesBaseUrlWithoutTrailingSlash()
    {
        var builder = new HandoffUrlBuilder("https://example.crm.dynamics.com");

        var url = builder.BuildPlaybookLibraryUrl();

        url.Should().StartWith("https://example.crm.dynamics.com/main.aspx");
    }

    #endregion

    #region URL Format Tests

    [Fact]
    public void BuildWebResourceUrl_ProducesCorrectFormat()
    {
        var analysisId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var sourceFileId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var url = _builder.BuildAnalysisWorkspaceUrl(analysisId, sourceFileId);

        url.Should().StartWith($"{BaseUrl}/main.aspx?pagetype=webresource&webresourceName=");
        url.Should().Contain("&data=");
    }

    #endregion

    #region BuildAnalysisWorkspaceUrl Tests

    [Fact]
    public void BuildAnalysisWorkspaceUrl_WithRequiredParams_ReturnsCorrectUrl()
    {
        var analysisId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var sourceFileId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var url = _builder.BuildAnalysisWorkspaceUrl(analysisId, sourceFileId);

        url.Should().Contain("webresourceName=sprk_AnalysisWorkspaceLauncher");
        var data = ExtractDecodedData(url);
        data.Should().Contain("analysisId=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        data.Should().Contain("sourceFileId=bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        data.Should().NotContain("playbookId");
    }

    [Fact]
    public void BuildAnalysisWorkspaceUrl_WithPlaybookId_IncludesPlaybookParam()
    {
        var analysisId = Guid.NewGuid();
        var sourceFileId = Guid.NewGuid();
        var playbookId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var url = _builder.BuildAnalysisWorkspaceUrl(analysisId, sourceFileId, playbookId);

        var data = ExtractDecodedData(url);
        data.Should().Contain("playbookId=cccccccc-cccc-cccc-cccc-cccccccccccc");
    }

    [Fact]
    public void BuildAnalysisWorkspaceUrl_WithoutPlaybookId_ExcludesPlaybookParam()
    {
        var url = _builder.BuildAnalysisWorkspaceUrl(Guid.NewGuid(), Guid.NewGuid());

        var data = ExtractDecodedData(url);
        data.Should().NotContain("playbookId");
    }

    #endregion

    #region BuildDocumentUploadWizardUrl Tests

    [Fact]
    public void BuildDocumentUploadWizardUrl_WithRequiredParams_ReturnsCorrectUrl()
    {
        var parentEntityId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var url = _builder.BuildDocumentUploadWizardUrl(
            "sprk_matter", parentEntityId, "Test Matter");

        url.Should().Contain("webresourceName=sprk_documentuploadwizard");
        var data = ExtractDecodedData(url);
        data.Should().Contain("parentEntityType=sprk_matter");
        data.Should().Contain($"parentEntityId={parentEntityId}");
        data.Should().Contain("parentEntityName=Test+Matter");
    }

    [Fact]
    public void BuildDocumentUploadWizardUrl_WithContainerId_IncludesContainerParam()
    {
        var containerId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        var url = _builder.BuildDocumentUploadWizardUrl(
            "sprk_matter", Guid.NewGuid(), "Matter", containerId);

        var data = ExtractDecodedData(url);
        data.Should().Contain($"containerId={containerId}");
    }

    [Fact]
    public void BuildDocumentUploadWizardUrl_WithoutContainerId_ExcludesContainerParam()
    {
        var url = _builder.BuildDocumentUploadWizardUrl(
            "sprk_matter", Guid.NewGuid(), "Matter");

        var data = ExtractDecodedData(url);
        data.Should().NotContain("containerId");
    }

    [Fact]
    public void BuildDocumentUploadWizardUrl_EncodesSpecialCharactersInName()
    {
        var url = _builder.BuildDocumentUploadWizardUrl(
            "sprk_matter", Guid.NewGuid(), "Smith & Jones (2024)");

        var data = ExtractDecodedData(url);
        // The data parameter value is URL-encoded, so & becomes %26 and spaces become +
        data.Should().Contain("parentEntityName=Smith");
    }

    #endregion

    #region BuildSummarizeFilesWizardUrl Tests

    [Fact]
    public void BuildSummarizeFilesWizardUrl_ReturnsCorrectUrl()
    {
        var documentId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        var bffUrl = "https://spe-api-dev-67e2xz.azurewebsites.net";

        var url = _builder.BuildSummarizeFilesWizardUrl(documentId, bffUrl);

        url.Should().Contain("webresourceName=sprk_summarizefileswizard");
        var data = ExtractDecodedData(url);
        data.Should().Contain($"documentId={documentId}");
        data.Should().Contain("bffBaseUrl=");
    }

    #endregion

    #region BuildPlaybookLibraryUrl Tests

    [Fact]
    public void BuildPlaybookLibraryUrl_WithNoParams_ReturnsUrlWithEmptyData()
    {
        var url = _builder.BuildPlaybookLibraryUrl();

        url.Should().Contain("webresourceName=sprk_playbooklibrary");
        // data parameter should be present but empty when no params
        url.Should().Contain("&data=");
    }

    [Fact]
    public void BuildPlaybookLibraryUrl_WithDocumentId_IncludesDocumentParam()
    {
        var documentId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var url = _builder.BuildPlaybookLibraryUrl(documentId: documentId);

        var data = ExtractDecodedData(url);
        data.Should().Contain($"documentId={documentId}");
    }

    [Fact]
    public void BuildPlaybookLibraryUrl_WithMatterId_IncludesMatterParam()
    {
        var matterId = Guid.Parse("66666666-7777-8888-9999-aaaaaaaaaaaa");

        var url = _builder.BuildPlaybookLibraryUrl(matterId: matterId);

        var data = ExtractDecodedData(url);
        data.Should().Contain($"matterId={matterId}");
    }

    [Fact]
    public void BuildPlaybookLibraryUrl_WithBothParams_IncludesBoth()
    {
        var documentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();

        var url = _builder.BuildPlaybookLibraryUrl(documentId, matterId);

        var data = ExtractDecodedData(url);
        data.Should().Contain("documentId=");
        data.Should().Contain("matterId=");
    }

    #endregion

    #region BuildCreateMatterWizardUrl Tests

    [Fact]
    public void BuildCreateMatterWizardUrl_WithNoName_ReturnsUrlWithEmptyData()
    {
        var url = _builder.BuildCreateMatterWizardUrl();

        url.Should().Contain("webresourceName=sprk_creatematterwizard");
    }

    [Fact]
    public void BuildCreateMatterWizardUrl_WithName_IncludesNameParam()
    {
        var url = _builder.BuildCreateMatterWizardUrl("New Matter");

        var data = ExtractDecodedData(url);
        data.Should().Contain("matterName=New+Matter");
    }

    [Fact]
    public void BuildCreateMatterWizardUrl_WithEmptyString_ExcludesNameParam()
    {
        var url = _builder.BuildCreateMatterWizardUrl("");

        var data = ExtractDecodedData(url);
        data.Should().NotContain("matterName");
    }

    [Fact]
    public void BuildCreateMatterWizardUrl_WithNullName_ExcludesNameParam()
    {
        var url = _builder.BuildCreateMatterWizardUrl(null);

        var data = ExtractDecodedData(url);
        data.Should().NotContain("matterName");
    }

    #endregion

    #region BuildCreateEventWizardUrl Tests

    [Fact]
    public void BuildCreateEventWizardUrl_WithNoParams_ReturnsBaseUrl()
    {
        var url = _builder.BuildCreateEventWizardUrl();

        url.Should().Contain("webresourceName=sprk_createeventwizard");
    }

    [Fact]
    public void BuildCreateEventWizardUrl_WithMatterId_IncludesMatterParam()
    {
        var matterId = Guid.Parse("12345678-1234-1234-1234-123456789012");

        var url = _builder.BuildCreateEventWizardUrl(matterId: matterId);

        var data = ExtractDecodedData(url);
        data.Should().Contain($"matterId={matterId}");
    }

    [Fact]
    public void BuildCreateEventWizardUrl_WithMatterName_IncludesNameParam()
    {
        var url = _builder.BuildCreateEventWizardUrl(matterName: "Deposition");

        var data = ExtractDecodedData(url);
        data.Should().Contain("matterName=Deposition");
    }

    [Fact]
    public void BuildCreateEventWizardUrl_WithBothParams_IncludesBoth()
    {
        var matterId = Guid.NewGuid();

        var url = _builder.BuildCreateEventWizardUrl(matterId, "Review Hearing");

        var data = ExtractDecodedData(url);
        data.Should().Contain("matterId=");
        data.Should().Contain("matterName=Review+Hearing");
    }

    #endregion

    #region URL Encoding Tests

    [Fact]
    public void BuildDocumentUploadWizardUrl_EncodesAmpersandInValues()
    {
        var url = _builder.BuildDocumentUploadWizardUrl(
            "sprk_matter", Guid.NewGuid(), "A & B");

        // The entire data segment should be URL-encoded
        url.Should().NotContain("parentEntityName=A & B");
    }

    [Fact]
    public void BuildCreateMatterWizardUrl_EncodesUnicodeCharacters()
    {
        var url = _builder.BuildCreateMatterWizardUrl("Muller & Sohne GmbH");

        // Should be encoded in the URL
        url.Should().Contain("data=");
        var data = ExtractDecodedData(url);
        data.Should().Contain("matterName=");
    }

    [Fact]
    public void BuildSummarizeFilesWizardUrl_EncodesBffUrl()
    {
        var bffUrl = "https://example.com/api?key=value&other=123";

        var url = _builder.BuildSummarizeFilesWizardUrl(Guid.NewGuid(), bffUrl);

        // The data section is double-encoded (params are encoded, then the whole data string is encoded)
        url.Should().Contain("data=");
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Extracts and decodes the 'data' query parameter from the generated URL.
    /// </summary>
    private static string ExtractDecodedData(string url)
    {
        var uri = new Uri(url);
        var query = HttpUtility.ParseQueryString(uri.Query);
        var data = query["data"];
        return data is not null ? HttpUtility.UrlDecode(data) : "";
    }

    #endregion
}
