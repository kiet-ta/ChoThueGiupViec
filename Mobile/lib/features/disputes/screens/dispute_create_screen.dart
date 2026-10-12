import 'package:flutter/material.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
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

  const DisputeCreateScreen({super.key, required this.role, required this.orderId, this.service, this.uploader});

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
      _category != null && _description.text.trim().isNotEmpty && _evidence.isNotEmpty && _evidence.length <= maxEvidenceCount;

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

  Future<void> _submit() async {
    if (_sending) return; // one request per tap
    final errors = validateDispute(widget.role, _category, _description.text, _evidence.length);
    setState(() {
      _errors = errors;
      _serverFields = const {};
      _failure = null;
    });
    if (!errors.isEmpty) return;

    setState(() => _sending = true);
    try {
      final created = await _service.create(widget.role, buildDispute(widget.orderId, _category!, _description.text, _evidence));
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
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: const Text('Gửi khiếu nại')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _created != null ? _sent(_created!) : _form(),
        ),
      ),
    );
  }

  Widget _sent(Dispute d) {
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        NordicCard(
          child: Column(
            children: [
              const Icon(Icons.check_circle_rounded, size: 48.0, color: NordicColors.secondaryAccent),
              const SizedBox(height: 12.0),
              const Text('Đã gửi khiếu nại', key: Key('dispute-sent'), style: NordicTypography.h3),
              const SizedBox(height: 8.0),
              Text(
                'Admin sẽ xem xét. Hạn xử lý dự kiến: ${formatVietnamDateTime(d.slaDueAt)}.',
                textAlign: TextAlign.center,
                style: NordicTypography.bodyRegular,
              ),
              const SizedBox(height: 16.0),
              NordicButton(key: const Key('dispute-done'), label: 'Xong', onPressed: () => Navigator.of(context).pop(true)),
            ],
          ),
        ),
      ],
    );
  }

  Widget _form() {
    String? fieldError(String key) => _serverFields[key];
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        Text('Đơn #${widget.orderId}', style: NordicTypography.h3),
        const SizedBox(height: 4.0),
        const Text(
          'Khiếu nại phải gửi trong vòng 24 giờ sau ca làm, và mỗi đơn chỉ có một khiếu nại.',
          style: NordicTypography.bodySmall,
        ),
        const SizedBox(height: 16.0),
        const Text('Loại khiếu nại', style: NordicTypography.labelMedium),
        const SizedBox(height: 8.0),
        Wrap(
          spacing: 8.0,
          runSpacing: 8.0,
          children: [
            for (final c in categoriesFor(widget.role))
              ChoiceChip(
                key: Key('dispute-category-${c.code}'),
                label: Text(c.label),
                selected: _category == c.code,
                onSelected: (_) => setState(() => _category = c.code),
              ),
          ],
        ),
        _error(_errors.category ?? fieldError('category')),
        const SizedBox(height: 16.0),
        const Text('Mô tả sự việc', style: NordicTypography.labelMedium),
        const SizedBox(height: 8.0),
        TextField(
          key: const Key('dispute-description'),
          controller: _description,
          maxLines: 5,
          onChanged: (_) => setState(() {}),
          decoration: InputDecoration(
            border: const OutlineInputBorder(),
            hintText: 'Chuyện gì đã xảy ra?',
            counterText: '${_description.text.trim().length}/$maxDescriptionLength',
          ),
        ),
        _error(_errors.description ?? fieldError('description')),
        const SizedBox(height: 16.0),
        Text('Ảnh bằng chứng (${_evidence.length}/$maxEvidenceCount)', style: NordicTypography.labelMedium),
        const SizedBox(height: 8.0),
        for (var i = 0; i < _evidence.length; i++)
          ListTile(
            key: Key('dispute-evidence-$i'),
            dense: true,
            contentPadding: EdgeInsets.zero,
            leading: const Icon(Icons.image_outlined),
            title: Text('Ảnh ${i + 1}', style: NordicTypography.bodyRegular),
            trailing: IconButton(
              key: Key('dispute-remove-$i'),
              tooltip: 'Bỏ ảnh ${i + 1}',
              icon: const Icon(Icons.close_rounded),
              onPressed: () => setState(() => _evidence.removeAt(i)),
            ),
          ),
        if (_uploader.isAvailable)
          NordicButton(
            key: const Key('dispute-add-photo'),
            label: 'Thêm ảnh',
            variant: NordicButtonVariant.secondary,
            isLoading: _uploading,
            onPressed: _uploading || _evidence.length >= maxEvidenceCount ? null : _addPhoto,
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
        const SizedBox(height: 16.0),
        if (_failure != null)
          Padding(
            padding: const EdgeInsets.only(bottom: 12.0),
            child: Text(_failure!, key: const Key('dispute-failure'), style: NordicTypography.bodyRegular.copyWith(color: NordicColors.error)),
          ),
        NordicButton(
          key: const Key('dispute-submit'),
          label: 'Gửi khiếu nại',
          isLoading: _sending,
          onPressed: _sending || !_uploader.isAvailable || !_complete ? null : _submit,
        ),
      ],
    );
  }

  Widget _error(String? message) => message == null
      ? const SizedBox.shrink()
      : Padding(
          padding: const EdgeInsets.only(top: 4.0),
          child: Text(message, style: NordicTypography.bodySmall.copyWith(color: NordicColors.error)),
        );
}
