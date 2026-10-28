import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/avatar_initials.dart';
import '../../../core/widgets/grouped_list_card.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../../identity/services/token_storage.dart';
import '../models/customer_profile.dart';
import '../services/customer_profile_service.dart';

/// The customer profile: who is signed in, name and email to edit (MOB-UI-14,
/// `Mobile/DESIGN-SYSTEM.md`: `NordicPage`, "Lưu" above the keyboard, a failed save shakes).
class CustomerProfileScreen extends StatefulWidget {
  final CustomerProfileService? service;
  final TokenStorage? tokenStorage;

  const CustomerProfileScreen({super.key, this.service, this.tokenStorage});

  @override
  State<CustomerProfileScreen> createState() => _CustomerProfileScreenState();
}

class _CustomerProfileScreenState extends State<CustomerProfileScreen> {
  late final CustomerProfileService _service;
  late final TokenStorage _tokenStorage;

  final _formKey = GlobalKey<FormState>();

  /// Failed saves so far: each one shakes the fields.
  int _formErrors = 0;
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
          _errorMessage = 'Chưa tải được hồ sơ. Kiểm tra mạng rồi thử lại.';
          _isLoading = false;
        });
      }
    }
  }

  Future<void> _handleSave() async {
    if (!_formKey.currentState!.validate()) {
      setState(() => _formErrors++);
      return;
    }

    setState(() {
      _isSaving = true;
    });

    try {
      final updated = await _service.updateProfile(
        fullName: _nameController.text,
        email: _emailController.text.trim().isEmpty
            ? null
            : _emailController.text.trim(),
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
              'Đã lưu hồ sơ',
              style: TextStyle(
                color: Colors.white,
                fontWeight: FontWeight.w600,
              ),
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
              e is ArgumentError
                  ? e.message.toString()
                  : 'Chưa lưu được hồ sơ. Thử lại nhé.',
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
        title: const Text('Đăng xuất?', style: NordicTypography.h3),
        content: const Text(
          'Lần sau bạn cần mã xác nhận để vào lại.',
          style: NordicTypography.bodyRegular,
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text(
              'Huỷ',
              style: TextStyle(color: NordicColors.textSecondary),
            ),
          ),
          ElevatedButton(
            style: ElevatedButton.styleFrom(
              backgroundColor: NordicColors.error,
            ),
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text(
              'Đăng xuất',
              style: TextStyle(color: Colors.white),
            ),
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
    final ready = !_isLoading && _errorMessage == null;
    return NordicPage.pushed(
      title: 'Hồ sơ',
      // "Lưu" sits in the action bar, above the keyboard (DESIGN-SYSTEM §5 Form).
      actions: ready
          ? [
              NordicButton(
                key: const Key('saveProfileButton'),
                label: 'Lưu',
                isLoading: _isSaving,
                onPressed: _isSaving ? null : _handleSave,
              ),
            ]
          : null,
      children: [
        if (_isLoading)
          const SizedBox(height: 320, child: LoadingStateWidget())
        else if (_errorMessage != null)
          SizedBox(
            height: 360,
            child: ErrorStateWidget(
              message: _errorMessage!,
              onRetry: _loadProfile,
            ),
          )
        else
          _buildProfileContent(),
      ],
    );
  }

  /// A small label above the input.
  Widget _field(String label, Widget input) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        label,
        style: NordicTypography.bodySmall.copyWith(fontWeight: FontWeight.w600),
      ),
      const SizedBox(height: 6.0),
      input,
    ],
  );

  Widget _buildProfileContent() {
    final profile = _profile!;
    // A new customer has no score yet (0 from the server): start them at the full 5 instead of a discouraging 0.
    final shown = profile.trustScore > 0
        ? profile
        : profile.copyWith(trustScore: 5.0);

    return Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NordicCard(
            padding: const EdgeInsets.all(NordicSpace.gutter),
            child: Column(
              children: [
                AvatarInitials(profile.fullName, size: 72.0),
                const SizedBox(height: NordicSpace.sm),
                Text(
                  profile.fullName.isNotEmpty ? profile.fullName : 'Khách hàng',
                  textAlign: TextAlign.center,
                  style: NordicTypography.titleMedium,
                ),
                const SizedBox(height: NordicSpace.xs),
                Wrap(
                  alignment: WrapAlignment.center,
                  spacing: NordicSpace.xs,
                  runSpacing: NordicSpace.xxs,
                  children: [
                    StatusPill(
                      profile.statusDisplayName,
                      tone: profile.accountStatus.toUpperCase() == 'ACTIVE'
                          ? PillTone.ok
                          : PillTone.warn,
                      dot: true,
                    ),
                    StatusPill(
                      '${shown.trustScore.toStringAsFixed(1).replaceAll('.', ',')} • ${shown.trustScoreTierName}',
                      tone: PillTone.warn,
                    ),
                  ],
                ),
                const SizedBox(height: NordicSpace.xs),
                Text(
                  'Thành viên từ ${profile.formattedCreatedAt}',
                  style: NordicTypography.bodySmall,
                ),
              ],
            ),
          ),
          const SizedBox(height: NordicSpace.lg),
          // A failed save shakes the fields (DESIGN-SYSTEM §5 Form).
          ErrorShake(
            errors: _formErrors,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                _field(
                  'Họ và tên',
                  TextFormField(
                    key: const Key('fullNameField'),
                    controller: _nameController,
                    textCapitalization: TextCapitalization.words,
                    decoration: const InputDecoration(
                      hintText: 'Ví dụ: Nguyễn Văn An',
                    ),
                    validator: (val) {
                      if (val == null || val.trim().isEmpty) {
                        return 'Nhập họ và tên.';
                      }
                      if (val.trim().length > 100) {
                        return 'Họ và tên tối đa 100 ký tự.';
                      }
                      return null;
                    },
                  ),
                ),
                const SizedBox(height: NordicSpace.md),
                _field(
                  'Email (không bắt buộc)',
                  TextFormField(
                    key: const Key('emailField'),
                    controller: _emailController,
                    keyboardType: TextInputType.emailAddress,
                    decoration: const InputDecoration(
                      hintText: 'Ví dụ: an.nguyen@gmail.com',
                    ),
                    validator: (val) {
                      if (val != null && val.trim().isNotEmpty) {
                        if (val.trim().length > 255) {
                          return 'Email tối đa 255 ký tự.';
                        }
                        final emailRegex = RegExp(
                          r'^[^@\s]+@[^@\s]+\.[^@\s]+$',
                        );
                        if (!emailRegex.hasMatch(val.trim())) {
                          return 'Email chưa đúng. Ví dụ: an@gmail.com';
                        }
                      }
                      return null;
                    },
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: NordicSpace.md),
          // The phone is the sign-in identity: shown, never edited.
          _field(
            'Số điện thoại (dùng để đăng nhập)',
            Container(
              height: 52.0,
              padding: const EdgeInsets.symmetric(horizontal: NordicSpace.md),
              decoration: BoxDecoration(
                color: NordicColors.surfaceLow,
                borderRadius: NordicRadius.fieldAll,
                border: Border.all(color: NordicColors.border),
              ),
              child: Row(
                children: [
                  Expanded(
                    child: Text(
                      profile.phoneNumber,
                      style: const TextStyle(
                        fontSize: 15.0,
                        color: NordicColors.textTitle,
                        fontFeatures: [FontFeature.tabularFigures()],
                      ),
                    ),
                  ),
                  const Icon(
                    Icons.lock_outline,
                    size: 18.0,
                    color: NordicColors.textSecondary,
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: NordicSpace.section),
          // The same sign-out row as the Tài khoản tab.
          GroupedListCard(
            items: [
              GroupedListItem(
                key: const Key('logoutButton'),
                icon: Icons.logout,
                title: 'Đăng xuất',
                danger: true,
                onTap: _handleLogout,
              ),
            ],
          ),
        ],
      ),
    );
  }
}
