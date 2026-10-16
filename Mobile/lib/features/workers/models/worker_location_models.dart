/// The position the server stored for the worker (contract workers.md §2.3A, `PUT /api/workers/me/location`).
class WorkerLocationDto {
  final double latitude;
  final double longitude;

  const WorkerLocationDto({required this.latitude, required this.longitude});

  factory WorkerLocationDto.fromJson(Map<String, dynamic> json) {
    return WorkerLocationDto(
      latitude: (json['latitude'] as num?)?.toDouble() ?? 0.0,
      longitude: (json['longitude'] as num?)?.toDouble() ?? 0.0,
    );
  }
}
