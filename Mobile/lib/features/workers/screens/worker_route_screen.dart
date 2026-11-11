import 'package:flutter/material.dart';

import '../../../core/device/device_location.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_pill.dart';
import '../models/worker_assignment_models.dart';
import '../models/worker_route_models.dart';
import '../services/workers_assignments_service.dart';
import '../services/workers_route_service.dart';

/// The way to the customer's house (ROUTE-MOB-01, #760, `Mobile/DESIGN-SYSTEM-WORKER.md`): up to 3 routes from the worker's
/// position, the fastest first (Valhalla, motorbike), each with distance and travel time. The worker picks one to compare and
/// opens the phone's map app with `Chỉ đường`; the map app picks its own road, and the screen says so.
///
/// Only an accepted job is readable (the server answers 404 for anything else), so the address is not shown earlier.
/// A position that cannot be read, a routing engine that is down and a job without coordinates each show a message, and
/// `Chỉ đường` stays available whenever the destination (point or written address) is known.
class WorkerRouteScreen extends StatefulWidget {
  final int assignmentId;
  final WorkersAssignmentsService? assignmentsService;
  final WorkersRouteService? routeService;
  final IDeviceLocationProvider? locationProvider;
  final IMapLauncher? mapLauncher;

  const WorkerRouteScreen({
    super.key,
    required this.assignmentId,
    this.assignmentsService,
    this.routeService,
    this.locationProvider,
    this.mapLauncher,
  });

  @override
  State<WorkerRouteScreen> createState() => _WorkerRouteScreenState();
}

class _WorkerRouteScreenState extends State<WorkerRouteScreen> {
  late final WorkersAssignmentsService _assignments;
  late final WorkersRouteService _routes;
  late final IDeviceLocationProvider _location;
  late final IMapLauncher _launcher;

  WorkerAssignmentDetailDto? _job;
  List<WorkerRouteOptionDto> _options = const [];
  int _selected = 0;
  bool _loading = true;

  /// Why the job itself could not be read; the whole screen is then only this message.
  String? _jobError;

  /// Why the routes are missing (position, routing engine); the job and `Chỉ đường` stay.
  String? _routeNote;
  String? _launchError;

