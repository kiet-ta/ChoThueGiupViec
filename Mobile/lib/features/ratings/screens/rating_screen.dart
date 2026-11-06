import 'package:flutter/material.dart';

import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/progress_ring.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../customers/services/favorite_worker_service.dart';
import '../logic/rating_logic.dart';
import '../models/rating_models.dart';
import '../services/ratings_service.dart';

/// Arguments of the rating route: whoever opens the screen after a completed job passes who is rating and which assignment.
class RatingScreenArgs {
  final RatingRole role;
  final int assignmentId;

  /// Shown in the title ("Đánh giá Nguyễn Thị Mai"); optional.
  final String? ratedName;

  /// Worker id when role == RatingRole.customer; optional.
  final int? workerId;

  const RatingScreenArgs({
    required this.role,
    required this.assignmentId,
    this.ratedName,
    this.workerId,
  });
}

/// Two-way rating (MOB-M6-01 customer rates the worker, MOB-M6-02 worker rates the customer); contract ratings.md 2.1-2.2.
class RatingScreen extends StatefulWidget {
  final RatingRole role;
  final int assignmentId;
  final String? ratedName;
  final int? workerId;
  final IRatingsService? service;
  final FavoriteWorkerService? favoriteService;

  const RatingScreen({
    super.key,
    required this.role,
    required this.assignmentId,
    this.ratedName,
    this.workerId,
    this.service,
    this.favoriteService,
  });

  @override
  State<RatingScreen> createState() => _RatingScreenState();
}

enum _Phase { loading, loadFailed, closed, form, submitted }

class _RatingScreenState extends State<RatingScreen> {
  late final IRatingsService _service;
  late final FavoriteWorkerService _favoriteService;
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

  bool _isFavorite = false;
  bool _favoriteLoading = false;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? RatingsService();
    _favoriteService = widget.favoriteService ?? FavoriteWorkerService();
    _loadWindow();
    _loadFavoriteStatus();
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

  Future<void> _loadFavoriteStatus() async {
    if (widget.role != RatingRole.customer || widget.workerId == null) return;
    try {
      final list = await _favoriteService.getFavoriteWorkers();
      if (!mounted) return;
      setState(() {
        _isFavorite = list.any((w) => w.workerId == widget.workerId);
      });
    } catch (_) {
      // Silently ignore check error; defaults to not favorited.
    }
  }

