import 'package:flutter/material.dart';

import '../../../core/device/device_location.dart';
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
import '../../../core/widgets/status_state_widget.dart';
import '../../customers/models/vn_admin_units.dart';
import '../../customers/widgets/admin_unit_picker_field.dart';
import '../models/worker_work_area_models.dart';
import '../services/workers_work_area_service.dart';

/// "Nơi làm việc": where the freelancer works (contract workers.md §2.3B, decisions D24). The base point comes from the
/// phone's GPS, the radius is 5, 10 or 15 km and the wards / communes are shown to the worker only. The dispatch search
/// offers a job only to a freelancer who registered it.
/// MOB-WORKER-AREA-01 (#556): `NordicPage.pushed`, the save button in `actions`, a wrong entry shakes its message.
class WorkerWorkAreaScreen extends StatefulWidget {
  final WorkersWorkAreaService? service;

  /// The phone's position; replaceable in tests.
  final IDeviceLocationProvider? locationProvider;

  const WorkerWorkAreaScreen({super.key, this.service, this.locationProvider});

  @override
  State<WorkerWorkAreaScreen> createState() => _WorkerWorkAreaScreenState();
}

class _WorkerWorkAreaScreenState extends State<WorkerWorkAreaScreen> {
  static const radii = [5, 10, 15];
  static const maxDistricts = 10;
  static const defaultProvince = 'Thành phố Hồ Chí Minh';

  late final WorkersWorkAreaService _service;
  late final IDeviceLocationProvider _location;

  bool _loading = true;
  String? _loadError;
  bool _saving = false;
  bool _locating = false;

  double? _latitude;
  double? _longitude;
  int _radiusKm = 10;
  List<String> _districts = [];

  String? _message;

