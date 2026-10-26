import 'package:flutter/material.dart';

import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/bottom_action_bar.dart';
import '../../../core/widgets/nordic_button.dart';
import '../../../core/widgets/status_state_widget.dart';
import '../models/customer_address.dart';
import '../services/customer_address_service.dart';
import '../widgets/address_card.dart';
import 'address_form_screen.dart';

/// Screen listing customer addresses with support for editing, deleting,
/// setting default address, and navigating to address creation.
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
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(16.0),
        ),
        backgroundColor: NordicColors.surface,
        title: Text('Xác Nhận Xoá', style: NordicTypography.h3),
        content: Text(
          'Bạn có chắc chắn muốn xoá địa chỉ "${address.label}" không?',
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
            content: Text('Đã xoá địa chỉ "${address.label}" thành công.'),
            backgroundColor: NordicColors.primary,
          ),
        );
        _loadAddresses();
      } catch (e) {
        if (!mounted) return;
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text('Xoá thất bại: $e'),
            backgroundColor: NordicColors.error,
          ),
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Sổ địa chỉ')),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: RefreshIndicator(
            onRefresh: _loadAddresses,
            color: NordicColors.primary,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(20.0, 8.0, 20.0, 24.0),
              children: [
                if (_isLoading)
                  const LoadingStateWidget()
                else if (_errorMessage != null)
                  ErrorStateWidget(
                    message: _errorMessage!,
                    onRetry: _loadAddresses,
                  )
                else if (_addresses.isEmpty)
                  const EmptyStateWidget(
                    title: 'Chưa Có Địa Chỉ Nào',
                    subtitle: 'Thêm địa chỉ để đặt ca và nhận báo giá.',
                    icon: Icons.location_on_outlined,
                  )
                else
                  Material(
                    color: NordicColors.surface,
                    clipBehavior: Clip.antiAlias,
                    shape: RoundedRectangleBorder(
                      borderRadius: BorderRadius.circular(16.0),
                      side: const BorderSide(color: NordicColors.border),
                    ),
                    child: Column(
                      children: [
                        for (var i = 0; i < _addresses.length; i++) ...[
                          if (i > 0) const Divider(height: 1, thickness: 1),
                          AddressCard(
                            address: _addresses[i],
                            onEdit: () => _navigateToEdit(_addresses[i]),
                            onDelete: () => _confirmDelete(_addresses[i]),
                          ),
                        ],
                      ],
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
      bottomNavigationBar: BottomActionBar(
        children: [
          NordicButton(
            label: 'Thêm Địa Chỉ Mới',
            icon: const Icon(Icons.add, size: 18.0),
            variant: NordicButtonVariant.secondary,
            onPressed: _navigateToAdd,
          ),
        ],
      ),
    );
  }
}
