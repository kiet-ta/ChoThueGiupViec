import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/theme/nordic_spacing.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/nordic_page.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/customer_address.dart';
import '../services/customer_address_service.dart';
import '../widgets/address_card.dart';
import 'address_form_screen.dart';

/// Screen listing customer addresses with support for editing, deleting,
/// setting default address, and navigating to address creation. MOB-UI-14: one pressable card
/// per address, "Thêm địa chỉ" in the action bar (`Mobile/DESIGN-SYSTEM.md`).
class AddressListScreen extends StatefulWidget {
  final CustomerAddressService? addressService;

  const AddressListScreen({super.key, this.addressService});

  @override
  State<AddressListScreen> createState() => _AddressListScreenState();
}

class _AddressListScreenState extends State<AddressListScreen> {
  late final CustomerAddressService _service;
  List<CustomerAddress> _addresses = [];
  bool _isLoading = true;
  String? _errorMessage;

  @override
  void initState() {
    super.initState();
    _service = widget.addressService ?? CustomerAddressService();
    _loadAddresses();
  }

  Future<void> _loadAddresses() async {
    setState(() {
      _isLoading = true;
      _errorMessage = null;
    });

    try {
      final list = await _service.getAddresses();
      if (!mounted) return;
      setState(() {
        _addresses = list;
        _isLoading = false;
      });
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _errorMessage = e
            .toString()
            .replaceAll('Exception: ', '')
            .replaceAll('HttpException: ', '');
        _isLoading = false;
      });
    }
  }

  Future<void> _navigateToAdd() async {
    final result = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (context) => AddressFormScreen(addressService: _service),
      ),
    );

    if (result == true) {
      _loadAddresses();
    }
  }

  Future<void> _navigateToEdit(CustomerAddress address) async {
    final result = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (context) => AddressFormScreen(
          initialAddress: address,
          addressService: _service,
        ),
      ),
    );

    if (result == true) {
      _loadAddresses();
    }
  }

  Future<void> _confirmDelete(CustomerAddress address) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogCtx) => AlertDialog(
        backgroundColor: NordicColors.surface,
        title: Text('Xoá địa chỉ này?', style: NordicTypography.h3),
        content: Text(
          '"${address.label}" sẽ bị xoá khỏi danh sách.',
          style: NordicTypography.bodyRegular,
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.of(dialogCtx).pop(false),
            child: const Text('Huỷ'),
          ),
          ElevatedButton(
            onPressed: () => Navigator.of(dialogCtx).pop(true),
            style: ElevatedButton.styleFrom(
              backgroundColor: NordicColors.error,
              foregroundColor: Colors.white,
            ),
            child: const Text('Xoá'),
          ),
        ],
      ),
    );

    if (confirmed == true) {
      try {
        await _service.deleteAddress(address.addressId);
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Đã xoá địa chỉ "${address.label}".'),
            backgroundColor: NordicColors.primary,
          ),
        );
        _loadAddresses();
      } catch (e) {
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: const Text('Chưa xoá được địa chỉ. Thử lại nhé.'),
            backgroundColor: NordicColors.error,
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return NordicPage.pushed(
      title: 'Địa chỉ của tôi',
      onRefresh: _loadAddresses,
      actions: [
        NordicButton(
          label: 'Thêm địa chỉ',
          icon: const Icon(Icons.add, size: 18.0),
          variant: _addresses.isEmpty && !_isLoading
              ? NordicButtonVariant.primary
              : NordicButtonVariant.secondary,
          onPressed: _navigateToAdd,
        ),
      ],
      children: [
        if (_isLoading)
          const SizedBox(height: 320, child: LoadingStateWidget())
        else if (_errorMessage != null)
          ErrorStateWidget(message: _errorMessage!, onRetry: _loadAddresses)
        else if (_addresses.isEmpty)
          const EmptyStateWidget(
            title: 'Chưa có địa chỉ nào',
            subtitle: 'Thêm địa chỉ để đặt ca và xem giá.',
            icon: Icons.location_on_outlined,
          )
        else
          for (var i = 0; i < _addresses.length; i++) ...[
            if (i > 0) const SizedBox(height: NordicSpace.sm),
            AddressCard(
              address: _addresses[i],
              onEdit: () => _navigateToEdit(_addresses[i]),
              onDelete: () => _confirmDelete(_addresses[i]),
            ),
          ],
      ],
    );
  }
}
