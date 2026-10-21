import 'dart:typed_data';

import 'package:image_picker/image_picker.dart';

import 'camera_wrapper.dart';

/// Opens the platform picker for [source]; the seam that lets tests run without the `image_picker` plugin.
typedef PickImage = Future<XFile?> Function(ImageSource source);

/// The real camera and photo library (`image_picker`), behind [ICameraWrapper].
///
/// [CameraWrapper] stays the mock-able wrapper that Dispatch, Disputes and their tests use; a screen that needs the real
/// device passes this one. The picture is scaled to [maxWidthPx] and re-encoded as JPEG at [jpegQuality]: a phone photo is
/// often above the 5 MB the server accepts, and the server measures sharpness on a 640 px copy anyway.
class DeviceCameraWrapper implements ICameraWrapper {
  static const double maxWidthPx = 1920;
  static const int jpegQuality = 85;

  final PickImage _pick;

  DeviceCameraWrapper({PickImage? pick}) : _pick = pick ?? _pickWithPlugin;

  static Future<XFile?> _pickWithPlugin(ImageSource source) =>
      ImagePicker().pickImage(
        source: source,
        maxWidth: maxWidthPx,
        imageQuality: jpegQuality,
      );

  @override
  Future<CapturedImage?> capturePhoto() => _read(ImageSource.camera);

  @override
  Future<CapturedImage?> pickFromGallery() => _read(ImageSource.gallery);

  /// Null when the person cancelled; a platform error (camera or library permission denied) is thrown to the caller.
  Future<CapturedImage?> _read(ImageSource source) async {
    final file = await _pick(source);
    if (file == null) return null;
    final Uint8List bytes = await file.readAsBytes();
    return CapturedImage(
      path: file.path,
      bytes: bytes,
      width: 0,
      height: 0,
      capturedAt: DateTime.now().toUtc(),
    );
  }
}

/// The content type from the first bytes (the server checks the same signatures), or null when it is not JPEG, PNG or WebP.
String? imageContentTypeOf(Uint8List b) {
  if (b.length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) {
    return 'image/jpeg';
  }
  const png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
  if (b.length >= png.length && _startsWith(b, png)) return 'image/png';
  if (b.length >= 12 &&
      _startsWith(b, const [0x52, 0x49, 0x46, 0x46]) &&
      b[8] == 0x57 &&
      b[9] == 0x45 &&
      b[10] == 0x42 &&
      b[11] == 0x50) {
    return 'image/webp';
  }
  return null;
}

bool _startsWith(Uint8List b, List<int> prefix) {
  for (var i = 0; i < prefix.length; i++) {
    if (b[i] != prefix[i]) return false;
  }
  return true;
}
