import 'package:flutter/material.dart';

import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/device_camera_wrapper.dart';
import '../../../core/motion/error_shake.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/stat_tile.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/job_photo_models.dart';
import '../services/workers_assignments_service.dart';
import '../services/workers_photos_service.dart';
import '../widgets/job_photo_image.dart';

/// Screen for Before/After photo capture & VoL verification (contract workers.md §2.4).
/// Redesigned per ticket #671 / approved prototype photos.html:
/// - Hero Focus Card (active angle in center stage)
/// - Compact Angle Checklist (space-efficient overview of progress)
/// - Minimum 5 angles (Option A) with ability to add extra angles (>5) and delete extra ones
/// - Auto-upload upon capture with instant VoL inspection
/// - Auto-advancing flow (Trước -> Sau -> next angle)
/// - Bottom bar action: 'Chụp ảnh' while taking, transitions to 'Nghiệm thu' when 5/5 complete
/// - Once the job is already handed over (AWAITING_ACCEPTANCE / COMPLETED) the capture and submit actions are gone (#677)
class WorkerPhotosScreen extends StatefulWidget {
  final int assignmentId;
  final WorkersPhotosService? photosService;
  final WorkersAssignmentsService? assignmentsService;
  final ICameraWrapper? camera;

  const WorkerPhotosScreen({
    super.key,
    required this.assignmentId,
    this.photosService,
    this.assignmentsService,
    this.camera,
  });

  @override
  State<WorkerPhotosScreen> createState() => _WorkerPhotosScreenState();
}

class _WorkerPhotosScreenState extends State<WorkerPhotosScreen> {
  late final WorkersPhotosService _service;
  late final WorkersAssignmentsService _assignmentsService;
  late final ICameraWrapper _camera;

  String _selectedPhase = 'BEFORE'; // BEFORE | AFTER
  int _selectedAngle = 1;

  CapturedImage? _picked;
  String? _pickedType;

  bool _isLoading = false;
  bool _isSubmitting = false;

  /// The job was already handed to the customer: nothing can be captured or submitted any more.
  bool _submitted = false;
  String? _errorMessage;
  String? _successMessage;
  int _errors = 0;

  final List<JobPhotoDto> _uploadedPhotos = [];

  final List<Map<String, dynamic>> _angleGuides = [
    {
      'angle': 1,
      'title': 'Góc 1: Toàn cảnh',
      'name': 'Toàn cảnh',
      'desc': 'Chụp tổng quan toàn bộ căn phòng',
    },
    {
      'angle': 2,
      'title': 'Góc 2: Bàn ghế & tủ',
      'name': 'Bàn ghế & tủ',
      'desc': 'Chụp cận cảnh mặt bàn và tủ kệ',
    },
    {
      'angle': 3,
      'title': 'Góc 3: Cửa kính',
      'name': 'Cửa kính',
      'desc': 'Chụp mặt kính cửa sổ và cửa ra vào',
    },
    {
      'angle': 4,
      'title': 'Góc 4: Sàn nhà',
      'name': 'Sàn nhà',
      'desc': 'Chụp mặt sàn phòng khách và góc thảm',
    },
    {
      'angle': 5,
      'title': 'Góc 5: Khu vực bếp',
      'name': 'Khu vực bếp',
      'desc': 'Chụp bồn rửa và mặt bếp thi công',
    },
  ];

  @override
  void initState() {
    super.initState();
    _service = widget.photosService ?? WorkersPhotosService();
    _assignmentsService =
        widget.assignmentsService ?? WorkersAssignmentsService();
    _camera = widget.camera ?? DeviceCameraWrapper();
    _loadPhotos();
    _loadStatus();
  }

  /// Reads whether the job was already submitted. Returns false when the status cannot be read, so a flaky network never
  /// blocks the worker (the server stays the final judge of the submit).
  Future<bool> _loadStatus() async {
    try {
      final response = await _assignmentsService.getAssignment(
        widget.assignmentId,
      );
      final submitted = response.data?.isSubmittedForAcceptance ?? false;
      if (submitted && mounted) setState(() => _submitted = true);
      return submitted;
    } catch (_) {
      return false;
    }
  }

