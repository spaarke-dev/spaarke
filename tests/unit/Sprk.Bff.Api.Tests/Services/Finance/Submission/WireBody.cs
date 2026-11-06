using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Sprk.Bff.Api.Tests.Services.Finance.Submission;

/// <summary>
/// ebilling-r1 task 095 contract fix (2026-10-29): an <see cref="IResult"/> as it goes on the wire. A test that asserts a refusal's
/// <c>Code</c> on an exception or a typed result cannot see where the code lands in the JSON body; this executes the result and parses
/// the body, so the assertion is on what a client reads (<c>errorCode ?? reasonCode</c>, contract section 6).
/// </summary>
internal static class WireBody
{
    /// <summary>The status and the parsed JSON body of <paramref name="result"/> as the framework writes it.</summary>
    public static async Task<(int Status, JsonElement Body)> OfAsync(IResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var http = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        http.Response.Body = new MemoryStream();

        await result.ExecuteAsync(http);

        http.Response.Body.Position = 0;
        using var doc = await JsonDocument.ParseAsync(http.Response.Body);
        return (http.Response.StatusCode, doc.RootElement.Clone());
    }

    /// <summary>The contract's client rule: <c>errorCode ?? reasonCode</c>.</summary>
    public static string? Code(JsonElement body) =>
        body.TryGetProperty("errorCode", out var errorCode) ? errorCode.GetString()
            : body.TryGetProperty("reasonCode", out var reasonCode) ? reasonCode.GetString() : null;
}
