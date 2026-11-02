import 'package:flutter/material.dart';

import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/device_camera_wrapper.dart';
import '../../../core/network/api_client.dart';
import '../../../core/motion/error_shake.dart';
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
import '../services/workers_photos_service.dart';
import '../widgets/job_photo_image.dart';

/// Screen for Before/After photo capture & VoL verification (contract workers.md §2.4).
/// Implements MOB-M4-04 / MOB-PHOTO-01: angle guides, a real photo from the camera or the library, uploaded as a file;
/// the server measures its sharpness (threshold >= 100.0 per Q03) and the worker is told to retake a blurry one.
class WorkerPhotosScreen extends StatefulWidget {
  final int assignmentId;
  final WorkersPhotosService? photosService;

  /// The real camera and library by default; a test passes a fake.
  final ICameraWrapper? camera;

  const WorkerPhotosScreen({
    super.key,
    required this.assignmentId,
    this.photosService,
    this.camera,
  });

  @override
  State<WorkerPhotosScreen> createState() => _WorkerPhotosScreenState();
}

class _WorkerPhotosScreenState extends State<WorkerPhotosScreen> {
  late final WorkersPhotosService _service;
  late final ICameraWrapper _camera;

  String _selectedPhase = 'BEFORE'; // BEFORE | AFTER
  int _selectedAngle = 1;

  /// The photo taken or picked, not sent yet, and its content type (from its first bytes).
  CapturedImage? _picked;
  String? _pickedType;

  bool _isLoading = false;
  String? _errorMessage;
  String? _successMessage;

  /// Bumped on every failure so the message shakes.
  int _errors = 0;

  final List<JobPhotoDto> _uploadedPhotos = [];

  static const List<Map<String, dynamic>> _angleGuides = [
    {
      'angle': 1,
      'title': 'Góc 1: Toàn cảnh',
      'desc': 'Chụp tổng quan toàn bộ căn phòng',
    },
    {
      'angle': 2,
      'title': 'Góc 2: Bàn ghế & tủ',
      'desc': 'Chụp cận cảnh mặt bàn và tủ kệ',
    },
    {
      'angle': 3,
      'title': 'Góc 3: Cửa kính',
      'desc': 'Chụp mặt kính cửa sổ và cửa ra vào',
    },
    {
      'angle': 4,
      'title': 'Góc 4: Sàn nhà',
      'desc': 'Chụp mặt sàn phòng khách và góc thảm',
    },
    {
      'angle': 5,
      'title': 'Góc 5: Khu vực bếp',
      'desc': 'Chụp bồn rửa và mặt bếp thi công',
    },
  ];

  @override
  void initState() {
    super.initState();
    _service = widget.photosService ?? WorkersPhotosService();
    _camera = widget.camera ?? DeviceCameraWrapper();
    _loadPhotos();
  }

