import 'dart:async';

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
import '../../../core/widgets/pressable.dart';
import '../../../core/widgets/status_pill.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../logic/bank_logic.dart';
import '../services/worker_bank_service.dart';

/// The freelancer's payout account (LEDGER-20, LEDGER-28); contract workers.md (`GET`, `PUT /workers/me/bank` and
/// `POST /workers/me/bank-change-requests`), decision D19. A worker has ONE account at a time. The first one is saved at once; a change is a
/// REQUEST that an Admin approves, and 15 days after a request was sent no new one can be sent. It is a sandbox account: no real transfer is made.
/// A new withdrawal request copies this account, and the Admin sees it in the approval list.
/// Built with the shared Nordic kit only (`Mobile/DESIGN-SYSTEM.md`, `DESIGN-SYSTEM-WORKER.md`).
class BankAccountScreen extends StatefulWidget {
  final IWorkerBankService? service;

  const BankAccountScreen({super.key, this.service});

  @override
  State<BankAccountScreen> createState() => _BankAccountScreenState();
}

class _BankAccountScreenState extends State<BankAccountScreen> {
  late final IWorkerBankService _service;
  final _bankName = TextEditingController();
  final _account = TextEditingController();

  WorkerBank? _bank;
  String? _loadError;
  bool _forbidden = false;
  bool _loading = true;

  String? _nameError;
  String? _accountError;
  String? _saveError;
  String? _notice;
  bool _saving = false;
  int _errors = 0;

  /// The form of a change request is open (an account exists and the worker may send a request).
  bool _changing = false;

  /// A second tap while the save runs must not send another one.
  bool _inFlight = false;

  @override
  void initState() {
    super.initState();
    _service = widget.service ?? WorkerBankService();
    _load();
  }

