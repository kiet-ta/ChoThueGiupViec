import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/job_offer.dart';
import '../routes.dart';
import '../services/dispatch_service.dart';
import '../widgets/job_offer_card.dart';

/// Screen for displaying incoming worker job offers with 30s countdown (MOB-M3-01).
class DispatchOffersScreen extends StatefulWidget {
  final DispatchService? dispatchService;

  /// How often the screen asks for a new offer while none is shown: the backend pushes nothing to the worker, and an offer lives 30 s.
  final Duration pollInterval;

  const DispatchOffersScreen({
    super.key,
    this.dispatchService,
    this.pollInterval = const Duration(seconds: 5),
  });

  @override
  State<DispatchOffersScreen> createState() => _DispatchOffersScreenState();
}

class _DispatchOffersScreenState extends State<DispatchOffersScreen> {
  late final DispatchService _dispatchService;
  bool _isLoading = true;
  String? _errorMessage;
  JobOffer? _currentOffer;
  bool _isAccepting = false;
  bool _isDeclining = false;
  String? _successMessage;
  Timer? _pollTimer;
  int? _acceptedAssignmentId;

  @override
  void initState() {
    super.initState();
    _dispatchService = widget.dispatchService ?? DispatchService();
    _loadCurrentOffer();
    _pollTimer = Timer.periodic(widget.pollInterval, (_) => _poll());
  }

  @override
  void dispose() {
    _pollTimer?.cancel();
    super.dispose();
  }

  /// Quiet check for a new offer: no spinner, errors ignored (the next tick retries). Skipped while an offer or a result is on screen.
  Future<void> _poll() async {
    if (_currentOffer != null ||
        _isLoading ||
        _isAccepting ||
        _isDeclining ||
        _successMessage != null) {
      return;
    }
    try {
      final offer = await _dispatchService.getCurrentOffer();
      if (!mounted || offer == null || _currentOffer != null) return;
      setState(() {
        _currentOffer = offer;
        _errorMessage = null;
      });
    } catch (_) {}
  }

