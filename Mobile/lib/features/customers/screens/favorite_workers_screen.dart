import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../models/favorite_worker.dart';
import '../services/favorite_worker_service.dart';
import '../widgets/favorite_worker_card.dart';

/// Screen listing customer's saved favorite workers matching .spec/contracts/customers.md §2.3.
class FavoriteWorkersScreen extends StatefulWidget {
  final FavoriteWorkerService? service;

  const FavoriteWorkersScreen({
    super.key,
    this.service,
  });

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
        _errorMessage = e.toString().replaceAll('Exception: ', '').replaceAll('HttpException: ', '');
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
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: Text('Thợ Quen Yêu Thích', style: NordicTypography.h3),
        centerTitle: true,
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: RefreshIndicator(
            onRefresh: _loadFavorites,
            color: NordicColors.primary,
            child: ListView(
              padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 12.0),
              children: [
                const Align(
                  alignment: Alignment.centerLeft,
                  child: EyebrowBadge(text: 'DANH SÁCH TIN CẬY'),
                ),
                const SizedBox(height: 8.0),
                Text(
                  'Thợ quen của bạn',
                  style: NordicTypography.h1,
                ),
                const SizedBox(height: 4.0),
                Text(
                  'Những chuyên viên giúp việc bạn đã lưu để ưu tiên chỉ định phục vụ cho các ca dọn dẹp tiếp theo.',
                  style: NordicTypography.bodyRegular,
                ),
                const SizedBox(height: 16.0),

                if (_isLoading)
                  const Padding(
                    padding: EdgeInsets.symmetric(vertical: 40.0),
                    child: Center(
                      child: CircularProgressIndicator(
                        valueColor: AlwaysStoppedAnimation<Color>(NordicColors.primary),
                      ),
                    ),
                  )
                else if (_errorMessage != null)
                  Container(
                    padding: const EdgeInsets.all(16.0),
                    decoration: BoxDecoration(
                      color: NordicColors.errorContainer,
                      borderRadius: BorderRadius.circular(12.0),
                    ),
                    child: Column(
                      children: [
                        Text(
                          _errorMessage!,
                          style: NordicTypography.bodyRegular.copyWith(color: NordicColors.error),
                        ),
                        const SizedBox(height: 8.0),
                        NordicButton(
                          label: 'Thử lại',
                          variant: NordicButtonVariant.secondary,
                          onPressed: _loadFavorites,
                        ),
                      ],
                    ),
                  )
                else if (_workers.isEmpty)
                  Container(
                    padding: const EdgeInsets.symmetric(vertical: 48.0, horizontal: 20.0),
                    alignment: Alignment.center,
                    child: Column(
                      children: [
                        Container(
                          width: 64.0,
                          height: 64.0,
                          decoration: const BoxDecoration(
                            color: NordicColors.surfaceSubtle,
                            shape: BoxShape.circle,
                          ),
                          child: const Icon(
                            Icons.favorite_border_rounded,
                            color: NordicColors.secondaryAccent,
                            size: 32.0,
                          ),
                        ),
                        const SizedBox(height: 16.0),
                        Text('Chưa Có Thợ Quen Nào', style: NordicTypography.h3),
                        const SizedBox(height: 8.0),
                        Text(
                          'Sau mỗi ca hoàn thành ưng ý, bạn có thể lưu chuyên viên vào danh sách này để đặt lại nhanh chóng.',
                          textAlign: TextAlign.center,
                          style: NordicTypography.bodyRegular,
                        ),
                      ],
                    ),
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
    );
  }
}
