import 'package:flutter/material.dart';

import '../../../core/motion/error_shake.dart';
import '../../../core/network/api_client.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_radius.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_card.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/nordic_text_input.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/payout_logic.dart';
import '../logic/wallet_logic.dart';
import '../models/wallet_models.dart';
import '../services/wallet_service.dart';

/// The freelancer's wallet and advance request (LEDGER-16); contract ledger.md section 6, decisions D28 and E.
/// Pushed from the income screen. The server says what can be withdrawn; the device only checks the typed number
/// against it, and a second request is blocked here as well as by the server (one PENDING request per worker).
/// Built with the shared Nordic kit only (`Mobile/DESIGN-SYSTEM.md`, `DESIGN-SYSTEM-WORKER.md`).
class WalletScreen extends StatefulWidget {
  final IWalletService? service;

  const WalletScreen({super.key, this.service});

  @override
  State<WalletScreen> createState() => _WalletScreenState();
}

class _WalletScreenState extends State<WalletScreen> {
  static final TextStyle _warningStyle = NordicTypography.bodyRegular.copyWith(
    color: NordicColors.onAmberSoft,
  );

  late final IWalletService _service;
  final _amount = TextEditingController();

  Wallet? _wallet;
  String? _loadError;
  bool _forbidden = false;
  bool _loading = true;

  String? _fieldError;
  String? _actionError;
  String? _notice;
  bool _busy = false;
  int _errors = 0;

