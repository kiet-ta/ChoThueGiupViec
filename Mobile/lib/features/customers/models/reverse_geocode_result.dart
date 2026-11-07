/// Structured reverse geocoding result matching contract customers.md §2.4.
class ReverseGeocodeResult {
  final String city;
  final String district;
  final String addressLine;
  final String formattedAddress;
  final double latitude;
  final double longitude;

  const ReverseGeocodeResult({
    required this.city,
    required this.district,
    required this.addressLine,
    required this.formattedAddress,
    required this.latitude,
    required this.longitude,
  });

  factory ReverseGeocodeResult.fromJson(Map<String, dynamic> json) {
    return ReverseGeocodeResult(
      city: json['city'] as String? ?? '',
      district: json['district'] as String? ?? '',
      addressLine: json['addressLine'] as String? ?? '',
      formattedAddress: json['formattedAddress'] as String? ?? '',
      latitude: (json['latitude'] as num?)?.toDouble() ?? 0.0,
      longitude: (json['longitude'] as num?)?.toDouble() ?? 0.0,
    );
  }

  Map<String, dynamic> toJson() => {
    'city': city,
    'district': district,
    'addressLine': addressLine,
    'formattedAddress': formattedAddress,
    'latitude': latitude,
    'longitude': longitude,
  };
}
