import 'package:flutter/painting.dart';

/// Corner radius tokens of the Mobile app (MOB-UI-13, `Mobile/DESIGN-SYSTEM.md` §3.4). Shapes
/// stay soft and rounded (owner decision 2026-10-28): buttons and chips are pills, cards 20,
/// input fields 14, small tiles 12, sheets 28 on top. Real circles stay `BoxShape.circle`.
class NordicRadius {
  NordicRadius._();

  static const double card = 20.0;
  static const double field = 14.0;
  static const double tile = 12.0;
  static const double sheet = 28.0;

  static const BorderRadius cardAll = BorderRadius.all(Radius.circular(card));
  static const BorderRadius fieldAll = BorderRadius.all(Radius.circular(field));
  static const BorderRadius tileAll = BorderRadius.all(Radius.circular(tile));
  static const BorderRadius sheetTop = BorderRadius.vertical(
    top: Radius.circular(sheet),
  );

  /// Buttons, chips and pills: fully rounded ends.
  static const BorderRadius pill = BorderRadius.all(Radius.circular(999.0));
}
