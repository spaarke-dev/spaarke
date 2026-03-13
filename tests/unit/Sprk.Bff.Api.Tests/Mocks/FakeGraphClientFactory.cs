using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Graph;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Tests.Mocks;

/// <summary>
/// Fake IGraphClientFactory for unit/integration tests using CustomWebAppFactory.
///
/// Returns a GraphServiceClient backed by a <see cref="FakeGraphHttpHandler"/> that
/// intercepts ALL outgoing HTTP requests and returns stub JSON responses instead of
/// calling real Microsoft Graph / Azure AD endpoints.
///
/// This prevents:
///   - MsalServiceException from real OBO token exchange (mapped to 401 by global exception handler)
///   - ODataError from real Graph API calls with fake tokens (also mapped to 401)
///
/// ForUserAsync still validates the Authorization header via TokenHelper.ExtractBearerToken
/// so that endpoints without RequireAuthorization() (e.g., OBO endpoints) correctly return
/// 401 when no bearer token is present.
///
/// For tests that need specific Graph response data, consider creating a dedicated test
/// fixture that configures the handler responses.
/// </summary>
public sealed class FakeGraphClientFactory : IGraphClientFactory
{
    public Task<GraphServiceClient> ForUserAsync(HttpContext ctx, CancellationToken ct = default)
    {
        // Validate bearer token presence (throws UnauthorizedAccessException if missing).
        // This preserves the same auth-check behavior as the real GraphClientFactory.ForUserAsync,
        // which calls TokenHelper.ExtractBearerToken before attempting OBO exchange.
        // Without this, OBO endpoints (which lack RequireAuthorization()) would return 200
        // even when no Authorization header is present.
        _ = TokenHelper.ExtractBearerToken(ctx);

        return Task.FromResult(CreateFakeClient());
    }

    public GraphServiceClient ForApp()
    {
        return CreateFakeClient();
    }

    private static GraphServiceClient CreateFakeClient()
    {
        var handler = new FakeGraphHttpHandler();
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://graph.microsoft.com/v1.0/")
        };
        return new GraphServiceClient(httpClient);
    }
}