  Future<void> _loadPhotos() async {
    try {
      final response = await _service.getPhotos(widget.assignmentId);
      if (response.data != null && mounted) {
        setState(() {
          _uploadedPhotos.clear();
          _uploadedPhotos.addAll(response.data!);
          _updateAutoTarget();
        });
      }
    } catch (_) {
      // Ignore network errors on initial test load
    }
  }

  void _fail(String message) {
    if (!mounted) return;
    setState(() {
      _isLoading = false;
      _errorMessage = message;
      _errors++;
      _successMessage = null;
    });
  }

  int _acceptedCount(String phase) => _uploadedPhotos
      .where((p) => p.photoPhase == phase && p.isAccepted)
      .map((p) => p.angleNo)
      .toSet()
      .length;

  bool _hasAccepted(String phase, int angle) => _uploadedPhotos.any(
    (p) => p.photoPhase == phase && p.angleNo == angle && p.isAccepted,
  );

  JobPhotoDto? _photoFor(String phase, int angle) {
    final matches = _uploadedPhotos.where(
      (p) => p.photoPhase == phase && p.angleNo == angle && p.isAccepted,
    );
    return matches.isNotEmpty ? matches.last : null;
  }

  bool get _isAllAccepted {
    final total = _angleGuides.length;
    if (total < 5) return false;
    return _acceptedCount('BEFORE') >= total &&
        _acceptedCount('AFTER') >= total;
  }

  void _updateAutoTarget() {
    // Find next missing angle
    final missingBefore = _angleGuides.firstWhere(
      (g) => !_hasAccepted('BEFORE', g['angle'] as int),
      orElse: () => {},
    );
    if (missingBefore.isNotEmpty) {
      _selectedAngle = missingBefore['angle'] as int;
      _selectedPhase = 'BEFORE';
      return;
    }

    final missingAfter = _angleGuides.firstWhere(
      (g) => !_hasAccepted('AFTER', g['angle'] as int),
      orElse: () => {},
    );
    if (missingAfter.isNotEmpty) {
      _selectedAngle = missingAfter['angle'] as int;
      _selectedPhase = 'AFTER';
    }
  }

  void _autoAdvanceAfterSuccess(int completedAngle, String completedPhase) {
    if (completedPhase == 'BEFORE') {
      // Auto-advance to AFTER of the exact same angle
      if (!_hasAccepted('AFTER', completedAngle)) {
        _selectedAngle = completedAngle;
        _selectedPhase = 'AFTER';
        return;
      }
    }

    // Otherwise find the next angle missing BEFORE, or next missing AFTER
    for (final g in _angleGuides) {
      final a = g['angle'] as int;
      if (!_hasAccepted('BEFORE', a)) {
        _selectedAngle = a;
        _selectedPhase = 'BEFORE';
        return;
      }
    }
    for (final g in _angleGuides) {
      final a = g['angle'] as int;
      if (!_hasAccepted('AFTER', a)) {
        _selectedAngle = a;
        _selectedPhase = 'AFTER';
        return;
      }
    }
  }

  String _uploadError(ApiException e) {
    final fields = e.fieldErrors;
    if (fields != null) {
      final blur = fields['volScore'];
      if (blur != null && blur.isNotEmpty) return blur.first;
      if (fields.containsKey('contentType')) {
        return 'Chỉ nhận ảnh JPEG, PNG hoặc WebP.';
      }
      if (fields.containsKey('file')) {
        return 'Ảnh không hợp lệ hoặc lớn hơn 5 MB. Vui lòng chụp lại.';
      }
    }
    return e.message;
  }

  Future<void> _pick({required bool fromCamera}) async {
    final CapturedImage? photo;
    try {
      photo = fromCamera
          ? await _camera.capturePhoto()
          : await _camera.pickFromGallery();
    } catch (_) {
      _fail(
        'Không mở được camera hoặc thư viện ảnh. Hãy cấp quyền cho ứng dụng trong Cài đặt rồi thử lại.',
      );
      return;
    }
    if (photo == null || !mounted) return;

    final bytes = photo.bytes;
    if (bytes == null || bytes.isEmpty) {
      _fail('Không đọc được ảnh vừa chọn. Vui lòng chụp lại.');
      return;
    }
    final type = imageContentTypeOf(bytes);
    if (type == null) {
      _fail('Chỉ nhận ảnh JPEG, PNG hoặc WebP.');
      return;
    }
    if (bytes.length > UploadJobPhotoRequest.maxBytes) {
      _fail('Ảnh lớn hơn 5 MB. Vui lòng chụp lại hoặc chọn ảnh nhỏ hơn.');
      return;
    }

    setState(() {
      _picked = photo;
      _pickedType = type;
      _errorMessage = null;
      _successMessage = null;
    });

    // Auto-upload immediately upon capture / selection
    await _uploadPhoto();
  }

