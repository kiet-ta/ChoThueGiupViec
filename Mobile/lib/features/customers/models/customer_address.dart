/// Housing types supported by TỔ ẤM per PRD §1.1 and .spec/contracts/customers.md §2.2
enum HousingType {
  apartment('APARTMENT', 'Căn hộ chung cư', 1),
  house('HOUSE', 'Nhà riêng / Nhà phố', 10),
  room('ROOM', 'Phòng trọ', 1);

  final String apiValue;
  final String displayName;
  final int maxFloors;

  const HousingType(this.apiValue, this.displayName, this.maxFloors);

  static HousingType fromApiValue(String? value) {
    switch (value?.toUpperCase()) {
      case 'APARTMENT':
        return HousingType.apartment;
      case 'HOUSE':
        return HousingType.house;
      case 'ROOM':
        return HousingType.room;
      default:
        return HousingType.apartment;
    }
  }
}

/// Address model matching CUSTOMER_ADDRESS table and .spec/contracts/customers.md.
/// Server computes canonical S_total (totalAreaM2 = floorAreaM2 x numFloors).
class CustomerAddress {
  final int addressId;
  final String label;
  final String addressLine;
  final String district;
  final String city;
  final HousingType housingType;
  final double floorAreaM2;
  final int numFloors;
  final double totalAreaM2;
  final int? bedrooms;
  final int? bathrooms;
  final double latitude;
  final double longitude;
  final bool isDefault;
  final DateTime? createdAt;

  const CustomerAddress({
    required this.addressId,
    required this.label,
    required this.addressLine,
    required this.district,
    required this.city,
    required this.housingType,
    required this.floorAreaM2,
    required this.numFloors,
    required this.totalAreaM2,
    this.bedrooms,
    this.bathrooms,
    required this.latitude,
    required this.longitude,
    this.isDefault = false,
    this.createdAt,
  });

  /// Client-side preview computation for S_total = S_sàn x N_tầng (PRD §1.1).
  static double calculateTotalArea(double floorArea, int floors) {
    if (floorArea <= 0 || floors <= 0) return 0.0;
    final total = floorArea * floors;
    return double.parse(total.toStringAsFixed(2));
  }

  factory CustomerAddress.fromJson(Map<String, dynamic> json) {
    final floorArea = (json['floorAreaM2'] as num?)?.toDouble() ?? 0.0;
    final floors = (json['numFloors'] as num?)?.toInt() ?? 1;
    final serverTotal = (json['totalAreaM2'] as num?)?.toDouble();

    return CustomerAddress(
      addressId: (json['addressId'] as num?)?.toInt() ?? 0,
      label: json['label'] as String? ?? '',
      addressLine: json['addressLine'] as String? ?? '',
      district: json['district'] as String? ?? '',
      city: json['city'] as String? ?? '',
      housingType: HousingType.fromApiValue(json['housingType'] as String?),
      floorAreaM2: floorArea,
      numFloors: floors,
      totalAreaM2: serverTotal ?? calculateTotalArea(floorArea, floors),
      bedrooms: (json['bedrooms'] as num?)?.toInt(),
      bathrooms: (json['bathrooms'] as num?)?.toInt(),
      latitude: (json['latitude'] as num?)?.toDouble() ?? 0.0,
      longitude: (json['longitude'] as num?)?.toDouble() ?? 0.0,
      isDefault: json['isDefault'] as bool? ?? false,
      createdAt: json['createdAt'] != null ? DateTime.tryParse(json['createdAt'] as String) : null,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'label': label,
      'addressLine': addressLine,
      'district': district,
      'city': city,
      'housingType': housingType.apiValue,
      'floorAreaM2': floorAreaM2,
      'numFloors': numFloors,
      if (bedrooms != null) 'bedrooms': bedrooms,
      if (bathrooms != null) 'bathrooms': bathrooms,
      'latitude': latitude,
      'longitude': longitude,
      'isDefault': isDefault,
    };
  }

  CustomerAddress copyWith({
    int? addressId,
    String? label,
    String? addressLine,
    String? district,
    String? city,
    HousingType? housingType,
    double? floorAreaM2,
    int? numFloors,
    double? totalAreaM2,
    int? bedrooms,
    int? bathrooms,
    double? latitude,
    double? longitude,
    bool? isDefault,
    DateTime? createdAt,
  }) {
    return CustomerAddress(
      addressId: addressId ?? this.addressId,
      label: label ?? this.label,
      addressLine: addressLine ?? this.addressLine,
      district: district ?? this.district,
      city: city ?? this.city,
      housingType: housingType ?? this.housingType,
      floorAreaM2: floorAreaM2 ?? this.floorAreaM2,
      numFloors: numFloors ?? this.numFloors,
      totalAreaM2: totalAreaM2 ?? this.totalAreaM2,
      bedrooms: bedrooms ?? this.bedrooms,
      bathrooms: bathrooms ?? this.bathrooms,
      latitude: latitude ?? this.latitude,
      longitude: longitude ?? this.longitude,
      isDefault: isDefault ?? this.isDefault,
      createdAt: createdAt ?? this.createdAt,
    );
  }

  /// Full display address string
  String get fullAddress => '$addressLine, $district, $city';
}
