import 'vn_wards.dart';

/// A ward, commune or special zone of the post-merger list (WORKERS-WARD-V2-01, #959, decisions D40), as the server returns it.
class WorkerWard {
  final int code;
  final String name;
  final int provinceCode;
  final String provinceName;

  const WorkerWard({
    required this.code,
    required this.name,
    required this.provinceCode,
    required this.provinceName,
  });

  /// The ward of the bundled list (`vn_wards.dart`), with its province's name.
  factory WorkerWard.fromList(VnWard ward) => WorkerWard(
    code: ward.code,
    name: ward.name,
    provinceCode: ward.provinceCode,
    provinceName: provinceNameOf(ward.provinceCode),
  );

  factory WorkerWard.fromJson(Map<String, dynamic> json) => WorkerWard(
    code: (json['code'] as num).toInt(),
    name: json['name'] as String? ?? '',
    provinceCode: (json['provinceCode'] as num?)?.toInt() ?? 0,
    provinceName: json['provinceName'] as String? ?? '',
  );
}

final Map<int, String> _provinceNames = {
  for (final p in vnProvinces) p.code: p.name,
};

final Map<int, List<VnWard>> _wardsByProvince = () {
  final map = <int, List<VnWard>>{};
  for (final ward in vnWards) {
    (map[ward.provinceCode] ??= []).add(ward);
  }
  return map;
}();

/// The name of a province of the bundled list; empty for an unknown code.
String provinceNameOf(int provinceCode) => _provinceNames[provinceCode] ?? '';

/// The wards of one province of the bundled list, in the order of the source.
List<VnWard> wardsOfProvince(int provinceCode) =>
    _wardsByProvince[provinceCode] ?? const [];

/// The work area a freelancer registered (contract workers.md §2.3B, decisions D24, D40): a base point, a radius and the wards /
/// communes shown to the worker. Nothing registered = [isSet] false. [unresolvedNames] are old stored names that stand for no
/// ward (or several) in the current list: the worker chooses them again.
class WorkerWorkArea {
  final bool isSet;
  final double? latitude;
  final double? longitude;
  final int? radiusKm;
  final List<WorkerWard> wards;

  /// The ward names as plain text (the server still sends them); the Tài khoản tab counts them.
  final List<String> districts;
  final List<String> unresolvedNames;

  const WorkerWorkArea({
    required this.isSet,
    this.latitude,
    this.longitude,
    this.radiusKm,
    this.wards = const [],
    this.districts = const [],
    this.unresolvedNames = const [],
  });

  const WorkerWorkArea.empty() : this(isSet: false);

  factory WorkerWorkArea.fromJson(Map<String, dynamic> json) {
    final rawWards = json['wards'];
    final rawDistricts = json['districts'];
    final rawUnresolved = json['unresolvedNames'];
    return WorkerWorkArea(
      isSet: json['isSet'] as bool? ?? false,
      latitude: (json['latitude'] as num?)?.toDouble(),
      longitude: (json['longitude'] as num?)?.toDouble(),
      radiusKm: (json['radiusKm'] as num?)?.toInt(),
      wards: rawWards is List
          ? rawWards
                .whereType<Map<String, dynamic>>()
                .map(WorkerWard.fromJson)
                .toList()
          : const [],
      districts: rawDistricts is List
          ? rawDistricts.whereType<String>().toList()
          : const [],
      unresolvedNames: rawUnresolved is List
          ? rawUnresolved.whereType<String>().toList()
          : const [],
    );
  }
}

/// Body of `PUT /api/v1/workers/me/work-area`.
class SaveWorkAreaRequest {
  final double latitude;
  final double longitude;
  final int radiusKm;

  /// The codes of the chosen wards, in order (at most 10).
  final List<int> wardCodes;

  const SaveWorkAreaRequest({
    required this.latitude,
    required this.longitude,
    required this.radiusKm,
    required this.wardCodes,
  });

  Map<String, dynamic> toJson() => {
    'latitude': latitude,
    'longitude': longitude,
    'radiusKm': radiusKm,
    'wardCodes': wardCodes,
  };
}
