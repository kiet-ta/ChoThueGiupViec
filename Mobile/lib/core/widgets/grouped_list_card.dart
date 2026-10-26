import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';
import '../theme/nordic_typography.dart';

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

  const GroupedListCard({super.key, required this.items});

  @override
  Widget build(BuildContext context) {
    return Material(
      color: NordicColors.surface,
      clipBehavior: Clip.antiAlias,
      shape: RoundedRectangleBorder(
        borderRadius: BorderRadius.circular(16),
        side: const BorderSide(color: NordicColors.border),
      ),
      child: Column(
        children: [
          for (var i = 0; i < items.length; i++) ...[
            if (i > 0) const Divider(height: 1, thickness: 1),
            _row(items[i]),
          ],
        ],
      ),
    );
  }

  Widget _row(GroupedListItem item) {
    final color = item.danger ? NordicColors.error : NordicColors.textTitle;
    return InkWell(
      key: item.key,
      onTap: item.onTap,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 56),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
          child: Row(
            children: [
              Icon(
                item.icon,
                color: item.danger
                    ? NordicColors.error
                    : NordicColors.textSecondary,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      item.title,
                      style: TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.w500,
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
                  padding: const EdgeInsets.only(left: 8),
                  child: Text(
                    item.trailing!,
                    style: const TextStyle(
                      fontSize: 13,
                      color: NordicColors.textSecondary,
                    ),
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
    );
  }
}
