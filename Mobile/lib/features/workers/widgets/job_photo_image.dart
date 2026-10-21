import 'package:flutter/material.dart';

import '../models/job_photo_models.dart';
import '../services/workers_photos_service.dart';

/// A stored job photo, loaded from the authenticated file endpoint (contract workers.md §2.4.3) with the Bearer token.
///
/// A photo without a stored file (an old client URL) and a photo that cannot be loaded show the same quiet placeholder,
/// so a missing image never breaks the screen.
class JobPhotoImage extends StatelessWidget {
  final WorkersPhotosService service;
  final JobPhotoDto? photo;
  final double? height;
  final double? width;

  /// A thumbnail in a list row: the placeholder is the icon alone.
  final bool compact;

  const JobPhotoImage({
    super.key,
    required this.service,
    required this.photo,
    this.height,
    this.width,
    this.compact = false,
  });

  @override
  Widget build(BuildContext context) {
    final photo = this.photo;
    final source = photo == null ? null : service.photoImage(photo.photoUrl);
    if (source == null) return _placeholder(photo == null);

    return Image.network(
      source.url,
      headers: source.headers,
      height: height,
      width: width,
      fit: BoxFit.cover,
      errorBuilder: (_, _, _) => _placeholder(false),
    );
  }

  Widget _placeholder(bool noPhoto) {
    return SizedBox(
      height: height,
      width: width,
      child: Center(
        child: compact
            ? const Icon(Icons.image, size: 24.0, color: Color(0xFF94A3B8))
            : Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  const Icon(Icons.image, size: 36.0, color: Color(0xFF94A3B8)),
                  const SizedBox(height: 4.0),
                  Text(
                    noPhoto ? 'Chưa có ảnh' : 'Không tải được ảnh',
                    style: const TextStyle(
                      fontSize: 10.0,
                      color: Color(0xFFCBD5E1),
                    ),
                  ),
                ],
              ),
      ),
    );
  }
}
