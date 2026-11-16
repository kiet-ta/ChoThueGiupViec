import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/worker_cancel.dart';
import '../services/dispatch_service.dart';

/// "Huỷ ca" of an accepted job that has not started (Q15, decisions D35). Closed it is one red button; opened it reads the
/// preview first, so the worker sees the consequence (the n-th cancel of 30 days, the lock at 3, the auto-accept switch
/// turning off, a cancel close to the start refunding the order) and the way to the incident report (no penalty) before
/// confirming with a reason. Same inline pattern as the customer's "Huỷ đơn" (`order_detail_screen.dart`).
class WorkerCancelSection extends StatefulWidget {
  final int assignmentId;
  final DispatchService service;
  final ValueChanged<WorkerCancelResult> onCancelled;

  /// Opens the incident report (BR-10) for this job instead of cancelling.
  final VoidCallback onReportIncident;

  /// D43: called instead of [onCancelled] when the cancel of an agency worker only told the agency (no count, no lock, the job stays).
  /// When null the section shows that confirmation itself, so a screen that only knows the freelancer result keeps working.
  final ValueChanged<WorkerCancelResult>? onAgencyNotified;

  const WorkerCancelSection({
    super.key,
    required this.assignmentId,
    required this.service,
    required this.onCancelled,
    required this.onReportIncident,
    this.onAgencyNotified,
  });

  @override
  State<WorkerCancelSection> createState() => _WorkerCancelSectionState();
}

class _WorkerCancelSectionState extends State<WorkerCancelSection> {
  final _reason = TextEditingController();
  bool _open = false;
  bool _loadingPreview = false;
  bool _cancelling = false;
  WorkerCancelPreview? _preview;
  WorkerCancelResult? _agencyNotice;
  String? _error;
  int _errors = 0;

  @override
  void dispose() {
    _reason.dispose();
    super.dispose();
  }

  Future<void> _openForm() async {
    setState(() {
      _open = true;
      _error = null;
      _loadingPreview = true;
    });
    try {
      final preview = await widget.service.getCancelPreview(
        widget.assignmentId,
      );
      if (!mounted) return;
      setState(() => _preview = preview);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _errors++;
        _error = workerCancelFailure(e);
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _errors++;
        _error = 'Chưa tải được thông tin huỷ ca. Bạn thử lại nhé.';
      });
    } finally {
      if (mounted) setState(() => _loadingPreview = false);
    }
  }

  Future<void> _submit() async {
    if (_cancelling) return;
    final problem = validateWorkerCancelReason(_reason.text);
    if (problem != null) {
      return setState(() {
        _errors++;
        _error = problem;
      });
    }
    setState(() {
      _cancelling = true;
      _error = null;
    });
    try {
      final result = await widget.service.cancelAssignment(
        widget.assignmentId,
        _reason.text.trim(),
      );
      if (!mounted) return;
      if (result.isAgencyNotice) {
        final callback = widget.onAgencyNotified;
        if (callback != null) {
          callback(result);
        } else {
          setState(() => _agencyNotice = result);
        }
        return;
      }
      widget.onCancelled(result);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _errors++;
        _error = workerCancelFailure(e);
      });
    } finally {
      if (mounted) setState(() => _cancelling = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final notice = _agencyNotice;
    if (notice != null) {
      return NordicCard(
        key: const Key('worker-cancel-agency-sent'),
        child: Text(
          workerCancelNotice(notice),
          style: NordicTypography.bodyRegular,
        ),
      );
    }
    if (!_open) {
      return NordicButton(
        key: const Key('open-worker-cancel'),
        label: 'Huỷ ca',
        width: double.infinity,
        variant: NordicButtonVariant.danger,
        onPressed: _openForm,
      );
    }

    final preview = _preview;
    return NordicCard(
      key: const Key('worker-cancel-form'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Text('Huỷ ca', style: NordicTypography.titleSmall),
          const SizedBox(height: NordicSpace.xs),
          if (_loadingPreview)
            const LoadingStateWidget(message: 'Đang kiểm tra...')
          else if (preview != null) ...[
            if (preview.rescueFreeCancel)
              Container(
                key: const Key('worker-cancel-rescue-free'),
                width: double.infinity,
                margin: const EdgeInsets.only(bottom: NordicSpace.xs),
                padding: const EdgeInsets.all(NordicSpace.sm),
                decoration: const BoxDecoration(
                  color: NordicColors.primaryFixed,
                  borderRadius: NordicRadius.tileAll,
                ),
                child: Text(
                  workerCancelConsequences(preview).first,
                  style: NordicTypography.bodySmall.copyWith(
                    color: NordicColors.primary,
                  ),
                ),
              ),
            for (final line in workerCancelConsequences(
              preview,
            ).skip(preview.rescueFreeCancel ? 1 : 0))
              Padding(
                padding: const EdgeInsets.only(bottom: NordicSpace.xxs),
                child: Text(line, style: NordicTypography.bodySmall),
              ),
            if (preview.willLockAccount) ...[
              const SizedBox(height: NordicSpace.xxs),
              Container(
                key: const Key('worker-cancel-lock-warning'),
                width: double.infinity,
                padding: const EdgeInsets.all(NordicSpace.sm),
                decoration: const BoxDecoration(
                  color: NordicColors.amberSoft,
                  borderRadius: NordicRadius.tileAll,
                ),
                child: Text(
                  'Đây là lần huỷ thứ ${preview.nextCount}: tài khoản của bạn sẽ bị khoá cho tới khi quản trị viên mở lại.',
                  style: NordicTypography.bodySmall.copyWith(
                    color: NordicColors.onAmberSoft,
                  ),
                ),
              ),
            ],
            const SizedBox(height: NordicSpace.xs),
            // The incident report waives a freelancer's penalty; an agency worker is not penalised by the app (D43).
            if (!preview.agencyWorker) ...[
              NordicButton(
                key: const Key('worker-cancel-report-incident'),
                label: 'Gặp sự cố trên đường? Báo sự cố',
                width: double.infinity,
                variant: NordicButtonVariant.ghost,
                onPressed: widget.onReportIncident,
              ),
              const SizedBox(height: NordicSpace.xs),
            ],
            NordicTextInput(
              key: const Key('worker-cancel-reason'),
              label: 'Lý do huỷ ca',
              hintText: 'Ví dụ: ốm, việc gia đình đột xuất',
              controller: _reason,
              maxLines: 3,
            ),
          ],
          if (_error != null) ...[
            const SizedBox(height: NordicSpace.xs),
            ErrorShake(
              errors: _errors,
              child: Text(
                _error!,
                key: const Key('worker-cancel-error'),
                style: NordicTypography.bodySmall.copyWith(
                  color: NordicColors.error,
                ),
              ),
            ),
          ],
          const SizedBox(height: NordicSpace.sm),
          if (preview != null)
            NordicButton(
              key: const Key('submit-worker-cancel'),
              label: 'Xác nhận huỷ ca',
              width: double.infinity,
              variant: NordicButtonVariant.danger,
              isLoading: _cancelling,
              onPressed: _submit,
            ),
          const SizedBox(height: NordicSpace.xxs),
          NordicButton(
            label: 'Không huỷ nữa',
            width: double.infinity,
            variant: NordicButtonVariant.ghost,
            onPressed: () => setState(() {
              _open = false;
              _error = null;
            }),
          ),
        ],
      ),
    );
  }
}

