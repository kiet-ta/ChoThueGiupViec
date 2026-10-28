import 'package:flutter/material.dart';

import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/device_camera_wrapper.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/stat_tile.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/job_photo_models.dart';
import '../services/workers_photos_service.dart';
import '../widgets/job_photo_image.dart';
import '../widgets/tinted_card.dart';

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
      _fail('Vui lòng chụp hoặc chọn ảnh trước khi gửi.');
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
          'Góc $_selectedAngle chưa có ảnh BEFORE được chấp nhận. Vui lòng chọn đúng góc tương ứng!',
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
            'Tải ảnh thành công! Độ nét VoL: ${saved.volScore.toStringAsFixed(1)} (ĐẠT CHUẨN)';
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

    return Scaffold(
      appBar: AppBar(title: Text('Ảnh Before/After #${widget.assignmentId}')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 24.0),
            children: [
              const Text(
                'Chụp Ảnh Trước & Sau Làm',
                style: NordicTypography.h2,
              ),
              const SizedBox(height: 6.0),
              const Text(
                'Yêu cầu chụp 3–5 góc. Ảnh After phải khớp đúng góc ảnh Before và có VoL ≥ 100.0.',
                style: NordicTypography.bodySmall,
              ),
              const SizedBox(height: 16.0),

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
                  const SizedBox(width: 12.0),
                  Expanded(
                    child: StatTile(
                      label: 'Sau',
                      value: '${_acceptedCount('AFTER')}',
                      unit: '/5',
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 16.0),

              // Phase Switcher
              _buildPhaseSegmentedControl(),
              const SizedBox(height: 16.0),

              // Angle x phase grid: tap a cell to pick that photo
              _buildAngleSelector(),
              const SizedBox(height: 16.0),

              // Frame guide of the chosen angle
              _buildCameraGuideOverlay(currentGuide),
              const SizedBox(height: 16.0),

              // Messages
              if (_errorMessage != null) ...[
                TintedCard(
                  backgroundColor: NordicColors.errorContainer,
                  child: Padding(
                    padding: const EdgeInsets.all(12.0),
                    child: Row(
                      children: [
                        const Icon(
                          Icons.warning_amber_rounded,
                          color: NordicColors.error,
                        ),
                        const SizedBox(width: 8.0),
                        Expanded(
                          child: Text(
                            _errorMessage!,
                            style: const TextStyle(
                              color: NordicColors.error,
                              fontSize: 13.0,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16.0),
              ],
              if (_successMessage != null) ...[
                TintedCard(
                  backgroundColor: NordicColors.primaryFixed,
                  child: Padding(
                    padding: const EdgeInsets.all(12.0),
                    child: Row(
                      children: [
                        const Icon(
                          Icons.check_circle_outline,
                          color: NordicColors.primary,
                        ),
                        const SizedBox(width: 8.0),
                        Expanded(
                          child: Text(
                            _successMessage!,
                            style: const TextStyle(
                              color: NordicColors.primary,
                              fontSize: 13.0,
                              fontWeight: FontWeight.bold,
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
                const SizedBox(height: 16.0),
              ],

              // The photo about to be sent
              if (_picked?.bytes != null) ...[
                ClipRect(
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
                const SizedBox(height: 12.0),
              ],

              // Photo input
              Row(
                children: [
                  Expanded(
                    child: NordicButton(
                      label: 'Chụp ảnh',
                      variant: NordicButtonVariant.secondary,
                      onPressed: _isLoading
                          ? null
                          : () => _pick(fromCamera: true),
                    ),
                  ),
                  const SizedBox(width: 8.0),
                  Expanded(
                    child: NordicButton(
                      label: 'Chọn từ thư viện',
                      variant: NordicButtonVariant.secondary,
                      onPressed: _isLoading
                          ? null
                          : () => _pick(fromCamera: false),
                    ),
                  ),
                ],
              ),

              const SizedBox(height: 20.0),
              // List of Uploaded Photos
              _buildUploadedPhotosList(),
            ],
          ),
        ),
      ),
      bottomNavigationBar: BottomActionBar(
        children: [
          NordicButton(
            label: 'Gửi Ảnh Chụp',
            variant: NordicButtonVariant.primary,
            isLoading: _isLoading,
            onPressed: _uploadPhoto,
          ),
        ],
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

  /// `.seg`: Trước / Sau in one square track, the chosen one filled.
  Widget _buildPhaseSegmentedControl() {
    Widget segment(String phase, String label) {
      final selected = _selectedPhase == phase;
      return Expanded(
        child: GestureDetector(
          onTap: () => setState(() => _selectedPhase = phase),
          child: Container(
            padding: const EdgeInsets.symmetric(vertical: 10.0),
            decoration: BoxDecoration(
              color: selected ? NordicColors.primary : Colors.transparent,
              borderRadius: BorderRadius.zero,
            ),
            child: Text(
              label,
              textAlign: TextAlign.center,
              style: TextStyle(
                fontWeight: FontWeight.w600,
                color: selected ? Colors.white : NordicColors.textTitle,
              ),
            ),
          ),
        ),
      );
    }

    return Container(
      padding: const EdgeInsets.all(4.0),
      decoration: BoxDecoration(
        color: NordicColors.surfaceContainer,
        borderRadius: BorderRadius.zero,
      ),
      child: Row(
        children: [
          segment('BEFORE', 'BEFORE (Trước)'),
          segment('AFTER', 'AFTER (Sau)'),
        ],
      ),
    );
  }

  /// `.angle` rows: one per angle with a Trước and a Sau cell; a check when the server accepted the photo, a camera
  /// icon when it is still missing. Tapping a cell chooses that phase and angle.
  Widget _buildAngleSelector() {
    Widget cell(String phase, int angle) {
      final done = _hasAccepted(phase, angle);
      final selected = _selectedPhase == phase && _selectedAngle == angle;
      return InkWell(
        key: Key('photo-cell-$phase-$angle'),
        borderRadius: BorderRadius.zero,
        onTap: () => setState(() {
          _selectedPhase = phase;
          _selectedAngle = angle;
        }),
        child: Container(
          width: 64.0,
          height: 48.0,
          decoration: BoxDecoration(
            color: done ? NordicColors.primaryFixed : null,
            borderRadius: BorderRadius.zero,
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
      );
    }

    return NordicCard(
      child: Column(
        children: [
          for (int i = 1; i <= 5; i++) ...[
            if (i > 1) const Divider(height: 16.0),
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
                const SizedBox(width: 8.0),
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
          const SizedBox(width: 12.0),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  guide['title'] as String,
                  style: NordicTypography.titleSmall,
                ),
                const SizedBox(height: 2.0),
                Text(
                  guide['desc'] as String,
                  style: NordicTypography.bodySmall,
                ),
              ],
            ),
          ),
          const SizedBox(width: 8.0),
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
        padding: const EdgeInsets.all(4.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: Text(
                    'Đã Tải Ảnh ($_selectedPhase)',
                    style: NordicTypography.titleSmall,
                  ),
                ),
                const SizedBox(width: 8.0),
                Text(
                  '${photos.length}/5 đạt (Min 3)',
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                    color: NordicColors.primary,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 12.0),
            if (photos.isEmpty)
              const Padding(
                padding: EdgeInsets.symmetric(vertical: 12.0),
                child: Center(
                  child: Text(
                    'Chưa có ảnh nào được tải lên cho giai đoạn này.',
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
                        ClipRect(
                          child: JobPhotoImage(
                            service: _service,
                            photo: p,
                            height: 40.0,
                            width: 40.0,
                            compact: true,
                          ),
                        ),
                        const SizedBox(width: 8.0),
                        const Icon(
                          Icons.check_circle,
                          color: NordicColors.primary,
                          size: 18.0,
                        ),
                        const SizedBox(width: 8.0),
                        Text(
                          'Góc ${p.angleNo}',
                          style: NordicTypography.labelMedium,
                        ),
                      ],
                    ),
                    Text(
                      'VoL: ${p.volScore.toStringAsFixed(1)}',
                      style: const TextStyle(
                        fontSize: 12.0,
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
