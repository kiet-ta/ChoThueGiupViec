import 'package:flutter/material.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/rating_logic.dart';
import '../models/rating_models.dart';
import '../services/ratings_service.dart';

/// Arguments of the rating route: whoever opens the screen after a completed job passes who is rating and which assignment.
class RatingScreenArgs {
  final RatingRole role;
  final int assignmentId;

  /// Shown in the title ("Đánh giá Nguyễn Thị Mai"); optional.
  final String? ratedName;

  const RatingScreenArgs({required this.role, required this.assignmentId, this.ratedName});
}

/// Two-way rating (MOB-M6-01 customer rates the worker, MOB-M6-02 worker rates the customer); contract ratings.md 2.1-2.2.
class RatingScreen extends StatefulWidget {
  final RatingRole role;
  final int assignmentId;
  final String? ratedName;
  final IRatingsService? service;

  const RatingScreen({
    super.key,
    required this.role,
    required this.assignmentId,
    this.ratedName,
    this.service,
  });

  @override
  State<RatingScreen> createState() => _RatingScreenState();
}

enum _Phase { loading, loadFailed, closed, form, submitted }

class _RatingScreenState extends State<RatingScreen> {
  late final IRatingsService _service;
  final _comment = TextEditingController();

  _Phase _phase = _Phase.loading;
  RatingWindow? _window;
  String? _loadError;

