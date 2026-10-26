import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_typography.dart';

/// ui-templates `.section-title`: a 15 px title with an optional link or note on the right.
class SectionTitle extends StatelessWidget {
  final String title;

  /// Text on the right: a link when [onAction] is set, a muted note otherwise.
  final String? trailing;
  final VoidCallback? onAction;

  const SectionTitle(this.title, {super.key, this.trailing, this.onAction});

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 24, bottom: 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Expanded(child: Text(title, style: NordicTypography.titleSmall)),
          if (trailing != null)
            onAction == null
                ? Text(trailing!, style: NordicTypography.bodySmall)
                : InkWell(
                    onTap: onAction,
                    child: Text(
                      trailing!,
                      style: const TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                        color: NordicColors.primary,
                      ),
                    ),
                  ),
        ],
      ),
    );
  }
}
