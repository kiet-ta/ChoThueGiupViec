import 'package:flutter/material.dart';

import '../../../core/device/camera_wrapper.dart';
import '../../../core/device/device_camera_wrapper.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
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
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: Text('Ảnh Before/After #${widget.assignmentId}'),
        backgroundColor: Colors.white,
        foregroundColor: NordicColors.textTitle,
        elevation: 0.5,
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: ListView(
            padding: const EdgeInsets.all(16.0),
            children: [
              const EyebrowBadge(text: 'Kiểm Tra Chất Lượng VoL'),
              const SizedBox(height: 8.0),
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

              // Phase Switcher
              _buildPhaseSegmentedControl(),
              const SizedBox(height: 16.0),

              // Angle Selector
              _buildAngleSelector(),
              const SizedBox(height: 16.0),

              // Camera Frame Guide Overlay
              _buildCameraGuideOverlay(currentGuide),
              const SizedBox(height: 16.0),

              // Messages
              if (_errorMessage != null) ...[
                TintedCard(
                  backgroundColor: const Color(0xFFFEE2E2),
                  child: Padding(
                    padding: const EdgeInsets.all(12.0),
                    child: Row(
                      children: [
                        const Icon(
                          Icons.warning_amber_rounded,
                          color: Color(0xFFDC2626),
                        ),
                        const SizedBox(width: 8.0),
                        Expanded(
                          child: Text(
                            _errorMessage!,
                            style: const TextStyle(
                              color: Color(0xFF991B1B),
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
                  backgroundColor: const Color(0xFFDCFCE7),
                  child: Padding(
                    padding: const EdgeInsets.all(12.0),
                    child: Row(
                      children: [
                        const Icon(
                          Icons.check_circle_outline,
                          color: Color(0xFF16A34A),
                        ),
                        const SizedBox(width: 8.0),
                        Expanded(
                          child: Text(
                            _successMessage!,
                            style: const TextStyle(
                              color: Color(0xFF15803D),
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
                ClipRRect(
                  borderRadius: BorderRadius.circular(12.0),
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

              // Photo Input & Action
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
              const SizedBox(height: 12.0),
              NordicButton(
                label: 'Gửi Ảnh Chụp',
                variant: NordicButtonVariant.primary,
                isLoading: _isLoading,
                onPressed: _uploadPhoto,
              ),

              const SizedBox(height: 20.0),
              // List of Uploaded Photos
              _buildUploadedPhotosList(),
            ],
          ),
        ),
      ),
    );
  }

  Widget _buildPhaseSegmentedControl() {
    return NordicCard(
      child: Padding(
        padding: const EdgeInsets.all(6.0),
        child: Row(
          children: [
            Expanded(
              child: GestureDetector(
                onTap: () => setState(() => _selectedPhase = 'BEFORE'),
                child: Container(
                  padding: const EdgeInsets.symmetric(vertical: 10.0),
                  decoration: BoxDecoration(
                    color: _selectedPhase == 'BEFORE'
                        ? NordicColors.primary
                        : Colors.transparent,
                    borderRadius: BorderRadius.circular(8.0),
                  ),
                  child: Text(
                    'BEFORE (Trước)',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontWeight: FontWeight.bold,
                      color: _selectedPhase == 'BEFORE'
                          ? Colors.white
                          : NordicColors.textTitle,
                    ),
                  ),
                ),
              ),
            ),
            Expanded(
              child: GestureDetector(
                onTap: () => setState(() => _selectedPhase = 'AFTER'),
                child: Container(
                  padding: const EdgeInsets.symmetric(vertical: 10.0),
                  decoration: BoxDecoration(
                    color: _selectedPhase == 'AFTER'
                        ? NordicColors.primary
                        : Colors.transparent,
                    borderRadius: BorderRadius.circular(8.0),
                  ),
                  child: Text(
                    'AFTER (Sau)',
                    textAlign: TextAlign.center,
                    style: TextStyle(
                      fontWeight: FontWeight.bold,
                      color: _selectedPhase == 'AFTER'
                          ? Colors.white
                          : NordicColors.textTitle,
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildAngleSelector() {
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          for (int i = 1; i <= 5; i++)
            Padding(
              padding: const EdgeInsets.only(right: 8.0),
              child: ChoiceChip(
                label: Text('Góc $i'),
                selected: _selectedAngle == i,
                selectedColor: NordicColors.primary,
                labelStyle: TextStyle(
                  color: _selectedAngle == i
                      ? Colors.white
                      : NordicColors.textTitle,
                  fontWeight: FontWeight.bold,
                ),
                onSelected: (selected) {
                  if (selected) {
                    setState(() => _selectedAngle = i);
                  }
                },
              ),
            ),
        ],
      ),
    );
  }

  Widget _buildCameraGuideOverlay(Map<String, dynamic> guide) {
    return Container(
      height: 180.0,
      width: double.infinity,
      decoration: BoxDecoration(
        color: const Color(0xFF0F172A),
        borderRadius: BorderRadius.circular(16.0),
        border: Border.all(color: NordicColors.primary, width: 2.0),
      ),
      child: Stack(
        children: [
          Center(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              children: [
                const Icon(
                  Icons.crop_free,
                  size: 54.0,
                  color: Color(0xFF38BDF8),
                ),
                const SizedBox(height: 8.0),
                Text(
                  guide['title'] as String,
                  style: const TextStyle(
                    color: Colors.white,
                    fontWeight: FontWeight.bold,
                    fontSize: 16.0,
                  ),
                ),
                const SizedBox(height: 4.0),
                Text(
                  guide['desc'] as String,
                  style: const TextStyle(
                    color: Color(0xFF94A3B8),
                    fontSize: 12.0,
                  ),
                ),
              ],
            ),
          ),
          Positioned(
            top: 10,
            right: 10,
            child: Container(
              padding: const EdgeInsets.symmetric(
                horizontal: 8.0,
                vertical: 4.0,
              ),
              decoration: BoxDecoration(
                color: Colors.black.withValues(alpha: 0.6),
                borderRadius: BorderRadius.circular(6.0),
              ),
              child: Text(
                'Giai đoạn: $_selectedPhase',
                style: const TextStyle(
                  color: Color(0xFF38BDF8),
                  fontSize: 11.0,
                  fontWeight: FontWeight.bold,
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildUploadedPhotosList() {
    final photos = _currentPhasePhotos;

    return NordicCard(
      child: Padding(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              mainAxisAlignment: MainAxisAlignment.spaceBetween,
              children: [
                Expanded(
                  child: Text(
                    'Đã Tải Ảnh ($_selectedPhase)',
                    style: NordicTypography.h3,
                  ),
                ),
                const SizedBox(width: 8.0),
                Text(
                  '${photos.length}/5 đạt (Min 3)',
                  style: TextStyle(
                    fontWeight: FontWeight.bold,
                    color: photos.length >= 3
                        ? const Color(0xFF16A34A)
                        : NordicColors.primary,
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
                        ClipRRect(
                          borderRadius: BorderRadius.circular(6.0),
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
                          color: Color(0xFF16A34A),
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
