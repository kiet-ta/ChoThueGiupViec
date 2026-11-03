import 'package:flutter/material.dart';

import '../device/location_access.dart';
import '../theme/nordic_colors.dart';
import '../theme/nordic_spacing.dart';
import '../theme/nordic_typography.dart';
import 'nordic_button.dart';
import 'status_state_widget.dart';

/// The Worker app cannot be used without location (owner decision, #442 (c)): [child] is built only while the phone's location is on and the
/// permission is granted. Every other state is a full page with one button and no way past it. The state is checked when the gate opens and
/// again every time the app comes back to the foreground, so a worker who switches location off later is stopped again.
class LocationGate extends StatefulWidget {
  final Widget child;
  final ILocationAccess access;

  const LocationGate({
    super.key,
    required this.child,
    this.access = const GeolocatorLocationAccess(),
  });

  @override
  State<LocationGate> createState() => _LocationGateState();
}

class _LocationGateState extends State<LocationGate>
    with WidgetsBindingObserver {
  /// Null while the first check runs.
  LocationAccessState? _state;
  bool _checkFailed = false;
  bool _busy = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _check();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _check();
  }

  Future<void> _check() => _run(widget.access.check);

  Future<void> _run(Future<LocationAccessState> Function() read) async {
    if (_busy) return;
    setState(() {
      _busy = true;
      _checkFailed = false;
    });
    try {
      final next = await read();
      if (!mounted) return;
      setState(() {
        _state = next;
        _busy = false;
      });
    } catch (_) {
      if (!mounted) return;
      // A check that failed never lets the worker in.
      setState(() {
        _state = null;
        _checkFailed = true;
        _busy = false;
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final state = _state;
    if (state == LocationAccessState.granted) return widget.child;

    return Scaffold(
      backgroundColor: NordicColors.canvas,
      body: SafeArea(
        child: Padding(
          padding: const EdgeInsets.all(NordicSpace.lg),
          child: _body(state),
        ),
      ),
    );
  }

  Widget _body(LocationAccessState? state) {
    if (_checkFailed) {
      return Center(
        child: ErrorStateWidget(
          message: 'Chưa kiểm tra được quyền vị trí của điện thoại.',
          onRetry: _check,
        ),
      );
    }
    if (state == null) {
      return const Center(
        child: LoadingStateWidget(message: 'Đang kiểm tra vị trí...'),
      );
    }

    final (title, message, label, onPressed) = switch (state) {
      LocationAccessState.serviceDisabled => (
        'Hãy bật định vị',
        'Ứng dụng dành cho thợ cần vị trí của bạn để nhận ca và xác nhận bạn đã đến nơi. Hãy bật định vị trên điện thoại.',
        'Bật định vị',
        widget.access.openLocationSettings,
      ),
      LocationAccessState.permissionDenied => (
        'Cần quyền vị trí',
        'Ứng dụng dành cho thợ không dùng được khi chưa có vị trí của bạn. Hãy cho phép ứng dụng dùng vị trí khi đang mở.',
        'Cho phép vị trí',
        () => _run(widget.access.requestPermission),
      ),
      _ => (
        'Quyền vị trí đang bị chặn',
        'Bạn đã từ chối quyền vị trí. Hãy bật lại trong Cài đặt của điện thoại để tiếp tục dùng ứng dụng dành cho thợ.',
        'Mở cài đặt',
        widget.access.openAppSettings,
      ),
    };

    return Column(
      mainAxisAlignment: MainAxisAlignment.center,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const Icon(
          Icons.location_on_outlined,
          size: 56.0,
          color: NordicColors.primary,
        ),
        const SizedBox(height: NordicSpace.md),
        Text(title, style: NordicTypography.h2, textAlign: TextAlign.center),
        const SizedBox(height: NordicSpace.xs),
        Text(
          message,
          style: NordicTypography.bodySmall,
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: NordicSpace.lg),
        NordicButton(
          key: const Key('location_gate_button'),
          label: label,
          onPressed: _busy ? null : () => onPressed(),
        ),
      ],
    );
  }
}
