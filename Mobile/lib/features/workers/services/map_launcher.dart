import 'package:url_launcher/url_launcher.dart';

/// Opens the phone's map app (behind an interface so tests do not need a platform).
abstract class IMapLauncher {
  /// True when a map app took the link.
  Future<bool> open(Uri uri);
}

/// `url_launcher` behind [IMapLauncher]: the link goes to an external map app, not to an in-app browser.
class UrlMapLauncher implements IMapLauncher {
  const UrlMapLauncher();

  @override
  Future<bool> open(Uri uri) async {
    try {
      return await launchUrl(uri, mode: LaunchMode.externalApplication);
    } catch (_) {
      return false;
    }
  }
}

/// The directions link to the customer's house (ROUTE-MOB-01, #760): the exact point when the job has coordinates, the
/// written address otherwise, by motorbike. Returns null when the job has neither (`0,0` and an empty address).
Uri? mapDirectionsUri({
  required double latitude,
  required double longitude,
  required String address,
}) {
  final hasPoint = latitude != 0.0 || longitude != 0.0;
  final destination = hasPoint
      ? '$latitude,$longitude'
      : (address.trim().isEmpty ? null : address.trim());
  if (destination == null) return null;
  return Uri.https('www.google.com', '/maps/dir/', {
    'api': '1',
    'destination': destination,
    'travelmode': 'two-wheeler',
  });
}
