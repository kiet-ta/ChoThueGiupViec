import 'dart:math' as math;

import 'package:flutter/physics.dart';
import 'package:flutter/widgets.dart';

/// A spring described the way Apple's design talks do (MOB-UI-12, `apple-design` skill §4):
/// [dampingRatio] sets the overshoot (1.0 = none, lower = bouncier) and [response] how fast
/// the value reaches its target, in seconds. A spring has no fixed duration: it settles when
/// it settles, and it can start from any value and velocity, so motion stays interruptible.
@immutable
class NordicSpring {
  final double dampingRatio;
  final double response;

  const NordicSpring({required this.dampingRatio, required this.response});

  /// The same spring for Flutter's physics (mass 1).
  SpringDescription get description {
    final omega = 2 * math.pi / response;
    return SpringDescription(
      mass: 1.0,
      stiffness: omega * omega,
      damping: 2 * dampingRatio * omega,
    );
  }

  /// A simulation from [from] to [to] that starts with [velocity] (units per second).
  SpringSimulation simulate({
    double from = 0.0,
    double to = 1.0,
    double velocity = 0.0,
  }) => SpringSimulation(
    description,
    from,
    to,
    velocity,
    tolerance: const Tolerance(distance: 0.001, velocity: 0.01),
  );

  /// How long a move from 0 to 1 at rest takes to settle: the duration to give a
  /// [SpringCurve] when a widget only accepts a duration and a curve.
  Duration get settleDuration {
    final simulation = simulate();
    const step = 1 / 240;
    var t = 0.0;
    while (!simulation.isDone(t) && t < 3.0) {
      t += step;
    }
    return Duration(microseconds: (t * 1e6).round());
  }
}

/// The curve a [NordicSpring] draws from 0 to 1 at rest, for implicit animations and
/// transitions (pair it with [NordicSpring.settleDuration]). An underdamped spring overshoots
/// past 1 before it settles, like [Curves.elasticOut].
class SpringCurve extends Curve {
  final NordicSpring spring;
  final SpringSimulation _simulation;
  final double _seconds;

  SpringCurve(this.spring)
    : _simulation = spring.simulate(),
      _seconds = spring.settleDuration.inMicroseconds / 1e6;

  @override
  double transformInternal(double t) => _simulation.x(t * _seconds);
}

/// Motion tokens of the Mobile app. Pick by what caused the motion:
/// - [standard]: anything that appears, moves or changes on its own (no overshoot);
/// - [momentum]: only after a gesture that carried momentum (a flick, a drag release);
/// - [shake]: the "no" of a wrong entry (see `ErrorShake`).
class NordicMotion {
  NordicMotion._();

  static const NordicSpring standard = NordicSpring(
    dampingRatio: 1.0,
    response: 0.35,
  );
  static const NordicSpring momentum = NordicSpring(
    dampingRatio: 0.8,
    response: 0.3,
  );
  static const NordicSpring shake = NordicSpring(
    dampingRatio: 0.3,
    response: 0.12,
  );

  /// Press feedback runs on pointer-down and must feel instant.
  static const Duration press = Duration(milliseconds: 100);

  /// A cross-fade replaces slides and springs when motion is reduced.
  static const Duration fade = Duration(milliseconds: 200);

  /// True when the platform asks for reduced motion: no slide, scale or shake, only fades.
  static bool reduced(BuildContext context) =>
      MediaQuery.maybeDisableAnimationsOf(context) ?? false;
}
