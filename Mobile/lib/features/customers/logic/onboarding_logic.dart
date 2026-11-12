import '../models/customer_profile.dart';

// Pure logic of the first-login onboarding of a customer (MOB-ONBOARD-01). No widgets, so plain tests cover it.

/// The three steps, in order. Only the name step cannot be skipped.
enum OnboardingStep { name, address, houseType }

/// The step after [step], or null after the last one.
OnboardingStep? nextStep(OnboardingStep step) => switch (step) {
  OnboardingStep.name => OnboardingStep.address,
  OnboardingStep.address => OnboardingStep.houseType,
  OnboardingStep.houseType => null,
};

/// The step before [step], or null before the first one.
OnboardingStep? previousStep(OnboardingStep step) => switch (step) {
  OnboardingStep.name => null,
  OnboardingStep.address => OnboardingStep.name,
  OnboardingStep.houseType => OnboardingStep.address,
};

/// "Bước n/3" counts from 1.
int stepNumber(OnboardingStep step) => step.index + 1;

const int onboardingStepCount = 3;

/// The name the customer wants to be called: shown to the workers of their orders (contract customers.md 2.1).
const int maxCallNameLength = 100;

/// The address line column is 255 characters.
const int maxAddressLineLength = 255;

/// The call name as it is saved: trimmed, runs of spaces made one.
String normalizeCallName(String raw) =>
    raw.trim().replaceAll(RegExp(r'\s+'), ' ');

/// The message under the name field, or null when the name can be saved.
String? callNameError(String raw) {
  final name = normalizeCallName(raw);
  if (name.isEmpty) return 'Cho chúng tôi biết nên gọi bạn là gì.';
  if (name.length > maxCallNameLength) {
    return 'Tên gọi tối đa $maxCallNameLength ký tự (đang ${name.length}).';
  }
  return null;
}

/// The message under the address line field, or null.
String? addressLineError(String raw) {
  final text = raw.trim();
  if (text.isEmpty) return 'Nhập số nhà và tên đường.';
  if (text.length > maxAddressLineLength) {
    return 'Địa chỉ tối đa $maxAddressLineLength ký tự.';
  }
  return null;
}

/// A customer without a name has not been through the onboarding: the server creates them with an empty name at the first login,
/// and a customer who quit halfway is asked again at the next login until there is a name.
bool needsOnboarding(CustomerProfile profile) =>
    profile.fullName.trim().isEmpty;

/// What the onboarding did, handed back to whoever opened it.
class OnboardingResult {
  final bool nameSaved;
  final bool addressSaved;

  const OnboardingResult({required this.nameSaved, required this.addressSaved});
}
