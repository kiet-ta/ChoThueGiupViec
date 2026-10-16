import 'package:flutter_test/flutter_test.dart';
import 'package:mobile/core/device/location_wrapper.dart';

void main() {
  test('the default position is District 1, Ho Chi Minh City, like the address form', () async {
    final point = await LocationWrapper().getCurrentLocation();

    // The same defaults as the address form and the GPS picker: a worker and a customer using the defaults are at the same place.
    expect(point.latitude, 10.7769);
    expect(point.longitude, 106.7009);
  });

  test('a worker at the default position is within the check-in radius of the default address', () {
    final wrapper = LocationWrapper();

    expect(
      wrapper.isWithinRadius(10.7769, 106.7009, 10.7769, 106.7009, 100.0),
      isTrue,
    );
  });
}
