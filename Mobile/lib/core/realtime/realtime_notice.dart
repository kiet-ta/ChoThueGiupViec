import 'package:flutter/material.dart';

import '../motion/nordic_motion.dart';
import '../theme/nordic_colors.dart';
import '../theme/nordic_elevation.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_spacing.dart';
import '../theme/nordic_typography.dart';
import '../widgets/nordic_button.dart';
import '../widgets/pressable.dart';

/// How loud a realtime notice is: `info` is a white card, `urgent` a red-tinted one (the rescue seat, D38).
enum RealtimeNoticeTone { info, urgent }

/// A notice that comes from the realtime channel and stays on top of whatever screen is open until it is closed or its
/// button is pressed (D38: the customer's apology, the check-in reminder and the urgent rescue notice must not vanish like a
/// snack bar). One at a time: a new notice replaces the one on screen. It lives in the root overlay, so it also shows over
/// a pushed screen. Styling follows `Mobile/DESIGN-SYSTEM.md` (card radius, card shadow, tokens only).
class RealtimeNoticePresenter {
  OverlayEntry? _entry;

  /// True while a notice is on screen.
  bool get isShowing => _entry != null;

  /// Shows the notice. [actionLabel] and [onAction] together add the main button; pressing it closes the notice first.
  void show(
    BuildContext context, {
    required String title,
    required String body,
    RealtimeNoticeTone tone = RealtimeNoticeTone.info,
    String? actionLabel,
    VoidCallback? onAction,
    String? detail,
  }) {
    final overlay = Overlay.maybeOf(context, rootOverlay: true);
    if (overlay == null) return;
    dismiss();
    late final OverlayEntry entry;
    entry = OverlayEntry(
      builder: (_) => _NoticeLayer(
        title: title,
        body: body,
        detail: detail,
        tone: tone,
        actionLabel: onAction == null ? null : actionLabel,
        onAction: onAction == null
            ? null
            : () {
                if (identical(_entry, entry)) dismiss();
                onAction();
              },
        onClose: () {
          if (identical(_entry, entry)) dismiss();
        },
      ),
    );
    _entry = entry;
    overlay.insert(entry);
  }

  /// Removes the notice on screen, if any.
  void dismiss() {
    final entry = _entry;
    _entry = null;
    if (entry == null) return;
    try {
      entry.remove();
    } catch (_) {
      // Already removed with its overlay (the app is closing).
    }
  }
}

class _NoticeLayer extends StatelessWidget {
  final String title;
  final String body;
  final String? detail;
  final RealtimeNoticeTone tone;
  final String? actionLabel;
  final VoidCallback? onAction;
  final VoidCallback onClose;

  const _NoticeLayer({
    required this.title,
    required this.body,
    required this.detail,
    required this.tone,
    required this.actionLabel,
    required this.onAction,
    required this.onClose,
  });

  @override
  Widget build(BuildContext context) {
    final urgent = tone == RealtimeNoticeTone.urgent;
    final reduced = NordicMotion.reduced(context);
    final card = Container(
      key: const Key('realtime-notice'),
      width: double.infinity,
      padding: const EdgeInsets.all(NordicSpace.md),
      decoration: BoxDecoration(
        color: urgent ? NordicColors.errorContainer : NordicColors.surface,
        borderRadius: NordicRadius.cardAll,
        border: Border.all(
          color: urgent ? NordicColors.error : NordicColors.border,
        ),
        boxShadow: NordicElevation.card,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Container(
                width: 36,
                height: 36,
                decoration: BoxDecoration(
                  color: urgent
                      ? NordicColors.error
                      : NordicColors.primaryFixed,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  urgent
                      ? Icons.priority_high_rounded
                      : Icons.notifications_active_outlined,
                  size: 20,
                  color: urgent
                      ? NordicColors.textInverse
                      : NordicColors.primary,
                ),
              ),
              const SizedBox(width: NordicSpace.sm),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (title.isNotEmpty)
                      Text(
                        title,
                        key: const Key('realtime-notice-title'),
                        style: NordicTypography.titleSmall.copyWith(
                          color: NordicColors.textTitle,
                        ),
                      ),
                    if (body.isNotEmpty) ...[
                      const SizedBox(height: NordicSpace.xxs),
                      Text(
                        body,
                        key: const Key('realtime-notice-body'),
                        style: NordicTypography.bodyRegular.copyWith(
                          color: NordicColors.textBody,
                        ),
                      ),
                    ],
                    if (detail != null && detail!.isNotEmpty) ...[
                      const SizedBox(height: NordicSpace.xs),
                      Text(
                        detail!,
                        key: const Key('realtime-notice-detail'),
                        style: NordicTypography.titleSmall.copyWith(
                          color: urgent
                              ? NordicColors.error
                              : NordicColors.primary,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
              Pressable(
                child: IconButton(
                  key: const Key('realtime-notice-close'),
                  tooltip: 'Đóng',
                  constraints: const BoxConstraints(
                    minWidth: 44,
                    minHeight: 44,
                  ),
                  padding: EdgeInsets.zero,
                  icon: const Icon(
                    Icons.close_rounded,
                    size: 22,
                    color: NordicColors.textSecondary,
                  ),
                  onPressed: onClose,
                ),
              ),
            ],
          ),
          if (actionLabel != null && onAction != null) ...[
            const SizedBox(height: NordicSpace.sm),
            NordicButton(
              key: const Key('realtime-notice-action'),
              label: actionLabel!,
              width: double.infinity,
              onPressed: onAction,
            ),
          ],
        ],
      ),
    );

    return Positioned(
      top: 0,
      left: 0,
      right: 0,
      child: SafeArea(
        bottom: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
            NordicSpace.gutter,
            NordicSpace.xs,
            NordicSpace.gutter,
            0,
          ),
          child: Material(
            type: MaterialType.transparency,
            child: TweenAnimationBuilder<double>(
              tween: Tween(begin: 0, end: 1),
              duration: reduced
                  ? NordicMotion.fade
                  : NordicMotion.standard.settleDuration,
              curve: reduced
                  ? Curves.linear
                  : SpringCurve(NordicMotion.standard),
              builder: (context, t, child) => Opacity(
                opacity: t.clamp(0.0, 1.0),
                child: Transform.translate(
                  offset: reduced ? Offset.zero : Offset(0, -24 * (1 - t)),
                  child: child,
                ),
              ),
              child: Semantics(liveRegion: true, child: card),
            ),
          ),
        ),
      ),
    );
  }
}
