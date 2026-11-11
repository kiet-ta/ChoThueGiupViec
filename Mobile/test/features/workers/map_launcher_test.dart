import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/features/workers/services/map_launcher.dart';

void main() {
  test('the link uses the point when the job has one', () {
    final uri = mapDirectionsUri(
      latitude: 10.8,
      longitude: 106.7,
      address: 'x',
    );
    expect(uri!.queryParameters['destination'], '10.8,106.7');
    expect(uri.queryParameters['travelmode'], 'two-wheeler');
  });

  test('the link uses the written address when the point is 0,0', () {
    final uri = mapDirectionsUri(latitude: 0, longitude: 0, address: ' Nhà A ');
    expect(uri!.queryParameters['destination'], 'Nhà A');
  });

  test('no point and no address: no link', () {
    expect(mapDirectionsUri(latitude: 0, longitude: 0, address: ' '), isNull);
  });
}
