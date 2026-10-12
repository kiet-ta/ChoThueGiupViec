import 'dart:convert';
import 'dart:typed_data';
import '../../../core/device/camera_wrapper.dart';
import '../../../core/network/api_client.dart';
import '../models/dispute_models.dart';
import 'evidence_uploader.dart';

/// Takes a photo with the shared [ICameraWrapper] and uploads it to `POST .../disputes/evidence` (contract disputes.md 2.1a),
/// returning the `url` to send in `evidenceUrls`. Failures are thrown as [ApiException] so the form can show the message.
class ApiEvidenceUploader implements IEvidenceUploader {
  final DisputeRole role;
  final ICameraWrapper _camera;
  final ApiClient _client;

  ApiEvidenceUploader({required this.role, ICameraWrapper? camera, ApiClient? client})
      : _camera = camera ?? CameraWrapper(),
        _client = client ?? ApiClient();

  @override
  bool get isAvailable => true;

  @override
  Future<String?> pickAndUpload() async {
    final photo = await _camera.capturePhoto();
    if (photo == null) return null; // the person cancelled

    final bytes = photo.bytes;
    if (bytes == null || bytes.isEmpty) {
      throw const ApiException(0, 'Không đọc được ảnh vừa chụp. Hãy chụp lại.');
    }
    final type = imageContentType(bytes);
    if (type == null) {
      throw const ApiException(0, 'Chỉ nhận ảnh JPEG, PNG hoặc WebP.');
    }

    final response = await _client.post<String>(
      '${role.pathPrefix}/disputes/evidence',
      body: {
        'fileName': fileNameOf(photo.path, type),
        'contentType': type,
        'contentBase64': base64Encode(bytes),
      },
      fromJson: (json) => (json as Map<String, dynamic>)['url'] as String? ?? '',
    );
    final url = response.data;
    if (url == null || url.isEmpty) {
      throw const ApiException(0, 'Máy chủ không trả địa chỉ của ảnh.');
    }
    return url;
  }
}

/// The content type from the first bytes (the server checks the same signatures), or null when it is not JPEG, PNG or WebP.
String? imageContentType(Uint8List b) {
  if (b.length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return 'image/jpeg';
  if (b.length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) {
    return 'image/png';
  }
  if (b.length >= 12 && b[0] == 0x52 && b[1] == 0x49 && b[2] == 0x46 && b[3] == 0x46 && b[8] == 0x57 && b[9] == 0x45 && b[10] == 0x42 && b[11] == 0x50) {
    return 'image/webp';
  }
  return null;
}

/// The last segment of a device path, at most 100 characters, or `photo.<ext>` when there is none.
String fileNameOf(String path, String contentType) {
  final ext = contentType == 'image/png' ? 'png' : contentType == 'image/webp' ? 'webp' : 'jpg';
  final name = path.split(RegExp(r'[\\/]')).where((s) => s.trim().isNotEmpty).lastOrNull?.trim();
  if (name == null) return 'photo.$ext';
  return name.length <= 100 ? name : name.substring(name.length - 100);
}
