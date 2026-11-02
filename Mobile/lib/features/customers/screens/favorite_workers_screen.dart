import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../booking/models/booking_models.dart';
import '../../booking/routes.dart';
import '../../booking/screens/booking_screen.dart';
import '../models/favorite_worker.dart';
import '../services/favorite_worker_service.dart';
import '../widgets/favorite_worker_card.dart';

/// Screen listing customer's saved favorite workers matching .spec/contracts/customers.md §2.3.
/// MOB-UI-14: `NordicPage` (tab or pushed), one card per helper (`Mobile/DESIGN-SYSTEM.md`).
class FavoriteWorkersScreen extends StatefulWidget {
  final FavoriteWorkerService? service;

  const FavoriteWorkersScreen({super.key, this.service});

  @override
  State<FavoriteWorkersScreen> createState() => _FavoriteWorkersScreenState();
}

class _FavoriteWorkersScreenState extends State<FavoriteWorkersScreen> {
  late final FavoriteWorkerService _service;
  List<FavoriteWorker> _workers = [];
  bool _isLoading = true;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? FavoriteWorkerService();
    _loadFavorites();
  }

  Future<void> _loadFavorites() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final list = await _service.getFavoriteWorkers();
      if (!mounted) return;
      setState(() {
        _workers = list;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _errorMessage = e
            .toString()
            .replaceAll('Exception: ', '')
            .replaceAll('HttpException: ', '');
        _isLoading = false;
      });
    }
  }

  Future<void> _removeWorker(FavoriteWorker worker) async {
    final removed = worker;
    try {
      await _service.removeFavoriteWorker(worker.workerId);
      if (!mounted) return;
      setState(() {
        _workers.removeWhere((w) => w.workerId == worker.workerId);
      });

      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Đã bỏ ${removed.fullName} khỏi yêu thích'),
          backgroundColor: NordicColors.contrastDark,
          action: SnackBarAction(
            label: 'Hoàn tác',
            textColor: NordicColors.accentAmber,
            onPressed: () async {
              await _service.addFavoriteWorker(removed.workerId);
              _loadFavorites();
            },
          ),
        ),
      );
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: const Text('Chưa bỏ yêu thích được. Thử lại nhé.'),
          backgroundColor: NordicColors.error,
        ),
      );
    }
  }

  /// Opens the booking form with this person already picked (booking.md 3.3 `preferredWorkerId`): the form
  /// greys out the days and shifts they are busy, and Dispatch offers them the job first.
  void _onBookWithWorker(FavoriteWorker worker) {
    Navigator.of(context).pushNamed(
      BookingRoutes.booking,
      arguments: BookingScreenArgs(
        serviceTier: ServiceTier.economy,
        preferredWorkerId: worker.workerId,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    // A tab of the customer shell (large title) or a pushed screen.
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    final children = <Widget>[
      if (_isLoading)
        const SizedBox(height: 320, child: LoadingStateWidget())
      else if (_errorMessage != null)
        ErrorStateWidget(message: _errorMessage!, onRetry: _loadFavorites)
      else if (_workers.isEmpty)
        const EmptyStateWidget(
          title: 'Chưa có ai trong danh sách',
          subtitle: 'Lưu người giúp việc sau một ca ưng ý để đặt lại nhanh.',
          icon: Icons.favorite_outline,
        )
      else
        for (var i = 0; i < _workers.length; i++) ...[
          if (i > 0) const SizedBox(height: NordicSpace.sm),
          FavoriteWorkerCard(
            worker: _workers[i],
            tintIndex: i,
            onBookJob: () => _onBookWithWorker(_workers[i]),
            onRemove: () => _removeWorker(_workers[i]),
          ),
        ],
      if (!_isLoading && _errorMessage == null && _workers.isNotEmpty) ...[
        const SizedBox(height: NordicSpace.md),
        Container(
          key: const Key('favorites-note'),
          padding: const EdgeInsets.all(NordicSpace.sm),
          decoration: const BoxDecoration(
            color: NordicColors.primaryFixed,
            borderRadius: NordicRadius.fieldAll,
          ),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(
                Icons.info_outline,
                size: 18,
                color: NordicColors.primary,
              ),
              const SizedBox(width: NordicSpace.xs),
              Expanded(
                child: Text(
                  'Đặt với người quen áp dụng cho gói Cơ bản.',
                  style: NordicTypography.bodySmall.copyWith(
                    color: NordicColors.primary,
                  ),
                ),
              ),
            ],
          ),
        ),
      ],
    ];
    if (pushed) {
      return NordicPage.pushed(
        title: 'Yêu thích',
        onRefresh: _loadFavorites,
        children: children,
      );
    }
    return NordicPage.tab(
      title: 'Yêu thích',
      onRefresh: _loadFavorites,
      children: [
        if (!_isLoading && _errorMessage == null)
          Padding(
            padding: const EdgeInsets.only(bottom: NordicSpace.sm),
            child: Text(
              '${_workers.length} người giúp việc quen',
              key: const Key('favorites-count'),
              style: NordicTypography.bodyRegular.copyWith(
                color: NordicColors.textSecondary,
              ),
            ),
          ),
        ...children,
      ],
    );
  }
}
