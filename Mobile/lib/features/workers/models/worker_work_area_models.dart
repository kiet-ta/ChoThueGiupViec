/// The work area a freelancer registered (contract workers.md §2.3B, decisions D24): a base point, a radius and the
/// wards / communes shown to the worker. Nothing registered = [isSet] false.
class WorkerWorkArea {
  final bool isSet;
  final double? latitude;
  final double? longitude;
  final int? radiusKm;
  final List<String> districts;

  const WorkerWorkArea({
    required this.isSet,
    this.latitude,
    this.longitude,
    this.radiusKm,
    this.districts = const [],
  });

  const WorkerWorkArea.empty() : this(isSet: false);

  factory WorkerWorkArea.fromJson(Map<String, dynamic> json) {
    final raw = json['districts'];
    return WorkerWorkArea(
      isSet: json['isSet'] as bool? ?? false,
      latitude: (json['latitude'] as num?)?.toDouble(),
      longitude: (json['longitude'] as num?)?.toDouble(),
      radiusKm: (json['radiusKm'] as num?)?.toInt(),
      districts: raw is List ? raw.whereType<String>().toList() : const [],
    );
  }
}

/// Body of `PUT /api/workers/me/work-area`.
class SaveWorkAreaRequest {
  final double latitude;
  final double longitude;
  final int radiusKm;
  final List<String> districts;

  const SaveWorkAreaRequest({
    required this.latitude,
    required this.longitude,
    required this.radiusKm,
    required this.districts,
  });

  Map<String, dynamic> toJson() => {
    'latitude': latitude,
    'longitude': longitude,
    'radiusKm': radiusKm,
    'districts': districts,
  };
}
