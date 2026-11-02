import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_elevation.dart';
import '../theme/nordic_radius.dart';
import '../theme/nordic_spacing.dart';
import '../theme/nordic_typography.dart';
import 'pressable.dart';

/// One row of a [GroupedListCard].
class GroupedListItem {
  final IconData icon;
  final String title;
  final String? subtitle;

  /// Short muted text before the chevron (a count, a status).
  final String? trailing;
  final VoidCallback? onTap;
  final Key? key;

  /// A destructive row (logout): error color, no chevron.
  final bool danger;

  const GroupedListItem({
    required this.icon,
    required this.title,
    this.subtitle,
    this.trailing,
    this.onTap,
    this.key,
    this.danger = false,
  });
}

/// ui-templates `.list`: rows in one white card separated by hairlines.
class GroupedListCard extends StatelessWidget {
  final List<GroupedListItem> items;
  final List<Widget>? children;

  const GroupedListCard({super.key, required this.items}) : children = null;

  const GroupedListCard.custom({super.key, required this.children})
    : items = const [];

  @override
  Widget build(BuildContext context) {
    final rows = children ?? [for (final item in items) _row(item)];
    return DecoratedBox(
      decoration: const BoxDecoration(
        borderRadius: NordicRadius.cardAll,
        boxShadow: NordicElevation.card,
      ),
      child: Material(
        color: NordicColors.surface,
        clipBehavior: Clip.antiAlias,
        shape: RoundedRectangleBorder(
          borderRadius: NordicRadius.cardAll,
          side: const BorderSide(color: NordicColors.border),
        ),
        child: Column(
          children: [
            for (var i = 0; i < rows.length; i++) ...[
              if (i > 0)
                const Divider(
                  height: 1,
                  thickness: 1,
                  color: NordicColors.surfaceContainer,
                ),
              rows[i],
            ],
          ],
        ),
      ),
    );
  }

  Widget _row(GroupedListItem item) {
    final color = item.danger ? NordicColors.error : NordicColors.textTitle;
    return Pressable(
      enabled: item.onTap != null,
      child: InkWell(
        key: item.key,
        onTap: item.onTap,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 64),
          child: Padding(
            padding: const EdgeInsets.symmetric(
              horizontal: NordicSpace.md,
              vertical: NordicSpace.sm,
            ),
            child: Row(
              children: [
                Container(
                  width: 40,
                  height: 40,
                  decoration: const BoxDecoration(
                    color: NordicColors.surfaceSubtle,
                    borderRadius: NordicRadius.tileAll,
                  ),
                  child: Icon(
                    item.icon,
                    color: item.danger
                        ? NordicColors.error
                        : NordicColors.textSecondary,
                  ),
                ),
                const SizedBox(width: NordicSpace.sm),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        item.title,
                        style: NordicTypography.titleSmall.copyWith(
                          color: color,
                        ),
                      ),
                      if (item.subtitle != null)
                        Text(item.subtitle!, style: NordicTypography.bodySmall),
                    ],
                  ),
                ),
                if (item.trailing != null)
                  Padding(
                    padding: const EdgeInsets.only(left: NordicSpace.xs),
                    child: Text(
                      item.trailing!,
                      style: NordicTypography.bodySmall,
                    ),
                  ),
                if (!item.danger)
                  const Icon(
                    Icons.chevron_right,
                    color: NordicColors.borderVariant,
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