  /// Bumped on every wrong entry so the message shakes.
  int _errors = 0;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? WorkersWorkAreaService();
    _location = widget.locationProvider ?? const GeolocatorLocationProvider();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _loadError = null;
    });
    try {
      final response = await _service.getWorkArea();
      final area = response.data ?? const WorkerWorkArea.empty();
      if (!mounted) return;
      setState(() {
        _loading = false;
        if (area.isSet) {
          _latitude = area.latitude;
          _longitude = area.longitude;
          _radiusKm = radii.contains(area.radiusKm) ? area.radiusKm! : 10;
          _districts = List.of(area.districts);
        }
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _loadError = 'Chưa tải được nơi làm việc của bạn.';
      });
    }
  }

  void _fail(String message) {
    setState(() {
      _message = message;
      _errors++;
    });
  }

  static String _locationFailure(
    DeviceLocationFailure failure,
  ) => switch (failure) {
    DeviceLocationFailure.serviceDisabled =>
      'Vị trí trên điện thoại đang tắt. Hãy bật vị trí rồi thử lại.',
    DeviceLocationFailure.permissionDenied =>
      'Bạn chưa cho phép ứng dụng dùng vị trí. Hãy cho phép rồi thử lại.',
    DeviceLocationFailure.permissionDeniedForever =>
      'Ứng dụng bị chặn dùng vị trí. Hãy bật lại trong Cài đặt của điện thoại.',
    DeviceLocationFailure.timeout =>
      'Chưa lấy được vị trí. Hãy ra chỗ thoáng và thử lại.',
    DeviceLocationFailure.failed => 'Chưa lấy được vị trí. Vui lòng thử lại.',
  };

  Future<void> _useCurrentLocation() async {
    setState(() {
      _locating = true;
      _message = null;
    });
    try {
      final point = await _location.currentPosition();
      if (!mounted) return;
      setState(() {
        _locating = false;
        _latitude = point.latitude;
        _longitude = point.longitude;
      });
    } on DeviceLocationException catch (e) {
      if (!mounted) return;
      setState(() => _locating = false);
      _fail(_locationFailure(e.failure));
    } catch (_) {
      if (!mounted) return;
      setState(() => _locating = false);
      _fail(_locationFailure(DeviceLocationFailure.failed));
    }
  }

  Future<void> _save() async {
    if (_latitude == null || _longitude == null) {
      _fail('Bạn chưa chọn vị trí. Bấm "Dùng vị trí hiện tại" trước khi lưu.');
      return;
    }
    setState(() {
      _saving = true;
      _message = null;
    });
    try {
      await _service.saveWorkArea(
        SaveWorkAreaRequest(
          latitude: _latitude!,
          longitude: _longitude!,
          radiusKm: _radiusKm,
          districts: _districts,
        ),
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('Đã lưu nơi làm việc.')));
      Navigator.of(context).pop(true);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _saving = false);
      // The server's own words, the first refused field when there is one.
      final fields = e.fieldErrors;
      final first = fields == null || fields.isEmpty
          ? null
          : fields.values.expand((v) => v).firstOrNull;
      _fail(first ?? e.message);
    } catch (_) {
      if (!mounted) return;
      setState(() => _saving = false);
      _fail('Chưa lưu được nơi làm việc. Vui lòng thử lại.');
    }
  }

  Future<void> _pickDistricts() async {
    final picked = await showModalBottomSheet<List<String>>(
      context: context,
      isScrollControlled: true,
      backgroundColor: NordicColors.surface,
      builder: (context) =>
          _DistrictsSheet(selected: _districts, maxCount: maxDistricts),
    );
    if (picked != null && mounted) setState(() => _districts = picked);
  }

  @override
  Widget build(BuildContext context) {
    return NordicPage.pushed(
      title: 'Nơi làm việc',
      actions: _loading || _loadError != null
          ? null
          : [
              NordicButton(
                key: const Key('save-work-area'),
                label: 'Lưu nơi làm việc',
                isLoading: _saving,
                onPressed: _saving ? null : _save,
              ),
            ],
      children: _loading
          ? const [
              SizedBox(height: NordicSpace.xl),
              LoadingStateWidget(message: 'Đang tải nơi làm việc...'),
            ]
          : _loadError != null
          ? [ErrorStateWidget(message: _loadError!, onRetry: _load)]
          : _form(),
    );
  }

  List<Widget> _form() {
    final hasPoint = _latitude != null && _longitude != null;
    return [
      const Text(
        'Cho biết bạn nhận việc quanh đâu. Bạn chỉ được mời việc trong khu vực này.',
        style: NordicTypography.bodyRegular,
      ),
      const SizedBox(height: NordicSpace.md),

      // The base point
      NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Icon(
                  hasPoint ? Icons.location_on : Icons.location_off_outlined,
                  color: hasPoint
                      ? NordicColors.primary
                      : NordicColors.textSecondary,
                ),
                const SizedBox(width: NordicSpace.xs),
                Expanded(
                  child: Text(
                    hasPoint
                        ? 'Điểm làm việc: ${_latitude!.toStringAsFixed(4)}, ${_longitude!.toStringAsFixed(4)}'
                        : 'Chưa chọn vị trí',
                    key: const Key('work-area-point'),
                    style: NordicTypography.titleSmall,
                  ),
                ),
              ],
            ),
            const SizedBox(height: NordicSpace.sm),
            NordicButton(
              key: const Key('use-current-location'),
              label: hasPoint
                  ? 'Lấy lại vị trí hiện tại'
                  : 'Dùng vị trí hiện tại',
              variant: NordicButtonVariant.secondary,
              icon: const Icon(Icons.my_location, size: 18.0),
              isLoading: _locating,
              onPressed: _locating ? null : _useCurrentLocation,
            ),
          ],
        ),
      ),
      const SizedBox(height: NordicSpace.lg),

      // The radius
      const Text('Bán kính nhận việc', style: NordicTypography.titleSmall),
      const SizedBox(height: NordicSpace.xs),
      Row(
        children: [
          for (final km in radii) ...[
            if (km != radii.first) const SizedBox(width: NordicSpace.xs),
            Expanded(child: _radiusPill(km)),
          ],
        ],
      ),
      const SizedBox(height: NordicSpace.lg),

      // The wards / communes shown to the worker
      const Text('Phường/xã hay làm', style: NordicTypography.titleSmall),
      const SizedBox(height: NordicSpace.xxs),
      Text(
        'Chỉ để bạn xem lại, tối đa $maxDistricts nơi.',
        style: NordicTypography.bodySmall.copyWith(
          color: NordicColors.textSecondary,
        ),
      ),
      const SizedBox(height: NordicSpace.xs),
      if (_districts.isNotEmpty)
        Wrap(
          spacing: NordicSpace.xs,
          runSpacing: NordicSpace.xs,
          children: [
            for (final name in _districts)
              InputChip(
                key: Key('district-chip-$name'),
                label: Text(name),
                onDeleted: () => setState(() => _districts.remove(name)),
              ),
          ],
        ),
      const SizedBox(height: NordicSpace.xs),
      NordicButton(
        key: const Key('pick-districts'),
        label: _districts.isEmpty ? 'Chọn phường/xã' : 'Thêm hoặc bỏ phường/xã',
        variant: NordicButtonVariant.secondary,
        icon: const Icon(Icons.add_location_alt_outlined, size: 18.0),
        onPressed: _pickDistricts,
      ),

      ErrorShake(
        errors: _errors,
        child: _message == null
            ? const SizedBox.shrink()
            : Padding(
                padding: const EdgeInsets.only(top: NordicSpace.md),
                child: Container(
                  width: double.infinity,
                  padding: const EdgeInsets.all(NordicSpace.sm),
                  decoration: const BoxDecoration(
                    color: NordicColors.errorContainer,
                    borderRadius: NordicRadius.tileAll,
                  ),
                  child: Text(
                    _message!,
                    key: const Key('work-area-error'),
                    style: NordicTypography.bodySmall.copyWith(
                      color: NordicColors.error,
                    ),
                  ),
                ),
              ),
      ),
    ];
  }

  Widget _radiusPill(int km) {
    final on = _radiusKm == km;
    return Pressable(
      child: InkWell(
        key: Key('radius-$km'),
        borderRadius: NordicRadius.pill,
        onTap: () => setState(() => _radiusKm = km),
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: NordicSpace.sm),
          alignment: Alignment.center,
          decoration: BoxDecoration(
            color: on ? NordicColors.primary : NordicColors.surface,
            borderRadius: NordicRadius.pill,
            border: Border.all(
              color: on ? NordicColors.primary : NordicColors.border,
            ),
          ),
          child: Text(
            '$km km',
            style: NordicTypography.titleSmall.copyWith(
              color: on ? NordicColors.textInverse : NordicColors.textTitle,
            ),
          ),
        ),
      ),
    );
  }
}