  int? _stars;
  final Map<String, int> _criteria = {};
  RatingErrors _errors = const RatingErrors();
  Map<String, String> _serverFields = const {};
  String? _failure;
  bool _sending = false;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? RatingsService();
    _loadWindow();
  }

  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  Future<void> _loadWindow() async {
    setState(() {
      _phase = _Phase.loading;
      _loadError = null;
    });
    try {
      final window = await _service.getWindow(widget.role, widget.assignmentId);
      if (!mounted) return;
      setState(() {
        _window = window;
        _phase = window.canRate ? _Phase.form : _Phase.closed;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _loadError = e.isNotFound ? 'Không tìm thấy ca làm này.' : e.message;
        _phase = _Phase.loadFailed;
      });
    }
  }

  bool get _complete => _stars != null && widget.role.criteria.every((c) => _criteria[c.key] != null);

  Future<void> _submit() async {
    if (_sending) return; // one request per tap
    final errors = validateRating(widget.role, _stars, _criteria, _comment.text);
    setState(() {
      _errors = errors;
      _serverFields = const {};
      _failure = null;
    });
    if (!errors.isEmpty) return;

    setState(() => _sending = true);
    try {
      await _service.submit(widget.role, widget.assignmentId, buildSubmission(widget.role, _stars!, _criteria, _comment.text));
      if (!mounted) return;
      setState(() => _phase = _Phase.submitted);
    } on ApiException catch (e) {
      final failure = ratingFailure(e);
      if (!mounted) return;
      setState(() {
        _failure = failure.message;
        _serverFields = failure.fields;
      });
      if (failure.reloadWindow) await _loadWindow();
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final title = widget.ratedName == null ? widget.role.title : '${widget.role.title} ${widget.ratedName}';
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(title: Text(title)),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: _body(),
        ),
      ),
    );
  }

  Widget _body() {
    switch (_phase) {
      case _Phase.loading:
        return const LoadingStateWidget(message: 'Đang kiểm tra cửa sổ đánh giá…');
      case _Phase.loadFailed:
        return ErrorStateWidget(message: _loadError ?? 'Không tải được.', onRetry: _loadWindow);
      case _Phase.closed:
        return _closed();
      case _Phase.submitted:
        return _thanks();
      case _Phase.form:
        return _form();
    }
  }

  Widget _closed() {
    final message = reasonMessage(_window?.reason ?? '') ?? 'Hiện chưa đánh giá được ca này.';
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        NordicCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Icon(Icons.hourglass_empty_rounded, color: NordicColors.textSecondary),
              const SizedBox(height: 8.0),
              Text(message, key: const Key('rating-closed-message'), style: NordicTypography.bodyRegular),
            ],
          ),
        ),
      ],
    );
  }

  Widget _thanks() {
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        NordicCard(
          child: Column(
            children: [
              const Icon(Icons.check_circle_rounded, size: 48.0, color: NordicColors.secondaryAccent),
              const SizedBox(height: 12.0),
              const Text('Cảm ơn bạn đã đánh giá', key: Key('rating-thanks'), style: NordicTypography.h3),
              const SizedBox(height: 8.0),
              Text(
                widget.role.isInternalOnly
                    ? 'Đánh giá của bạn đã được lưu và chỉ dùng nội bộ để giữ chất lượng dịch vụ.'
                    : 'Đánh giá đã được lưu vĩnh viễn và góp vào điểm uy tín của thợ.',
                textAlign: TextAlign.center,
                style: NordicTypography.bodyRegular,
              ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _form() {
    final window = _window!;
    return ListView(
      padding: const EdgeInsets.all(16.0),
      children: [
        const EyebrowBadge(text: 'Đánh giá 2 chiều cố định'),
        const SizedBox(height: 12.0),
        Text(widget.role == RatingRole.customer ? 'Ca làm vừa rồi thế nào?' : 'Gia chủ phối hợp ra sao?', style: NordicTypography.h2),
        const SizedBox(height: 4.0),
        Text(countdownText(window.secondsRemaining), key: const Key('rating-countdown'), style: NordicTypography.labelMedium.copyWith(color: NordicColors.textSecondary)),
        if (widget.role.isInternalOnly) ...[
          const SizedBox(height: 4.0),
          Text('Đánh giá này chỉ dùng nội bộ, gia chủ không nhìn thấy.', key: const Key('rating-internal'), style: NordicTypography.bodySmall),
        ],
        const SizedBox(height: 16.0),
        NordicCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text('Đánh giá tổng thể', style: NordicTypography.h3),
              const SizedBox(height: 8.0),
              StarRow(
                keyPrefix: 'stars',
                label: 'Tổng thể',
                value: _stars,
                onChanged: (v) => setState(() => _stars = v),
              ),
              _fieldError(_errors.stars ?? _serverFields['stars']),
              const SizedBox(height: 16.0),
              const Text('Theo từng tiêu chí', style: NordicTypography.h3),
              for (final c in widget.role.criteria) ...[
                const SizedBox(height: 12.0),
                Text(c.label, style: NordicTypography.labelMedium),
                StarRow(
                  keyPrefix: 'criterion-${c.key}',
                  label: c.label,
                  value: _criteria[c.key],
                  onChanged: (v) => setState(() => _criteria[c.key] = v),
                ),
                _fieldError(_errors.criteria[c.key] ?? _serverFields['criteria.${c.key}']),
              ],
              _fieldError(_serverFields['criteria']),
            ],
          ),
        ),
        const SizedBox(height: 12.0),
        NordicCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text('Nhận xét (không bắt buộc)', style: NordicTypography.h3),
              const SizedBox(height: 8.0),
              TextField(
                key: const Key('rating-comment'),
                controller: _comment,
                maxLines: 4,
                onChanged: (_) => setState(() {}),
                decoration: const InputDecoration(hintText: 'Chia sẻ thêm về ca làm…'),
              ),
              Text(
                '${_comment.text.trim().length}/$maxCommentLength ký tự',
                style: NordicTypography.bodySmall.copyWith(color: NordicColors.textSecondary),
              ),
              _fieldError(_errors.comment ?? _serverFields['comment']),
            ],
          ),
        ),
        const SizedBox(height: 12.0),
        Text(
          'Đánh giá được lưu vĩnh viễn, không sửa hay gửi lại được sau khi bấm Gửi.',
          style: NordicTypography.bodySmall.copyWith(color: NordicColors.textSecondary),
        ),
        if (_failure != null) ...[
          const SizedBox(height: 12.0),
          Text(_failure!, key: const Key('rating-failure'), style: NordicTypography.bodyRegular.copyWith(color: NordicColors.error)),
        ],
        const SizedBox(height: 16.0),
        NordicButton(
          key: const Key('rating-submit'),
          label: 'Gửi đánh giá',
          isLoading: _sending,
          onPressed: _complete && !_sending ? _submit : null,
        ),
      ],
    );
  }

  Widget _fieldError(String? message) {
    if (message == null) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 4.0),
      child: Text(message, style: NordicTypography.bodySmall.copyWith(color: NordicColors.error)),
    );
  }
}

/// Five tappable stars; [onChanged] gets 1-5. Every star has a spoken label ("Tổng thể: 3 sao").
class StarRow extends StatelessWidget {
  final String keyPrefix;
  final String label;
  final int? value;
  final ValueChanged<int> onChanged;

  const StarRow({super.key, required this.keyPrefix, required this.label, required this.value, required this.onChanged});

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        for (var i = minScore; i <= maxScore; i++)
          Semantics(
            button: true,
            selected: value != null && i <= value!,
            label: '$label: $i sao',
            child: IconButton(
              key: Key('$keyPrefix-$i'),
              onPressed: () => onChanged(i),
              icon: Icon(
                value != null && i <= value! ? Icons.star_rounded : Icons.star_border_rounded,
                color: NordicColors.accentAmber,
                size: 32.0,
              ),
            ),
          ),
      ],
    );
  }
}
