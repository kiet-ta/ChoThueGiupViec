import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../identity/services/token_storage.dart';
import '../models/customer_profile.dart';
import '../services/customer_profile_service.dart';

/// Screen displaying and editing customer profile according to TỔ ẤM — Nordic Care.
class CustomerProfileScreen extends StatefulWidget {
  final CustomerProfileService? service;
  final TokenStorage? tokenStorage;

  const CustomerProfileScreen({
    super.key,
    this.service,
    this.tokenStorage,
  });

  @override
  State<CustomerProfileScreen> createState() => _CustomerProfileScreenState();
}

class _CustomerProfileScreenState extends State<CustomerProfileScreen> {
  late final CustomerProfileService _service;
  late final TokenStorage _tokenStorage;

  final _formKey = GlobalKey<FormState>();
  late final TextEditingController _nameController;
  late final TextEditingController _emailController;

  CustomerProfile? _profile;
  bool _isLoading = true;
  bool _isSaving = false;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? CustomerProfileService();
    _tokenStorage = widget.tokenStorage ?? TokenStorage();
    _nameController = TextEditingController();
    _emailController = TextEditingController();
    _loadProfile();
  }

  @override
  void dispose() {
    _nameController.dispose();
    _emailController.dispose();
    super.dispose();
  }

  Future<void> _loadProfile() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final profile = await _service.getProfile();
      if (mounted) {
        setState(() {
          _profile = profile;
          _nameController.text = profile.fullName;
          _emailController.text = profile.email ?? '';
          _isLoading = false;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _errorMessage = 'Không thể tải thông tin hồ sơ: $e';
          _isLoading = false;
        });
      }
    }
  }

  Future<void> _handleSave() async {
    if (!_formKey.currentState!.validate()) {
      return;
    }

    setState(() {
      _isSaving = true;
    });

    try {
      final updated = await _service.updateProfile(
        fullName: _nameController.text,
        email: _emailController.text.trim().isEmpty ? null : _emailController.text.trim(),
      );

      if (mounted) {
        setState(() {
          _profile = updated;
          _isSaving = false;
        });

        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            backgroundColor: NordicColors.primary,
            content: Text(
              'Cập nhật hồ sơ thành công!',
              style: TextStyle(color: Colors.white, fontWeight: FontWeight.w600),
            ),
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        setState(() {
          _isSaving = false;
        });

        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            backgroundColor: NordicColors.error,
            content: Text(
              e is ArgumentError ? e.message.toString() : 'Lỗi cập nhật: $e',
              style: const TextStyle(color: Colors.white),
            ),
          ),
        );
      }
    }
  }

  Future<void> _handleLogout() async {
    final confirm = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Đăng xuất tài khoản', style: NordicTypography.h3),
        content: const Text(
          'Bạn có chắc chắn muốn đăng xuất khỏi ứng dụng?',
          style: NordicTypography.bodyRegular,
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Huỷ', style: TextStyle(color: NordicColors.textSecondary)),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(backgroundColor: NordicColors.error),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Đăng xuất', style: TextStyle(color: Colors.white)),
          ),
        ],
      ),
    );

    if (confirm == true) {
      _tokenStorage.clear();
      if (mounted) {
        Navigator.pop(context);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: const Text('Hồ Sơ Cá Nhân', style: NordicTypography.h2),
        backgroundColor: NordicColors.surface,
        elevation: 0,
        centerTitle: false,
        iconTheme: const IconThemeData(color: NordicColors.primary),
      ),
      body: _isLoading
          ? const Center(
              child: CircularProgressIndicator(
                valueColor: AlwaysStoppedAnimation<Color>(NordicColors.primary),
              ),
            )
          : _errorMessage != null
              ? Center(
                  child: Padding(
                    padding: const EdgeInsets.all(24.0),
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        const Icon(Icons.error_outline_rounded, size: 48, color: NordicColors.error),
                        const SizedBox(height: 12),
                        Text(_errorMessage!, style: NordicTypography.bodyRegular, textAlign: TextAlign.center),
                        const SizedBox(height: 16),
                        NordicButton(label: 'Thử lại', onPressed: _loadProfile),
                      ],
                    ),
                  ),
                )
              : _buildProfileContent(),
    );
  }

  Widget _buildProfileContent() {
    final profile = _profile!;

    return SingleChildScrollView(
      padding: const EdgeInsets.all(16.0),
      child: Form(
        key: _formKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            // Header Avatar Card
            NordicCard(
              isHighlighted: true,
              child: Row(
                children: [
                  Container(
                    width: 72,
                    height: 72,
                    decoration: BoxDecoration(
                      color: NordicColors.primary.withValues(alpha: 0.1),
                      shape: BoxShape.circle,
                      border: Border.all(
                        color: NordicColors.primary.withValues(alpha: 0.3),
                        width: 2.0,
                      ),
                    ),
                    alignment: Alignment.center,
                    child: Text(
                      profile.initialLetter,
                      style: const TextStyle(
                        fontSize: 32,
                        fontWeight: FontWeight.bold,
                        color: NordicColors.primary,
                      ),
                    ),
                  ),
                  const SizedBox(width: 16),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          profile.fullName.isNotEmpty ? profile.fullName : 'Khách Hàng',
                          style: NordicTypography.h2,
                        ),
                        const SizedBox(height: 4),
                        Row(
                          children: [
                            const Icon(
                              Icons.phone_android_rounded,
                              size: 16,
                              color: NordicColors.textSecondary,
                            ),
                            const SizedBox(width: 4),
                            Text(
                              profile.phoneNumber,
                              style: NordicTypography.bodySmall.copyWith(
                                color: NordicColors.textSecondary,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 8),
                        Wrap(
                          spacing: 8,
                          runSpacing: 4,
                          children: [
                            // Status badge
                            Container(
                              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                              decoration: BoxDecoration(
                                color: profile.statusBadgeBgColor,
                                borderRadius: BorderRadius.circular(999),
                                border: Border.all(
                                  color: profile.statusBadgeColor.withValues(alpha: 0.3),
                                ),
                              ),
                              child: Text(
                                profile.statusDisplayName,
                                style: TextStyle(
                                  color: profile.statusBadgeColor,
                                  fontSize: 11,
                                  fontWeight: FontWeight.bold,
                                ),
                              ),
                            ),
                            // Trust score badge
                            Container(
                              padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                              decoration: BoxDecoration(
                                color: const Color(0xFFFEF3C7),
                                borderRadius: BorderRadius.circular(999),
                                border: Border.all(
                                  color: const Color(0xFFF59E0B).withValues(alpha: 0.4),
                                ),
                              ),
                              child: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  const Icon(Icons.star_rounded, size: 14, color: Color(0xFFB45309)),
                                  const SizedBox(width: 3),
                                  Text(
                                    '${profile.trustScore.toStringAsFixed(1)} • ${profile.trustScoreTierName}',
                                    style: const TextStyle(
                                      color: Color(0xFFB45309),
                                      fontSize: 11,
                                      fontWeight: FontWeight.bold,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),

            // Identity info card
            Container(
              padding: const EdgeInsets.all(14.0),
              decoration: BoxDecoration(
                color: NordicColors.surfaceSubtle,
                borderRadius: BorderRadius.circular(12.0),
                border: Border.all(color: NordicColors.border),
              ),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Icon(
                    Icons.lock_outline_rounded,
                    color: NordicColors.primary,
                    size: 20,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'SĐT Định Danh: ${profile.phoneNumber}',
                          style: NordicTypography.labelMedium.copyWith(
                            color: NordicColors.textTitle,
                          ),
                        ),
                        const SizedBox(height: 2),
                        const Text(
                          'Số điện thoại được bảo vệ và dùng làm định danh đăng nhập OTP, không thể thay đổi.',
                          style: NordicTypography.bodySmall,
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),

            // Form Fields Card
            NordicCard(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const Text('THÔNG TIN LIÊN HỆ', style: NordicTypography.labelMedium),
                  const SizedBox(height: 16),

                  // Full name
                  TextFormField(
                    key: const Key('fullNameField'),
                    controller: _nameController,
                    decoration: InputDecoration(
                      labelText: 'Họ và tên *',
                      labelStyle: const TextStyle(color: NordicColors.textSecondary),
                      hintText: 'Ví dụ: Nguyễn Văn An',
                      prefixIcon: const Icon(Icons.person_outline_rounded, color: NordicColors.primary),
                      border: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(10.0),
                        borderSide: const BorderSide(color: NordicColors.border),
                      ),
                      enabledBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(10.0),
                        borderSide: const BorderSide(color: NordicColors.border),
                      ),
                      focusedBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(10.0),
                        borderSide: const BorderSide(color: NordicColors.primary, width: 2.0),
                      ),
                    ),
                    validator: (val) {
                      if (val == null || val.trim().isEmpty) {
                        return 'Vui lòng nhập họ và tên';
                      }
                      if (val.trim().length > 100) {
                        return 'Họ và tên không được quá 100 ký tự';
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // Email
                  TextFormField(
                    key: const Key('emailField'),
                    controller: _emailController,
                    keyboardType: TextInputType.emailAddress,
                    decoration: InputDecoration(
                      labelText: 'Email',
                      labelStyle: const TextStyle(color: NordicColors.textSecondary),
                      hintText: 'Ví dụ: an.nguyen@example.com',
                      prefixIcon: const Icon(Icons.email_outlined, color: NordicColors.primary),
                      border: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(10.0),
                        borderSide: const BorderSide(color: NordicColors.border),
                      ),
                      enabledBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(10.0),
                        borderSide: const BorderSide(color: NordicColors.border),
                      ),
                      focusedBorder: OutlineInputBorder(
                        borderRadius: BorderRadius.circular(10.0),
                        borderSide: const BorderSide(color: NordicColors.primary, width: 2.0),
                      ),
                    ),
                    validator: (val) {
                      if (val != null && val.trim().isNotEmpty) {
                        if (val.trim().length > 255) {
                          return 'Email không được quá 255 ký tự';
                        }
                        final emailRegex = RegExp(r'^[^@\s]+@[^@\s]+\.[^@\s]+$');
                        if (!emailRegex.hasMatch(val.trim())) {
                          return 'Email không đúng định dạng';
                        }
                      }
                      return null;
                    },
                  ),
                  const SizedBox(height: 16),

                  // Member since
                  Row(
                    children: [
                      const Icon(Icons.calendar_month_outlined, size: 18, color: NordicColors.textSecondary),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          'Thành viên từ: ${profile.formattedCreatedAt}',
                          style: NordicTypography.bodySmall.copyWith(
                            color: NordicColors.textSecondary,
                          ),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
            const SizedBox(height: 16),

            // Trust score explanation card
            NordicCard(
              child: Row(
                children: [
                  Container(
                    width: 44,
                    height: 44,
                    decoration: BoxDecoration(
                      color: const Color(0xFFFEF3C7),
                      borderRadius: BorderRadius.circular(12),
                    ),
                    child: const Icon(
                      Icons.verified_outlined,
                      color: Color(0xFFB45309),
                      size: 24,
                    ),
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'Đặc quyền điểm tin cậy ${profile.trustScore.toStringAsFixed(1)} ★',
                          style: NordicTypography.h3,
                        ),
                        const SizedBox(height: 2),
                        const Text(
                          'Khách hàng uy tín được ưu tiên ghép thợ xếp hạng cao và hỗ trợ giải quyết sự vụ 24/7.',
                          style: NordicTypography.bodySmall,
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(height: 24),

            // Save button
            NordicButton(
              key: const Key('saveProfileButton'),
              label: 'Lưu Thay Đổi',
              isLoading: _isSaving,
              onPressed: _isSaving ? null : _handleSave,
            ),
            const SizedBox(height: 12),

            // Logout button
            NordicButton(
              key: const Key('logoutButton'),
              label: 'Đăng Xuất',
              variant: NordicButtonVariant.ghost,
              icon: const Icon(Icons.logout_rounded, size: 18, color: NordicColors.error),
              onPressed: _handleLogout,
            ),
          ],
        ),
      ),
    );
  }
}
