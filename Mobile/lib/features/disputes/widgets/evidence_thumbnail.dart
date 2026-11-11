import 'package:flutter/material.dart';

import '../../../core/network/api_config.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_typography.dart';

/// The address to load an evidence photo from, or null when it cannot be one.
///
/// The upload answers with a path on the server (`/files/dispute-evidence/<name>`, contract disputes.md 2.1a); it is made
/// absolute with the API base address. A full `http(s)` address is used as it is. The file route is anonymous, so no token is
/// sent: the photo may live on another host, and the Bearer token must never go there.
String? evidenceImageUrl(String url, {String baseUrl = apiBaseUrl}) {
  final text = url.trim();
  if (text.startsWith('https://') || text.startsWith('http://')) return text;
  if (text.startsWith('/files/')) return '$baseUrl$text';
  return null;
}

/// One uploaded evidence photo on the dispute form: the photo itself, with the grey "Ảnh n" tile while it loads and when it
/// cannot be read, so the remove button next to it always has something to sit on.
class EvidenceThumbnail extends StatelessWidget {
  final String url;
  final int index;

  const EvidenceThumbnail({super.key, required this.url, required this.index});

  @override
  Widget build(BuildContext context) {
    final source = evidenceImageUrl(url);
    if (source == null) return _placeholder();

    return ClipRRect(
      borderRadius: NordicRadius.tileAll,
      child: Image.network(
        source,
        key: Key('dispute-evidence-image-$index'),
        fit: BoxFit.cover,
        width: double.infinity,
        height: double.infinity,
        semanticLabel: 'Ảnh bằng chứng ${index + 1}',
        loadingBuilder: (_, child, progress) =>
            progress == null ? child : _placeholder(),
        errorBuilder: (_, _, _) => _placeholder(),
      ),
    );
  }

  Widget _placeholder() {
    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          const Icon(Icons.image_outlined, color: NordicColors.textSecondary),
          Text('Ảnh ${index + 1}', style: NordicTypography.bodySmall),
        ],
      ),
    );
  }
}
