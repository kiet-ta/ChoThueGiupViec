import 'package:flutter/material.dart';

import '../../../core/models/user_role.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';

/// Top of the sign-in screens (MOB-UI-10), in the look of the welcome hero: a housework photo
/// for the role under a dark gradient, a white back button and TỔ ẤM mark, then a large white
/// [title] and [subtitle]. The form sheet of the screen overlaps its bottom edge, so the text
/// keeps [sheetOverlap] of free space below it.
class AuthPhotoHeader extends StatelessWidget {
  final AppRole role;
  final String title;
  final Key? titleKey;
  final String subtitle;

  /// How far the form sheet below slides over the photo.
  static const double sheetOverlap = 28.0;

  const AuthPhotoHeader({
    super.key,
    required this.role,
    required this.title,
    this.titleKey,
    required this.subtitle,
  });

  /// A welcome photo (assets/images/welcome/CREDITS.txt) that matches the role.
  static String photoFor(AppRole role) => role == AppRole.worker
      ? 'assets/images/welcome/caddy-stairs.jpg'
      : 'assets/images/welcome/mop-living-room.jpg';

  @override
  Widget build(BuildContext context) {
    final top = MediaQuery.paddingOf(context).top;
    final canPop = Navigator.of(context).canPop();
    return SizedBox(
      height:
          (MediaQuery.sizeOf(context).height * 0.42).clamp(260.0, 380.0) + top,
      child: Stack(
        fit: StackFit.expand,
        children: [
          Image.asset(
            photoFor(role),
            key: const Key('auth-photo'),
            fit: BoxFit.cover,
          ),
          // Darker top for the back button, much darker bottom for the title.
          DecoratedBox(
            decoration: BoxDecoration(
              gradient: LinearGradient(
                begin: Alignment.topCenter,
                end: Alignment.bottomCenter,
                stops: const [0, 0.35, 1],
                colors: [
                  NordicColors.contrastDark.withValues(alpha: 0.45),
                  NordicColors.contrastDark.withValues(alpha: 0.1),
                  NordicColors.contrastDark.withValues(alpha: 0.85),
                ],
              ),
            ),
          ),
          Padding(
            padding: EdgeInsets.fromLTRB(
              8.0,
              top + 4.0,
              20.0,
              sheetOverlap + 20.0,
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    if (canPop)
                      IconButton(
                        key: const Key('auth-back'),
                        tooltip: 'Quay lại',
                        onPressed: () => Navigator.of(context).maybePop(),
                        icon: const Icon(
                          Icons.arrow_back,
                          color: NordicColors.textInverse,
                        ),
                      )
                    else
                      const SizedBox(height: 48.0),
                    const Spacer(),
                    _brand(),
                  ],
                ),
                const Spacer(),
                Padding(
                  padding: const EdgeInsets.only(left: 12.0),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        key: titleKey,
                        style: NordicTypography.screenTitle.copyWith(
                          fontSize: 30.0,
                          color: NordicColors.textInverse,
                        ),
                      ),
                      const SizedBox(height: 6.0),
                      Text(
                        subtitle,
                        style: NordicTypography.bodyRegular.copyWith(
                          color: NordicColors.textInverse.withValues(
                            alpha: 0.88,
                          ),
                          fontFeatures: const [FontFeature.tabularFigures()],
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _brand() {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Container(
          width: 32.0,
          height: 32.0,
          decoration: BoxDecoration(
            color: NordicColors.primary,
            borderRadius: BorderRadius.circular(10.0),
          ),
          child: const Icon(
            Icons.home_outlined,
            color: NordicColors.textInverse,
            size: 18.0,
          ),
        ),
        const SizedBox(width: 8.0),
        Text(
          'TỔ ẤM',
          style: NordicTypography.labelMedium.copyWith(
            letterSpacing: 1.0,
            fontWeight: FontWeight.w700,
            color: NordicColors.textInverse,
          ),
        ),
      ],
    );
  }
}

/// The form sheet (canvas colour, like the bottom action bar) under an [AuthPhotoHeader]: rounded on top and slid
/// [AuthPhotoHeader.sheetOverlap] up over the photo.
class AuthFormSheet extends StatelessWidget {
  final List<Widget> children;

  const AuthFormSheet({super.key, required this.children});

  @override
  Widget build(BuildContext context) {
    return Transform.translate(
      offset: const Offset(0.0, -AuthPhotoHeader.sheetOverlap),
      child: Container(
        padding: const EdgeInsets.fromLTRB(20.0, 24.0, 20.0, 8.0),
        decoration: const BoxDecoration(
          color: NordicColors.canvas,
          borderRadius: BorderRadius.vertical(top: Radius.circular(28.0)),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: children,
        ),
      ),
    );
  }
}