  @override
  void initState() {
    super.initState();
    _assignments = widget.assignmentsService ?? WorkersAssignmentsService();
    _routes = widget.routeService ?? WorkersRouteService();
    _location = widget.locationProvider ?? const GeolocatorLocationProvider();
    _launcher = widget.mapLauncher ?? const UrlMapLauncher();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _jobError = null;
      _routeNote = null;
      _launchError = null;
    });
    try {
      final response = await _assignments.getAssignment(widget.assignmentId);
      _job = response.data;
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _jobError = e.statusCode == 404
            ? 'Không tìm thấy ca làm này, hoặc bạn chưa nhận ca.'
            : 'Chưa tải được ca làm. Vui lòng thử lại.';
      });
      return;
    }
    if (_job == null) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _jobError = 'Chưa tải được ca làm. Vui lòng thử lại.';
      });
      return;
    }

    final job = _job!;
    if (job.latitude == 0.0 && job.longitude == 0.0) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _routeNote = 'Đơn này chưa có toạ độ nên chưa tính được lộ trình.';
      });
      return;
    }

    try {
      final from = await _location.currentPosition();
      final response = await _routes.getRoutes(
        widget.assignmentId,
        fromLat: from.latitude,
        fromLng: from.longitude,
      );
      if (!mounted) return;
      setState(() {
        _loading = false;
        _options = response.data?.routes ?? const [];
        _selected = 0;
        if (_options.isEmpty) _routeNote = 'Chưa tính được lộ trình.';
      });
    } on DeviceLocationException catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _routeNote = deviceLocationFailureText(e.failure);
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _routeNote = switch (e.statusCode) {
          409 => 'Ca này đã bắt đầu nên không còn lộ trình.',
          502 => 'Chưa tính được lộ trình. Bạn vẫn có thể bấm Chỉ đường.',
          _ => 'Chưa tính được lộ trình. Vui lòng thử lại.',
        };
      });
    }
  }

  Future<void> _openMap() async {
    final job = _job;
    if (job == null) return;
    final uri = mapDirectionsUri(
      latitude: job.latitude,
      longitude: job.longitude,
      address: job.fullAddress,
    );
    if (uri == null) {
      setState(() => _launchError = 'Ca này chưa có địa chỉ để chỉ đường.');
      return;
    }
    final ok = await _launcher.open(uri);
    if (!mounted) return;
    setState(
      () => _launchError = ok ? null : 'Không mở được ứng dụng bản đồ. Hãy cài một ứng dụng bản đồ rồi thử lại.',
    );
  }

  @override
  Widget build(BuildContext context) {
    final job = _job;
    final canNavigate =
        job != null &&
        (job.latitude != 0.0 ||
            job.longitude != 0.0 ||
            job.fullAddress.trim().isNotEmpty);

    return NordicPage.pushed(
      title: 'Đường đến nhà khách',
      actions: [
        if (canNavigate)
          NordicButton(
            key: const Key('route-navigate-button'),
            label: 'Chỉ đường',
            variant: NordicButtonVariant.primary,
            icon: const Icon(Icons.navigation_outlined, size: 18.0),
            onPressed: _openMap,
          ),
      ],
      children: [
        if (_loading)
          const Center(
            child: Padding(
              padding: EdgeInsets.all(NordicSpace.lg),
              child: CircularProgressIndicator(key: Key('route-loading')),
            ),
          )
        else if (_jobError != null) ...[
          Text(
            _jobError!,
            key: const Key('route-job-error'),
            style: NordicTypography.bodyRegular.copyWith(
              color: NordicColors.error,
            ),
          ),
          const SizedBox(height: NordicSpace.md),
          NordicButton(
            key: const Key('route-retry-button'),
            label: 'Thử lại',
            variant: NordicButtonVariant.secondary,
            onPressed: _load,
          ),
        ] else ...[
          if (job != null) _addressCard(job),
          const SizedBox(height: NordicSpace.md),
          if (_routeNote != null)
            Text(
              _routeNote!,
              key: const Key('route-note'),
              style: NordicTypography.bodyRegular.copyWith(
                color: NordicColors.textSecondary,
              ),
            ),
          for (var i = 0; i < _options.length; i++) _optionTile(i),
          if (_options.isNotEmpty) ...[
            const SizedBox(height: NordicSpace.xs),
            Text(
              'Ứng dụng bản đồ sẽ tự chọn đường đi. Thời gian ở đây là ước tính, chưa tính tắc đường.',
              key: const Key('route-disclaimer'),
              style: NordicTypography.bodySmall,
            ),
          ],
          if (_launchError != null) ...[
            const SizedBox(height: NordicSpace.sm),
            Text(
              _launchError!,
              key: const Key('route-launch-error'),
              style: NordicTypography.bodyRegular.copyWith(
                color: NordicColors.error,
              ),
            ),
          ],
          if (_routeNote != null) ...[
            const SizedBox(height: NordicSpace.sm),
            NordicButton(
              key: const Key('route-retry-button'),
              label: 'Tính lại lộ trình',
              variant: NordicButtonVariant.secondary,
              onPressed: _load,
            ),
          ],
        ],
      ],
    );
  }

  Widget _addressCard(WorkerAssignmentDetailDto job) => NordicCard(
    key: const Key('route-address-card'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text('Địa chỉ nhà khách', style: NordicTypography.bodySmall),
        const SizedBox(height: NordicSpace.xxs),
        Text(
          job.fullAddress.isEmpty ? 'Chưa có địa chỉ' : job.fullAddress,
          style: NordicTypography.titleSmall,
        ),
      ],
    ),
  );

  Widget _optionTile(int index) {
    final option = _options[index];
    final selected = index == _selected;
    return Padding(
      padding: const EdgeInsets.only(bottom: NordicSpace.xs),
      child: NordicCard(
        key: Key('route-option-$index'),
        isHighlighted: selected,
        onTap: () => setState(() => _selected = index),
        child: Row(
          children: [
            Expanded(
              child: Text(
                routeOptionLabel(index, option),
                style: NordicTypography.titleSmall,
              ),
            ),
            if (index == 0)
              const StatusPill('Nhanh nhất', tone: PillTone.ok)
            else if (selected)
              const StatusPill('Đang chọn'),
          ],
        ),
      ),
    );
  }
}