/// What the worker reads before confirming, in plain words (decisions D35).
List<String> workerCancelConsequences(WorkerCancelPreview p) => p.agencyWorker
    ? [
        p.substituteSearchPossible
            ? 'Doanh nghiệp sẽ nhận thông báo và đổi thợ khác cho ca này.'
            : 'Ca bắt đầu sau chưa tới 2 giờ: doanh nghiệp được báo, nhưng hệ thống sẽ điều thợ cứu hộ ngay và doanh nghiệp có thể bị tính vi phạm.',
        'Lần huỷ này không tính vào số lần huỷ và tài khoản của bạn không bị khoá.',
      ]
    : p.rescueFreeCancel
    ? const [
        'Đây là ca cứu hộ: huỷ trước giờ check-in thì không bị phạt và không tính vào số lần huỷ.',
        'Đơn sẽ bị huỷ và gia chủ được hoàn tiền.',
      ]
    : [
        p.substituteSearchPossible
            ? 'Hệ thống sẽ báo gia chủ và tìm thợ khác thay bạn.'
            : 'Ca bắt đầu sau chưa tới 30 phút: đơn sẽ bị huỷ và gia chủ được hoàn tiền.',
        'Đây là lần huỷ thứ ${p.nextCount} trong ${p.windowDays} ngày. Huỷ ${p.lockCount} lần, tài khoản sẽ bị khoá.',
        if (p.autoAcceptOn) '"Tự động nhận đơn" sẽ tắt.',
        'Gặp sự cố bất khả kháng thì báo sự cố để không bị tính vi phạm.',
      ];

/// A server refusal of the cancel, in plain words; other errors show the server's message.
String workerCancelFailure(ApiException e) {
  if (e.statusCode == 400) {
    return e.fieldErrors?['reason']?.firstOrNull ?? 'Lý do huỷ chưa hợp lệ.';
  }
  if (e.statusCode == 403) {
    return 'Thợ của doanh nghiệp báo vắng qua doanh nghiệp, không huỷ ca trên ứng dụng.';
  }
  if (e.statusCode == 404) return 'Không tìm thấy ca này trong lịch của bạn.';
  if (e.statusCode == 409) {
    return 'Ca này không huỷ được nữa: đã tới giờ làm hoặc đã check-in.';
  }
  return e.message.isNotEmpty
      ? e.message
      : 'Chưa huỷ được ca. Bạn thử lại nhé.';
}
