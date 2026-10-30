import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_spacing.dart';
import '../theme/nordic_typography.dart';
import 'pressable.dart';

/// The deep-green header of the customer screens (`Mobile/DESIGN-SYSTEM.md` §4, customer redesign v2 §2.2):
/// it bleeds to the top edge (the status bar height is added here), has rounded bottom corners and three thin
/// rings in the top right corner. The bottom keeps [overlap] + [NordicSpace.section] free for a [RaisedPanel]
/// that is moved up over it with `Transform.translate(Offset(0, -overlap))`.
/// With [appBarTitle] it carries a back button (44x44, white at 8 %) and the title in the middle.
class DarkHeader extends StatelessWidget {
  final Widget child;
  final double overlap;
  final String? appBarTitle;
  final VoidCallback? onBack;

  const DarkHeader({
    super.key,
    required this.child,
    this.overlap = 64.0,
    this.appBarTitle,
    this.onBack,
  });

  @override
  Widget build(BuildContext context) {
    final top = MediaQuery.paddingOf(context).top;
    return Container(
      clipBehavior: Clip.antiAlias,
      decoration: const BoxDecoration(
        color: NordicColors.contrastDark,
        borderRadius: BorderRadius.vertical(
          bottom: Radius.circular(NordicRadius.sheet + 4),
        ),
      ),
      child: Stack(
        children: [
          const Positioned(
            right: -110,
            top: -90,
            child: IgnorePointer(
              child: ExcludeSemantics(
                child: CustomPaint(size: Size(300, 300), painter: _Rings()),
              ),
            ),
          ),
          Padding(
            padding: EdgeInsets.fromLTRB(
              NordicSpace.gutter,
              top + (appBarTitle == null ? NordicSpace.lg : NordicSpace.xs),
              NordicSpace.gutter,
              overlap + NordicSpace.section,
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (appBarTitle != null) ...[
                  _AppBar(title: appBarTitle!, onBack: onBack),
                  const SizedBox(height: NordicSpace.md),
                ],
                child,
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _AppBar extends StatelessWidget {
  final String title;
  final VoidCallback? onBack;

  const _AppBar({required this.title, this.onBack});

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: 56,
      child: Stack(
        alignment: Alignment.center,
        children: [
          Align(
            alignment: Alignment.centerLeft,
            child: Pressable(
              child: Semantics(
                button: true,
                label: 'Quay lại',
                child: Material(
                  color: NordicColors.textInverse.withValues(alpha: 0.08),
                  shape: const CircleBorder(),
                  clipBehavior: Clip.antiAlias,
                  child: InkWell(
                    onTap: onBack ?? () => Navigator.of(context).maybePop(),
                    child: const SizedBox(
                      width: 44,
                      height: 44,
                      child: ExcludeSemantics(
                        child: Icon(
                          Icons.arrow_back,
                          size: 22,
                          color: NordicColors.textInverse,
                        ),
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
          Text(
            title,
            style: NordicTypography.appBarTitle.copyWith(
              color: NordicColors.textInverse,
            ),
          ),
        ],
      ),
    );
  }
}

/// Three thin rings in the corner of the header: depth without a gradient.
class _Rings extends CustomPainter {
  const _Rings();

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5
      ..color = NordicColors.textInverse.withValues(alpha: 0.07);
    final center = size.center(Offset.zero);
    for (final r in const [60.0, 100.0, 140.0]) {
      canvas.drawCircle(center, r, paint);
    }
  }

  @override
  bool shouldRepaint(covariant CustomPainter oldDelegate) => false;
}
