import 'package:flutter/material.dart';
import 'package:flutter_map/flutter_map.dart';
import 'package:latlong2/latlong.dart';

import '../../../core/device/device_location.dart';
import '../../../core/device/location_wrapper.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_page.dart';
import '../models/worker_assignment_models.dart';
import '../models/worker_route_models.dart';
import '../services/workers_assignments_service.dart';
import '../services/workers_route_service.dart';

/// The way to the customer's house on a map (ROUTE-MOB-01, #760, `Mobile/DESIGN-SYSTEM-WORKER.md`): up to 3 routes from the
/// worker's position drawn on OpenStreetMap tiles (Valhalla, motorbike), the chosen one in the primary colour. A slim bar over
/// the map picks the route and `Chỉ đường` opens the phone's map app; that app chooses its own road, and the bar says so.
///
/// Only an accepted job is readable (the server answers 404 for anything else), so the address is not shown earlier.
/// A position that cannot be read, a routing engine that is down and a job without coordinates each show a message over the
/// map (the house stays on it when its point is known), and `Chỉ đường` stays available whenever the destination is known.
class WorkerRouteScreen extends StatefulWidget {
  final int assignmentId;
  final WorkersAssignmentsService? assignmentsService;
  final WorkersRouteService? routeService;
  final IDeviceLocationProvider? locationProvider;
  final IMapLauncher? mapLauncher;

  /// False in tests: no tile is requested from the network.
  final bool loadTiles;

  const WorkerRouteScreen({
    super.key,
    required this.assignmentId,
    this.assignmentsService,
    this.routeService,
    this.locationProvider,
    this.mapLauncher,
    this.loadTiles = true,
  });

  @override
  State<WorkerRouteScreen> createState() => _WorkerRouteScreenState();
}

class _WorkerRouteScreenState extends State<WorkerRouteScreen> {
  late final WorkersAssignmentsService _assignments;
  late final WorkersRouteService _routes;
  late final IDeviceLocationProvider _location;
  late final IMapLauncher _launcher;
  final MapController _map = MapController();

  WorkerAssignmentDetailDto? _job;
  GeoPoint? _from;
  List<WorkerRouteOptionDto> _options = const [];
  List<List<LatLng>> _lines = const [];
  int _selected = 0;
  bool _loading = true;
  bool _mapReady = false;

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

  @override
  void dispose() {
    _map.dispose();
    super.dispose();
  }

  bool get _hasPoint =>
      _job != null && (_job!.latitude != 0.0 || _job!.longitude != 0.0);

  LatLng get _house => LatLng(_job!.latitude, _job!.longitude);

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _jobError = null;
      _routeNote = null;
      _launchError = null;
      _options = const [];
      _lines = const [];
      _from = null;
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

