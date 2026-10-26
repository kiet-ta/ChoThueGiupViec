import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/favorite_worker.dart';
import '../services/favorite_worker_service.dart';
import '../widgets/favorite_worker_card.dart';

/// Screen listing customer's saved favorite workers matching .spec/contracts/customers.md §2.3.
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
          content: Text('Đã bỏ yêu thích ${removed.fullName}.'),
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
          content: Text('Thao tác thất bại: $e'),
          backgroundColor: NordicColors.error,
        ),
      );
    }
  }

  void _onBookWithWorker(FavoriteWorker worker) {
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text('Đang tạo lịch đặt ca ưu tiên với ${worker.fullName}'),
        backgroundColor: NordicColors.primary,
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    // ui-templates `customer/favorites.html`: a tab of the customer shell (large title) or a pushed screen.
    final pushed = ModalRoute.of(context)?.canPop ?? false;
    return Scaffold(
      appBar: pushed ? AppBar(title: const Text('Yêu thích')) : null,
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 390.0),
            child: RefreshIndicator(
              onRefresh: _loadFavorites,
              color: NordicColors.primary,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 32.0),
                children: [
                  if (!pushed)
                    Padding(
                      padding: const EdgeInsets.only(top: 8.0, bottom: 16.0),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        children: [
                          const Expanded(
                            child: Text(
                              'Yêu thích',
                              style: NordicTypography.screenTitle,
                            ),
                          ),
                          if (!_isLoading && _errorMessage == null)
                            Text(
                              '${_workers.length} thợ',
                              key: const Key('favorites-count'),
                              style: NordicTypography.bodySmall,
                            ),
                        ],
                      ),
                    ),
                  if (_isLoading)
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: 40.0),
                      child: Center(
                        child: CircularProgressIndicator(
                          valueColor: AlwaysStoppedAnimation<Color>(
                            NordicColors.primary,
                          ),
                        ),
                      ),
                    )
                  else if (_errorMessage != null)
                    ErrorStateWidget(
                      message: _errorMessage!,
                      onRetry: _loadFavorites,
                    )
                  else if (_workers.isEmpty)
                    const EmptyStateWidget(
                      title: 'Chưa Có Thợ Quen Nào',
                      subtitle: 'Lưu thợ sau một ca ưng ý để đặt lại nhanh.',
                      icon: Icons.favorite_outline,
                    )
                  else
                    ..._workers.map(
                      (worker) => Padding(
                        padding: const EdgeInsets.only(bottom: 12.0),
                        child: FavoriteWorkerCard(
                          worker: worker,
                          onBookJob: () => _onBookWithWorker(worker),
                          onRemove: () => _removeWorker(worker),
                        ),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
