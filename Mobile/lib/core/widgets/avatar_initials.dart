import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';

/// ui-templates `.avatar`: initials of a person in a round tile (no photos in the app).
class AvatarInitials extends StatelessWidget {
  final String name;
  final double size;
  final Color background;
  final Color foreground;

  const AvatarInitials(
    this.name, {
    super.key,
    this.size = 44,
    this.background = NordicColors.surfaceContainer,
    this.foreground = NordicColors.primary,
  });

  /// First letter of the last two words: "Nguyễn Thị Hà" -> "TH"; empty -> "?".
  static String initialsOf(String name) {
    final words = name.trim().split(RegExp(r'\s+')).where((w) => w.isNotEmpty);
    if (words.isEmpty) return '?';
    final picked = words.length == 1
        ? [words.first]
        : words.toList().sublist(words.length - 2);
    return picked.map((w) => w.characters.first.toUpperCase()).join();
  }

  @override
  Widget build(BuildContext context) {
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(color: background, shape: BoxShape.circle),
      child: Text(
        initialsOf(name),
        style: TextStyle(
          fontSize: size * 0.34,
          fontWeight: FontWeight.w600,
          color: foreground,
        ),
      ),
    );
  }
}
