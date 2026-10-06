import 'package:flutter/material.dart';
import '../theme/nordic_colors.dart';
import '../theme/nordic_typography.dart';
import 'nordic_card.dart';

/// Lightweight custom preview map widget showing booking target and worker location.
/// Avoids heavy third-party map dependencies while providing visual geographical context.
class MiniMapWidget extends StatelessWidget {
  final double targetLatitude;
  final double targetLongitude;
  final String? addressLabel;
  final double? workerLatitude;
  final double? workerLongitude;
  final double? distanceKm;
  final double radiusMeters;
  final double height;

  const MiniMapWidget({
    super.key,
    required this.targetLatitude,
    required this.targetLongitude,
    this.addressLabel,
    this.workerLatitude,
    this.workerLongitude,
    this.distanceKm,
    this.radiusMeters = 100.0,
    this.height = 160.0,
  });

  @override
  Widget build(BuildContext context) {
    return NordicCard(
      padding: EdgeInsets.zero,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(16.0),
        child: SizedBox(
          height: height,
          width: double.infinity,
          child: Stack(
            children: [
              // Custom map canvas background
              Positioned.fill(
                child: CustomPaint(
                  painter: _MapCanvasPainter(
                    hasWorker: workerLatitude != null && workerLongitude != null,
                  ),
                ),
              ),

              // Center Target Marker
              Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      padding: const EdgeInsets.all(8.0),
                      decoration: const BoxDecoration(
                        color: NordicColors.primary,
                        shape: BoxShape.circle,
                        boxShadow: [
                          BoxShadow(
                            color: Colors.black26,
                            blurRadius: 6.0,
                            offset: Offset(0, 2),
                          ),
                        ],
                      ),
                      child: const Icon(
                        Icons.home_rounded,
                        color: Colors.white,
                        size: 20.0,
                      ),
                    ),
                    if (addressLabel != null) ...[
                      const SizedBox(height: 4.0),
                      Container(
                        padding: const EdgeInsets.symmetric(
                          horizontal: 8.0,
                          vertical: 2.0,
                        ),
                        decoration: BoxDecoration(
                          color: Colors.white.withValues(alpha: 0.9),
                          borderRadius: BorderRadius.circular(12.0),
                          border: Border.all(color: NordicColors.border),
                        ),
                        child: Text(
                          addressLabel!,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: NordicTypography.bodySmall.copyWith(
                            color: NordicColors.textTitle,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                      ),
                    ],
                  ],
                ),
              ),

              // Top right info badge (Distance or Radius)
              Positioned(
                top: 10.0,
                right: 10.0,
                child: Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 10.0,
                    vertical: 4.0,
                  ),
                  decoration: BoxDecoration(
                    color: Colors.white.withValues(alpha: 0.92),
                    borderRadius: BorderRadius.circular(20.0),
                    border: Border.all(color: NordicColors.border),
                  ),
                  child: Row(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      const Icon(
                        Icons.near_me_rounded,
                        size: 14.0,
                        color: NordicColors.secondaryAccent,
                      ),
                      const SizedBox(width: 4.0),
                      Text(
                        distanceKm != null
                            ? '${distanceKm!.toStringAsFixed(1)} km'
                            : 'Bán kính ${(radiusMeters).toInt()}m',
                        style: NordicTypography.labelSmall.copyWith(
                          color: NordicColors.textTitle,
                          fontWeight: FontWeight.bold,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _MapCanvasPainter extends CustomPainter {
  final bool hasWorker;

  const _MapCanvasPainter({required this.hasWorker});

  @override
  void paint(Canvas canvas, Size size) {
    final bgPaint = Paint()..color = const Color(0xFFF2EFE9);
    canvas.drawRect(Offset.zero & size, bgPaint);

    // Grid lines
    final gridPaint = Paint()
      ..color = const Color(0xFFE5E0D5)
      ..strokeWidth = 1.0;

    const spacing = 28.0;
    for (double x = 0; x < size.width; x += spacing) {
      canvas.drawLine(Offset(x, 0), Offset(x, size.height), gridPaint);
    }
    for (double y = 0; y < size.height; y += spacing) {
      canvas.drawLine(Offset(0, y), Offset(size.width, y), gridPaint);
    }

    // Radius circle around center
    final center = Offset(size.width / 2, size.height / 2);
    final radiusPaint = Paint()
      ..color = NordicColors.secondaryAccent.withValues(alpha: 0.15)
      ..style = PaintingStyle.fill;
    final strokeRadiusPaint = Paint()
      ..color = NordicColors.secondaryAccent.withValues(alpha: 0.4)
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.5;

    canvas.drawCircle(center, 44.0, radiusPaint);
    canvas.drawCircle(center, 44.0, strokeRadiusPaint);

    // Optional worker position
    if (hasWorker) {
      final workerOffset = Offset(center.dx - 50.0, center.dy + 35.0);
      final workerPaint = Paint()..color = NordicColors.accentAmber;
      canvas.drawCircle(workerOffset, 8.0, workerPaint);

      final linePaint = Paint()
        ..color = NordicColors.textSecondary.withValues(alpha: 0.4)
        ..strokeWidth = 1.5
        ..style = PaintingStyle.stroke;
      canvas.drawLine(workerOffset, center, linePaint);
    }
  }

  @override
  bool shouldRepaint(covariant _MapCanvasPainter oldDelegate) =>
      oldDelegate.hasWorker != hasWorker;
}
