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
/// - [onRefresh] adds pull to refresh.
class NordicPage extends StatelessWidget {
  final String title;
  final Key? titleKey;
  final String? subtitle;
  final Widget? headerAction;
  final List<Widget> children;
  final List<Widget>? actions;
  final List<Widget>? appBarActions;
  final Future<void> Function()? onRefresh;
  final bool large;

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
    this.padding = const EdgeInsets.fromLTRB(
      NordicSpace.gutter,
      NordicSpace.md,
      NordicSpace.gutter,
      NordicSpace.xl,
    ),
  }) : large = true,
       appBarActions = null;

  const NordicPage.pushed({
    super.key,
    required this.title,
    this.titleKey,
    required this.children,
    this.actions,
    this.appBarActions,
    this.onRefresh,
    this.padding = const EdgeInsets.fromLTRB(
      NordicSpace.gutter,
      NordicSpace.xs,
      NordicSpace.gutter,
      NordicSpace.xl,
    ),
  }) : large = false,
       subtitle = null,
       headerAction = null;

  @override
  Widget build(BuildContext context) {
    Widget list = ListView(
      padding: padding,
      physics: const AlwaysScrollableScrollPhysics(),
      children: [if (large) _largeTitle(), ...children],
    );
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
        if (actions != null && actions!.isNotEmpty)
          BottomActionBar(children: actions!),
      ],
    );
    if (large) {
      return Scaffold(body: SafeArea(bottom: false, child: body));
    }
    return Scaffold(
      appBar: AppBar(
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
