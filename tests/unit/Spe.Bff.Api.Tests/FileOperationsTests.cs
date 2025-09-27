using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json;
using Spe.Bff.Api.Models;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class FileOperationsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public FileOperationsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    #region Update Item (PATCH) Tests

    [Fact]
    public async Task UpdateItem_WithoutBearer_Returns401()
    {
        var request = new UpdateFileRequest("newname.txt");
        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync("/api/obo/drives/test-drive/items/test-item", content);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateItem_Rename_Returns200WithUpdatedItem()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new UpdateFileRequest("renamed-file.txt");
        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync("/api/obo/drives/test-drive/items/test-item", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var item = JsonConvert.DeserializeObject<DriveItemDto>(responseContent);

        item.Should().NotBeNull();
        item!.Name.Should().Be("renamed-file.txt");
        item.LastModifiedDateTime.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateItem_Move_Returns200WithUpdatedItem()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new UpdateFileRequest(ParentReferenceId: "new-parent-folder-id");
        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync("/api/obo/drives/test-drive/items/test-item", content);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var responseContent = await response.Content.ReadAsStringAsync();
        var item = JsonConvert.DeserializeObject<DriveItemDto>(responseContent);

        item.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateItem_EmptyRequest_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new UpdateFileRequest();
        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync("/api/obo/drives/test-drive/items/test-item", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateItem_InvalidItemId_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var request = new UpdateFileRequest("newname.txt");
        var json = JsonConvert.SerializeObject(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await _client.PatchAsync("/api/obo/drives/test-drive/items/", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Delete Item Tests

    [Fact]
    public async Task DeleteItem_WithoutBearer_Returns401()
    {
        var response = await _client.DeleteAsync("/api/obo/drives/test-drive/items/test-item");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DeleteItem_ValidItem_Returns204()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.DeleteAsync("/api/obo/drives/test-drive/items/test-item");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent); // 204
    }

    [Fact]
    public async Task DeleteItem_EmptyItemId_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.DeleteAsync("/api/obo/drives/test-drive/items/");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Download Content with Range Tests

    [Fact]
    public async Task DownloadContent_WithoutBearer_Returns401()
    {
        var response = await _client.GetAsync("/api/obo/drives/test-drive/items/test-item/content");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task DownloadContent_FullFile_Returns200WithContent()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/drives/test-drive/items/test-document/content");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.AcceptRanges.Should().Contain("bytes");

        var content = await response.Content.ReadAsByteArrayAsync();
        content.Should().NotBeEmpty();

        // Should have proper content type
        response.Content.Headers.ContentType?.MediaType.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task DownloadContent_WithRange_Returns206PartialContent()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        _client.DefaultRequestHeaders.Range = new RangeHeaderValue(0, 1023); // First 1KB

        var response = await _client.GetAsync("/api/obo/drives/test-drive/items/test-document/content");

        response.StatusCode.Should().Be(HttpStatusCode.PartialContent); // 206
        response.Headers.ETag.Should().NotBeNull();
        response.Headers.AcceptRanges.Should().Contain("bytes");
        response.Content.Headers.ContentRange.Should().NotBeNull();

        var content = await response.Content.ReadAsByteArrayAsync();
        content.Length.Should().Be(1024); // Exactly the requested range
    }

    [Fact]
    public async Task DownloadContent_WithIfNoneMatch_Returns304WhenETagMatches()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // First request to get the ETag
        var firstResponse = await _client.GetAsync("/api/obo/drives/test-drive/items/test-document/content");
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var etag = firstResponse.Headers.ETag?.Tag;
        etag.Should().NotBeNullOrEmpty();

        // Second request with If-None-Match
        _client.DefaultRequestHeaders.IfNoneMatch.Add(new EntityTagHeaderValue(etag!));
        var secondResponse = await _client.GetAsync("/api/obo/drives/test-drive/items/test-document/content");

        secondResponse.StatusCode.Should().Be(HttpStatusCode.NotModified); // 304
    }

    [Fact]
    public async Task DownloadContent_InvalidRange_Returns416()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        _client.DefaultRequestHeaders.Range = new RangeHeaderValue(999999999, 999999999); // Range beyond file size

        var response = await _client.GetAsync("/api/obo/drives/test-drive/items/test-document/content");

        response.StatusCode.Should().Be(HttpStatusCode.RequestedRangeNotSatisfiable); // 416
    }

    [Fact]
    public async Task DownloadContent_MultipleRanges_HandlesCorrectly()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // Test different range patterns
        var rangesToTest = new[]
        {
            new RangeHeaderValue(0, 511),     // First 512 bytes
            new RangeHeaderValue(512, 1023),  // Next 512 bytes
            new RangeHeaderValue(1024, null), // From byte 1024 to end
        };

        foreach (var range in rangesToTest)
        {
            // Clear previous range
            _client.DefaultRequestHeaders.Range = null;
            _client.DefaultRequestHeaders.Range = range;

            var response = await _client.GetAsync("/api/obo/drives/test-drive/items/test-document/content");

            if (range.Ranges.FirstOrDefault()?.From < 5 * 1024 * 1024) // Assuming test file is around 1-5MB
            {
                response.StatusCode.Should().Be(HttpStatusCode.PartialContent); // 206
                response.Content.Headers.ContentRange.Should().NotBeNull();
            }
        }
    }

    [Fact]
    public async Task DownloadContent_DifferentContentTypes_ReturnsCorrectMimeType()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var testCases = new[]
        {
            ("test-image", "image/jpeg"),
            ("test-video", "video/mp4"),
            ("test-document", "application/pdf"),
            ("test-text", "text/plain")
        };

        foreach (var (itemId, expectedContentType) in testCases)
        {
            var response = await _client.GetAsync($"/api/obo/drives/test-drive/items/{itemId}/content");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Content.Headers.ContentType?.MediaType.Should().Be(expectedContentType);
        }
    }

    [Fact]
    public async Task DownloadContent_EmptyItemId_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.GetAsync("/api/obo/drives/test-drive/items//content");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public async Task FileOperations_IntegrationTest_RenameAndDelete()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // 1. Rename the file
        var renameRequest = new UpdateFileRequest("integration-test.txt");
        var json = JsonConvert.SerializeObject(renameRequest);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var renameResponse = await _client.PatchAsync("/api/obo/drives/test-drive/items/integration-item", content);
        renameResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var renamedItem = JsonConvert.DeserializeObject<DriveItemDto>(await renameResponse.Content.ReadAsStringAsync());
        renamedItem!.Name.Should().Be("integration-test.txt");

        // 2. Download the renamed file
        var downloadResponse = await _client.GetAsync("/api/obo/drives/test-drive/items/integration-item/content");
        downloadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Delete the file
        var deleteResponse = await _client.DeleteAsync("/api/obo/drives/test-drive/items/integration-item");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    #endregion
}