  Future<void> _uploadPhoto() async {
    final picked = _picked;
    final bytes = picked?.bytes;
    final type = _pickedType;
    if (picked == null || bytes == null || type == null) {
      _fail('Bạn chưa chọn ảnh. Hãy chụp hoặc chọn ảnh trước khi gửi.');
      return;
    }

    if (_selectedPhase == 'AFTER') {
      final hasBeforeAngle = _hasAccepted('BEFORE', _selectedAngle);
      if (!hasBeforeAngle && _uploadedPhotos.isNotEmpty) {
        _fail(
          'Góc $_selectedAngle chưa có ảnh trước được chấp nhận. Hãy chọn đúng góc.',
        );
        return;
      }
    }

    setState(() {
      _isLoading = true;
      _errorMessage = null;
      _successMessage = null;
    });

    final targetAngle = _selectedAngle;
    final targetPhase = _selectedPhase;

    try {
      final response = await _service.uploadPhoto(
        assignmentId: widget.assignmentId,
        request: UploadJobPhotoRequest(
          photoPhase: targetPhase,
          angleNo: targetAngle,
          bytes: bytes,
          contentType: type,
        ),
      );
      final saved = response.data;
      if (saved == null) {
        _fail('Máy chủ không trả kết quả của ảnh. Vui lòng thử lại.');
        return;
      }
      if (!mounted) return;
      setState(() {
        _isLoading = false;
        _uploadedPhotos.removeWhere(
          (p) => p.photoPhase == saved.photoPhase && p.angleNo == saved.angleNo,
        );
        _uploadedPhotos.add(saved);
        _picked = null;
        _pickedType = null;
        _successMessage =
            'Đã tải ảnh lên. Độ nét: ${saved.volScore.toStringAsFixed(1)} (đạt)';
        _autoAdvanceAfterSuccess(targetAngle, targetPhase);
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      final blurry = e.fieldErrors?.containsKey('volScore') ?? false;
      setState(() {
        if (blurry) {
          _picked = null;
          _pickedType = null;
        }
      });
      _fail(_uploadError(e));
    } catch (_) {
      if (!mounted) return;
      _fail('Không tải được ảnh lên. Vui lòng thử lại.');
    }
  }

  Future<void> _submitCompletion() async {
    if (!_isAllAccepted || _submitted) return;
    setState(() => _isSubmitting = true);
    final navigator = Navigator.of(context);
    final messenger = ScaffoldMessenger.of(context);
    try {
      await _service.submitCompletion(widget.assignmentId);
      if (!mounted) return;
      setState(() {
        _isSubmitting = false;
        _submitted = true;
      });
      messenger.showSnackBar(
        const SnackBar(
          content: Text('Đã gửi nghiệm thu. Chờ gia chủ xác nhận.'),
        ),
      );
      navigator.popUntil((route) => route.isFirst);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _isSubmitting = false);
      // The job may have been submitted already (a double tap, another device): say so instead of the server's English text.
      if (await _loadStatus()) return;
      _fail('Chưa gửi được nghiệm thu: ${e.message}');
    } catch (_) {
      if (!mounted) return;
      setState(() => _isSubmitting = false);
      _fail('Chưa gửi được nghiệm thu. Vui lòng thử lại.');
    }
  }

  void _addAngle(String name) {
    if (name.trim().isEmpty) return;
    setState(() {
      final nextAngle = _angleGuides.length + 1;
      _angleGuides.add({
        'angle': nextAngle,
        'title': 'Góc $nextAngle: ${name.trim()}',
        'name': name.trim(),
        'desc': 'Chụp kiểm tra khu vực ${name.trim()}',
      });
      _selectedAngle = nextAngle;
      _selectedPhase = 'BEFORE';
    });
  }

  void _deleteAngle(int angleNo) {
    if (_angleGuides.length <= 5) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Phương án A: Tối thiểu 5 góc chụp!')),
      );
      return;
    }
    setState(() {
      _angleGuides.removeWhere((g) => g['angle'] == angleNo);
      _uploadedPhotos.removeWhere((p) => p.angleNo == angleNo);
      for (int i = 0; i < _angleGuides.length; i++) {
        final newNo = i + 1;
        _angleGuides[i]['angle'] = newNo;
        final name = _angleGuides[i]['name'] as String;
        _angleGuides[i]['title'] = 'Góc $newNo: $name';
      }
      if (_selectedAngle > _angleGuides.length) {
        _selectedAngle = _angleGuides.length;
      }
    });
  }

  void _showAddAngleDialog() {
    final textController = TextEditingController();
    showDialog(
      context: context,
      builder: (ctx) {
        return AlertDialog(
          shape: RoundedRectangleBorder(borderRadius: NordicRadius.cardAll),
          title: const Text(
            'Thêm góc chụp',
            style: NordicTypography.titleSmall,
          ),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Wrap(
                spacing: NordicSpace.xs,
                runSpacing: NordicSpace.xs,
                children: [
                  for (final preset in [
                    'Ban công',
                    'Phòng WC',
                    'Phòng ngủ',
                    'Trần & đèn',
                  ])
                    ActionChip(
                      label: Text(preset, style: NordicTypography.labelSmall),
                      onPressed: () {
                        Navigator.of(ctx).pop();
                        _addAngle(preset);
                      },
                    ),
                ],
              ),
              const SizedBox(height: NordicSpace.md),
              TextField(
                key: const Key('add-angle-name-input'),
                controller: textController,
                decoration: const InputDecoration(
                  hintText: 'Nhập tên góc khác...',
                  border: OutlineInputBorder(),
                  isDense: true,
                ),
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.of(ctx).pop(),
              child: const Text('Hủy'),
            ),
            NordicButton(
              label: 'Thêm',
              variant: NordicButtonVariant.primary,
              onPressed: () {
                if (textController.text.trim().isNotEmpty) {
                  Navigator.of(ctx).pop();
                  _addAngle(textController.text.trim());
                }
              },
            ),
          ],
        );
      },
    );
  }

  String _angleName(int angle) {
    final guide = _angleGuides.firstWhere(
      (g) => g['angle'] == angle,
      orElse: () => _angleGuides.first,
    );
    if (guide.containsKey('name')) return guide['name'] as String;
    final title = guide['title'] as String;
    final i = title.indexOf(': ');
    return i < 0 ? title : title.substring(i + 2);
  }

  @override
  Widget build(BuildContext context) {
    final currentGuide = _angleGuides.firstWhere(
      (g) => g['angle'] == _selectedAngle,
      orElse: () => _angleGuides.first,
    );

    return NordicPage.pushed(
      title: 'Ảnh trước và sau',
      // Primary bottom action button (Chụp ảnh while incomplete, Nghiệm thu when complete); none once handed over
      actions: _submitted
          ? null
          : [
              if (_isAllAccepted)
                NordicButton(
                  key: const Key('photo-submit-button'),
                  label: _isSubmitting ? 'Đang gửi…' : 'Nghiệm thu',
                  variant: NordicButtonVariant.primary,
                  icon: const Icon(Icons.task_alt, size: 18.0),
                  isLoading: _isSubmitting,
                  onPressed: _submitCompletion,
                )
              else
                NordicButton(
                  key: const Key('photo-capture-button'),
                  label: 'Chụp ảnh',
                  variant: NordicButtonVariant.primary,
                  icon: const Icon(Icons.photo_camera_outlined, size: 18.0),
                  isLoading: _isLoading,
                  onPressed: () => _pick(fromCamera: true),
                ),
            ],
      children: [
        if (_submitted)
          _message(
            'Đang chờ gia chủ xác nhận. Ca này đã được gửi nghiệm thu.',
            icon: Icons.hourglass_top_rounded,
            background: NordicColors.primaryFixed,
            foreground: NordicColors.primary,
          ),
        // KPI statistics
        Row(
          children: [
            Expanded(
              child: StatTile(
                label: 'Trước',
                value: '${_acceptedCount('BEFORE')}',
                unit: '/${_angleGuides.length}',
              ),
            ),
            const SizedBox(width: NordicSpace.sm),
            Expanded(
              child: StatTile(
                label: 'Sau',
                value: '${_acceptedCount('AFTER')}',
                unit: '/${_angleGuides.length}',
              ),
            ),
          ],
        ),
        const SizedBox(height: NordicSpace.md),

        // 1. HERO FOCUS CARD (Active angle in center stage)
        _buildHeroFocusCard(currentGuide),
        const SizedBox(height: NordicSpace.md),

        // Messages
        ErrorShake(
          errors: _errors,
          child: _errorMessage == null
              ? const SizedBox.shrink()
              : _message(
                  _errorMessage!,
                  icon: Icons.warning_amber_rounded,
                  background: NordicColors.errorContainer,
                  foreground: NordicColors.error,
                ),
        ),
        if (_successMessage != null)
          _message(
            _successMessage!,
            icon: Icons.check_circle_outline,
            background: NordicColors.primaryFixed,
            foreground: NordicColors.primary,
          ),

        // 2. COMPACT ANGLE CHECKLIST
        _buildCompactAngleChecklist(),
        const SizedBox(height: NordicSpace.md),

        // Summary of uploaded count for tests
        Text(
          '${_uploadedPhotos.where((p) => p.photoPhase == _selectedPhase && p.isAccepted).length}/${_angleGuides.length} đạt',
          key: const Key('uploaded-photos-count-text'),
          style: NordicTypography.bodySmall.copyWith(
            color: NordicColors.textSecondary,
          ),
        ),
      ],
    );
  }

  Widget _buildHeroFocusCard(Map<String, dynamic> guide) {
    final activePhoto = _photoFor(_selectedPhase, _selectedAngle);

    return NordicCard(
      isHighlighted: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Icon(
                Icons.center_focus_strong,
                size: 20.0,
                color: NordicColors.primary,
              ),
              const SizedBox(width: NordicSpace.xs),
              Expanded(
                child: Text(
                  guide['title'] as String,
                  style: NordicTypography.titleSmall,
                  overflow: TextOverflow.ellipsis,
                ),
              ),
              const SizedBox(width: NordicSpace.xs),
              StatusPill(
                _selectedPhase == 'BEFORE' ? 'Trước' : 'Sau',
                tone: PillTone.ok,
              ),
            ],
          ),
          const SizedBox(height: NordicSpace.sm),

          // Preview / Viewfinder
          Pressable(
            child: InkWell(
              onTap: _isLoading || _submitted
                  ? null
                  : () => _pick(fromCamera: true),
              borderRadius: NordicRadius.tileAll,
              child: Container(
                width: double.infinity,
                height: 140.0,
                decoration: BoxDecoration(
                  color: NordicColors.surfaceContainer,
                  borderRadius: NordicRadius.tileAll,
                  border: Border.all(
                    color: activePhoto != null
                        ? NordicColors.primary
                        : NordicColors.borderVariant,
                    width: activePhoto != null ? 2.0 : 1.5,
                  ),
                ),
                child: _isLoading
                    ? Center(
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: const [
                            CircularProgressIndicator(),
                            SizedBox(height: NordicSpace.xs),
                            Text(
                              'Đang đo nét VoL...',
                              style: NordicTypography.bodySmall,
                            ),
                          ],
                        ),
                      )
                    : activePhoto != null
                    ? Stack(
                        fit: StackFit.expand,
                        children: [
                          ClipRRect(
                            borderRadius: NordicRadius.tileAll,
                            child: JobPhotoImage(
                              service: _service,
                              photo: activePhoto,
                              height: 140.0,
                              width: double.infinity,
                            ),
                          ),
                          Positioned(
                            bottom: 6.0,
                            left: 6.0,
                            right: 6.0,
                            child: Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 8.0,
                                vertical: 4.0,
                              ),
                              decoration: BoxDecoration(
                                color: Colors.black.withValues(alpha: 0.75),
                                borderRadius: BorderRadius.circular(6.0),
                              ),
                              child: Row(
                                mainAxisAlignment:
                                    MainAxisAlignment.spaceBetween,
                                children: [
                                  Text(
                                    '✓ ${_selectedPhase == "BEFORE" ? "Trước" : "Sau"} đạt',
                                    style: const TextStyle(
                                      color: Colors.white,
                                      fontSize: 11.0,
                                      fontWeight: FontWeight.w600,
                                    ),
                                  ),
                                  Text(
                                    'Độ nét: ${activePhoto.volScore.toStringAsFixed(1)}',
                                    style: const TextStyle(
                                      color: Colors.white,
                                      fontSize: 11.0,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ),
                        ],
                      )
                    : Center(
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            const Icon(
                              Icons.photo_camera_outlined,
                              size: 36.0,
                              color: NordicColors.primary,
                            ),
                            const SizedBox(height: NordicSpace.xxs),
                            Text(
                              'Chụp ảnh ${_selectedPhase == "BEFORE" ? "Trước" : "Sau"}',
                              style: NordicTypography.titleSmall,
                            ),
                            const SizedBox(height: 2.0),
                            const Text(
                              'Chạm vào đây để mở máy ảnh',
                              style: NordicTypography.bodySmall,
                            ),
                          ],
                        ),
                      ),
              ),
            ),
          ),
          const SizedBox(height: NordicSpace.xs),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              TextButton.icon(
                onPressed: _isLoading || _submitted
                    ? null
                    : () => _pick(fromCamera: false),
                icon: const Icon(
                  Icons.photo_library_outlined,
                  size: 16.0,
                  color: NordicColors.textSecondary,
                ),
                label: const Text('Chọn từ thư viện'),
                style: TextButton.styleFrom(
                  visualDensity: VisualDensity.compact,
                  padding: const EdgeInsets.symmetric(horizontal: 8.0),
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _buildCompactAngleChecklist() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: Text(
                'TIẾN ĐỘ CÁC GÓC (${_angleGuides.length})',
                style: NordicTypography.labelMedium.copyWith(
                  color: NordicColors.textSecondary,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
            TextButton.icon(
              onPressed: _submitted ? null : _showAddAngleDialog,
              icon: const Icon(Icons.add_circle_outline, size: 16.0),
              label: const Text('Thêm góc'),
              style: TextButton.styleFrom(
                padding: const EdgeInsets.symmetric(horizontal: 8.0),
                minimumSize: const Size(0, 32),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
            ),
          ],
        ),
        const SizedBox(height: NordicSpace.xxs),

        for (final guide in _angleGuides) ...[
          _buildCompactAngleRow(guide['angle'] as int),
          const SizedBox(height: NordicSpace.xxs),
        ],
      ],
    );
  }

  Widget _buildCompactAngleRow(int angle) {
    final isSelected = _selectedAngle == angle;
    final beforePhoto = _photoFor('BEFORE', angle);
    final afterPhoto = _photoFor('AFTER', angle);
    final hasBefore = beforePhoto != null;
    final hasAfter = afterPhoto != null;

    return Pressable(
      child: InkWell(
        key: Key('photo-angle-$angle'),
        borderRadius: NordicRadius.tileAll,
        onTap: () {
          setState(() {
            _selectedAngle = angle;
            if (!hasBefore) {
              _selectedPhase = 'BEFORE';
            } else if (!hasAfter) {
              _selectedPhase = 'AFTER';
            }
          });
        },
        child: Container(
          padding: const EdgeInsets.symmetric(
            horizontal: NordicSpace.sm,
            vertical: NordicSpace.xs,
          ),
          decoration: BoxDecoration(
            color: isSelected
                ? NordicColors.primaryFixed.withValues(alpha: 0.3)
                : NordicColors.surface,
            borderRadius: NordicRadius.tileAll,
            border: Border.all(
              color: isSelected ? NordicColors.primary : NordicColors.border,
              width: isSelected ? 1.5 : 1.0,
            ),
          ),
          child: Row(
            children: [
              Container(
                width: 22.0,
                height: 22.0,
                decoration: BoxDecoration(
                  color: isSelected
                      ? NordicColors.primary
                      : NordicColors.surfaceContainer,
                  shape: BoxShape.circle,
                ),
                alignment: Alignment.center,
                child: Text(
                  '$angle',
                  style: TextStyle(
                    fontSize: 11.0,
                    fontWeight: FontWeight.bold,
                    color: isSelected ? Colors.white : NordicColors.textTitle,
                  ),
                ),
              ),
              const SizedBox(width: NordicSpace.xs),
              Expanded(
                child: Text(
                  _angleName(angle),
                  style: NordicTypography.titleSmall.copyWith(fontSize: 13.5),
                  overflow: TextOverflow.ellipsis,
                ),
              ),

              // Mini pill Trước
              InkWell(
                key: Key('photo-cell-BEFORE-$angle'),
                onTap: () => setState(() {
                  _selectedAngle = angle;
                  _selectedPhase = 'BEFORE';
                }),
                child: Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 6.0,
                    vertical: 2.0,
                  ),
                  decoration: BoxDecoration(
                    color: hasBefore
                        ? NordicColors.primaryFixed
                        : isSelected && _selectedPhase == 'BEFORE'
                        ? NordicColors.primary
                        : NordicColors.surfaceContainer,
                    borderRadius: BorderRadius.circular(4.0),
                  ),
                  child: Text(
                    hasBefore
                        ? '✓ ${beforePhoto.volScore.toStringAsFixed(0)}'
                        : isSelected && _selectedPhase == 'BEFORE'
                        ? 'Đang chụp'
                        : 'Trước',
                    style: TextStyle(
                      fontSize: 10.5,
                      fontWeight: FontWeight.bold,
                      color: hasBefore
                          ? NordicColors.primary
                          : isSelected && _selectedPhase == 'BEFORE'
                          ? Colors.white
                          : NordicColors.textSecondary,
                    ),
                  ),
                ),
              ),
              const SizedBox(width: NordicSpace.xxs),

              // Mini pill Sau
              InkWell(
                key: Key('photo-cell-AFTER-$angle'),
                onTap: () => setState(() {
                  _selectedAngle = angle;
                  _selectedPhase = 'AFTER';
                }),
                child: Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 6.0,
                    vertical: 2.0,
                  ),
                  decoration: BoxDecoration(
                    color: hasAfter
                        ? NordicColors.primaryFixed
                        : isSelected && _selectedPhase == 'AFTER'
                        ? NordicColors.primary
                        : NordicColors.surfaceContainer,
                    borderRadius: BorderRadius.circular(4.0),
                  ),
                  child: Text(
                    hasAfter
                        ? '✓ ${afterPhoto.volScore.toStringAsFixed(0)}'
                        : isSelected && _selectedPhase == 'AFTER'
                        ? 'Đang chụp'
                        : hasBefore
                        ? 'Sau'
                        : '🔒',
                    style: TextStyle(
                      fontSize: 10.5,
                      fontWeight: FontWeight.bold,
                      color: hasAfter
                          ? NordicColors.primary
                          : isSelected && _selectedPhase == 'AFTER'
                          ? Colors.white
                          : NordicColors.textSecondary,
                    ),
                  ),
                ),
              ),

              // Option A: Delete button only when > 5 angles
              if (_angleGuides.length > 5) ...[
                const SizedBox(width: NordicSpace.xxs),
                IconButton(
                  key: Key('delete-angle-$angle'),
                  icon: const Icon(
                    Icons.delete_outline,
                    size: 16.0,
                    color: NordicColors.error,
                  ),
                  onPressed: _submitted ? null : () => _deleteAngle(angle),
                  visualDensity: VisualDensity.compact,
                  padding: EdgeInsets.zero,
                  constraints: const BoxConstraints(),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _message(
    String text, {
    required IconData icon,
    required Color background,
    required Color foreground,
  }) {
    return Padding(
      padding: const EdgeInsets.only(bottom: NordicSpace.sm),
      child: Container(
        padding: const EdgeInsets.all(NordicSpace.sm),
        decoration: BoxDecoration(
          color: background,
          borderRadius: NordicRadius.fieldAll,
        ),
        child: Row(
          children: [
            Icon(icon, color: foreground, size: 20.0),
            const SizedBox(width: NordicSpace.xs),
            Expanded(
              child: Text(
                text,
                style: NordicTypography.bodySmall.copyWith(
                  color: foreground,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
