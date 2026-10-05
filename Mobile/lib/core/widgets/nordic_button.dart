import 'package:flutter/material.dart';
import '../theme/nordic_colors.dart';

enum NordicButtonVariant { primary, secondary, ghost }

/// Pill-shaped CTA button matching the TỔ ẤM Nordic Care specification.
class NordicButton extends StatelessWidget {
  final String label;
  final VoidCallback? onPressed;
  final NordicButtonVariant variant;
  final Widget? icon;
  final bool isLoading;
  final double? width;

  const NordicButton({
    super.key,
    required this.label,
    required this.onPressed,
    this.variant = NordicButtonVariant.primary,
    this.icon,
    this.isLoading = false,
    this.width,
  });

  @override
  Widget build(BuildContext context) {
    final effectiveChild = isLoading
        ? const SizedBox(
            width: 20.0,
            height: 20.0,
            child: CircularProgressIndicator(
              strokeWidth: 2.0,
              valueColor: AlwaysStoppedAnimation<Color>(Colors.white),
            ),
          )
        : Row(
            mainAxisSize: MainAxisSize.min,
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              if (icon != null) ...[
                icon!,
                const SizedBox(width: 8.0),
              ],
              Text(label),
            ],
          );

    Widget button;
    switch (variant) {
      case NordicButtonVariant.primary:
        button = ElevatedButton(
          onPressed: isLoading ? null : onPressed,
          style: ElevatedButton.styleFrom(
            backgroundColor: NordicColors.primary,
            foregroundColor: Colors.white,
            shape: const StadiumBorder(),
            padding: const EdgeInsets.symmetric(horizontal: 24.0, vertical: 14.0),
            elevation: 0,
          ),
          child: effectiveChild,
        );
        break;
      case NordicButtonVariant.secondary:
        button = OutlinedButton(
          onPressed: isLoading ? null : onPressed,
          style: OutlinedButton.styleFrom(
            foregroundColor: NordicColors.textTitle,
            side: const BorderSide(color: NordicColors.border, width: 1.0),
            shape: const StadiumBorder(),
            padding: const EdgeInsets.symmetric(horizontal: 24.0, vertical: 14.0),
          ),
          child: effectiveChild,
        );
        break;
      case NordicButtonVariant.ghost:
        button = TextButton(
          onPressed: isLoading ? null : onPressed,
          style: TextButton.styleFrom(
            foregroundColor: NordicColors.textTitle,
            shape: const StadiumBorder(),
            padding: const EdgeInsets.symmetric(horizontal: 20.0, vertical: 12.0),
          ),
          child: effectiveChild,
        );
        break;
    }

    if (width != null) {
      return SizedBox(width: width, child: button);
    }
    return button;
  }
}
