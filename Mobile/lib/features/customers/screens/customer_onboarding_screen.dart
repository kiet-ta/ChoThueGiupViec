import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/step_dots.dart';
import '../../identity/services/api_error.dart';
import '../logic/address_rules.dart';
import '../logic/onboarding_logic.dart';
import '../models/customer_address.dart';
import '../models/vn_admin_units.dart';
import '../services/customer_address_service.dart';
import '../services/customer_profile_service.dart';
import '../widgets/admin_unit_picker_field.dart';
import '../widgets/area_calculator_card.dart';
import '../widgets/gps_picker.dart';

/// What a customer sees after the first login (MOB-ONBOARD-01): three short steps, a name to be called, the home address
/// and the house type. Only the name is required and it is saved as soon as step 1 is done. "Để sau" on the address or the
/// house type ends the run: the server needs the house type and the area to keep an address (contract customers.md 2.2), and
/// the area sets the price of an order, so an address without them is not saved and nothing is guessed.
///
/// Pops with an [OnboardingResult].
class CustomerOnboardingScreen extends StatefulWidget {
  final CustomerProfileService? profileService;
  final CustomerAddressService? addressService;

  const CustomerOnboardingScreen({
    super.key,
    this.profileService,
    this.addressService,
  });

  @override
  State<CustomerOnboardingScreen> createState() =>
      _CustomerOnboardingScreenState();
}

class _CustomerOnboardingScreenState extends State<CustomerOnboardingScreen> {
  late final CustomerProfileService _profiles;
  late final CustomerAddressService _addresses;

  final _nameForm = GlobalKey<FormState>();
  final _addressForm = GlobalKey<FormState>();
  final _houseForm = GlobalKey<FormState>();

  final _name = TextEditingController();
  final _addressLine = TextEditingController();
  final _floorArea = TextEditingController(text: defaultFloorAreaText);
  final _floors = TextEditingController(text: '1');

  OnboardingStep _step = OnboardingStep.name;
  String? _city = 'Thành phố Hồ Chí Minh';
  String? _ward;
  double _latitude = 10.7769;
  double _longitude = 106.7009;
  HousingType? _housingType;
  String? _typeError;

  bool _nameSaved = false;
  bool _addressSaved = false;
  bool _busy = false;
  String? _error;

  /// Failed attempts so far: each one shakes the form.
  int _errors = 0;

  @override
  void initState() {
    super.initState();
    _profiles = widget.profileService ?? CustomerProfileService();
    _addresses = widget.addressService ?? CustomerAddressService();
    _floorArea.addListener(_refresh);
    _floors.addListener(_refresh);
  }

  @override
  void dispose() {
    _floorArea.removeListener(_refresh);
    _floors.removeListener(_refresh);
    _name.dispose();
    _addressLine.dispose();
    _floorArea.dispose();
    _floors.dispose();
    super.dispose();
  }

  void _refresh() => setState(() {});

  HousingType get _typeForRules => _housingType ?? HousingType.house;
  double get _area => parseFloorArea(_floorArea.text);
  int get _floorCount => effectiveFloors(_typeForRules, _floors.text);

  void _fail(Object e) {
    setState(() {
      _busy = false;
      _errors++;
      _error = switch (e) {
        ApiException() => e.message,
        ArgumentError() => e.message.toString(),
        _ => 'Chưa lưu được. Thử lại nhé.',
      };
    });
  }

  void _goTo(OnboardingStep step) => setState(() {
    _step = step;
    _error = null;
  });

  void _finish() => Navigator.of(
    context,
  ).pop(OnboardingResult(nameSaved: _nameSaved, addressSaved: _addressSaved));

  /// Back goes to the previous step with everything typed kept; the first step cannot be left.
  void _back() {
    if (_busy) return;
    final previous = previousStep(_step);
    if (previous != null) _goTo(previous);
  }

  Future<void> _saveName() async {
    if (_busy) return;
    if (!_nameForm.currentState!.validate()) {
      setState(() => _errors++);
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await _profiles.updateProfile(fullName: normalizeCallName(_name.text));
      if (!mounted) return;
      _nameSaved = true;
      setState(() => _busy = false);
      _goTo(OnboardingStep.address);
    } catch (e) {
      if (mounted) _fail(e);
    }
  }

  void _continueToHouseType() {
    if (!_addressForm.currentState!.validate()) {
      setState(() => _errors++);
      return;
    }
    _goTo(OnboardingStep.houseType);
  }

