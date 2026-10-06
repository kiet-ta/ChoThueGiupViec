import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../models/customer_address.dart';
import '../services/customer_address_service.dart';
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
  late final TextEditingController _districtController;
  late final TextEditingController _cityController;
  late final TextEditingController _floorAreaController;
  late final TextEditingController _numFloorsController;

  late HousingType _housingType;
  late double _latitude;
  late double _longitude;
  late bool _isDefault;

  bool _isSubmitting = false;
  String? _generalError;

  bool get _isEditing => widget.initialAddress != null;

  @override
  void initState() {
    super.initState();
    _service = widget.addressService ?? CustomerAddressService();
    final init = widget.initialAddress;

    _labelController = TextEditingController(text: init?.label ?? '');
    _addressLineController = TextEditingController(text: init?.addressLine ?? '');
    _districtController = TextEditingController(text: init?.district ?? 'Quận 1');
    _cityController = TextEditingController(text: init?.city ?? 'TP. Hồ Chí Minh');
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
    _districtController.dispose();
    _cityController.dispose();
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
    if (_housingType == HousingType.apartment || _housingType == HousingType.room) {
      return 1;
    }
    return int.tryParse(_numFloorsController.text) ?? 1;
  }

  double get _previewTotalArea {
    return CustomerAddress.calculateTotalArea(_currentFloorArea, _currentNumFloors);
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
    if (!_formKey.currentState!.validate()) return;

    setState(() {
      _isSubmitting = true;
      _generalError = null;
    });

    try {
      final input = CustomerAddress(
        addressId: widget.initialAddress?.addressId ?? 0,
        label: _labelController.text.trim(),
        addressLine: _addressLineController.text.trim(),
        district: _districtController.text.trim(),
        city: _cityController.text.trim(),
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
          content: Text(
            _isEditing
                ? 'Đã cập nhật địa chỉ thành công!'
                : 'Đã lưu địa chỉ mới vào sổ địa chỉ!',
          ),
          backgroundColor: NordicColors.primary,
        ),
      );
      Navigator.pop(context, true);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _isSubmitting = false;
        _generalError = e.toString().replaceAll('Exception: ', '').replaceAll('ArgumentError: ', '');
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: Text(
          _isEditing ? 'Chỉnh Sửa Địa Chỉ' : 'Thêm Địa Chỉ Mới',
          style: NordicTypography.h3,
        ),
        centerTitle: true,
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: Form(
            key: _formKey,
            child: ListView(
              padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 12.0),
              children: [
                Align(
                  alignment: Alignment.centerLeft,
                  child: EyebrowBadge(
                    text: _isEditing ? 'CHỈNH SỬA THÔNG TIN' : 'THÔNG TIN ĐỊA CHỈ MỚI',
                  ),
                ),
                const SizedBox(height: 8.0),
                Text(
                  _isEditing ? 'Cập nhật địa chỉ' : 'Địa chỉ phục vụ',
                  style: NordicTypography.h1,
                ),
                const SizedBox(height: 4.0),
                Text(
                  'Thông tin chi tiết loại nhà và diện tích giúp hệ thống ước lượng thời gian & giá dọn dẹp chính xác.',
                  style: NordicTypography.bodyRegular,
                ),
                const SizedBox(height: 16.0),

                if (_generalError != null) ...[
                  Container(
                    padding: const EdgeInsets.all(12.0),
                    decoration: BoxDecoration(
                      color: NordicColors.errorContainer,
                      borderRadius: BorderRadius.circular(8.0),
                    ),
                    child: Text(
                      _generalError!,
                      style: NordicTypography.bodySmall.copyWith(
                        color: NordicColors.error,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                  const SizedBox(height: 16.0),
                ],

                // 1. Basic Info Card
                NordicCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Thông Tin Nhận Diện', style: NordicTypography.h3),
                      const SizedBox(height: 12.0),
                      TextFormField(
                        controller: _labelController,
                        decoration: const InputDecoration(
                          labelText: 'Tên gợi nhớ (Nhà riêng, Căn hộ, Cơ quan)*',
                          hintText: 'Ví dụ: Nhà riêng',
                          border: OutlineInputBorder(),
                        ),
                        validator: (val) {
                          if (val == null || val.trim().isEmpty) {
                            return 'Vui lòng nhập tên nhãn địa chỉ';
                          }
                          if (val.length > 50) return 'Tên nhãn tối đa 50 ký tự';
                          return null;
                        },
                      ),
                      const SizedBox(height: 12.0),
                      TextFormField(
                        controller: _addressLineController,
                        decoration: const InputDecoration(
                          labelText: 'Địa chỉ chi tiết (Số nhà, tên đường)*',
                          hintText: 'Ví dụ: 123 Nguyễn Thị Minh Khai, P. Bến Thành',
                          border: OutlineInputBorder(),
                        ),
                        validator: (val) {
                          if (val == null || val.trim().isEmpty) {
                            return 'Vui lòng nhập số nhà và tên đường';
                          }
                          if (val.length > 255) return 'Địa chỉ chi tiết tối đa 255 ký tự';
                          return null;
                        },
                      ),
                      const SizedBox(height: 12.0),
                      Row(
                        children: [
                          Expanded(
                            child: TextFormField(
                              controller: _districtController,
                              decoration: const InputDecoration(
                                labelText: 'Quận / Huyện*',
                                border: OutlineInputBorder(),
                              ),
                              validator: (val) {
                                if (val == null || val.trim().isEmpty) {
                                  return 'Nhập Quận/Huyện';
                                }
                                return null;
                              },
                            ),
                          ),
                          const SizedBox(width: 10.0),
                          Expanded(
                            child: TextFormField(
                              controller: _cityController,
                              decoration: const InputDecoration(
                                labelText: 'Tỉnh / Thành phố*',
                                border: OutlineInputBorder(),
                              ),
                              validator: (val) {
                                if (val == null || val.trim().isEmpty) {
                                  return 'Nhập Tỉnh/TP';
                                }
                                return null;
                              },
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16.0),

                // 2. Housing Type & Area Calculation Card
                NordicCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text('Loại Nhà & Diện Tích', style: NordicTypography.h3),
                      const SizedBox(height: 8.0),
                      Text(
                        'Chọn đúng phân loại nhà để áp dụng định mức diện tích quy chuẩn (PRD §1.1).',
                        style: NordicTypography.bodySmall,
                      ),
                      const SizedBox(height: 12.0),

                      // Housing Type Selector
                      Wrap(
                        spacing: 8.0,
                        runSpacing: 8.0,
                        children: HousingType.values.map((type) {
                          final isSelected = _housingType == type;
                          return ChoiceChip(
                            label: Text(type.displayName),
                            selected: isSelected,
                            selectedColor: NordicColors.primary,
                            backgroundColor: NordicColors.surfaceSubtle,
                            labelStyle: TextStyle(
                              fontSize: 13.0,
                              fontWeight: isSelected ? FontWeight.w700 : FontWeight.w500,
                              color: isSelected ? Colors.white : NordicColors.textTitle,
                            ),
                            shape: RoundedRectangleBorder(
                              borderRadius: BorderRadius.circular(8.0),
                              side: BorderSide(
                                color: isSelected ? NordicColors.primary : NordicColors.border,
                              ),
                            ),
                            onSelected: (_) => _onHousingTypeSelected(type),
                          );
                        }).toList(),
                      ),
                      const SizedBox(height: 14.0),

                      // Floor area and Floors input
                      Row(
                        children: [
                          Expanded(
                            child: TextFormField(
                              controller: _floorAreaController,
                              keyboardType: const TextInputType.numberWithOptions(decimal: true),
                              decoration: InputDecoration(
                                labelText: 'Diện tích sàn (m²)*',
                                helperText: _housingType == HousingType.room
                                    ? 'Phòng trọ tối đa 30 m²'
                                    : 'Diện tích 1 sàn',
                                border: const OutlineInputBorder(),
                              ),
                              validator: (val) {
                                final d = double.tryParse(val ?? '');
                                if (d == null || d <= 0) return 'Diện tích phải > 0';
                                if (d > 9999.99) return 'Tối đa 9999.99 m²';
                                if (_housingType == HousingType.room && d > 30.0) {
                                  return 'Phòng trọ tối đa 30 m²';
                                }
                                return null;
                              },
                            ),
                          ),
                          const SizedBox(width: 12.0),
                          Expanded(
                            child: TextFormField(
                              controller: _numFloorsController,
                              keyboardType: TextInputType.number,
                              enabled: _housingType == HousingType.house,
                              decoration: InputDecoration(
                                labelText: 'Số tầng*',
                                helperText: _housingType != HousingType.house
                                    ? 'Cố định 1 tầng'
                                    : 'Từ 1 đến 10 tầng',
                                border: const OutlineInputBorder(),
                              ),
                              validator: (val) {
                                if (_housingType != HousingType.house) return null;
                                final f = int.tryParse(val ?? '');
                                if (f == null || f < 1 || f > 10) {
                                  return 'Từ 1 đến 10 tầng';
                                }
                                return null;
                              },
                            ),
                          ),
                        ],
                      ),
                      const SizedBox(height: 16.0),

                      // Live calculation card
                      AreaCalculatorCard(
                        floorAreaM2: _currentFloorArea,
                        numFloors: _currentNumFloors,
                        totalAreaM2: _previewTotalArea,
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16.0),

                // 3. GPS Coordinates Card
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
                const SizedBox(height: 16.0),

                // 4. Default Switch Card
                NordicCard(
                  padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 8.0),
                  child: Material(
                    color: Colors.transparent,
                    child: SwitchListTile(
                      contentPadding: EdgeInsets.zero,
                      title: Text(
                        'Đặt làm địa chỉ mặc định',
                        style: NordicTypography.bodyRegular.copyWith(fontWeight: FontWeight.w600),
                      ),
                      subtitle: Text(
                        'Tự động chọn địa chỉ này khi tạo lịch đặt ca dọn dẹp mới.',
                        style: NordicTypography.bodySmall,
                      ),
                      value: _isDefault,
                      activeThumbColor: NordicColors.primary,
                      onChanged: (val) => setState(() => _isDefault = val),
                    ),
                  ),
                ),
                const SizedBox(height: 24.0),
              ],
            ),
          ),
        ),
      ),
      bottomNavigationBar: Container(
        padding: const EdgeInsets.all(16.0),
        decoration: const BoxDecoration(
          color: NordicColors.surface,
          border: Border(top: BorderSide(color: NordicColors.border)),
        ),
        child: SafeArea(
          top: false,
          child: NordicButton(
            label: _isEditing ? 'Lưu Thay Đổi' : 'Tạo Địa Chỉ Mới',
            isLoading: _isSubmitting,
            width: double.infinity,
            onPressed: _handleSubmit,
          ),
        ),
      ),
    );
  }
}
