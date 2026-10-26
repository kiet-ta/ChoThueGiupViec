import 'package:flutter/material.dart';

import '../../core/models/user_role.dart';
import '../../core/theme/nordic_colors.dart';
import '../../core/theme/nordic_typography.dart';
import '../identity/screens/phone_input_screen.dart';
import '../identity/services/token_storage.dart';
import 'customer_shell.dart';
import 'home_actions.dart';
import 'worker_shell.dart';

/// Root screen of TỔ ẤM — Nordic Care (390x844 frame).
/// Signed out: the minimal welcome of ui-templates `shared/welcome.html` (logo, one line, two role
/// buttons). Signed-in customer: [CustomerShell] with the bottom navigation (MOB-UI-02).
/// Signed-in worker: [WorkerShell] with its bottom navigation (MOB-UI-03).
class HomeScreen extends StatefulWidget {
  const HomeScreen({super.key});

  @override
  State<HomeScreen> createState() => _HomeScreenState();
}

class _HomeScreenState extends State<HomeScreen> {
  /// The signed-in user, or null: the Home shows the actions of this role (MOB-NAV).
  AuthUserView? get _session {
    final storage = TokenStorage();
    final user = storage.currentUser;
    if (!storage.isAuthenticated || user == null) return null;
    return AuthUserView(user.role, user.id);
  }

  /// Back from a pushed screen (the login in particular): show the state of the session as it is now.
  void _refresh() {
    if (!mounted) return;
    setState(() {});
  }

  void _openLogin(AppRole role) {
    Navigator.push(
      context,
      MaterialPageRoute(builder: (context) => PhoneInputScreen(role: role)),
    ).then((_) => _refresh());
  }

  void _signOut() {
    TokenStorage().clear();
    _refresh();
  }

  @override
  Widget build(BuildContext context) {
    final session = _session;
    if (session?.role == SessionRoles.customer) {
      return CustomerShell(onSignOut: _signOut);
    }
    if (session?.role == SessionRoles.worker) {
      return WorkerShell(onSignOut: _signOut, workerId: session!.id);
    }
    return _welcome();
  }

  Widget _welcome() {
    return Scaffold(
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 390.0),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(20, 0, 20, 40),
              child: Column(
                children: [
                  Expanded(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      children: [
                        Container(
                          width: 72,
                          height: 72,
                          decoration: BoxDecoration(
                            color: NordicColors.primary,
                            borderRadius: BorderRadius.circular(22),
                          ),
                          child: const Icon(
                            Icons.home_outlined,
                            color: Colors.white,
                            size: 38,
                          ),
                        ),
                        const SizedBox(height: 16),
                        const Text('TỔ ẤM', style: NordicTypography.greeting),
                        const SizedBox(height: 16),
                        const Text(
                          'Nhà sạch, lòng nhẹ.',
                          style: TextStyle(
                            fontSize: 14,
                            color: NordicColors.textSecondary,
                          ),
                        ),
                      ],
                    ),
                  ),
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      key: const Key('welcome-customer'),
                      onPressed: () => _openLogin(AppRole.customer),
                      child: const Text('Tôi cần giúp việc'),
                    ),
                  ),
                  const SizedBox(height: 12),
                  SizedBox(
                    width: double.infinity,
                    child: OutlinedButton(
                      key: const Key('welcome-worker'),
                      onPressed: () => _openLogin(AppRole.worker),
                      child: const Text('Tôi là người giúp việc'),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// The role and id of the signed-in user, as the Home reads them.
class AuthUserView {
  final String role;
  final int id;

  const AuthUserView(this.role, this.id);
}
