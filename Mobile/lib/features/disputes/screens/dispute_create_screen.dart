import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/progress_ring.dart';
import '../../payouts/logic/payout_logic.dart';
import '../logic/dispute_logic.dart';
import '../models/dispute_models.dart';
import '../services/disputes_service.dart';
import '../services/api_evidence_uploader.dart';
import '../services/evidence_uploader.dart';

/// The form that files a dispute about one order (MOB-M6-03); contract disputes.md 2.1.
/// Evidence photos go through [IEvidenceUploader] (by default [ApiEvidenceUploader]); when the uploader is unavailable the form
/// says so instead of offering a send that could not work.
class DisputeCreateScreen extends StatefulWidget {
  final DisputeRole role;
  final int orderId;
  final IDisputesService? service;
  final IEvidenceUploader? uploader;

  const DisputeCreateScreen({
    super.key,
    required this.role,
    required this.orderId,
    this.service,
    this.uploader,
  });

  @override
  State<DisputeCreateScreen> createState() => _DisputeCreateScreenState();
}

class _DisputeCreateScreenState extends State<DisputeCreateScreen> {
  late final IDisputesService _service;
  late final IEvidenceUploader _uploader;
  final _description = TextEditingController();
  final List<String> _evidence = [];

  String? _category;
  DisputeErrors _errors = const DisputeErrors();
  Map<String, String> _serverFields = const {};
  String? _failure;
  bool _sending = false;
  bool _uploading = false;
  Dispute? _created;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? DisputesService();
    _uploader = widget.uploader ?? ApiEvidenceUploader(role: widget.role);
  }

  @override
  void dispose() {
    _description.dispose();
    super.dispose();
  }

  bool get _complete =>
      _category != null &&
      _description.text.trim().isNotEmpty &&
      _evidence.isNotEmpty &&
      _evidence.length <= maxEvidenceCount;

  Future<void> _addPhoto() async {
    if (_uploading || _evidence.length >= maxEvidenceCount) return;
    setState(() {
      _uploading = true;
      _failure = null;
    });
    try {
      final url = await _uploader.pickAndUpload();
      if (!mounted) return;
      setState(() {
        if (url != null) _evidence.add(url);
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _failure = e.message);
    } finally {
      if (mounted) setState(() => _uploading = false);
    }
  }

  /// Refused sends so far: each one shakes the form.
  int _formErrors = 0;

  Future<void> _submit() async {
    if (_sending) return; // one request per tap
    final errors = validateDispute(
      widget.role,
      _category,
      _description.text,
      _evidence.length,
    );
    setState(() {
      _errors = errors;
      _serverFields = const {};
      _failure = null;
    });
    if (!errors.isEmpty) {
      setState(() => _formErrors++);
      return;
    }

    setState(() => _sending = true);
    try {
      final created = await _service.create(
        widget.role,
        buildDispute(widget.orderId, _category!, _description.text, _evidence),
      );
      if (!mounted) return;
      setState(() => _created = created);
    } on ApiException catch (e) {
      final failure = disputeFailure(e);
      if (!mounted) return;
      setState(() {
        _failure = failure.message;
        _serverFields = failure.fields;
      });
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final sent = _created != null;
    return NordicPage.pushed(
      title: 'Khiếu nại',
      leading: const CloseButton(),
      bodyOverride: sent
          ? _sent(_created!)
          : ErrorShake(errors: _formErrors, child: _form()),
      actions: sent
          ? null
          : [
              NordicButton(
                key: const Key('dispute-submit'),
                label: 'Gửi khiếu nại',
                isLoading: _sending,
                onPressed: _sending || !_uploader.isAvailable || !_complete
                    ? null
                    : _submit,
              ),
            ],
      children: const [],
    );
  }

  Widget _sent(Dispute d) {
    return ListView(
      padding: const EdgeInsets.all(20.0),
      children: [
        const SizedBox(height: NordicSpace.lg),
        const Center(
          child: ProgressRing(
            value: 1.0,
            size: 112.0,
            child: Icon(Icons.check, size: 44.0, color: NordicColors.primary),
          ),
        ),
        const SizedBox(height: NordicSpace.md),
        const Text(
          'Đã gửi khiếu nại',
          key: Key('dispute-sent'),
          textAlign: TextAlign.center,
          style: NordicTypography.titleMedium,
        ),
        const SizedBox(height: NordicSpace.xs),
        Text(
          'Chúng tôi sẽ xem xét. Hạn xử lý dự kiến: ${formatVietnamDateTime(d.slaDueAt)}.',
          textAlign: TextAlign.center,
          style: NordicTypography.bodySmall,
        ),
        const SizedBox(height: NordicSpace.lg),
        NordicButton(
          key: const Key('dispute-done'),
          label: 'Xong',
          width: double.infinity,
          onPressed: () => Navigator.of(context).pop(true),
        ),
      ],
    );
  }

  Widget _caps(String text, {String? trailing}) => Padding(
    padding: const EdgeInsets.only(top: 24.0, bottom: 8.0),
    child: Row(
      children: [
        Expanded(child: Text(text, style: NordicTypography.titleSmall)),
        if (trailing != null) Text(trailing, style: NordicTypography.bodySmall),
      ],
    ),
  );

  Widget _form() {
    String? fieldError(String key) => _serverFields[key];
    return ListView(
      padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 24.0),
      children: [
        Container(
          padding: const EdgeInsets.all(12.0),
          decoration: BoxDecoration(
            color: NordicColors.surfaceLow,
            borderRadius: NordicRadius.cardAll,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Đơn #${widget.orderId}',
                style: NordicTypography.titleSmall,
              ),
              const SizedBox(height: NordicSpace.xxs),
              const Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Icon(
                    Icons.timer_outlined,
                    size: 16.0,
                    color: NordicColors.textSecondary,
                  ),
                  SizedBox(width: 6.0),
                  Expanded(
                    child: Text(
                      'Gửi trong vòng 24 giờ sau ca làm; mỗi đơn chỉ có một khiếu nại.',
                      style: NordicTypography.bodySmall,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
        _caps('Vấn đề'),
        Wrap(
          spacing: 8.0,
          runSpacing: 8.0,
          children: [
            for (final c in categoriesFor(widget.role))
              ChoiceChip(
                key: Key('dispute-category-${c.code}'),
                label: Text(c.label),
                selected: _category == c.code,
                showCheckmark: false,
                onSelected: (_) => setState(() => _category = c.code),
              ),
          ],
        ),
        _error(_errors.category ?? fieldError('category')),
        _caps('Mô tả'),
        TextField(
          key: const Key('dispute-description'),
          controller: _description,
          maxLines: 5,
          onChanged: (_) => setState(() {}),
          decoration: InputDecoration(
            hintText: 'Chuyện gì đã xảy ra?',
            counterText:
                '${_description.text.trim().length}/$maxDescriptionLength',
          ),
        ),
        _error(_errors.description ?? fieldError('description')),
        _caps('Ảnh bằng chứng (${_evidence.length}/$maxEvidenceCount)'),
        if (_evidence.isNotEmpty) ...[
          GridView.count(
            crossAxisCount: 3,
            mainAxisSpacing: 8.0,
            crossAxisSpacing: 8.0,
            shrinkWrap: true,
            physics: const NeverScrollableScrollPhysics(),
            children: [
              for (var i = 0; i < _evidence.length; i++)
                Container(
                  key: Key('dispute-evidence-$i'),
                  decoration: BoxDecoration(
                    color: NordicColors.surfaceContainer,
                    borderRadius: NordicRadius.tileAll,
                  ),
                  child: Stack(
                    children: [
                      Center(
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            const Icon(
                              Icons.image_outlined,
                              color: NordicColors.textSecondary,
                            ),
                            Text(
                              'Ảnh ${i + 1}',
                              style: NordicTypography.bodySmall,
                            ),
                          ],
                        ),
                      ),
                      Positioned(
                        top: 0.0,
                        right: 0.0,
                        child: IconButton(
                          key: Key('dispute-remove-$i'),
                          tooltip: 'Bỏ ảnh ${i + 1}',
                          iconSize: 18.0,
                          icon: const Icon(Icons.close),
                          onPressed: () =>
                              setState(() => _evidence.removeAt(i)),
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          ),
          const SizedBox(height: NordicSpace.xs),
        ],
        if (_uploader.isAvailable)
          NordicButton(
            key: const Key('dispute-add-photo'),
            label: 'Thêm ảnh',
            width: double.infinity,
            icon: const Icon(Icons.add_a_photo_outlined, size: 18.0),
            variant: NordicButtonVariant.secondary,
            isLoading: _uploading,
            onPressed: _uploading || _evidence.length >= maxEvidenceCount
                ? null
                : _addPhoto,
          )
        else
          const NordicCard(
            child: Text(
              'Chưa thể đính kèm ảnh trong phiên bản này (máy chủ chưa có chức năng tải ảnh), nên chưa gửi được khiếu nại từ ứng dụng.',
              key: Key('dispute-unavailable'),
              style: NordicTypography.bodyRegular,
            ),
          ),
        _error(_errors.evidence ?? fieldError('evidenceUrls')),
        if (_failure != null)
          Padding(
            padding: const EdgeInsets.only(top: 12.0),
            child: Text(
              _failure!,
              key: const Key('dispute-failure'),
              style: NordicTypography.bodyRegular.copyWith(
                color: NordicColors.error,
              ),
            ),
          ),
      ],
    );
  }

  Widget _error(String? message) => message == null
      ? const SizedBox.shrink()
      : Padding(
          padding: const EdgeInsets.only(top: 4.0),
          child: Text(
            message,
            style: NordicTypography.bodySmall.copyWith(
              color: NordicColors.error,
            ),
          ),
        );
}
