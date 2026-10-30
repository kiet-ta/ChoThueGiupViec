import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
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

  /// Bumped on every failed accept or decline so the offer shakes.
  int _errors = 0;

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
        _successMessage = 'Đã nhận việc. Đến nơi để bắt đầu.';
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isAccepting = false;
        _errors++;
        _errorMessage = 'Chưa nhận được việc: ${e.toString()}';
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
        _errors++;
        _errorMessage = 'Chưa bỏ qua được ca này: ${e.toString()}';
      });
    }
  }

  void _handleTimeout() {
    if (!mounted) return;
    final consecutive = _currentOffer?.isConsecutiveShift ?? false;
    setState(() {
      _currentOffer = null;
      _errorMessage = consecutive
          ? 'Hết thời gian xác nhận ca thứ hai (120s). Đơn có thể bị huỷ và hoàn tiền cho gia chủ.'
          : 'Hết thời gian nhận việc (30s). Đơn đã chuyển cho thợ khác.';
    });
  }

  @override
  Widget build(BuildContext context) {
    final accepted =
        !_isLoading &&
        _errorMessage == null &&
        _currentOffer == null &&
        _successMessage != null;
    return NordicPage.pushed(
      title: 'Việc mới',
      onRefresh: _loadCurrentOffer,
      appBarActions: [
        IconButton(
          icon: const Icon(Icons.refresh),
          tooltip: 'Làm mới',
          onPressed: _isLoading ? null : _loadCurrentOffer,
        ),
      ],
      actions: accepted ? _resultActions() : null,
      children: _buildContent(),
    );
  }

  /// The buttons of the result state sit in the action bar: go to the job, or look for the next one.
  List<Widget> _resultActions() {
    return [
      if (_acceptedAssignmentId != null)
        NordicButton(
          key: const Key('go_to_check_in_button'),
          label: 'Đến nơi',
          onPressed: () => Navigator.of(
            context,
          ).pushNamed(DispatchRoutes.checkIn, arguments: _acceptedAssignmentId),
        ),
      NordicButton(
        label: 'Tiếp tục tìm việc',
        variant: _acceptedAssignmentId != null
            ? NordicButtonVariant.secondary
            : NordicButtonVariant.primary,
        onPressed: _loadCurrentOffer,
      ),
    ];
  }

  List<Widget> _buildContent() {
    if (_isLoading) {
      return const [
        SizedBox(height: NordicSpace.xl),
        LoadingStateWidget(message: 'Đang tìm việc mới...'),
      ];
    }

    if (_errorMessage != null && _currentOffer == null) {
      return [
        ErrorStateWidget(message: _errorMessage!, onRetry: _loadCurrentOffer),
      ];
    }

    if (_successMessage != null && _currentOffer == null) {
      return [
        NordicCard(
          padding: const EdgeInsets.all(NordicSpace.lg),
          child: Column(
            children: [
              const Icon(
                Icons.check_circle_outline,
                size: 64.0,
                color: NordicColors.success,
              ),
              const SizedBox(height: NordicSpace.md),
              Text(
                _successMessage!,
                textAlign: TextAlign.center,
                style: NordicTypography.titleMedium,
              ),
            ],
          ),
        ),
      ];
    }

    if (_currentOffer == null) {
      return [
        EmptyStateWidget(
          title: 'Chưa có việc mới',
          subtitle: 'Ứng dụng đang tìm việc phù hợp trong bán kính 5 - 10 km. Bạn giữ ứng dụng mở nhé.',
          action: NordicButton(
            label: 'Quét lại',
            variant: NordicButtonVariant.secondary,
            onPressed: _loadCurrentOffer,
          ),
        ),
      ];
    }

    return [
      ErrorShake(
        errors: _errors,
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
            const SizedBox(height: NordicSpace.xs),
            const Text('Có gia chủ cần giúp việc', style: NordicTypography.h2),
            const SizedBox(height: NordicSpace.md),
            if (_errorMessage != null) ...[
              Container(
                padding: const EdgeInsets.all(NordicSpace.sm),
                margin: const EdgeInsets.only(bottom: NordicSpace.md),
                decoration: BoxDecoration(
                  color: NordicColors.errorContainer,
                  borderRadius: NordicRadius.fieldAll,
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
      ),
    ];
  }
}
