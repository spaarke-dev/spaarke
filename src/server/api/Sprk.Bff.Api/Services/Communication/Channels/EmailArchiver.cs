using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// Email implementation of <see cref="ICommunicationArchiver"/> (ADR-045 rule 4). The ONLY R4 channel
/// archiver. A thin adapter over the existing <see cref="EmlGenerationService"/> — <c>.eml</c> generation
/// behavior is unchanged; the seam simply routes archival through a <see cref="CommunicationType"/>-keyed
/// boundary so a future channel can supply its own transcript format without touching
/// <see cref="CommunicationService"/> (NFR-04).
/// </summary>
public sealed class EmailArchiver : ICommunicationArchiver
{
    private readonly EmlGenerationService _emlGenerationService;

    public EmailArchiver(EmlGenerationService emlGenerationService)
    {
        _emlGenerationService = emlGenerationService;
    }

    public CommunicationType SupportedType => CommunicationType.Email;

    public EmlResult GenerateEml(
        SendCommunicationRequest request,
        SendCommunicationResponse response,
        IReadOnlyList<EmlAttachment>? attachments = null)
        => _emlGenerationService.GenerateEml(request, response, attachments);
}
