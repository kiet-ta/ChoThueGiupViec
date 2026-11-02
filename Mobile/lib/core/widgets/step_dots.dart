import 'package:flutter/material.dart';

import '../theme/nordic_colors.dart';

/// State of one step of a [StepDots].
enum DotState { done, current, todo }

/// One step: a short label and its state.
class StepItem {
  final String label;
  final DotState state;

  const StepItem(this.label, this.state);
}

/// ui-templates `.steps`: a horizontal stepper of 3-4 dots joined by a line.
class StepDots extends StatelessWidget {
  final List<StepItem> steps;

  const StepDots({super.key, required this.steps});

  @override
  Widget build(BuildContext context) {
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        for (var i = 0; i < steps.length; i++)
          Expanded(child: _step(steps[i], i == 0)),
      ],
    );
  }

  Widget _step(StepItem step, bool first) {
    final reached = step.state != DotState.todo;
    return Column(
      children: [
        SizedBox(
          height: 28,
          child: Stack(
            alignment: Alignment.center,
            children: [
              if (!first)
                Align(
                  alignment: Alignment.centerLeft,
                  child: FractionallySizedBox(
                    widthFactor: 0.5,
                    child: Container(
                      height: 2,
                      color: reached
                          ? NordicColors.primary
                          : NordicColors.border,
                    ),
                  ),
                ),
              Container(
                width: 28,
                height: 28,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  color: step.state == DotState.done
                      ? NordicColors.primary
                      : NordicColors.surface,
                  border: Border.all(
                    width: 2,
                    color: reached ? NordicColors.primary : NordicColors.border,
                  ),
                ),
                child: step.state == DotState.done
                    ? const Icon(Icons.check, size: 16, color: Colors.white)
                    : null,
              ),
            ],
          ),
        ),
        const SizedBox(height: 6),
        Text(
          step.label,
          textAlign: TextAlign.center,
          style: TextStyle(
            fontSize: 11,
            fontWeight: step.state == DotState.current
                ? FontWeight.w600
                : FontWeight.w400,
            color: step.state == DotState.current
                ? NordicColors.textTitle
                : NordicColors.textSecondary,
          ),
        ),
      ],
    );
  }
}