  Future<void> _loadPhotos() async {
    try {
      final response = await _service.getPhotos(widget.assignmentId);
      if (response.data != null) {
        setState(() {
          _uploadedPhotos.clear();
          _uploadedPhotos.addAll(response.data!);
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
    if (photo == null || !mounted) return; // the worker cancelled

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
  }

  /// The words a failed upload is shown in, by the field the server named (contract workers.md §2.4.1).
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

  Future<void> _uploadPhoto() async {
    final picked = _picked;
    final bytes = picked?.bytes;
    final type = _pickedType;
    if (picked == null || bytes == null || type == null) {
      _fail('Bạn chưa chọn ảnh. Hãy chụp hoặc chọn ảnh trước khi gửi.');
      return;
    }

    // Angle matching check for AFTER phase
    if (_selectedPhase == 'AFTER') {
      final hasBeforeAngle = _uploadedPhotos.any(
        (p) =>
            p.photoPhase == 'BEFORE' &&
            p.angleNo == _selectedAngle &&
            p.isAccepted,
      );
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

    try {
      final response = await _service.uploadPhoto(
        assignmentId: widget.assignmentId,
        request: UploadJobPhotoRequest(
          photoPhase: _selectedPhase,
          angleNo: _selectedAngle,
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
        _uploadedPhotos.add(saved);
        _picked = null;
        _pickedType = null;
        _successMessage =
            'Đã tải ảnh lên. Độ nét: ${saved.volScore.toStringAsFixed(1)} (đạt)';
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      final blurry = e.fieldErrors?.containsKey('volScore') ?? false;
      setState(() {
        if (blurry) {
          _picked =
              null; // a blurry photo is not sent again: the worker retakes it
          _pickedType = null;
        }
      });
      _fail(_uploadError(e));
    } catch (_) {
      if (!mounted) return;
      _fail('Không tải được ảnh lên. Vui lòng thử lại.');
    }
  }

  List<JobPhotoDto> get _currentPhasePhotos => _uploadedPhotos
      .where((p) => p.photoPhase == _selectedPhase && p.isAccepted)
      .toList();

  @override
  Widget build(BuildContext context) {
    final currentGuide = _angleGuides.firstWhere(
      (g) => g['angle'] == _selectedAngle,
      orElse: () => _angleGuides.first,
    );

    return NordicPage.pushed(
      title: 'Ảnh trước và sau',
      actions: [
        NordicButton(
          label: 'Gửi ảnh',
          variant: NordicButtonVariant.primary,
          isLoading: _isLoading,
          onPressed: _uploadPhoto,
        ),
      ],
      children: [
        const Text(
          'Chụp 3–5 góc. Ảnh sau phải đúng góc với ảnh trước và có độ nét ≥ 100.',
          style: NordicTypography.bodySmall,
        ),
        const SizedBox(height: NordicSpace.md),

        // Count per phase
        Row(
          children: [
            Expanded(
              child: StatTile(
                label: 'Trước',
                value: '${_acceptedCount('BEFORE')}',
                unit: '/5',
              ),
            ),
            const SizedBox(width: NordicSpace.sm),
            Expanded(
              child: StatTile(
                label: 'Sau',
                value: '${_acceptedCount('AFTER')}',
                unit: '/5',
              ),
            ),
          ],
        ),
        const SizedBox(height: NordicSpace.md),

        // Phase Switcher
        _buildPhaseSegmentedControl(),
        const SizedBox(height: NordicSpace.md),

        // Angle x phase grid: tap a cell to pick that photo
        _buildAngleSelector(),
        const SizedBox(height: NordicSpace.md),

        // Frame guide of the chosen angle
        _buildCameraGuideOverlay(currentGuide),
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

        // The photo about to be sent
        if (_picked?.bytes != null) ...[
          ClipRRect(
            borderRadius: NordicRadius.tileAll,
            child: Image.memory(
              _picked!.bytes!,
              height: 180.0,
              width: double.infinity,
              fit: BoxFit.cover,
              errorBuilder: (_, _, _) => const SizedBox(
                height: 180.0,
                child: Center(child: Text('Không xem trước được ảnh')),
              ),
            ),
          ),
          const SizedBox(height: NordicSpace.sm),
        ],

        // Photo input
        Row(
          children: [
            Expanded(
              child: NordicButton(
                label: 'Chụp ảnh',
                variant: NordicButtonVariant.secondary,
                onPressed: _isLoading ? null : () => _pick(fromCamera: true),
              ),
            ),
            const SizedBox(width: NordicSpace.xs),
            Expanded(
              child: NordicButton(
                label: 'Chọn từ thư viện',
                variant: NordicButtonVariant.secondary,
                onPressed: _isLoading ? null : () => _pick(fromCamera: false),
              ),
            ),
          ],
        ),

        const SizedBox(height: NordicSpace.gutter),
        // List of Uploaded Photos
        _buildUploadedPhotosList(),
      ],
    );
  }

  /// A one-line result under the guide: an error (red) or a success (green), always with an icon and words.
  Widget _message(
    String text, {
    required IconData icon,
    required Color background,
    required Color foreground,
  }) {
    return Padding(
      padding: const EdgeInsets.only(bottom: NordicSpace.md),
      child: Container(
        padding: const EdgeInsets.all(NordicSpace.sm),
        decoration: BoxDecoration(
          color: background,
          borderRadius: NordicRadius.fieldAll,
        ),
        child: Row(
          children: [
            Icon(icon, color: foreground),
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

  int _acceptedCount(String phase) => _uploadedPhotos
      .where((p) => p.photoPhase == phase && p.isAccepted)
      .map((p) => p.angleNo)
      .toSet()
      .length;

  bool _hasAccepted(String phase, int angle) => _uploadedPhotos.any(
    (p) => p.photoPhase == phase && p.angleNo == angle && p.isAccepted,
  );

  /// `.seg`: Trước / Sau in one rounded track, the chosen one filled.
  Widget _buildPhaseSegmentedControl() {
    Widget segment(String phase, String label) {
      final selected = _selectedPhase == phase;
      return Expanded(
        child: Pressable(
          child: GestureDetector(
            onTap: () => setState(() => _selectedPhase = phase),
            child: Container(
              padding: const EdgeInsets.symmetric(vertical: NordicSpace.sm),
              decoration: BoxDecoration(
                color: selected
                    ? NordicColors.primary
                    : NordicColors.surfaceContainer,
                borderRadius: NordicRadius.pill,
              ),
              child: Text(
                label,
                textAlign: TextAlign.center,
                style: NordicTypography.titleSmall.copyWith(
                  fontWeight: FontWeight.w600,
                  color: selected
                      ? NordicColors.textInverse
                      : NordicColors.textTitle,
                ),
              ),
            ),
          ),
        ),
      );
    }

    return Container(
      padding: const EdgeInsets.all(NordicSpace.xxs),
      decoration: BoxDecoration(
        color: NordicColors.surfaceContainer,
        borderRadius: NordicRadius.pill,
      ),
      child: Row(
        children: [segment('BEFORE', 'Trước'), segment('AFTER', 'Sau')],
      ),
    );
  }

  /// `.angle` rows: one per angle with a Trước and a Sau cell; a check when the server accepted the photo, a camera
  /// icon when it is still missing. Tapping a cell chooses that phase and angle.
  Widget _buildAngleSelector() {
    Widget cell(String phase, int angle) {
      final done = _hasAccepted(phase, angle);
      final selected = _selectedPhase == phase && _selectedAngle == angle;
      return Pressable(
        child: InkWell(
          key: Key('photo-cell-$phase-$angle'),
          borderRadius: NordicRadius.tileAll,
          onTap: () => setState(() {
            _selectedPhase = phase;
            _selectedAngle = angle;
          }),
          child: Container(
            width: 64.0,
            height: 48.0,
            decoration: BoxDecoration(
              color: done ? NordicColors.primaryFixed : null,
              borderRadius: NordicRadius.tileAll,
              border: done
                  ? null
                  : Border.all(
                      color: selected
                          ? NordicColors.primary
                          : NordicColors.borderVariant,
                      width: selected ? 2.0 : 1.5,
                    ),
            ),
            child: Icon(
              done ? Icons.check : Icons.add_a_photo_outlined,
              color: done ? NordicColors.primary : NordicColors.textSecondary,
            ),
          ),
        ),
      );
    }

    return NordicCard(
      child: Column(
        children: [
          for (int i = 1; i <= 5; i++) ...[
            if (i > 1) const Divider(height: NordicSpace.md),
            Row(
              children: [
                Expanded(
                  child: InkWell(
                    key: Key('photo-angle-$i'),
                    onTap: () => setState(() => _selectedAngle = i),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Góc $i',
                          style: _selectedAngle == i
                              ? NordicTypography.titleSmall.copyWith(
                                  color: NordicColors.primary,
                                )
                              : NordicTypography.titleSmall,
                        ),
                        Text(_angleName(i), style: NordicTypography.bodySmall),
                      ],
                    ),
                  ),
                ),
                cell('BEFORE', i),
                const SizedBox(width: NordicSpace.xs),
                cell('AFTER', i),
              ],
            ),
          ],
        ],
      ),
    );
  }

  /// "Toàn cảnh" from "Góc 1: Toàn cảnh".
  String _angleName(int angle) {
    final title =
        _angleGuides.firstWhere(
              (g) => g['angle'] == angle,
              orElse: () => _angleGuides.first,
            )['title']
            as String;
    final i = title.indexOf(': ');
    return i < 0 ? title : title.substring(i + 2);
  }

  Widget _buildCameraGuideOverlay(Map<String, dynamic> guide) {
    return NordicCard(
      isHighlighted: true,
      child: Row(
        children: [
          const Icon(Icons.crop_free, size: 40.0, color: NordicColors.primary),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  guide['title'] as String,
                  style: NordicTypography.titleSmall,
                ),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  guide['desc'] as String,
                  style: NordicTypography.bodySmall,
                ),
              ],
            ),
          ),
          const SizedBox(width: NordicSpace.xs),
          StatusPill(
            _selectedPhase == 'BEFORE' ? 'Trước' : 'Sau',
            tone: PillTone.ok,
          ),
        ],
      ),
    );
  }

  Widget _buildUploadedPhotosList() {
    final photos = _currentPhasePhotos;

    return NordicCard(
      child: Padding(
        padding: const EdgeInsets.all(NordicSpace.xxs),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: Text(
                    'Ảnh đã tải (${_selectedPhase == "BEFORE" ? "trước" : "sau"})',
                    style: NordicTypography.titleSmall,
                  ),
                ),
                const SizedBox(width: NordicSpace.xs),
                Text(
                  '${photos.length}/5 đạt',
                  style: NordicTypography.titleSmall.copyWith(
                    fontWeight: FontWeight.bold,
                    color: NordicColors.primary,
                  ),
                ),
              ],
            ),
            const SizedBox(height: NordicSpace.sm),
            if (photos.isEmpty)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: NordicSpace.sm),
                child: Center(
                  child: Text(
                    'Chưa có ảnh nào ở phần này.',
                    style: NordicTypography.bodySmall,
                  ),
                ),
              )
            else
              for (final p in photos) ...[
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Row(
                      children: [
                        ClipRRect(
                          borderRadius: NordicRadius.tileAll,
                          child: JobPhotoImage(
                            service: _service,
                            photo: p,
                            height: 40.0,
                            width: 40.0,
                            compact: true,
                          ),
                        ),
                        const SizedBox(width: NordicSpace.xs),
                        const Icon(
                          Icons.check_circle,
                          color: NordicColors.primary,
                          size: 18.0,
                        ),
                        const SizedBox(width: NordicSpace.xs),
                        Text(
                          'Góc ${p.angleNo}',
                          style: NordicTypography.labelMedium,
                        ),
                      ],
                    ),
                    Text(
                      'Độ nét ${p.volScore.toStringAsFixed(1)}',
                      style: NordicTypography.bodySmall.copyWith(
                        color: NordicColors.textSecondary,
                      ),
                    ),
                  ],
                ),
                if (p != photos.last) const Divider(),
              ],
          ],
        ),
      ),
    );
  }
}