  Future<void> _toggleFavorite() async {
    final workerId = widget.workerId;
    if (workerId == null || _favoriteLoading) return;
    setState(() => _favoriteLoading = true);
    final willFavorite = !_isFavorite;
    try {
      if (willFavorite) {
        await _favoriteService.addFavoriteWorker(workerId);
      } else {
        await _favoriteService.removeFavoriteWorker(workerId);
      }
      if (!mounted) return;
      setState(() {
        _isFavorite = willFavorite;
        _favoriteLoading = false;
      });
      final name = widget.ratedName;
      final message = willFavorite
          ? (name != null
                ? 'Đã thêm $name vào danh sách yêu thích'
                : 'Đã thêm vào danh sách yêu thích')
          : (name != null
                ? 'Đã bỏ $name khỏi danh sách yêu thích'
                : 'Đã bỏ khỏi danh sách yêu thích');
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(message), duration: const Duration(seconds: 2)),
      );
    } catch (_) {
      if (!mounted) return;
      setState(() => _favoriteLoading = false);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Không thể cập nhật danh sách yêu thích'),
          duration: Duration(seconds: 2),
        ),
      );
    }
  }

  bool get _complete =>
      _stars != null &&
      widget.role.criteria.every((c) => _criteria[c.key] != null);

  Future<void> _submit() async {
    if (_sending) return; // one request per tap
    final errors = validateRating(
      widget.role,
      _stars,
      _criteria,
      _comment.text,
    );
    setState(() {
      _errors = errors;
      _serverFields = const {};
      _failure = null;
    });
    if (!errors.isEmpty) return;

    setState(() => _sending = true);
    try {
      await _service.submit(
        widget.role,
        widget.assignmentId,
        buildSubmission(widget.role, _stars!, _criteria, _comment.text),
      );
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
    final title = widget.ratedName == null
        ? widget.role.title
        : '${widget.role.title} ${widget.ratedName}';
    return NordicPage.pushed(
      title: title,
      leading: const CloseButton(),
      bodyOverride: _body(),
      actions: _phase == _Phase.form
          ? [
              NordicButton(
                key: const Key('rating-submit'),
                label: 'Gửi đánh giá',
                isLoading: _sending,
                onPressed: _complete && !_sending ? _submit : null,
              ),
            ]
          : null,
      children: const [],
    );
  }

  Widget _body() {
    switch (_phase) {
      case _Phase.loading:
        return const LoadingStateWidget(message: 'Đang tải…');
      case _Phase.loadFailed:
        return ErrorStateWidget(
          message: _loadError ?? 'Không tải được.',
          onRetry: _loadWindow,
        );
      case _Phase.closed:
        return _closed();
      case _Phase.submitted:
        return _thanks();
      case _Phase.form:
        return _form();
    }
  }

  Widget _closed() {
    final message =
        reasonMessage(_window?.reason ?? '') ??
        'Hiện chưa đánh giá được ca này.';
    return ListView(
      padding: const EdgeInsets.all(20.0),
      children: [
        NordicCard(
          child: Row(
            children: [
              const Icon(
                Icons.timer_outlined,
                color: NordicColors.textSecondary,
              ),
              const SizedBox(width: NordicSpace.sm),
              Expanded(
                child: Text(
                  message,
                  key: const Key('rating-closed-message'),
                  style: NordicTypography.bodyRegular,
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _favoriteToggle() {
    if (widget.role != RatingRole.customer || widget.workerId == null) {
      return const SizedBox.shrink();
    }
    return Center(
      child: Padding(
        padding: const EdgeInsets.only(
          top: NordicSpace.xs,
          bottom: NordicSpace.sm,
        ),
        child: Semantics(
          button: true,
          label: _isFavorite
              ? 'Bỏ yêu thích người làm'
              : 'Thêm người làm vào danh sách yêu thích',
          child: InkWell(
            key: const Key('rating-favorite-toggle'),
            borderRadius: BorderRadius.circular(20),
            onTap: _favoriteLoading ? null : _toggleFavorite,
            child: Container(
              padding: const EdgeInsets.symmetric(
                horizontal: 14.0,
                vertical: 8.0,
              ),
              decoration: BoxDecoration(
                color: _isFavorite
                    ? NordicColors.primaryFixed
                    : NordicColors.surfaceContainer,
                borderRadius: BorderRadius.circular(20),
                border: Border.all(
                  color: _isFavorite
                      ? NordicColors.primary
                      : NordicColors.borderVariant,
                ),
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Icon(
                    _isFavorite
                        ? Icons.favorite_rounded
                        : Icons.favorite_border_rounded,
                    key: const Key('rating-favorite-icon'),
                    size: 20.0,
                    color: _isFavorite
                        ? NordicColors.primary
                        : NordicColors.textSecondary,
                  ),
                  const SizedBox(width: 8.0),
                  Text(
                    _isFavorite ? 'Thợ yêu thích' : 'Lưu vào thợ yêu thích',
                    style: NordicTypography.bodySmall.copyWith(
                      color: _isFavorite
                          ? NordicColors.primary
                          : NordicColors.textSecondary,
                      fontWeight: FontWeight.w600,
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

  Widget _thanks() {
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
          'Cảm ơn bạn đã đánh giá',
          key: Key('rating-thanks'),
          textAlign: TextAlign.center,
          style: NordicTypography.titleMedium,
        ),
        const SizedBox(height: NordicSpace.xs),
        Text(
          widget.role.isInternalOnly
              ? 'Đánh giá của bạn đã được lưu và chỉ dùng nội bộ để giữ chất lượng dịch vụ.'
              : 'Đánh giá đã được lưu và góp vào điểm uy tín của người làm.',
          textAlign: TextAlign.center,
          style: NordicTypography.bodySmall,
        ),
        if (widget.role == RatingRole.customer && widget.workerId != null) ...[
          const SizedBox(height: NordicSpace.lg),
          _favoriteToggle(),
        ],
      ],
    );
  }

  Widget _form() {
    final window = _window!;
    return ListView(
      padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 24.0),
      children: [
        if (widget.ratedName != null) ...[
          Center(child: AvatarInitials(widget.ratedName!, size: 72.0)),
          const SizedBox(height: NordicSpace.xs),
          Center(
            child: Text(
              widget.ratedName!,
              key: const Key('rating-worker-name'),
              style: NordicTypography.titleSmall,
            ),
          ),
          const SizedBox(height: NordicSpace.xs),
        ],
        _favoriteToggle(),
        Row(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            const Icon(
              Icons.timer_outlined,
              size: 18.0,
              color: NordicColors.textSecondary,
            ),
            const SizedBox(width: 6.0),
            Flexible(
              child: Text(
                countdownText(window.secondsRemaining),
                key: const Key('rating-countdown'),
                style: NordicTypography.bodySmall,
              ),
            ),
          ],
        ),
        if (widget.role.isInternalOnly) ...[
          const SizedBox(height: NordicSpace.xxs),
          const Text(
            'Đánh giá này chỉ dùng nội bộ, gia chủ không nhìn thấy.',
            key: Key('rating-internal'),
            textAlign: TextAlign.center,
            style: NordicTypography.bodySmall,
          ),
        ],
        const SizedBox(height: NordicSpace.lg),
        Center(
          child: StarRow(
            keyPrefix: 'stars',
            label: 'Tổng thể',
            value: _stars,
            size: 44.0,
            onChanged: (v) => setState(() => _stars = v),
          ),
        ),
        Center(child: _fieldError(_errors.stars ?? _serverFields['stars'])),
        const SizedBox(height: NordicSpace.lg),
        NordicCard(
          padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 4.0),
          child: Column(
            children: [
              for (var i = 0; i < widget.role.criteria.length; i++) ...[
                if (i > 0) const Divider(height: 1, thickness: 1),
                _criterion(widget.role.criteria[i]),
              ],
              _fieldError(_serverFields['criteria']),
            ],
          ),
        ),
        const SizedBox(height: NordicSpace.md),
        TextField(
          key: const Key('rating-comment'),
          controller: _comment,
          maxLines: 4,
          onChanged: (_) => setState(() {}),
          decoration: const InputDecoration(
            hintText: 'Nhận xét (không bắt buộc)',
          ),
        ),
        const SizedBox(height: NordicSpace.xxs),
        Text(
          '${_comment.text.trim().length}/$maxCommentLength ký tự',
          textAlign: TextAlign.right,
          style: NordicTypography.bodySmall,
        ),
        _fieldError(_errors.comment ?? _serverFields['comment']),
        const SizedBox(height: NordicSpace.sm),
        const Text(
          'Đánh giá được lưu vĩnh viễn, không sửa hay gửi lại được sau khi bấm Gửi.',
          style: NordicTypography.bodySmall,
        ),
        if (_failure != null) ...[
          const SizedBox(height: NordicSpace.sm),
          Text(
            _failure!,
            key: const Key('rating-failure'),
            style: NordicTypography.bodyRegular.copyWith(
              color: NordicColors.error,
            ),
          ),
        ],
      ],
    );
  }

  /// ui-templates `.crit`: the criterion on the left, its stars on the right.
  Widget _criterion(RatingCriterion c) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 8.0),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(child: Text(c.label, style: NordicTypography.titleSmall)),
            StarRow(
              keyPrefix: 'criterion-${c.key}',
              label: c.label,
              value: _criteria[c.key],
              size: 28.0,
              onChanged: (v) => setState(() => _criteria[c.key] = v),
            ),
          ],
        ),
        _fieldError(
          _errors.criteria[c.key] ?? _serverFields['criteria.${c.key}'],
        ),
      ],
    ),
  );

  Widget _fieldError(String? message) {
    if (message == null) return const SizedBox.shrink();
    return Padding(
      padding: const EdgeInsets.only(top: 4.0),
      child: Text(
        message,
        style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
      ),
    );
  }
}

/// Five tappable stars; [onChanged] gets 1-5. Every star has a spoken label ("Tổng thể: 3 sao").
class StarRow extends StatelessWidget {
  final String keyPrefix;
  final String label;
  final int? value;
  final ValueChanged<int> onChanged;
  final double size;

  const StarRow({
    super.key,
    required this.keyPrefix,
    required this.label,
    required this.value,
    required this.onChanged,
    this.size = 32.0,
  });

  @override
  Widget build(BuildContext context) {
    return Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (var i = minScore; i <= maxScore; i++)
          Semantics(
            button: true,
            selected: value != null && i <= value!,
            label: '$label: $i sao',
            child: IconButton(
              key: Key('$keyPrefix-$i'),
              onPressed: () => onChanged(i),
              padding: EdgeInsets.all(size < 30.0 ? 2.0 : 4.0),
              constraints: BoxConstraints(
                minWidth: size + 8.0,
                minHeight: size + 8.0,
              ),
              icon: Icon(
                value != null && i <= value!
                    ? Icons.star_rounded
                    : Icons.star_border_rounded,
                color: value != null && i <= value!
                    ? NordicColors.accentAmber
                    : NordicColors.surfaceDim,
                size: size,
              ),
            ),
          ),
      ],
    );
  }
}