/// A searchable list of the wards / communes of one province with a tick for each, at most [maxCount] in all.
class _DistrictsSheet extends StatefulWidget {
  final List<String> selected;
  final int maxCount;

  const _DistrictsSheet({required this.selected, required this.maxCount});

  @override
  State<_DistrictsSheet> createState() => _DistrictsSheetState();
}

class _DistrictsSheetState extends State<_DistrictsSheet> {
  late final List<String> _picked = List.of(widget.selected);
  String _province = _WorkerWorkAreaScreenState.defaultProvince;
  String _query = '';
  String? _limitMessage;

  @override
  Widget build(BuildContext context) {
    final options = vietnamAdminUnits[_province] ?? const <String>[];
    final matches = options
        .where((name) => matchesAdminUnitQuery(name, _query))
        .toList();

    return SafeArea(
      child: Padding(
        padding: EdgeInsets.only(
          bottom: MediaQuery.of(context).viewInsets.bottom,
        ),
        child: SizedBox(
          height: MediaQuery.of(context).size.height * 0.8,
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(
                  NordicSpace.gutter,
                  NordicSpace.md,
                  NordicSpace.gutter,
                  NordicSpace.xs,
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      'Chọn phường/xã (${_picked.length}/${widget.maxCount})',
                      style: NordicTypography.titleMedium,
                    ),
                    const SizedBox(height: NordicSpace.sm),
                    DropdownButtonFormField<String>(
                      key: const Key('district-province'),
                      initialValue: _province,
                      isExpanded: true,
                      decoration: const InputDecoration(
                        labelText: 'Tỉnh/thành',
                      ),
                      items: [
                        for (final province in vietnamAdminUnits.keys)
                          DropdownMenuItem(
                            value: province,
                            child: Text(
                              province,
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                      ],
                      onChanged: (value) {
                        if (value != null) setState(() => _province = value);
                      },
                    ),
                    const SizedBox(height: NordicSpace.xs),
                    TextField(
                      key: const Key('district-search'),
                      decoration: const InputDecoration(
                        labelText: 'Tìm phường/xã',
                        prefixIcon: Icon(Icons.search),
                      ),
                      onChanged: (value) => setState(() => _query = value),
                    ),
                    if (_limitMessage != null)
                      Padding(
                        padding: const EdgeInsets.only(top: NordicSpace.xs),
                        child: Text(
                          _limitMessage!,
                          style: NordicTypography.bodySmall.copyWith(
                            color: NordicColors.error,
                          ),
                        ),
                      ),
                  ],
                ),
              ),
              Expanded(
                child: ListView.builder(
                  itemCount: matches.length,
                  itemBuilder: (context, index) {
                    final name = matches[index];
                    final on = _picked.contains(name);
                    return CheckboxListTile(
                      key: Key('district-option-$name'),
                      value: on,
                      title: Text(name),
                      onChanged: (value) => setState(() {
                        if (value == true) {
                          if (_picked.length >= widget.maxCount) {
                            _limitMessage =
                                'Chọn tối đa ${widget.maxCount} phường/xã.';
                            return;
                          }
                          _picked.add(name);
                        } else {
                          _picked.remove(name);
                        }
                        _limitMessage = null;
                      }),
                    );
                  },
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(
                  NordicSpace.gutter,
                  NordicSpace.xs,
                  NordicSpace.gutter,
                  NordicSpace.md,
                ),
                child: NordicButton(
                  key: const Key('district-done'),
                  label: 'Xong',
                  onPressed: () => Navigator.of(context).pop(_picked),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
