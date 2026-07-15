using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Channels;

/// <summary>
/// Channel-extensibility seam for the "archive" leg of a communication (ADR-045 rule 4 / NFR-04).
/// One implementation per <see cref="CommunicationType"/>; <see cref="EmailArchiver"/> is the sole R4
/// implementation (wraps RFC-2822 <c>.eml</c> generation). A future channel supplies its own archiver
/// keyed to its own <see cref="SupportedType"/> without any change to <see cref="CommunicationService"/>,
/// the Association Engine, the enrichment service, the regarding model, or the review UI.
/// </summary>
public interface ICommunicationArchiver
{
    /// <summary>The <see cref="CommunicationType"/> this archiver handles (dispatch key).</summary>
    CommunicationType SupportedType { get; }

    /// <summary>
    /// Produces the archival artifact (bytes + suggested filename) for the sent communication. For email
    /// this is an RFC-2822 <c>.eml</c>; another channel would emit its own portable transcript format.
    /// </summary>
    EmlResult GenerateEml(
        SendCommunicationRequest request,
        SendCommunicationResponse response,
        IReadOnlyList<EmlAttachment>? attachments = null);
}
