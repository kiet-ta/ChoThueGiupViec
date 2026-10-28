import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../models/customer_address.dart';
import '../models/vn_admin_units.dart';
import '../services/customer_address_service.dart';
import '../widgets/admin_unit_picker_field.dart';
import '../widgets/area_calculator_card.dart';
import '../widgets/gps_picker.dart';

/// Screen allowing customers to create or edit an address in their address book.
/// Enforces business rules from PRD §1.1 and .spec/contracts/customers.md §2.2.
class AddressFormScreen extends StatefulWidget {
  final CustomerAddress? initialAddress;
  final CustomerAddressService? addressService;

  const AddressFormScreen({
    super.key,
    this.initialAddress,
    this.addressService,
  });

  @override
  State<AddressFormScreen> createState() => _AddressFormScreenState();
}

class _AddressFormScreenState extends State<AddressFormScreen> {
  late final CustomerAddressService _service;
  final _formKey = GlobalKey<FormState>();

  late final TextEditingController _labelController;
  late final TextEditingController _addressLineController;
  late final TextEditingController _floorAreaController;

  /// Province or city, then ward / commune of [vietnamAdminUnits] (sent as `city` and `district`).
  String? _city;
  String? _ward;
  late final TextEditingController _numFloorsController;

  late HousingType _housingType;
  late double _latitude;
  late double _longitude;
  late bool _isDefault;

  bool _isSubmitting = false;
  String? _generalError;

  bool get _isEditing => widget.initialAddress != null;

  /// Failed saves so far: each one shakes the form.
  int _formErrors = 0;

  @override
  void initState() {
    super.initState();
    _service = widget.addressService ?? CustomerAddressService();
    final init = widget.initialAddress;

    _labelController = TextEditingController(text: init?.label ?? '');
    _addressLineController = TextEditingController(
      text: init?.addressLine ?? '',
    );
    // A new address starts in Ho Chi Minh City, like the GPS default below; an old address keeps its saved text.
    _city = init?.city ?? 'Thành phố Hồ Chí Minh';
    _ward = init?.district;
    _floorAreaController = TextEditingController(
      text: init != null ? init.floorAreaM2.toString() : '60.0',
    );
    _numFloorsController = TextEditingController(
      text: init != null ? init.numFloors.toString() : '1',
    );

    _housingType = init?.housingType ?? HousingType.house;
    _latitude = init?.latitude ?? 10.7769;
    _longitude = init?.longitude ?? 106.7009;
    _isDefault = init?.isDefault ?? false;

    _floorAreaController.addListener(_onAreaChanged);
    _numFloorsController.addListener(_onAreaChanged);
  }

  @override
  void dispose() {
    _floorAreaController.removeListener(_onAreaChanged);
    _numFloorsController.removeListener(_onAreaChanged);
    _labelController.dispose();
    _addressLineController.dispose();
    _floorAreaController.dispose();
    _numFloorsController.dispose();
    super.dispose();
  }

  void _onAreaChanged() {
    setState(() {});
  }

  double get _currentFloorArea {
    return double.tryParse(_floorAreaController.text) ?? 0.0;
  }

  int get _currentNumFloors {
    if (_housingType == HousingType.apartment ||
        _housingType == HousingType.room) {
      return 1;
    }
    return int.tryParse(_numFloorsController.text) ?? 1;
  }

  double get _previewTotalArea {
    return CustomerAddress.calculateTotalArea(
      _currentFloorArea,
      _currentNumFloors,
    );
  }

  void _onHousingTypeSelected(HousingType type) {
    setState(() {
      _housingType = type;
      if (type == HousingType.apartment || type == HousingType.room) {
        _numFloorsController.text = '1';
      }
      if (type == HousingType.room && _currentFloorArea > 30.0) {
        _floorAreaController.text = '30.0';
      }
    });
  }

