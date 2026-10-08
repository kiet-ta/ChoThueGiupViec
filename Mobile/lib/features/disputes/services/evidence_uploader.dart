/// Puts one evidence photo on the server and returns the address to send in `evidenceUrls`.
///
/// The contract says no upload endpoint exists yet (disputes.md 2.1) and the app has no photo picker package,
/// so the production implementation below says so instead of pretending. When a leader-approved upload endpoint and
/// picker exist, only this class changes.
abstract class IEvidenceUploader {
  /// False when photos cannot be attached at all; the form then explains it and does not offer to send.
  bool get isAvailable;

  /// Lets the person pick a photo and uploads it; null when the person cancelled.
  Future<String?> pickAndUpload();
}

/// The honest production implementation until an upload endpoint exists.
class UnavailableEvidenceUploader implements IEvidenceUploader {
  const UnavailableEvidenceUploader();

  @override
  bool get isAvailable => false;

  @override
  Future<String?> pickAndUpload() async => null;
}
