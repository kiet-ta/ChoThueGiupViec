import 'package:flutter/material.dart';

import '../../../core/theme/nordic_spacing.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/worker_assignment_models.dart';
import '../models/worker_extension_models.dart';
import '../services/workers_assignments_service.dart';
import 'worker_extension_screen.dart';

/// Opens the "Làm lần 2" answer screen for a job id: reads the job (`GET /api/v1/workers/assignments/{id}`, contract workers.md §2.3.4) and
/// shows the extension the customer paid for. A failed read is an error with a retry; a job without a waiting extension says so. Never a sample offer.
class WorkerExtensionLoader extends StatefulWidget {
  final int assignmentId;
  final WorkersAssignmentsService? assignmentsService;

  const WorkerExtensionLoader({
    super.key,
    required this.assignmentId,
    this.assignmentsService,
  });

  @override
  State<WorkerExtensionLoader> createState() => _WorkerExtensionLoaderState();
}

class _WorkerExtensionLoaderState extends State<WorkerExtensionLoader> {
  late final WorkersAssignmentsService _service;
  WorkerAssignmentDetailDto? _detail;
  String? _error;

  @override
  void initState() {
    super.initState();
    _service = widget.assignmentsService ?? WorkersAssignmentsService();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _detail = null;
      _error = null;
    });
    try {
      final response = await _service.getAssignment(widget.assignmentId);
      if (!mounted) return;
      setState(() {
        _detail = response.success ? response.data : null;
        _error = _detail == null
            ? (response.message.isNotEmpty
                  ? response.message
                  : 'Chưa tải được yêu cầu gia hạn.')
            : null;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() => _error = 'Chưa tải được yêu cầu gia hạn: $e');
    }
  }

  @override
  Widget build(BuildContext context) {
    final detail = _detail;
    if (detail != null) {
      final offer = WorkerExtensionOffer.fromDetail(detail);
      if (offer != null) return WorkerExtensionScreen(offer: offer);
    }
    return NordicPage.pushed(
      title: 'Làm lần 2',
      children: [
        if (detail != null)
          const ErrorStateWidget(
            title: 'Chưa có yêu cầu',
            message: 'Chưa có yêu cầu gia hạn nào để trả lời cho ca này.',
          )
        else if (_error != null)
          ErrorStateWidget(message: _error!, onRetry: _load)
        else ...const [
          SizedBox(height: NordicSpace.xl),
          LoadingStateWidget(message: 'Đang tải yêu cầu gia hạn...'),
        ],
      ],
    );
  }
}