  Future<void> _saveAddress() async {
    if (_busy) return;
    final valid = _houseForm.currentState!.validate();
    final typeMissing = _housingType == null;
    setState(() => _typeError = typeMissing ? 'Chọn loại nhà.' : null);
    if (!valid || typeMissing) {
      setState(() => _errors++);
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });
    try {
      await _addresses.createAddress(
        CustomerAddress(
          addressId: 0,
          label: 'Nhà',
          addressLine: _addressLine.text.trim(),
          district: _ward!,
          city: _city!,
          housingType: _housingType!,
          floorAreaM2: _area,
          numFloors: _floorCount,
          totalAreaM2: CustomerAddress.calculateTotalArea(_area, _floorCount),
          latitude: _latitude,
          longitude: _longitude,
          isDefault: true,
        ),
      );
      if (!mounted) return;
      _addressSaved = true;
      _finish();
    } catch (e) {
      if (mounted) _fail(e);
    }
  }

  void _pickType(HousingType type) {
    setState(() {
      _housingType = type;
      _typeError = null;
      final floors = floorsTextAfterTypeChange(type);
      if (floors != null) _floors.text = floors;
      final area = floorAreaTextAfterTypeChange(type, _area);
      if (area != null) _floorArea.text = area;
    });
  }

  /// A position fills the address line, province and ward when the server can read them; otherwise the person types them.
  Future<void> _onCoordinatesChanged(double lat, double lng) async {
    setState(() {
      _latitude = lat;
      _longitude = lng;
    });
    try {
      final res = await _addresses.reverseGeocode(lat, lng);
      if (!mounted) return;
      setState(() {
        final line = res.addressLine.isNotEmpty
            ? res.addressLine
            : res.formattedAddress;
        if (line.isNotEmpty) _addressLine.text = line;
        final newCity = matchAdminCity(res.city) ?? _city;
        final cityChanged = newCity != _city;
        _city = newCity;
        final ward = matchAdminWard(
          _city,
          res.district,
          addressLine: res.addressLine,
          formattedAddress: res.formattedAddress,
        );
        if (ward != null) {
          _ward = ward;
        } else if (cityChanged) {
          _ward = null;
        }
      });
    } catch (_) {
      // The coordinates stay; the address is typed by hand.
    }
  }

  @override
  Widget build(BuildContext context) {
    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _back();
      },
      child: NordicPage.pushed(
        title: 'Chào mừng bạn',
        leading: previousStep(_step) == null
            ? const SizedBox.shrink()
            : BackButton(key: const Key('onboarding-back'), onPressed: _back),
        actions: _actions(),
        children: [
          _steps(),
          const SizedBox(height: NordicSpace.lg),
          ErrorShake(
            errors: _errors,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (_error != null) _errorBanner(_error!),
                ...switch (_step) {
                  OnboardingStep.name => _nameStep(),
                  OnboardingStep.address => _addressStep(),
                  OnboardingStep.houseType => _houseStep(),
                },
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _steps() {
    const labels = ['Tên gọi', 'Địa chỉ', 'Loại nhà'];
    final current = _step.index;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        StepDots(
          key: const Key('onboarding-steps'),
          steps: [
            for (var i = 0; i < labels.length; i++)
              StepItem(
                labels[i],
                i < current
                    ? DotState.done
                    : i == current
                    ? DotState.current
                    : DotState.todo,
              ),
          ],
        ),
        const SizedBox(height: NordicSpace.xs),
        Text(
          'Bước ${stepNumber(_step)}/$onboardingStepCount',
          key: const Key('onboarding-step-label'),
          style: NordicTypography.bodySmall.copyWith(
            color: NordicColors.textSecondary,
          ),
        ),
      ],
    );
  }

  List<Widget> _actions() {
    switch (_step) {
      case OnboardingStep.name:
        return [
          NordicButton(
            key: const Key('onboarding-continue'),
            label: 'Tiếp tục',
            isLoading: _busy,
            onPressed: _saveName,
          ),
        ];
      case OnboardingStep.address:
        return [
          NordicButton(
            key: const Key('onboarding-skip'),
            label: 'Để sau',
            variant: NordicButtonVariant.secondary,
            onPressed: _busy ? null : _finish,
          ),
          NordicButton(
            key: const Key('onboarding-continue'),
            label: 'Tiếp tục',
            onPressed: _continueToHouseType,
          ),
        ];
      case OnboardingStep.houseType:
        return [
          NordicButton(
            key: const Key('onboarding-skip'),
            label: 'Để sau',
            variant: NordicButtonVariant.secondary,
            onPressed: _busy ? null : _finish,
          ),
          NordicButton(
            key: const Key('onboarding-save-address'),
            label: 'Lưu địa chỉ',
            isLoading: _busy,
            onPressed: _saveAddress,
          ),
        ];
    }
  }

  Widget _heading(String title, String subtitle) => Padding(
    padding: const EdgeInsets.only(bottom: NordicSpace.md),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(title, style: NordicTypography.titleMedium),
        const SizedBox(height: NordicSpace.xxs),
        Text(
          subtitle,
          style: NordicTypography.bodyRegular.copyWith(
            color: NordicColors.textSecondary,
          ),
        ),
      ],
    ),
  );

  Widget _errorBanner(String message) => Padding(
    padding: const EdgeInsets.only(bottom: NordicSpace.md),
    child: Container(
      key: const Key('onboarding-error'),
      padding: const EdgeInsets.all(NordicSpace.sm),
      decoration: const BoxDecoration(
        color: NordicColors.errorContainer,
        borderRadius: NordicRadius.tileAll,
      ),
      child: Text(
        message,
        style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
      ),
    ),
  );

  List<Widget> _nameStep() => [
    _heading(
      'Chúng tôi nên gọi bạn là gì?',
      'Tên này hiện cho thợ khi họ nhận ca của bạn.',
    ),
    Form(
      key: _nameForm,
      child: NordicTextInput(
        key: const Key('onboarding-name'),
        label: 'Tên gọi',
        hintText: 'Ví dụ: Anh Duy',
        controller: _name,
        readOnly: _busy,
        textInputAction: TextInputAction.done,
        onFieldSubmitted: (_) => _saveName(),
        validator: (value) => callNameError(value ?? ''),
      ),
    ),
  ];

  List<Widget> _addressStep() => [
    _heading(
      'Nhà của bạn ở đâu?',
      'Để báo giá và đặt ca nhanh hơn. Bạn có thể bỏ qua và thêm sau.',
    ),
    Form(
      key: _addressForm,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          NordicTextInput(
            key: const Key('onboarding-address-line'),
            label: 'Số nhà, đường',
            hintText: 'Ví dụ: 123 Nguyễn Thị Minh Khai',
            controller: _addressLine,
            validator: (value) => addressLineError(value ?? ''),
          ),
          const SizedBox(height: NordicSpace.md),
          AdminUnitPickerField(
            label: 'Tỉnh / Thành phố',
            value: _city,
            options: vietnamAdminUnits.keys.toList(),
            requiredMessage: 'Chọn tỉnh / thành phố.',
            notInListMessage: 'Chọn lại tỉnh / thành phố theo danh mục mới.',
            onSelected: (city) => setState(() {
              _city = city;
              _ward = null;
            }),
          ),
          const SizedBox(height: NordicSpace.md),
          AdminUnitPickerField(
            label: 'Phường / Xã',
            value: _ward,
            options: vietnamAdminUnits[_city] ?? const [],
            enabled: vietnamAdminUnits.containsKey(_city),
            helperText: vietnamAdminUnits.containsKey(_city)
                ? null
                : 'Chọn tỉnh / thành phố trước',
            requiredMessage: 'Chọn phường / xã.',
            notInListMessage: 'Chọn lại phường / xã theo danh mục mới.',
            onSelected: (ward) => setState(() => _ward = ward),
          ),
          const SizedBox(height: NordicSpace.lg),
          Text(
            'Vị trí trên bản đồ (không bắt buộc)',
            style: NordicTypography.titleSmall,
          ),
          const SizedBox(height: NordicSpace.xs),
          NordicCard(
            child: GpsCoordinatePicker(
              latitude: _latitude,
              longitude: _longitude,
              onCoordinatesChanged: _onCoordinatesChanged,
            ),
          ),
        ],
      ),
    ),
  ];

  List<Widget> _houseStep() => [
    _heading(
      'Nhà bạn thuộc loại nào?',
      'Diện tích quyết định giá mỗi ca. Chọn "Để sau" thì chưa lưu địa chỉ.',
    ),
    Wrap(
      spacing: NordicSpace.xs,
      runSpacing: NordicSpace.xs,
      children: [
        for (final type in HousingType.values)
          ChoiceChip(
            key: Key('onboarding-type-${type.apiValue}'),
            label: Text(type.displayName),
            selected: _housingType == type,
            showCheckmark: false,
            onSelected: (_) => _pickType(type),
          ),
      ],
    ),
    if (_typeError != null)
      Padding(
        padding: const EdgeInsets.only(top: NordicSpace.xxs),
        child: Text(
          _typeError!,
          key: const Key('onboarding-type-error'),
          style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
        ),
      ),
    const SizedBox(height: NordicSpace.md),
    Form(
      key: _houseForm,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Expanded(
            child: TextFormField(
              key: const Key('onboarding-area'),
              controller: _floorArea,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              decoration: InputDecoration(
                labelText: 'Mỗi sàn (m²)',
                helperText: floorAreaHelper(_typeForRules),
              ),
              validator: (value) => floorAreaError(_typeForRules, value),
            ),
          ),
          const SizedBox(width: NordicSpace.sm),
          Expanded(
            child: TextFormField(
              key: const Key('onboarding-floors'),
              controller: _floors,
              keyboardType: TextInputType.number,
              enabled: _typeForRules == HousingType.house,
              decoration: InputDecoration(
                labelText: 'Số tầng',
                helperText: floorsHelper(_typeForRules),
              ),
              validator: (value) => floorsError(_typeForRules, value),
            ),
          ),
        ],
      ),
    ),
    const SizedBox(height: NordicSpace.sm),
    AreaCalculatorCard(
      floorAreaM2: _area,
      numFloors: _floorCount,
      totalAreaM2: CustomerAddress.calculateTotalArea(_area, _floorCount),
    ),
  ];
}
