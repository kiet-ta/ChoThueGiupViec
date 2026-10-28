import 'package:flutter/cupertino.dart';

import 'nordic_motion.dart';

/// How every page of the app enters and leaves (MOB-UI-12, `apple-design` skill §2, §3, §5, §7),
/// set once in `NordicTheme.pageTransitionsTheme` for all platforms:
/// - the new page slides in from the right and leaves the same way, the page below moves back
///   a little (Apple's own curve and timing, from [CupertinoPageTransitionsBuilder]);
/// - the page can be dragged back from the left edge, following the finger 1:1; on release it
///   carries on at the finger's speed and can be caught again mid-way;
/// - under reduced motion pages only fade in and out and the page below stays
///   still; back is then the back button or the system back (the edge drag comes with the slide).
class NordicPageTransitionsBuilder extends PageTransitionsBuilder {
  const NordicPageTransitionsBuilder();

  static const CupertinoPageTransitionsBuilder _slide =
      CupertinoPageTransitionsBuilder();

  @override
  Duration get transitionDuration => _slide.transitionDuration;

  @override
  DelegatedTransitionBuilder? get delegatedTransition =>
      (context, animation, secondaryAnimation, allowSnapshotting, child) {
        if (NordicMotion.reduced(context)) return child;
        return _slide.delegatedTransition!(
          context,
          animation,
          secondaryAnimation,
          allowSnapshotting,
          child,
        );
      };

  @override
  Widget buildTransitions<T>(
    PageRoute<T> route,
    BuildContext context,
    Animation<double> animation,
    Animation<double> secondaryAnimation,
    Widget child,
  ) {
    if (NordicMotion.reduced(context)) {
      // A plain fade: no movement at all.
      return FadeTransition(
        opacity: CurvedAnimation(parent: animation, curve: Curves.easeOut),
        child: child,
      );
    }
    return _slide.buildTransitions(
      route,
      context,
      animation,
      secondaryAnimation,
      child,
    );
  }
}