/// <summary>
/// HTTP message handler that returns stub Graph API JSON responses for any request.
///
/// Routes (matched top-to-bottom, first match wins):
///   - /me → User resource with test identity
///   - /containers/{id}/drive → Drive resource with test drive ID
///   - /drives/{driveId}/items (not /content) → DriveItem collection with 3 test items
///   - /drives/{driveId}/items/{itemId}/content → Fake file binary
///   - /uploadSessions, /createUploadSession → Upload session response
///   - /users/ → User collection (empty)
///   - PATCH/DELETE on /drives/ → Success (200/204)
///   - All other GET requests → Empty JSON object with 200 OK
///
/// This is intentionally simple: it unblocks auth-gated integration tests by preventing
/// real HTTP calls. Tests that need richer Graph response data should use WireMock or
/// a purpose-built fixture.
/// </summary>
internal sealed class FakeGraphHttpHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var url = request.RequestUri?.PathAndQuery ?? string.Empty;
        var method = request.Method;

        string json;

        // ---------------------------------------------------------------
        // /me → Current user info (used by UserEndpoints /api/me)
        // ---------------------------------------------------------------
        if (url.Contains("/me", StringComparison.OrdinalIgnoreCase) &&
            !url.Contains("/members", StringComparison.OrdinalIgnoreCase))
        {
            json = """
            {
                "id": "test-user-00000000-0000-0000-0000-000000000001",
                "displayName": "Test User",
                "userPrincipalName": "testuser@spaarke.onmicrosoft.com",
                "mail": "testuser@spaarke.onmicrosoft.com"
            }
            """;
        }
        // ---------------------------------------------------------------
        // Container → Drive resolution
        // ---------------------------------------------------------------
        else if (url.Contains("/drive", StringComparison.OrdinalIgnoreCase) &&
                 url.Contains("/containers/", StringComparison.OrdinalIgnoreCase))
        {
            json = """
            {
                "id": "fake-drive-id-001",
                "driveType": "documentLibrary",
                "name": "Test Drive"
            }
            """;
        }
        // ---------------------------------------------------------------
        // DriveItem collection (file listing)
        // ---------------------------------------------------------------
        else if (url.Contains("/drives/", StringComparison.OrdinalIgnoreCase) &&
                 url.Contains("/items", StringComparison.OrdinalIgnoreCase) &&
                 !url.Contains("/content", StringComparison.OrdinalIgnoreCase) &&
                 method == HttpMethod.Get)
        {
            json = """
            {
                "@odata.context": "https://graph.microsoft.com/v1.0/$metadata#drives('fake-drive-id-001')/items",
                "value": [
                    {
                        "id": "fake-item-001",
                        "name": "Document A.pdf",
                        "size": 1024,
                        "eTag": "\"etag-001\"",
                        "lastModifiedDateTime": "2026-01-15T10:30:00Z",
                        "file": { "mimeType": "application/pdf" }
                    },
                    {
                        "id": "fake-item-002",
                        "name": "Document B.docx",
                        "size": 2048,
                        "eTag": "\"etag-002\"",
                        "lastModifiedDateTime": "2026-01-16T14:00:00Z",
                        "file": { "mimeType": "application/vnd.openxmlformats-officedocument.wordprocessingml.document" }
                    },
                    {
                        "id": "fake-item-003",
                        "name": "Folder A",
                        "eTag": "\"etag-003\"",
                        "lastModifiedDateTime": "2026-01-14T08:00:00Z",
                        "folder": { "childCount": 3 }
                    }
                ]
            }
            """;
        }
        // ---------------------------------------------------------------
        // File content download
        // ---------------------------------------------------------------
        else if (url.Contains("/content", StringComparison.OrdinalIgnoreCase))
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(Encoding.UTF8.GetBytes("fake file content"))
            };
            response.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
            response.Headers.ETag =
                new System.Net.Http.Headers.EntityTagHeaderValue("\"fake-etag\"");
            return Task.FromResult(response);
        }
        // ---------------------------------------------------------------
        // Upload session creation
        // ---------------------------------------------------------------
        else if (url.Contains("/uploadSessions", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("/upload-session", StringComparison.OrdinalIgnoreCase) ||
                 url.Contains("/createUploadSession", StringComparison.OrdinalIgnoreCase))
        {
            json = """
            {
                "uploadUrl": "https://graph.microsoft.com/v1.0/fake-upload-url",
                "expirationDateTime": "2026-12-31T23:59:59Z"
            }
            """;
        }
        // ---------------------------------------------------------------
        // PATCH on drive items (rename/move)
        // ---------------------------------------------------------------
        else if (url.Contains("/drives/", StringComparison.OrdinalIgnoreCase) &&
                 method == HttpMethod.Patch)
        {
            json = """
            {
                "id": "fake-item-001",
                "name": "Renamed Document.pdf",
                "size": 1024,
                "eTag": "\"etag-updated\"",
                "lastModifiedDateTime": "2026-03-13T10:00:00Z",
                "file": { "mimeType": "application/pdf" }
            }
            """;
        }
        // ---------------------------------------------------------------
        // DELETE on drive items
        // ---------------------------------------------------------------
        else if (url.Contains("/drives/", StringComparison.OrdinalIgnoreCase) &&
                 method == HttpMethod.Delete)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
        // ---------------------------------------------------------------
        // PUT for small file upload (OBO)
        // ---------------------------------------------------------------
        else if (method == HttpMethod.Put &&
                 url.Contains("/drives/", StringComparison.OrdinalIgnoreCase))
        {
            json = """
            {
                "id": "fake-uploaded-item",
                "name": "uploaded-file.txt",
                "size": 256,
                "eTag": "\"etag-new\"",
                "lastModifiedDateTime": "2026-03-13T12:00:00Z",
                "file": { "mimeType": "text/plain" }
            }
            """;
        }
        // ---------------------------------------------------------------
        // Container listing
        // ---------------------------------------------------------------
        else if (url.Contains("/containers", StringComparison.OrdinalIgnoreCase) &&
                 method == HttpMethod.Get)
        {
            json = """
            {
                "value": [
                    {
                        "id": "fake-container-001",
                        "displayName": "Test Container",
                        "containerTypeId": "00000000-0000-0000-0000-000000000001",
                        "status": "active"
                    }
                ]
            }
            """;
        }
        // ---------------------------------------------------------------
        // Default: empty JSON object for any other request
        // ---------------------------------------------------------------
        else
        {
            json = "{}";
        }

        var result = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return Task.FromResult(result);
    }
}