  Future<void> _handleSubmit() async {
    if (!_formKey.currentState!.validate()) {
      setState(() => _formErrors++);
      return;
    }

    setState(() {
      _isSubmitting = true;
      _generalError = null;
    });

    try {
      final input = CustomerAddress(
        addressId: widget.initialAddress?.addressId ?? 0,
        label: _labelController.text.trim(),
        addressLine: _addressLineController.text.trim(),
        district: _ward!,
        city: _city!,
        housingType: _housingType,
        floorAreaM2: _currentFloorArea,
        numFloors: _currentNumFloors,
        totalAreaM2: _previewTotalArea,
        latitude: _latitude,
        longitude: _longitude,
        isDefault: _isDefault,
      );

      if (_isEditing) {
        await _service.updateAddress(widget.initialAddress!.addressId, input);
      } else {
        await _service.createAddress(input);
      }

      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(_isEditing ? 'Đã lưu địa chỉ' : 'Đã thêm địa chỉ'),
          backgroundColor: NordicColors.primary,
        ),
      );
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isSubmitting = false;
        _generalError = e
            .toString()
            .replaceAll('Exception: ', '')
            .replaceAll('ArgumentError: ', '');
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    // Fields, housing type, area, position; "Lưu địa chỉ" in the action bar above the keyboard.
    return NordicPage.pushed(
      title: _isEditing ? 'Sửa địa chỉ' : 'Thêm địa chỉ',
      actions: [
        NordicButton(
          label: 'Lưu địa chỉ',
          isLoading: _isSubmitting,
          onPressed: _handleSubmit,
        ),
      ],
      children: [
        // One child: every field stays built, so the form validates all of them.
        Form(
          key: _formKey,
          child: ErrorShake(
            errors: _formErrors,
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                if (_generalError != null) ...[
                  Container(
                    padding: const EdgeInsets.all(NordicSpace.sm),
                    decoration: const BoxDecoration(
                      color: NordicColors.errorContainer,
                      borderRadius: NordicRadius.tileAll,
                    ),
                    child: Text(
                      _generalError!,
                      style: NordicTypography.bodySmall.copyWith(
                        color: NordicColors.error,
                      ),
                    ),
                  ),
                  const SizedBox(height: NordicSpace.md),
                ],
                TextFormField(
                  controller: _labelController,
                  textCapitalization: TextCapitalization.sentences,
                  decoration: const InputDecoration(
                    labelText: 'Tên gợi nhớ',
                    hintText: 'Ví dụ: Nhà riêng',
                  ),
                  validator: (val) {
                    if (val == null || val.trim().isEmpty) {
                      return 'Nhập tên gợi nhớ.';
                    }
                    if (val.length > 50) {
                      return 'Tên gợi nhớ tối đa 50 ký tự.';
                    }
                    return null;
                  },
                ),
                const SizedBox(height: NordicSpace.md),
                TextFormField(
                  controller: _addressLineController,
                  decoration: const InputDecoration(
                    labelText: 'Số nhà, đường',
                    hintText: 'Ví dụ: 123 Nguyễn Thị Minh Khai',
                  ),
                  validator: (val) {
                    if (val == null || val.trim().isEmpty) {
                      return 'Nhập số nhà và tên đường.';
                    }
                    if (val.length > 255) {
                      return 'Địa chỉ tối đa 255 ký tự.';
                    }
                    return null;
                  },
                ),
                const SizedBox(height: NordicSpace.md),
                AdminUnitPickerField(
                  label: 'Tỉnh / Thành phố',
                  value: _city,
                  options: vietnamAdminUnits.keys.toList(),
                  requiredMessage: 'Chọn tỉnh / thành phố.',
                  notInListMessage:
                      'Chọn lại tỉnh / thành phố theo danh mục mới.',
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

                _section('Loại nhà'),
                Wrap(
                  spacing: NordicSpace.xs,
                  runSpacing: NordicSpace.xs,
                  children: HousingType.values.map((type) {
                    return ChoiceChip(
                      label: Text(type.displayName),
                      selected: _housingType == type,
                      showCheckmark: false,
                      onSelected: (_) => _onHousingTypeSelected(type),
                    );
                  }).toList(),
                ),

                _section('Diện tích'),
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Expanded(
                      child: TextFormField(
                        controller: _floorAreaController,
                        keyboardType: const TextInputType.numberWithOptions(
                          decimal: true,
                        ),
                        decoration: InputDecoration(
                          labelText: 'Mỗi sàn (m²)',
                          helperText: _housingType == HousingType.room
                              ? 'Phòng trọ tối đa 30 m²'
                              : 'Diện tích một sàn',
                        ),
                        validator: (val) {
                          final d = double.tryParse(val ?? '');
                          if (d == null || d <= 0) {
                            return 'Nhập số lớn hơn 0.';
                          }
                          if (d > 9999.99) return 'Tối đa 9999,99 m².';
                          if (_housingType == HousingType.room && d > 30.0) {
                            return 'Phòng trọ tối đa 30 m².';
                          }
                          return null;
                        },
                      ),
                    ),
                    const SizedBox(width: NordicSpace.sm),
                    Expanded(
                      child: TextFormField(
                        controller: _numFloorsController,
                        keyboardType: TextInputType.number,
                        enabled: _housingType == HousingType.house,
                        decoration: InputDecoration(
                          labelText: 'Số tầng',
                          helperText: _housingType != HousingType.house
                              ? 'Cố định 1 tầng'
                              : 'Từ 1 đến 10 tầng',
                        ),
                        validator: (val) {
                          if (_housingType != HousingType.house) {
                            return null;
                          }
                          final f = int.tryParse(val ?? '');
                          if (f == null || f < 1 || f > 10) {
                            return 'Từ 1 đến 10 tầng.';
                          }
                          return null;
                        },
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: NordicSpace.sm),
                AreaCalculatorCard(
                  floorAreaM2: _currentFloorArea,
                  numFloors: _currentNumFloors,
                  totalAreaM2: _previewTotalArea,
                ),

                _section('Vị trí trên bản đồ'),
                NordicCard(
                  child: GpsCoordinatePicker(
                    latitude: _latitude,
                    longitude: _longitude,
                    onCoordinatesChanged: (lat, lng) {
                      setState(() {
                        _latitude = lat;
                        _longitude = lng;
                      });
                    },
                  ),
                ),
                const SizedBox(height: NordicSpace.md),
                Material(
                  color: NordicColors.surface,
                  clipBehavior: Clip.antiAlias,
                  shape: const RoundedRectangleBorder(
                    borderRadius: NordicRadius.cardAll,
                    side: BorderSide(color: NordicColors.border),
                  ),
                  child: SwitchListTile(
                    title: const Text(
                      'Dùng làm địa chỉ mặc định',
                      style: NordicTypography.titleSmall,
                    ),
                    value: _isDefault,
                    activeThumbColor: Colors.white,
                    activeTrackColor: NordicColors.primary,
                    onChanged: (val) => setState(() => _isDefault = val),
                  ),
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _section(String text) => Padding(
    padding: const EdgeInsets.only(top: NordicSpace.lg, bottom: NordicSpace.xs),
    child: Text(text, style: NordicTypography.titleSmall),
  );
}
