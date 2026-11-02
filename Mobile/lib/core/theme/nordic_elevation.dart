import 'package:flutter/painting.dart';

/// Shadow tokens of the Mobile app (`Mobile/DESIGN-SYSTEM.md` §3.5, redesign 2026-10-30).
/// Depth comes from a few layered, soft shadows tinted with the deep green, never from one
/// hard shadow. Three levels:
/// - [card]: every white card on the canvas (with its 1 px hairline);
/// - [raised]: the one floating panel of a screen (the Home booking card over the dark header);
/// - [cta]: the main dark button and the centre "Đặt ca" button.
class NordicElevation {
  NordicElevation._();

  static const List<BoxShadow> card = [
    BoxShadow(color: Color(0x0D101814), blurRadius: 2, offset: Offset(0, 1)),
    BoxShadow(
      color: Color(0x1A101814),
      blurRadius: 18,
      spreadRadius: -6,
      offset: Offset(0, 8),
    ),
  ];

  static const List<BoxShadow> raised = [
    BoxShadow(color: Color(0x0F101814), blurRadius: 2, offset: Offset(0, 1)),
    BoxShadow(color: Color(0x14101814), blurRadius: 24, offset: Offset(0, 10)),
    BoxShadow(
      color: Color(0x470F3D2E),
      blurRadius: 48,
      spreadRadius: -16,
      offset: Offset(0, 28),
    ),
  ];

  static const List<BoxShadow> cta = [
    BoxShadow(
      color: Color(0x8C0F3D2E),
      blurRadius: 22,
      spreadRadius: -8,
      offset: Offset(0, 10),
    ),
  ];

  /// The selected segment of a segmented control.
  static const List<BoxShadow> segment = [
    BoxShadow(color: Color(0x14101814), blurRadius: 2, offset: Offset(0, 1)),
    BoxShadow(
      color: Color(0x1A101814),
      blurRadius: 10,
      spreadRadius: -2,
      offset: Offset(0, 4),
    ),
  ];
}
