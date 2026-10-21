namespace CommonService.Application.Features.Workers.Services;

/// <summary>
/// Whether the legacy JSON photo upload (<c>{ photoPhase, angleNo, photoUrl }</c>) is still accepted. It is only in the Development
/// environment, where the stand-in measurement (<see cref="JobPhotoStandIn"/>, decisions D18) keeps the UAT stack working until the Mobile
/// app sends real files; everywhere else the photo has to be sent as a file. Set once in <c>WorkersModule</c>.
/// </summary>
public sealed record JobPhotoUploadPolicy(bool AllowUrlPhotos);
