namespace CommonService.Application.Features.Workers.Dtos;

/// <summary>A photo as the controller received it (the part named <c>file</c> of the multipart request).</summary>
public sealed record UploadedPhotoFile(string FileName, string ContentType, Stream Content, long Length);

/// <summary>The bytes of a stored job photo and the content type to answer with.</summary>
public sealed record JobPhotoFile(Stream Content, string ContentType);
