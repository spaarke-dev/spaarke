namespace Spe.Bff.Api.Models;

public record UserInfoResponse(
    string DisplayName,
    string UserPrincipalName,
    string Oid
);

public record UserCapabilitiesResponse(
    bool Read,
    bool Write,
    bool Delete,
    bool CreateFolder
);

public record CacheKeyCapabilities(string UserId, string ContainerId);