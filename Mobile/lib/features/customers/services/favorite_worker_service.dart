import 'dart:io';
import '../../identity/services/api_error.dart';
import '../../identity/services/token_storage.dart';
import '../models/favorite_worker.dart';

/// Service managing customer favorite workers matching .spec/contracts/customers.md §2.3.
///
/// [isMock] (only when a caller, a test, passes it): in-memory store, no network.
/// Off (the default, the real app): server or network errors are thrown as [ApiException], never hidden.
class FavoriteWorkerService {
  final String baseUrl;
  final HttpClient _httpClient;
  final TokenStorage _tokenStorage;
  final bool isMock;

  // In-memory store, used only in mock mode
  final List<FavoriteWorker> _mockStore = [];

  FavoriteWorkerService({
    String? baseUrl,
    HttpClient? httpClient,
    TokenStorage? tokenStorage,
    this.isMock = false,
  })  : baseUrl = baseUrl ?? 'http://10.0.2.2:5004',
        _httpClient = httpClient ?? HttpClient(),
        _tokenStorage = tokenStorage ?? TokenStorage() {
    if (isMock) _seedDefaultMockData();
  }

  void _seedDefaultMockData() {
    _mockStore.addAll([
      FavoriteWorker(
        workerId: 101,
        fullName: 'Nguyễn Thị Mai',
        ratingAvg: 4.9,
        completedJobs: 142,
        workStatus: 'ACTIVE',
        addedAt: DateTime.now().subtract(const Duration(days: 7)),
      ),
      FavoriteWorker(
        workerId: 102,
        fullName: 'Trần Văn Hùng',
        ratingAvg: 4.8,
        completedJobs: 98,
        workStatus: 'BUSY',
        addedAt: DateTime.now().subtract(const Duration(days: 3)),
      ),
      FavoriteWorker(
        workerId: 103,
        fullName: 'Lê Thị Cúc',
        ratingAvg: 5.0,
        completedJobs: 65,
        workStatus: 'ACTIVE',
        addedAt: DateTime.now().subtract(const Duration(days: 1)),
      ),
    ]);
  }

  List<FavoriteWorker> get mockStore => _mockStore;

  void clearMockStore() {
    _mockStore.clear();
  }

  Future<Map<String, dynamic>> _call(String method, String path, String fallbackMessage) => apiRequest(
        _httpClient,
        method,
        Uri.parse('$baseUrl/api/customers/me/favorite-workers$path'),
        bearerToken: _tokenStorage.accessToken,
        fallbackMessage: fallbackMessage,
      );

  /// GET /api/customers/me/favorite-workers
  Future<List<FavoriteWorker>> getFavoriteWorkers() async {
    if (!isMock) {
      final envelope = await _call('GET', '', 'Không tải được danh sách thợ quen.');
      final data = envelope['data'] as List<dynamic>? ?? [];
      return data.map((item) => FavoriteWorker.fromJson(item as Map<String, dynamic>)).toList();
    }

    // Sort by addedAt descending
    final sorted = List<FavoriteWorker>.from(_mockStore);
    sorted.sort((a, b) => (b.addedAt ?? DateTime(2000)).compareTo(a.addedAt ?? DateTime(2000)));
    return sorted;
  }

  /// PUT /api/customers/me/favorite-workers/{workerId} (Idempotent)
  Future<FavoriteWorker> addFavoriteWorker(int workerId) async {
    if (!isMock) {
      final envelope = await _call('PUT', '/$workerId', 'Thêm thợ quen thất bại.');
      return FavoriteWorker.fromJson(envelope['data'] as Map<String, dynamic>);
    }

    // Mock addition
    final existing = _mockStore.where((w) => w.workerId == workerId).firstOrNull;
    if (existing != null) return existing;

    final newWorker = FavoriteWorker(
      workerId: workerId,
      fullName: 'Chuyên viên #$workerId',
      ratingAvg: 4.85,
      completedJobs: 50,
      workStatus: 'ACTIVE',
      addedAt: DateTime.now(),
    );
    _mockStore.insert(0, newWorker);
    return newWorker;
  }

  /// DELETE /api/customers/me/favorite-workers/{workerId} (Idempotent)
  Future<void> removeFavoriteWorker(int workerId) async {
    if (!isMock) {
      await _call('DELETE', '/$workerId', 'Bỏ yêu thích thất bại.');
      return;
    }

    _mockStore.removeWhere((w) => w.workerId == workerId);
  }
}