  /// A second tap while a call runs must not send another one.
  bool _inFlight = false;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? WalletService();
    _load();
  }

  @override
  void dispose() {
    _amount.dispose();
    super.dispose();
  }

  Future<void> _load({bool keepNotice = false}) async {
    setState(() {
      _loading = true;
      _loadError = null;
      _forbidden = false;
      if (!keepNotice) _notice = null;
    });
    try {
      final wallet = await _service.getWallet();
      if (!mounted) return;
      setState(() {
        _wallet = wallet;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _wallet = null;
        _loadError = walletFailureMessage(e);
        _forbidden = e.isForbidden;
        _loading = false;
      });
    }
  }

  Future<void> _submit() async {
    final wallet = _wallet;
    if (wallet == null || _inFlight || !canRequestWithdrawal(wallet)) return;
    final problem = withdrawalAmountError(_amount.text, wallet.available);
    if (problem != null) {
      setState(() {
        _fieldError = problem;
        _actionError = null;
        _errors++;
      });
      return;
    }
    final amount = parseWithdrawalAmount(_amount.text)!;
    _inFlight = true;
    setState(() {
      _busy = true;
      _fieldError = null;
      _actionError = null;
      _notice = null;
    });
    try {
      await _service.requestWithdrawal(amount);
      if (!mounted) return;
      _amount.clear();
      _notice =
          'Đã gửi yêu cầu rút ${formatVnd(amount)}. Bạn sẽ nhận thông báo khi được duyệt.';
      await _load(keepNotice: true);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _actionError = withdrawalFailureMessage(e);
        _errors++;
      });
      if (shouldReloadWalletAfter(e)) await _load(keepNotice: true);
    } finally {
      _inFlight = false;
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _cancel(PendingWithdrawal pending) async {
    if (_inFlight) return;
    _inFlight = true;
    setState(() {
      _busy = true;
      _actionError = null;
      _notice = null;
    });
    try {
      await _service.cancelWithdrawal(pending.withdrawalId);
      if (!mounted) return;
      _notice = 'Đã huỷ yêu cầu rút ${formatVnd(pending.amount)}.';
      await _load(keepNotice: true);
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() => _actionError = withdrawalFailureMessage(e));
      if (shouldReloadWalletAfter(e)) await _load(keepNotice: true);
    } finally {
      _inFlight = false;
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final wallet = _wallet;
    final canSubmit = wallet != null && canRequestWithdrawal(wallet);
    return NordicPage.pushed(
      title: 'Ví của bạn',
      onRefresh: () => _load(),
      actions: wallet == null
          ? null
          : [
              NordicButton(
                key: const Key('wallet-submit'),
                label: 'Gửi yêu cầu rút',
                isLoading: _busy && wallet.pendingWithdrawal == null,
                onPressed: canSubmit && !_busy ? _submit : null,
              ),
            ],
      children: _content(),
    );
  }

  List<Widget> _content() {
    if (_loading && _wallet == null) {
      return const [
        SizedBox(
          height: 240.0,
          child: LoadingStateWidget(message: 'Đang tải ví…'),
        ),
      ];
    }
    final wallet = _wallet;
    if (wallet == null) {
      return [
        ErrorStateWidget(
          message: _loadError ?? 'Không tải được ví.',
          // Retrying does not help an agency worker; the explanation is the whole answer.
          onRetry: _forbidden ? null : () => _load(),
        ),
      ];
    }
    final nextText = nextAvailableText(wallet.nextAvailableAt);
    return [
      _summary(wallet, nextText),
      const SizedBox(height: NordicSpace.gutter),
      if (_notice != null) ...[
        _noticeCard(_notice!),
        const SizedBox(height: NordicSpace.sm),
      ],
      if (wallet.pendingWithdrawal != null) ...[
        _pendingCard(wallet.pendingWithdrawal!),
        const SizedBox(height: NordicSpace.sm),
      ],
      _form(wallet),
    ];
  }

  /// The withdrawable amount as the one big number, then the balance and the held amount.
  Widget _summary(Wallet wallet, String? nextText) {
    return Column(
      children: [
        const Text('Có thể rút ngay', style: NordicTypography.bodySmall),
        const SizedBox(height: NordicSpace.xs),
        Text(
          formatVnd(wallet.available),
          key: const Key('wallet-available'),
          style: NordicTypography.numberLarge,
        ),
        if (nextText != null) ...[
          const SizedBox(height: NordicSpace.xs),
          Text(
            nextText,
            key: const Key('wallet-next'),
            textAlign: TextAlign.center,
            style: NordicTypography.bodySmall,
          ),
        ],
        const SizedBox(height: NordicSpace.md),
        NordicCard(
          child: Column(
            children: [
              _figure(
                'Số dư trong ví',
                formatVnd(wallet.balance),
                const Key('wallet-balance'),
              ),
              _figure(
                'Đang giữ cho yêu cầu rút',
                formatVnd(wallet.held),
                const Key('wallet-held'),
              ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _figure(String label, String value, Key key) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: NordicSpace.xxs),
      child: Row(
        children: [
          Expanded(child: Text(label, style: NordicTypography.bodyRegular)),
          Text(value, key: key, style: NordicTypography.titleSmall),
        ],
      ),
    );
  }

  Widget _noticeCard(String text) {
    return NordicCard(
      child: Text(
        text,
        key: const Key('wallet-notice'),
        style: NordicTypography.bodyRegular,
      ),
    );
  }

  /// The request that waits for the Admin, with its cancel button.
  Widget _pendingCard(PendingWithdrawal pending) {
    return NordicCard(
      key: const Key('wallet-pending-card'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const Expanded(
                child: Text(
                  'Yêu cầu rút tiền',
                  style: NordicTypography.titleSmall,
                ),
              ),
              StatusPill(
                withdrawalStatusLabel(pending.status),
                dot: true,
                tone: PillTone.warn,
                textKey: const Key('wallet-pending-status'),
              ),
            ],
          ),
          const SizedBox(height: NordicSpace.xs),
          Text(
            formatVnd(pending.amount),
            key: const Key('wallet-pending-amount'),
            style: NordicTypography.numberSmall,
          ),
          const SizedBox(height: NordicSpace.xxs),
          Text(
            'Gửi lúc ${formatVietnamDateTime(pending.requestedAt)}',
            style: NordicTypography.bodySmall,
          ),
          const SizedBox(height: NordicSpace.sm),
          NordicButton(
            key: const Key('wallet-cancel'),
            label: 'Huỷ yêu cầu',
            variant: NordicButtonVariant.secondary,
            onPressed: _busy ? null : () => _cancel(pending),
          ),
        ],
      ),
    );
  }

  /// The warning (always shown), the amount field and the errors.
  Widget _form(Wallet wallet) {
    final locked = wallet.pendingWithdrawal != null;
    return ErrorShake(
      errors: _errors,
      child: NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text('Rút tiền ứng', style: NordicTypography.titleSmall),
            const SizedBox(height: NordicSpace.xs),
            Container(
              key: const Key('wallet-warning'),
              padding: const EdgeInsets.all(NordicSpace.sm),
              decoration: const BoxDecoration(
                color: NordicColors.amberSoft,
                borderRadius: NordicRadius.tileAll,
              ),
              child: Text(withdrawalSingleRequestWarning, style: _warningStyle),
            ),
            const SizedBox(height: NordicSpace.xs),
            const Text(
              withdrawalAdvanceHint,
              key: Key('wallet-hint'),
              style: NordicTypography.bodySmall,
            ),
            const SizedBox(height: NordicSpace.sm),
            const Text(
              'Số tiền muốn rút (đ)',
              style: NordicTypography.bodySmall,
            ),
            const SizedBox(height: NordicSpace.xxs),
            NordicTextInput(
              key: const Key('wallet-amount'),
              controller: _amount,
              hintText: 'Ví dụ: 100.000',
              keyboardType: TextInputType.number,
              readOnly: locked || _busy,
              onChanged: (_) {
                if (_fieldError != null) setState(() => _fieldError = null);
              },
            ),
            if (_fieldError != null)
              _errorText(_fieldError!, const Key('wallet-field-error')),
            if (_actionError != null)
              _errorText(_actionError!, const Key('wallet-error')),
            if (locked)
              const Padding(
                padding: EdgeInsets.only(top: NordicSpace.xs),
                child: Text(
                  'Bạn đang có một yêu cầu chờ duyệt nên chưa gửi thêm được.',
                  key: Key('wallet-locked'),
                  style: NordicTypography.bodySmall,
                ),
              )
            else if (wallet.available <= 0)
              const Padding(
                padding: EdgeInsets.only(top: NordicSpace.xs),
                child: Text(
                  'Hiện chưa có tiền có thể rút. Tiền công của ca được rút sau 24 giờ kể từ lúc hoàn thành.',
                  key: Key('wallet-nothing'),
                  style: NordicTypography.bodySmall,
                ),
              ),
          ],
        ),
      ),
    );
  }

  Widget _errorText(String text, Key key) {
    return Padding(
      padding: const EdgeInsets.only(top: NordicSpace.xs),
      child: Text(
        text,
        key: key,
        style: NordicTypography.bodySmall.copyWith(color: NordicColors.error),
      ),
    );
  }
}
