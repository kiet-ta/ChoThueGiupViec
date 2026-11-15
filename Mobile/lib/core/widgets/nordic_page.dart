import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_spacing.dart';
import '../theme/nordic_typography.dart';
import 'bottom_action_bar.dart';

/// The one scaffold for screens (MOB-UI-13, `Mobile/DESIGN-SYSTEM.md` §5):
/// - a **tab screen** ([NordicPage.tab]) has no app bar; its [title] is a large `display` title
///   at the top of the scroll, with an optional [subtitle] and [headerAction];
/// - a **pushed screen** ([NordicPage.pushed]) has a standard app bar with back and [title];
/// - [children] scroll inside the 20 px gutter;
/// - [actions] (usually one `NordicButton`) sit in a [BottomActionBar] laid out inside the body,
///   so the keyboard pushes them up instead of covering them;
/// - [onRefresh] adds pull to refresh;
/// - [onEndReached] loads the next page of a long list: it is called when the list is scrolled to about one
///   screen before its end, and also when the content does not fill the screen yet (MOB-PAGING-01). The screen
///   passes null when there is nothing more to load or a page is already loading.
class NordicPage extends StatelessWidget {
  final String title;
  final Key? titleKey;
  final String? subtitle;
  final Widget? headerAction;
  final List<Widget> children;
  final List<Widget>? actions;
  final List<Widget>? appBarActions;
  final Future<void> Function()? onRefresh;
  final VoidCallback? onEndReached;
  final bool large;
  final Widget? fullWidthHeader;
  final Widget? bodyOverride;
  final Widget? leading;

  /// The padding of the scrolling content; the gutter on the sides by default.
  final EdgeInsetsGeometry padding;

  const NordicPage.tab({
    super.key,
    required this.title,
    this.titleKey,
    this.subtitle,
    this.headerAction,
    required this.children,
    this.actions,
    this.onRefresh,
    this.onEndReached,
    this.padding = const EdgeInsets.fromLTRB(
      NordicSpace.gutter,
      NordicSpace.md,
      NordicSpace.gutter,
      NordicSpace.xl,
    ),
  }) : large = true,
       appBarActions = null,
       fullWidthHeader = null,
       bodyOverride = null,
       leading = null;

  const NordicPage.pushed({
    super.key,
    required this.title,
    this.titleKey,
    required this.children,
    this.actions,
    this.appBarActions,
    this.onRefresh,
    this.onEndReached,
    this.fullWidthHeader,
    this.bodyOverride,
    this.leading,
    this.padding = const EdgeInsets.fromLTRB(
      NordicSpace.gutter,
      NordicSpace.xs,
      NordicSpace.gutter,
      NordicSpace.xl,
    ),
  }) : large = false,
       subtitle = null,
       headerAction = null;

  /// One screen before the end of the list (or a list shorter than the screen) asks for the next page.
  /// [ScrollMetricsNotification] also fires when a page arrives and the content grows, so a short first page keeps
  /// loading until the screen is full or nothing is left.
  bool _nearEnd(Notification n) {
    final ScrollMetrics? m = switch (n) {
      ScrollUpdateNotification(:final metrics) => metrics,
      ScrollMetricsNotification(:final metrics) => metrics,
      _ => null,
    };
    if (m != null &&
        m.axis == Axis.vertical &&
        m.extentAfter < m.viewportDimension) {
      final load = onEndReached;
      // After the frame: a metrics notification can come during layout, where setState is not allowed.
      if (load != null) {
        WidgetsBinding.instance.addPostFrameCallback((_) => load());
      }
    }
    return false;
  }

  @override
  Widget build(BuildContext context) {
    final hasActions = actions != null && actions!.isNotEmpty;
    // Under a translucent bottom bar (the shells extend their body) or the home indicator, the
    // last item must still scroll clear of it; the action bar handles its own safe area.
    final inset = hasActions ? 0.0 : MediaQuery.paddingOf(context).bottom;
    Widget list =
        bodyOverride ??
        ListView(
          padding: padding.add(EdgeInsets.only(bottom: inset)),
          physics: const AlwaysScrollableScrollPhysics(),
          children: [if (large) _largeTitle(), ?fullWidthHeader, ...children],
        );
    if (onEndReached != null) {
      list = NotificationListener<Notification>(
        onNotification: _nearEnd,
        child: list,
      );
    }
    if (onRefresh != null) {
      list = RefreshIndicator(
        color: NordicColors.primary,
        onRefresh: onRefresh!,
        child: list,
      );
    }
    final body = Column(
      children: [
        Expanded(child: list),
        if (hasActions) BottomActionBar(children: actions!),
      ],
    );
    if (large) {
      return Scaffold(body: SafeArea(bottom: false, child: body));
    }
    return Scaffold(
      appBar: fullWidthHeader != null
          ? null
          : AppBar(
              // The default BackButton is read as "Back" (no Vietnamese Material localizations): the same BackButton,
              // with its tooltip and screen reader label "Quay lại" (MOB-A11Y-01), like DarkHeader. A screen that brings
              // its own leading keeps it.
              leading:
                  leading ??
                  ((ModalRoute.of(context)?.canPop ?? false)
                      ? Localizations.override(
                          context: context,
                          delegates: const [_BackTooltipDelegate()],
                          child: const BackButton(key: Key('nordic-page-back')),
                        )
                      : null),
              title: Text(title, key: titleKey),
              actions: appBarActions,
            ),
      body: body,
    );
  }

  Widget _largeTitle() {
    return Padding(
      padding: const EdgeInsets.only(bottom: NordicSpace.md),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (subtitle != null)
                  Padding(
                    padding: const EdgeInsets.only(bottom: NordicSpace.xxs),
                    child: Text(
                      subtitle!,
                      style: NordicTypography.bodyRegular.copyWith(
                        color: NordicColors.textSecondary,
                      ),
                    ),
                  ),
                Text(title, key: titleKey, style: NordicTypography.display),
              ],
            ),
          ),
          ?headerAction,
        ],
      ),
    );
  }
}

/// Material strings with only the back tooltip in Vietnamese, for the app bar's [BackButton] (MOB-A11Y-01).
class _ViBackTooltip extends DefaultMaterialLocalizations {
  const _ViBackTooltip();

  @override
  String get backButtonTooltip => 'Quay lại';
}

class _BackTooltipDelegate
    extends LocalizationsDelegate<MaterialLocalizations> {
  const _BackTooltipDelegate();

  @override
  bool isSupported(Locale locale) => true;

  @override
  Future<MaterialLocalizations> load(Locale locale) =>
      SynchronousFuture<MaterialLocalizations>(const _ViBackTooltip());

  @override
  bool shouldReload(_BackTooltipDelegate old) => false;
}
