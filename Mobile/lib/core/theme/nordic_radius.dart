import 'package:flutter/material.dart';

/// Corner radius tokens of the Mobile app (MOB-UI-11, owner decision 2026-10-28: square cards,
/// only buttons slightly rounded). Cards, sheets, inputs, images, pills and bars are square;
/// real circles (`BoxShape.circle`: avatars, round icon backgrounds) stay round.
class NordicRadius {
  NordicRadius._();

  /// Buttons and button-like controls (chips, segmented buttons).
  static const double button = 6.0;
  static const BorderRadius buttonAll = BorderRadius.all(
    Radius.circular(button),
  );
  static const OutlinedBorder buttonShape = RoundedRectangleBorder(
    borderRadius: buttonAll,
  );

  /// Everything that is not a button.
  static const BorderRadius none = BorderRadius.zero;
}