  Future<void> _loadCurrentOffer() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
      _successMessage = null;
    });

    try {
      final offer = await _dispatchService.getCurrentOffer();
      if (!mounted) return;
      setState(() {
        _currentOffer = offer;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _errorMessage = e.toString();
        _isLoading = false;
      });
    }
  }

  Future<void> _handleAccept() async {
    if (_currentOffer == null || _isAccepting) return;

    setState(() {
      _isAccepting = true;
      _errorMessage = null;
    });

    try {
      final result = await _dispatchService.acceptOffer(
        _currentOffer!.assignmentId,
      );
      if (!mounted) return;
      setState(() {
        _isAccepting = false;
        _currentOffer = null;
        _acceptedAssignmentId = result.assignmentId;
        _successMessage =
            'Nhận cuốc thành công! Mã ca làm #${result.assignmentId}';
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isAccepting = false;
        _errorMessage = 'Nhận cuốc không thành công: ${e.toString()}';
      });
    }
  }

  Future<void> _handleDecline() async {
    if (_currentOffer == null || _isDeclining) return;

    setState(() {
      _isDeclining = true;
      _errorMessage = null;
    });

    try {
      await _dispatchService.declineOffer(_currentOffer!.assignmentId);
      if (!mounted) return;
      setState(() {
        _isDeclining = false;
        _currentOffer = null;
        _successMessage = 'Đã bỏ qua ca làm việc.';
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isDeclining = false;
        _errorMessage = 'Không thể bỏ qua ca: ${e.toString()}';
      });
    }
  }

  void _handleTimeout() {
    if (!mounted) return;
    final consecutive = _currentOffer?.isConsecutiveShift ?? false;
    setState(() {
      _currentOffer = null;
      _errorMessage = consecutive
          ? 'Hết thời gian xác nhận ca thứ hai (120s). Đơn có thể bị huỷ và hoàn tiền cho khách.'
          : 'Hết thời gian nhận cuốc (30s). Đơn đã chuyển cho thợ khác.';
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Việc mới'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Làm mới',
            onPressed: _isLoading ? null : _loadCurrentOffer,
          ),
        ],
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _buildContent(),
        ),
      ),
    );
  }

  Widget _buildContent() {
    if (_isLoading) {
      return const LoadingStateWidget(
        message: 'Đang tìm kiếm ca làm việc mới...',
      );
    }

    if (_errorMessage != null && _currentOffer == null) {
      return SingleChildScrollView(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          children: [
            ErrorStateWidget(
              message: _errorMessage!,
              onRetry: _loadCurrentOffer,
            ),
          ],
        ),
      );
    }

    if (_successMessage != null && _currentOffer == null) {
      return SingleChildScrollView(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          children: [
            NordicCard(
              child: Padding(
                padding: const EdgeInsets.all(24.0),
                child: Column(
                  children: [
                    const Icon(
                      Icons.check_circle_outline,
                      size: 64.0,
                      color: NordicColors.success,
                    ),
                    const SizedBox(height: 16.0),
                    Text(
                      _successMessage!,
                      textAlign: TextAlign.center,
                      style: NordicTypography.h3,
                    ),
                    const SizedBox(height: 24.0),
                    if (_acceptedAssignmentId != null) ...[
                      NordicButton(
                        key: const Key('go_to_check_in_button'),
                        label: 'Đến nơi & Check-in',
                        onPressed: () => Navigator.of(context).pushNamed(
                          DispatchRoutes.checkIn,
                          arguments: _acceptedAssignmentId,
                        ),
                      ),
                      const SizedBox(height: 12.0),
                    ],
                    NordicButton(
                      label: 'Tiếp tục tìm ca',
                      onPressed: _loadCurrentOffer,
                    ),
                  ],
                ),
              ),
            ),
          ],
        ),
      );
    }

    if (_currentOffer == null) {
      return SingleChildScrollView(
        padding: const EdgeInsets.all(24.0),
        child: Column(
          children: [
            EmptyStateWidget(
              title: 'Chưa có cuốc mới',
              subtitle: 'Hệ thống đang quét các ca làm việc phù hợp với bạn trong bán kính 5 - 10 km. Vui lòng giữ ứng dụng mở.',
              action: ElevatedButton.icon(
                onPressed: _loadCurrentOffer,
                icon: const Icon(Icons.refresh_rounded, size: 18.0),
                label: const Text('Quét lại'),
              ),
            ),
          ],
        ),
      );
    }

    return SingleChildScrollView(
      padding: const EdgeInsets.all(16.0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Align(
            alignment: Alignment.centerLeft,
            child: StatusPill(
              _currentOffer!.isConsecutiveShift
                  ? 'Xác nhận ca thứ hai (120s)'
                  : 'Đề nghị ca mới (30s)',
              tone: PillTone.warn,
              dot: true,
            ),
          ),
          const SizedBox(height: 8.0),
          const Text('Có khách cần giúp việc!', style: NordicTypography.h2),
          const SizedBox(height: 16.0),
          if (_errorMessage != null) ...[
            Container(
              padding: const EdgeInsets.all(12.0),
              margin: const EdgeInsets.only(bottom: 16.0),
              decoration: BoxDecoration(
                color: NordicColors.errorContainer,
                borderRadius: BorderRadius.circular(8.0),
                border: Border.all(color: NordicColors.error),
              ),
              child: Text(
                _errorMessage!,
                style: NordicTypography.bodySmall.copyWith(
                  color: NordicColors.error,
                ),
              ),
            ),
          ],
          JobOfferCard(
            offer: _currentOffer!,
            onAccept: _handleAccept,
            onDecline: _handleDecline,
            onTimeout: _handleTimeout,
            isAccepting: _isAccepting,
            isDeclining: _isDeclining,
          ),
        ],
      ),
    );
  }
}
