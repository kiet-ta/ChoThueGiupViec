import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';

/// Tone of a [StatusPill]; the label always carries the meaning, the color only supports it.
enum PillTone { neutral, ok, warn, bad }

/// ui-templates `.pill`: a small rounded label for a status.
class StatusPill extends StatelessWidget {
  final String label;
  final PillTone tone;
  final bool dot;

  /// Key of the label's [Text], for tests that read the shown status.
  final Key? textKey;

  const StatusPill(
    this.label, {
    super.key,
    this.tone = PillTone.neutral,
    this.dot = false,
    this.textKey,
  });

  (Color, Color) get _colors => switch (tone) {
    PillTone.neutral => (NordicColors.surfaceContainer, NordicColors.textTitle),
    PillTone.ok => (NordicColors.primaryFixed, NordicColors.primary),
    PillTone.warn => (NordicColors.amberSoft, NordicColors.onAmberSoft),
    PillTone.bad => (NordicColors.errorContainer, NordicColors.error),
  };

  @override
  Widget build(BuildContext context) {
    final (background, foreground) = _colors;
    return Container(
      height: 24,
      padding: const EdgeInsets.symmetric(horizontal: 10),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (dot) ...[
            Container(
              width: 6,
              height: 6,
              decoration: BoxDecoration(
                color: foreground,
                shape: BoxShape.circle,
              ),
            ),
            const SizedBox(width: 4),
          ],
          Flexible(
            child: Text(
              label,
              key: textKey,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                fontSize: 11,
                fontWeight: FontWeight.w600,
                color: foreground,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
