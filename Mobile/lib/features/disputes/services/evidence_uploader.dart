/// Puts one evidence photo on the server and returns the address to send in `evidenceUrls`.
///
/// The production implementation is [ApiEvidenceUploader] (contract disputes.md 2.1a). [UnavailableEvidenceUploader] is what a
/// build without any upload would use: the form then explains it and does not offer to send.
abstract class IEvidenceUploader {
  /// False when photos cannot be attached at all; the form then explains it and does not offer to send.
  bool get isAvailable;

  /// Lets the person pick a photo and uploads it; null when the person cancelled.
  Future<String?> pickAndUpload();
}

/// For a build that cannot attach photos at all.
class UnavailableEvidenceUploader implements IEvidenceUploader {
  const UnavailableEvidenceUploader();

  @override
  bool get isAvailable => false;

  @override
  Future<String?> pickAndUpload() async => null;
}
