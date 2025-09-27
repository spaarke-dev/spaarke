using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using FluentAssertions;
using Newtonsoft.Json;
using Spe.Bff.Api.Models;
using Xunit;

namespace Spe.Bff.Api.Tests;

public class UploadEndpointsTests : IClassFixture<CustomWebAppFactory>
{
    private readonly HttpClient _client;

    public UploadEndpointsTests(CustomWebAppFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task CreateUploadSession_WithoutBearer_Returns401()
    {
        var response = await _client.PostAsync("/api/obo/drives/test-drive/upload-session?path=test.txt", null);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateUploadSession_WithoutPath_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.PostAsync("/api/obo/drives/test-drive/upload-session", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateUploadSession_WithValidParams_ReturnsSessionInfo()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var response = await _client.PostAsync("/api/obo/drives/test-drive/upload-session?path=test.txt&conflictBehavior=replace", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsStringAsync();
        var session = JsonConvert.DeserializeObject<UploadSessionResponse>(content);

        session.Should().NotBeNull();
        session!.UploadUrl.Should().NotBeNullOrEmpty();
        session.ExpirationDateTime.Should().BeAfter(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task CreateUploadSession_WithDifferentConflictBehaviors_ReturnsSession()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var behaviors = new[] { "replace", "rename", "fail" };

        foreach (var behavior in behaviors)
        {
            var response = await _client.PostAsync($"/api/obo/drives/test-drive/upload-session?path=test.txt&conflictBehavior={behavior}", null);

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var content = await response.Content.ReadAsStringAsync();
            var session = JsonConvert.DeserializeObject<UploadSessionResponse>(content);

            session.Should().NotBeNull();
            session!.UploadUrl.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task UploadChunk_WithoutBearer_Returns401()
    {
        var chunkData = CreateChunkData(8 * 1024 * 1024); // 8 MiB
        var content = new ByteArrayContent(chunkData);

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UploadChunk_WithoutUploadSessionUrl_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var chunkData = CreateChunkData(8 * 1024 * 1024);
        var content = new ByteArrayContent(chunkData);
        content.Headers.Add("Content-Range", "bytes 0-8388607/16777216");

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadChunk_WithoutContentRange_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var chunkData = CreateChunkData(8 * 1024 * 1024);
        var content = new ByteArrayContent(chunkData);
        content.Headers.Add("Upload-Session-Url", "https://graph.microsoft.com/v1.0/me/drive/items/test/uploadSession");

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadChunk_ValidIntermediateChunk_Returns202()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var chunkData = CreateChunkData(8 * 1024 * 1024); // 8 MiB
        var content = new ByteArrayContent(chunkData);
        content.Headers.Add("Upload-Session-Url", "https://graph.microsoft.com/v1.0/me/drive/items/test/uploadSession");
        content.Headers.Add("Content-Range", "bytes 0-8388607/16777216"); // First chunk of 16 MiB file

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);

        response.StatusCode.Should().Be(HttpStatusCode.Accepted); // 202 - more chunks expected
    }

    [Fact]
    public async Task UploadChunk_FinalChunk_Returns201WithDriveItem()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var chunkData = CreateChunkData(1024 * 1024); // 1 MiB final chunk
        var content = new ByteArrayContent(chunkData);
        content.Headers.Add("Upload-Session-Url", "https://graph.microsoft.com/v1.0/me/drive/items/test/uploadSession");
        content.Headers.Add("Content-Range", "bytes 8388608-9437183/9437184"); // Final chunk

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);

        response.StatusCode.Should().Be(HttpStatusCode.Created); // 201 - upload complete

        var responseContent = await response.Content.ReadAsStringAsync();
        var item = JsonConvert.DeserializeObject<DriveItemDto>(responseContent);

        item.Should().NotBeNull();
        item!.Id.Should().NotBeNullOrEmpty();
        item.Name.Should().NotBeNullOrEmpty();
        item.Size.Should().Be(9437184); // Total file size
    }

    [Fact]
    public async Task UploadChunk_ChunkTooLarge_Returns413()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var chunkData = CreateChunkData(11 * 1024 * 1024); // 11 MiB (exceeds 10 MiB limit)
        var content = new ByteArrayContent(chunkData);
        content.Headers.Add("Upload-Session-Url", "https://graph.microsoft.com/v1.0/me/drive/items/test/uploadSession");
        content.Headers.Add("Content-Range", "bytes 0-11534335/23068672");

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge); // 413
    }

    [Fact]
    public async Task UploadChunk_ChunkTooSmall_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var chunkData = CreateChunkData(1024 * 1024); // 1 MiB (below 8 MiB minimum for non-final chunk)
        var content = new ByteArrayContent(chunkData);
        content.Headers.Add("Upload-Session-Url", "https://graph.microsoft.com/v1.0/me/drive/items/test/uploadSession");
        content.Headers.Add("Content-Range", "bytes 0-1048575/16777216"); // Not final chunk

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest); // 400
    }

    [Fact]
    public async Task UploadChunk_InvalidContentRange_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var chunkData = CreateChunkData(8 * 1024 * 1024);
        var content = new ByteArrayContent(chunkData);
        content.Headers.Add("Upload-Session-Url", "https://graph.microsoft.com/v1.0/me/drive/items/test/uploadSession");
        content.Headers.Add("Content-Range", "invalid-range");

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadChunk_EmptyBody_Returns400()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        var content = new ByteArrayContent(Array.Empty<byte>());
        content.Headers.Add("Upload-Session-Url", "https://graph.microsoft.com/v1.0/me/drive/items/test/uploadSession");
        content.Headers.Add("Content-Range", "bytes 0-0/1");

        var response = await _client.PutAsync("/api/obo/upload-session/chunk", content);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadSession_HappyPath_CreatesSessionThenUploadsChunks()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");

        // 1. Create upload session
        var sessionResponse = await _client.PostAsync("/api/obo/drives/test-drive/upload-session?path=largefile.bin&conflictBehavior=replace", null);
        sessionResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var sessionContent = await sessionResponse.Content.ReadAsStringAsync();
        var session = JsonConvert.DeserializeObject<UploadSessionResponse>(sessionContent);
        session.Should().NotBeNull();

        // 2. Upload first chunk (intermediate)
        var firstChunk = CreateChunkData(8 * 1024 * 1024); // 8 MiB
        var firstContent = new ByteArrayContent(firstChunk);
        firstContent.Headers.Add("Upload-Session-Url", session!.UploadUrl);
        firstContent.Headers.Add("Content-Range", "bytes 0-8388607/10485760"); // 8 MiB of 10 MiB total

        var firstResponse = await _client.PutAsync("/api/obo/upload-session/chunk", firstContent);
        firstResponse.StatusCode.Should().Be(HttpStatusCode.Accepted); // 202

        // 3. Upload final chunk
        var finalChunk = CreateChunkData(2 * 1024 * 1024); // 2 MiB
        var finalContent = new ByteArrayContent(finalChunk);
        finalContent.Headers.Add("Upload-Session-Url", session.UploadUrl);
        finalContent.Headers.Add("Content-Range", "bytes 8388608-10485759/10485760"); // Final 2 MiB

        var finalResponse = await _client.PutAsync("/api/obo/upload-session/chunk", finalContent);
        finalResponse.StatusCode.Should().Be(HttpStatusCode.Created); // 201 - completed

        var finalResponseContent = await finalResponse.Content.ReadAsStringAsync();
        var completedItem = JsonConvert.DeserializeObject<DriveItemDto>(finalResponseContent);
        completedItem.Should().NotBeNull();
        completedItem!.Size.Should().Be(10485760); // 10 MiB total
    }

    private static byte[] CreateChunkData(int size)
    {
        var data = new byte[size];
        var random = new Random(42); // Fixed seed for consistent tests
        random.NextBytes(data);
        return data;
    }
}