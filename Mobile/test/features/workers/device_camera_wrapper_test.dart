import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:image_picker/image_picker.dart';
import 'package:mobile/core/device/camera_wrapper.dart';
import 'package:mobile/core/device/device_camera_wrapper.dart';

/// MOB-PHOTO-01: the real camera and library sit behind [ICameraWrapper]; the mock-able [CameraWrapper] is untouched.
void main() {
  final jpeg = Uint8List.fromList([0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3]);

  group('DeviceCameraWrapper', () {
    test(
      'capturePhoto opens the camera and returns the real bytes and path',
      () async {
        ImageSource? asked;
        final wrapper = DeviceCameraWrapper(
          pick: (source) async {
            asked = source;
            return XFile.fromData(
              jpeg,
              path: '/data/user/0/app/cache/IMG_1.jpg',
            );
          },
        );

        final photo = await wrapper.capturePhoto();

        expect(asked, ImageSource.camera);
        expect(photo!.bytes, jpeg);
        expect(photo.path, '/data/user/0/app/cache/IMG_1.jpg');
        expect(photo.path, isNot(contains('/mock/')));
      },
    );

    test('pickFromGallery opens the photo library', () async {
      ImageSource? asked;
      final wrapper = DeviceCameraWrapper(
        pick: (source) async {
          asked = source;
          return XFile.fromData(jpeg, path: 'library.jpg');
        },
      );

      final photo = await wrapper.pickFromGallery();

      expect(asked, ImageSource.gallery);
      expect(photo!.bytes, jpeg);
    });

    test('cancelling the picker gives null, not a mock photo', () async {
      final wrapper = DeviceCameraWrapper(pick: (_) async => null);

      expect(await wrapper.capturePhoto(), isNull);
      expect(await wrapper.pickFromGallery(), isNull);
    });

    test('a refused permission is thrown to the caller', () async {
      final wrapper = DeviceCameraWrapper(
        pick: (_) async => throw StateError('camera_access_denied'),
      );

      expect(wrapper.capturePhoto(), throwsA(isA<StateError>()));
    });

    test('the picture is scaled and compressed so it stays under the 5 MB the server accepts', () {
      expect(DeviceCameraWrapper.maxWidthPx, 1920);
      expect(DeviceCameraWrapper.jpegQuality, 85);
    });
  });

  group('the mock CameraWrapper keeps working for tests', () {
    test('the bare constructor still answers with the mock path', () async {
      final photo = await CameraWrapper().capturePhoto();

      expect(photo!.path, contains('/mock/photos/'));
    });

    test('an injected image is returned', () async {
      final mock = CapturedImage(
        path: '/test/before.jpg',
        bytes: jpeg,
        capturedAt: DateTime.utc(2026, 10, 21),
      );

      expect(
        (await CameraWrapper(mockImage: mock).capturePhoto())!.bytes,
        jpeg,
      );
    });
  });

  group('imageContentTypeOf', () {
    test('knows JPEG, PNG and WebP by their first bytes', () {
      expect(imageContentTypeOf(jpeg), 'image/jpeg');
      expect(
        imageContentTypeOf(
          Uint8List.fromList([
            0x89,
            0x50,
            0x4E,
            0x47,
            0x0D,
            0x0A,
            0x1A,
            0x0A,
            0,
          ]),
        ),
        'image/png',
      );
      expect(
        imageContentTypeOf(
          Uint8List.fromList([
            0x52,
            0x49,
            0x46,
            0x46,
            1,
            2,
            3,
            4,
            0x57,
            0x45,
            0x42,
            0x50,
          ]),
        ),
        'image/webp',
      );
    });

    test('refuses anything else, including HEIC and empty bytes', () {
      expect(imageContentTypeOf(Uint8List(0)), isNull);
      expect(
        imageContentTypeOf(Uint8List.fromList('hello world'.codeUnits)),
        isNull,
      );
      // an ISO base media file ("ftypheic"), the format an iPhone may keep
      expect(
        imageContentTypeOf(
          Uint8List.fromList([
            0,
            0,
            0,
            0x18,
            0x66,
            0x74,
            0x79,
            0x70,
            0x68,
            0x65,
            0x69,
            0x63,
          ]),
        ),
        isNull,
      );
    });
  });
}