  @override
  void dispose() {
    _bankName.dispose();
    _account.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _loadError = null;
      _forbidden = false;
    });
    try {
      final bank = await _service.getBank();
      if (!mounted) return;
      setState(() {
        _bank = bank;
        _changing = _changing && bankStage(bank) == BankStage.canRequest;
        _loading = false;
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _bank = null;
        _loadError = bankFailureMessage(e);
        _forbidden = e.isForbidden;
        _loading = false;
      });
    }
  }

  /// Saves the first account at once, or sends a change request after the worker confirms the 15-day lock.
  Future<void> _submit() async {
    final bank = _bank;
    if (_inFlight || bank == null) return;
    final nameProblem = bankNameError(_bankName.text);
    final accountProblem = accountError(_account.text);
    if (nameProblem != null || accountProblem != null) {
      setState(() {
        _nameError = nameProblem;
        _accountError = accountProblem;
        _saveError = null;
        _notice = null;
        _errors++;
      });
      return;
    }

    final change = bankStage(bank) == BankStage.canRequest;
    if (change && !await _confirmChange()) return;
    if (!mounted) return;

    _inFlight = true;
    setState(() {
      _saving = true;
      _nameError = null;
      _accountError = null;
      _saveError = null;
      _notice = null;
    });
    try {
      final name = _bankName.text.trim();
      final account = normalizeAccount(_account.text);
      final saved = change
          ? await _service.requestChange(bankName: name, bankAccountNo: account)
          : await _service.saveBank(bankName: name, bankAccountNo: account);
      if (!mounted) return;
      setState(() {
        _bank = saved;
        _changing = false;
        _bankName.clear();
        _account.clear();
        _notice = change
            ? 'Đã gửi đơn đổi tài khoản. Admin sẽ duyệt; trong 15 ngày bạn không gửi được đơn khác.'
            : 'Đã lưu tài khoản nhận tiền.';
      });
    } on ApiException catch (e) {
      if (!mounted) return;
      setState(() {
        _saveError = bankFailureMessage(e);
        _errors++;
      });
      // The state moved under the worker (a request is pending, the lock started, an account exists): show the real one.
      if (e.statusCode == 409) unawaited(_load());
    } finally {
      _inFlight = false;
      if (mounted) setState(() => _saving = false);
    }
  }

  /// The warning step before a request is sent: after it, 15 days without another one.
  Future<bool> _confirmChange() async {
    final ok = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        key: const Key('bank-confirm'),
        title: const Text('Gửi đơn đổi tài khoản?'),
        content: Text(
          '$bankChangeWarning\n\nTài khoản mới: ${_bankName.text.trim()} · ${maskedAccount(_account.text)}',
        ),
        actions: [
          TextButton(
            key: const Key('bank-confirm-no'),
            onPressed: () => Navigator.of(context).pop(false),
            child: const Text('Xem lại'),
          ),
          TextButton(
            key: const Key('bank-confirm-yes'),
            onPressed: () => Navigator.of(context).pop(true),
            child: const Text('Gửi đơn'),
          ),
        ],
      ),
    );
    return ok ?? false;
  }

  @override
  Widget build(BuildContext context) {
    final bank = _bank;
    final stage = bank == null ? null : bankStage(bank);
    final formOpen =
        stage == BankStage.firstAccount ||
        (stage == BankStage.canRequest && _changing);
    return NordicPage.pushed(
      title: 'Tài khoản nhận tiền',
      onRefresh: _load,
      actions: formOpen
          ? [
              NordicButton(
                key: const Key('bank-save'),
                label: stage == BankStage.firstAccount
                    ? 'Lưu tài khoản'
                    : 'Gửi đơn đổi tài khoản',
                isLoading: _saving,
                onPressed: _saving ? null : _submit,
              ),
            ]
          : null,
      children: _content(),
    );
  }

  List<Widget> _content() {
    if (_loading && _bank == null) {
      return const [
        SizedBox(
          height: 240.0,
          child: LoadingStateWidget(message: 'Đang tải tài khoản…'),
        ),
      ];
    }
    final bank = _bank;
    if (bank == null) {
      return [
        ErrorStateWidget(
          message: _loadError ?? 'Không tải được tài khoản nhận tiền.',
          // Retrying does not help an agency worker; the explanation is the whole answer.
          onRetry: _forbidden ? null : _load,
        ),
      ];
    }
    return [
      _summary(bank),
      const SizedBox(height: NordicSpace.sm),
      if (_notice != null) ...[
        NordicCard(
          child: Text(
            _notice!,
            key: const Key('bank-notice'),
            style: NordicTypography.bodyRegular,
          ),
        ),
        const SizedBox(height: NordicSpace.sm),
      ],
      ...switch (bankStage(bank)) {
        BankStage.firstAccount => [_form()],
        BankStage.canRequest => _changing ? _changeForm() : _requestChange(),
        BankStage.pending => [_pendingCard(bank.pendingRequest!)],
        BankStage.locked => [_lockedCard(bank)],
      },
    ];
  }

  /// An account exists and no lock: the way to a change request.
  List<Widget> _requestChange() => [
    NordicButton(
      key: const Key('bank-open-change'),
      label: 'Gửi đơn đổi tài khoản',
      width: double.infinity,
      variant: NordicButtonVariant.secondary,
      onPressed: () => setState(() {
        _changing = true;
        _notice = null;
        _saveError = null;
        _bankName.clear();
        _account.clear();
      }),
    ),
    const SizedBox(height: NordicSpace.xs),
    Text(
      'Muốn đổi tài khoản, bạn gửi đơn và Admin duyệt.',
      key: const Key('bank-change-hint'),
      textAlign: TextAlign.center,
      style: NordicTypography.bodySmall.copyWith(
        color: NordicColors.textSecondary,
      ),
    ),
  ];

  /// The form of a change request, with the warning about the 15 days above it and a way back below.
  List<Widget> _changeForm() => [
    NordicCard(
      key: const Key('bank-change-warning'),
      child: Text(bankChangeWarning, style: NordicTypography.bodySmall),
    ),
    const SizedBox(height: NordicSpace.sm),
    _form(),
    const SizedBox(height: NordicSpace.xs),
    NordicButton(
      key: const Key('bank-cancel-change'),
      label: 'Không đổi nữa',
      width: double.infinity,
      variant: NordicButtonVariant.ghost,
      onPressed: _saving ? null : () => setState(() => _changing = false),
    ),
  ];

  /// A request waits for the Admin: the account in use stays the current one.
  Widget _pendingCard(BankChangePending pending) => NordicCard(
    key: const Key('bank-pending'),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const StatusPill(
          'Đang chờ Admin duyệt',
          dot: true,
          tone: PillTone.warn,
        ),
        const SizedBox(height: NordicSpace.xs),
        Text(
          'Tài khoản mới: ${pending.bankName} · ${maskedAccount(pending.bankAccountNo)}',
          key: const Key('bank-pending-account'),
          style: NordicTypography.titleSmall,
        ),
        const SizedBox(height: NordicSpace.xxs),
        Text(
          bankPendingNote(pending),
          key: const Key('bank-pending-note'),
          style: NordicTypography.bodySmall,
        ),
        const SizedBox(height: NordicSpace.xxs),
        Text(
          'Trong lúc chờ, các khoản rút và lương vẫn dùng tài khoản hiện tại.',
          style: NordicTypography.bodySmall.copyWith(
            color: NordicColors.textSecondary,
          ),
        ),
      ],
    ),
  );

  /// A request was sent less than 15 days ago (decided): when a new one can be sent, and what the Admin decided.
  Widget _lockedCard(WorkerBank bank) {
    final decision = bankLastDecisionNote(bank.lastRequest);
    return NordicCard(
      key: const Key('bank-locked'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            bankLockNote(bank.lastRequest, bank.changeLockedUntil),
            key: const Key('bank-lock-note'),
            style: NordicTypography.bodyRegular,
          ),
          if (decision != null) ...[
            const SizedBox(height: NordicSpace.xs),
            Text(
              decision,
              key: const Key('bank-last-decision'),
              style: NordicTypography.bodySmall.copyWith(
                color: bank.lastRequest?.isRejected == true
                    ? NordicColors.error
                    : NordicColors.textSecondary,
              ),
            ),
          ],
        ],
      ),
    );
  }

  /// The account the worker has now, or a plain "no account yet".
  Widget _summary(WorkerBank bank) {
    return NordicCard(
      key: const Key('bank-summary'),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  bank.isSet ? bank.bankName : 'Chưa có tài khoản nhận tiền',
                  key: const Key('bank-summary-title'),
                  style: NordicTypography.titleSmall,
                ),
                const SizedBox(height: NordicSpace.xxs),
                Text(
                  bank.isSet ? maskedAccount(bank.bankAccountNo) : 'Thêm tài khoản để Admin biết khoản rút được chuyển cho ai.',
                  key: const Key('bank-summary-account'),
                  style: NordicTypography.bodySmall,
                ),
              ],
            ),
          ),
          const SizedBox(width: NordicSpace.xs),
          StatusPill(
            bank.isSet ? 'Đã thiết lập' : 'Chưa có',
            dot: true,
            tone: bank.isSet ? PillTone.ok : PillTone.warn,
            textKey: const Key('bank-summary-status'),
          ),
        ],
      ),
    );
  }

  Widget _form() {
    return ErrorShake(
      errors: _errors,
      child: NordicCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              bankSandboxNote,
              key: const Key('bank-sandbox-note'),
              style: NordicTypography.bodySmall,
            ),
            const SizedBox(height: NordicSpace.md),
            const Text('Tên ngân hàng', style: NordicTypography.bodySmall),
            const SizedBox(height: NordicSpace.xxs),
            NordicTextInput(
              key: const Key('bank-name'),
              controller: _bankName,
              hintText: 'Ví dụ: Vietcombank',
              readOnly: _saving,
              onChanged: (_) {
                if (_nameError != null) setState(() => _nameError = null);
              },
            ),
            if (_nameError != null)
              _errorText(_nameError!, const Key('bank-name-error')),
            const SizedBox(height: NordicSpace.xs),
            Wrap(
              spacing: NordicSpace.xs,
              runSpacing: NordicSpace.xs,
              children: [for (final b in commonBanks) _chip(b)],
            ),
            const SizedBox(height: NordicSpace.md),
            const Text('Số tài khoản', style: NordicTypography.bodySmall),
            const SizedBox(height: NordicSpace.xxs),
            NordicTextInput(
              key: const Key('bank-account'),
              controller: _account,
              hintText: 'Ví dụ: 0011001234567',
              keyboardType: TextInputType.number,
              readOnly: _saving,
              onChanged: (_) {
                if (_accountError != null) setState(() => _accountError = null);
              },
            ),
            if (_accountError != null)
              _errorText(_accountError!, const Key('bank-account-error')),
            if (_saveError != null)
              _errorText(_saveError!, const Key('bank-error')),
          ],
        ),
      ),
    );
  }

  /// A quick pick: tapping it fills the bank name field.
  Widget _chip(String name) {
    final selected = _bankName.text.trim() == name;
    return Semantics(
      button: true,
      selected: selected,
      child: Pressable(
        child: GestureDetector(
          key: Key('bank-pick-$name'),
          behavior: HitTestBehavior.opaque,
          onTap: _saving
              ? null
              : () => setState(() {
                  _bankName.text = name;
                  _nameError = null;
                }),
          child: Container(
            constraints: const BoxConstraints(minHeight: 44.0),
            padding: const EdgeInsets.symmetric(
              horizontal: NordicSpace.md,
              vertical: NordicSpace.xs,
            ),
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: selected ? NordicColors.primary : NordicColors.surface,
              borderRadius: NordicRadius.pill,
              border: Border.all(
                color: selected ? NordicColors.primary : NordicColors.border,
              ),
            ),
            child: Text(
              name,
              style: NordicTypography.bodySmall.copyWith(
                color: selected
                    ? NordicColors.textInverse
                    : NordicColors.textTitle,
              ),
            ),
          ),
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
