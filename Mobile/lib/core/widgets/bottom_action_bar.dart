import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_spacing.dart';

/// ui-templates `.btn-bar`: the main buttons of a detail screen, pinned at the bottom.
/// Use as `Scaffold.bottomNavigationBar`.
class BottomActionBar extends StatelessWidget {
  final List<Widget> children;

  const BottomActionBar({super.key, required this.children});

  @override
  Widget build(BuildContext context) {
    return Container(
      decoration: const BoxDecoration(
        color: NordicColors.surface,
        border: Border(top: BorderSide(color: NordicColors.border)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
            NordicSpace.gutter,
            NordicSpace.sm,
            NordicSpace.gutter,
            NordicSpace.sm,
          ),
          child: Row(
            children: [
              for (var i = 0; i < children.length; i++) ...[
                if (i > 0) const SizedBox(width: NordicSpace.sm),
                Expanded(child: children[i]),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
