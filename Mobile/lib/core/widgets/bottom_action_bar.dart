import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';

/// ui-templates `.btn-bar`: the main buttons of a detail screen, pinned at the bottom.
/// Use as `Scaffold.bottomNavigationBar`.
class BottomActionBar extends StatelessWidget {
  final List<Widget> children;

  const BottomActionBar({super.key, required this.children});

  @override
  Widget build(BuildContext context) {
    return Container(
      color: NordicColors.canvas,
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 12),
          child: Row(
            children: [
              for (var i = 0; i < children.length; i++) ...[
                if (i > 0) const SizedBox(width: 10),
                Expanded(child: children[i]),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
