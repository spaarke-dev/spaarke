using Microsoft.Graph;

namespace Spe.Bff.Api.Infrastructure.Errors;

/// <summary>
/// Helper for creating RFC 7807 Problem Details responses with Graph error differentiation.
/// </summary>
public static class ProblemDetailsHelper
{
    public static IResult FromGraphException(ServiceException ex)
    {
        var status = ex.ResponseStatusCode;
        var title = status == 403 ? "forbidden" : status == 401 ? "unauthorized" : "error";
        var code = GetErrorCode(ex);
        var detail = (status == 403 && code.Contains("Authorization_RequestDenied", StringComparison.OrdinalIgnoreCase))
            ? "missing graph app role (filestoragecontainer.selected) for the api identity."
            : status == 403 ? "api identity lacks required container-type permission for this operation."
            : ex.Message;

        string? graphRequestId = null;
        try
        {
            graphRequestId = ex.ResponseHeaders?.GetValues("request-id")?.FirstOrDefault() ??
                           ex.ResponseHeaders?.GetValues("client-request-id")?.FirstOrDefault();
        }
        catch
        {
            // Ignore header access errors
        }

        return Results.Problem(
            title: title,
            detail: detail,
            statusCode: status,
            extensions: new Dictionary<string, object?>
            {
                ["graphErrorCode"] = code,
                ["graphRequestId"] = graphRequestId
            });
    }

    public static IResult ValidationProblem(Dictionary<string, string[]> errors)
    {
        return Results.ValidationProblem(errors);
    }

    public static IResult ValidationError(string detail)
    {
        return Results.Problem(
            title: "Validation Error",
            statusCode: 400,
            detail: detail
        );
    }

    private static string GetErrorCode(ServiceException ex)
    {
        return ex.Message?.Contains("Authorization_RequestDenied") == true ? "Authorization_RequestDenied" :
               ex.Message?.Contains("TooManyRequests") == true ? "TooManyRequests" :
               ex.Message?.Contains("Forbidden") == true ? "Forbidden" :
               ex.ResponseStatusCode.ToString();
    }
}