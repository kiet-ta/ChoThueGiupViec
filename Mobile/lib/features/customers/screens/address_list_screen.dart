import 'package:flutter/material.dart';
import '../../../core/theme/nordic_colors.dart';
import '../../../core/theme/nordic_typography.dart';
import '../../../core/widgets/eyebrow_badge.dart';
import '../../../core/widgets/nordic_button.dart';
import '../models/customer_address.dart';
import '../services/customer_address_service.dart';
import '../widgets/address_card.dart';
import 'address_form_screen.dart';

/// Screen listing customer addresses with support for editing, deleting,
/// setting default address, and navigating to address creation.
class AddressListScreen extends StatefulWidget {
  final CustomerAddressService? addressService;

  const AddressListScreen({
    super.key,
    this.addressService,
  });

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
        _errorMessage = e.toString().replaceAll('Exception: ', '').replaceAll('HttpException: ', '');
        _isLoading = false;
      });
    }
  }

  Future<void> _navigateToAdd() async {
    final result = await Navigator.push<bool>(
      context,
      MaterialPageRoute(
        builder: (context) => AddressFormScreen(
          addressService: _service,
        ),
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
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.circular(16.0)),
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
      backgroundColor: NordicColors.canvas,
      appBar: AppBar(
        title: Text('Sổ Địa Chỉ', style: NordicTypography.h3),
        centerTitle: true,
      ),
      body: Center(
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 390.0),
          child: Column(
            children: [
              Expanded(
                child: RefreshIndicator(
                  onRefresh: _loadAddresses,
                  color: NordicColors.primary,
                  child: ListView(
                    padding: const EdgeInsets.symmetric(horizontal: 16.0, vertical: 12.0),
                    children: [
                      const Align(
                        alignment: Alignment.centerLeft,
                        child: EyebrowBadge(text: 'QUẢN LÝ ĐỊA ĐIỂM DỊCH VỤ'),
                      ),
                      const SizedBox(height: 8.0),
                      Text(
                        'Địa chỉ của bạn',
                        style: NordicTypography.h1,
                      ),
                      const SizedBox(height: 4.0),
                      Text(
                        'Lưu địa chỉ để việc đặt dịch vụ giúp việc diễn ra chính xác và nhanh chóng.',
                        style: NordicTypography.bodyRegular,
                      ),
                      const SizedBox(height: 16.0),

                      if (_isLoading)
                        const Padding(
                          padding: EdgeInsets.symmetric(vertical: 40.0),
                          child: Center(
                            child: CircularProgressIndicator(
                              valueColor: AlwaysStoppedAnimation<Color>(NordicColors.primary),
                            ),
                          ),
                        )
                      else if (_errorMessage != null)
                        Container(
                          padding: const EdgeInsets.all(16.0),
                          decoration: BoxDecoration(
                            color: NordicColors.errorContainer,
                            borderRadius: BorderRadius.circular(12.0),
                          ),
                          child: Column(
                            children: [
                              Text(
                                _errorMessage!,
                                style: NordicTypography.bodyRegular.copyWith(
                                  color: NordicColors.error,
                                ),
                              ),
                              const SizedBox(height: 8.0),
                              NordicButton(
                                label: 'Thử lại',
                                variant: NordicButtonVariant.secondary,
                                onPressed: _loadAddresses,
                              ),
                            ],
                          ),
                        )
                      else if (_addresses.isEmpty)
                        Container(
                          padding: const EdgeInsets.symmetric(vertical: 48.0, horizontal: 20.0),
                          alignment: Alignment.center,
                          child: Column(
                            children: [
                              Container(
                                width: 64.0,
                                height: 64.0,
                                decoration: BoxDecoration(
                                  color: NordicColors.surfaceSubtle,
                                  shape: BoxShape.circle,
                                ),
                                child: const Icon(
                                  Icons.location_off_outlined,
                                  color: NordicColors.secondaryAccent,
                                  size: 32.0,
                                ),
                              ),
                              const SizedBox(height: 16.0),
                              Text('Chưa Có Địa Chỉ Nào', style: NordicTypography.h3),
                              const SizedBox(height: 8.0),
                              Text(
                                'Bạn chưa lưu địa chỉ nào. Hãy bấm "Thêm Địa Chỉ Mới" bên dưới để bắt đầu!',
                                textAlign: TextAlign.center,
                                style: NordicTypography.bodyRegular,
                              ),
                            ],
                          ),
                        )
                      else
                        ..._addresses.map(
                          (addr) => Padding(
                            padding: const EdgeInsets.only(bottom: 12.0),
                            child: AddressCard(
                              address: addr,
                              onEdit: () => _navigateToEdit(addr),
                              onDelete: () => _confirmDelete(addr),
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ),

              // Bottom sticky CTA button
              Container(
                padding: const EdgeInsets.all(16.0),
                decoration: BoxDecoration(
                  color: NordicColors.surface,
                  border: const Border(
                    top: BorderSide(color: NordicColors.border),
                  ),
                ),
                child: SafeArea(
                  top: false,
                  child: NordicButton(
                    label: 'Thêm Địa Chỉ Mới',
                    icon: const Icon(Icons.add_location_alt_outlined, size: 18.0),
                    width: double.infinity,
                    onPressed: _navigateToAdd,
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
