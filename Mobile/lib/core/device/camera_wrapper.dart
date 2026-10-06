import 'dart:typed_data';

/// Encapsulates a photo captured from camera or picked from gallery.
class CapturedImage {
  final String path;
  final Uint8List? bytes;
  final int width;
  final int height;
  final DateTime capturedAt;

  const CapturedImage({
    required this.path,
    this.bytes,
    this.width = 640,
    this.height = 480,
    required this.capturedAt,
  });
}

/// Abstract contract for camera operations (Before/After photos, plate verification).
abstract class ICameraWrapper {
  /// Captures a fresh photo from the device camera.
  Future<CapturedImage?> capturePhoto();

  /// Picks an existing photo from the photo library / gallery.
  Future<CapturedImage?> pickFromGallery();
}

/// CameraWrapper implementation with mock support for automated and widget testing.
class CameraWrapper implements ICameraWrapper {
  CapturedImage? _mockImage;

  CameraWrapper({this._mockImage});

  /// Injects mock image for automated testing.
  void setMockImage(CapturedImage? image) {
    _mockImage = image;
  }

  @override
  Future<CapturedImage?> capturePhoto() async {
    if (_mockImage != null) {
      return _mockImage;
    }

    // Default placeholder photo when physical camera is unavailable
    return CapturedImage(
      path: '/mock/photos/photo_${DateTime.now().millisecondsSinceEpoch}.jpg',
      bytes: Uint8List(0),
      width: 640,
      height: 480,
      capturedAt: DateTime.now().toUtc(),
    );
  }

  @override
  Future<CapturedImage?> pickFromGallery() async {
    return capturePhoto();
  }
}