    if (!_hasPoint) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _routeNote = 'Đơn này chưa có toạ độ nên chưa tính được lộ trình.';
      });
      return;
    }

    try {
      final from = await _location.currentPosition();
      _from = from;
      final response = await _routes.getRoutes(
        widget.assignmentId,
        fromLat: from.latitude,
        fromLng: from.longitude,
      );
      if (!mounted) return;
      final options = response.data?.routes ?? const <WorkerRouteOptionDto>[];
      setState(() {
        _loading = false;
        _options = options;
        _lines = [
          for (final o in options)
            [for (final p in decodePolyline6(o.polyline)) LatLng(p.$1, p.$2)],
        ];
        _selected = 0;
        if (options.isEmpty) _routeNote = 'Chưa tính được lộ trình.';
      });
      _fitCamera();
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

  /// Zooms the map to all the routes (or to the house alone).
  void _fitCamera() {
    if (!_mapReady) return;
    final points = [
      for (final line in _lines) ...line,
      if (_from != null) LatLng(_from!.latitude, _from!.longitude),
      if (_hasPoint) _house,
    ];
    if (points.length < 2) return;
    _map.fitCamera(
      CameraFit.bounds(
        bounds: LatLngBounds.fromPoints(points),
        padding: const EdgeInsets.fromLTRB(48, 96, 48, 200),
      ),
    );
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
    return NordicPage.pushed(
      title: 'Đường đến nhà khách',
      bodyOverride: _body(),
      children: const [],
    );
  }

  Widget _body() {
    if (_loading) {
      return const Center(
        child: CircularProgressIndicator(key: Key('route-loading')),
      );
    }
    if (_jobError != null) {
      return Padding(
        padding: const EdgeInsets.all(NordicSpace.gutter),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
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
          ],
        ),
      );
    }

    final job = _job!;
    final canNavigate = _hasPoint || job.fullAddress.trim().isNotEmpty;
    return Stack(
      children: [
        Positioned.fill(child: _hasPoint ? _mapView() : const SizedBox()),
        if (_routeNote != null)
          Positioned(
            left: NordicSpace.md,
            right: NordicSpace.md,
            top: NordicSpace.md,
            child: _noteCard(),
          ),
        Positioned(
          left: 0,
          right: 0,
          bottom: 0,
          child: _bottomBar(canNavigate),
        ),
      ],
    );
  }

  Widget _mapView() {
    final house = _house;
    final from = _from;
    return FlutterMap(
      key: const Key('route-map'),
      mapController: _map,
      options: MapOptions(
        initialCenter: house,
        initialZoom: 14,
        onMapReady: () {
          _mapReady = true;
          _fitCamera();
        },
      ),
      children: [
        if (widget.loadTiles)
          TileLayer(
            urlTemplate: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            userAgentPackageName: 'com.example.mobile',
          ),
        PolylineLayer(
          polylines: [
            for (var i = 0; i < _lines.length; i++)
              if (i != _selected && _lines[i].length > 1)
                Polyline(
                  points: _lines[i],
                  strokeWidth: 4,
                  color: NordicColors.textSecondary.withValues(alpha: 0.6),
                ),
            if (_lines.length > _selected && _lines[_selected].length > 1)
              Polyline(
                points: _lines[_selected],
                strokeWidth: 6,
                color: NordicColors.primary,
              ),
          ],
        ),
        MarkerLayer(
          markers: [
            Marker(
              point: house,
              width: 40,
              height: 40,
              child: _pin(Icons.home_rounded, NordicColors.error),
            ),
            if (from != null)
              Marker(
                point: LatLng(from.latitude, from.longitude),
                width: 40,
                height: 40,
                child: _pin(Icons.two_wheeler, NordicColors.primary),
              ),
          ],
        ),
        const RichAttributionWidget(
          attributions: [TextSourceAttribution('© OpenStreetMap contributors')],
        ),
      ],
    );
  }

  Widget _pin(IconData icon, Color color) => DecoratedBox(
    decoration: BoxDecoration(
      color: color,
      shape: BoxShape.circle,
      border: Border.all(color: NordicColors.surface, width: 2),
    ),
    child: Icon(icon, color: NordicColors.textInverse, size: 22),
  );

  Widget _noteCard() => Material(
    color: NordicColors.surface,
    elevation: 2,
    borderRadius: NordicRadius.cardAll,
    child: Padding(
      padding: const EdgeInsets.all(NordicSpace.sm),
      child: Row(
        children: [
          Expanded(
            child: Text(
              _routeNote!,
              key: const Key('route-note'),
              style: NordicTypography.bodyRegular,
            ),
          ),
          TextButton(
            key: const Key('route-retry-button'),
            onPressed: _load,
            child: const Text('Thử lại'),
          ),
        ],
      ),
    ),
  );

  Widget _bottomBar(bool canNavigate) => Material(
    color: NordicColors.surface,
    elevation: 8,
    borderRadius: NordicRadius.sheetTop,
    child: SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(
          NordicSpace.gutter,
          NordicSpace.sm,
          NordicSpace.gutter,
          NordicSpace.sm,
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (_options.isNotEmpty) ...[
              Wrap(
                spacing: NordicSpace.xs,
                runSpacing: NordicSpace.xs,
                children: [for (var i = 0; i < _options.length; i++) _chip(i)],
              ),
              if (_options.length > 1) ...[
                const SizedBox(height: NordicSpace.xxs),
                const Text(
                  'Ứng dụng bản đồ sẽ tự chọn đường đi.',
                  key: Key('route-disclaimer'),
                  style: NordicTypography.bodySmall,
                ),
              ],
              const SizedBox(height: NordicSpace.xs),
            ],
            if (_launchError != null) ...[
              Text(
                _launchError!,
                key: const Key('route-launch-error'),
                style: NordicTypography.bodyRegular.copyWith(
                  color: NordicColors.error,
                ),
              ),
              const SizedBox(height: NordicSpace.xs),
            ],
            if (canNavigate)
              NordicButton(
                key: const Key('route-navigate-button'),
                label: 'Chỉ đường',
                variant: NordicButtonVariant.primary,
                icon: const Icon(Icons.navigation_outlined, size: 18.0),
                onPressed: _openMap,
              ),
          ],
        ),
      ),
    ),
  );

  Widget _chip(int index) {
    final option = _options[index];
    final selected = index == _selected;
    final text =
        '${formatRouteDistance(option.distanceMeters)} · '
        '${formatRouteDuration(option.durationSeconds)}';
    return ChoiceChip(
      key: Key('route-chip-$index'),
      selected: selected,
      showCheckmark: false,
      label: Text(index == 0 ? 'Nhanh nhất · $text' : text),
      labelStyle: NordicTypography.labelMedium.copyWith(
        color: selected ? NordicColors.textInverse : NordicColors.textTitle,
      ),
      selectedColor: NordicColors.primary,
      backgroundColor: NordicColors.surfaceContainer,
      onSelected: (_) => setState(() => _selected = index),
    );
  }
}
