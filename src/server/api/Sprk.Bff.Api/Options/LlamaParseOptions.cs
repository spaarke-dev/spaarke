namespace Sprk.Bff.Api.Configuration;

public class LlamaParseOptions
{
    public string ApiKeySecretName { get; init; } = string.Empty;
    public string BaseUrl { get; init; } = "https://api.cloud.llamaindex.ai";
    public int ParseTimeoutSeconds { get; init; } = 120;
    public int MaxPages { get; init; } = 500;
    public bool Enabled { get; init; } = false;
}
