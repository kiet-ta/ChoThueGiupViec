import 'dart:convert';
import 'dart:io';
import '../../identity/services/token_storage.dart';
import '../models/favorite_worker.dart';

/// Service managing customer favorite workers matching .spec/contracts/customers.md §2.3.
class FavoriteWorkerService {
  final String baseUrl;
  final HttpClient _httpClient;
  final TokenStorage _tokenStorage;
  final bool isMock;

  final List<FavoriteWorker> _mockStore = [];

  FavoriteWorkerService({
    String? baseUrl,
    HttpClient? httpClient,
    TokenStorage? tokenStorage,
    this.isMock = false,
  })  : baseUrl = baseUrl ?? 'http://10.0.2.2:5004',
        _httpClient = httpClient ?? HttpClient(),
        _tokenStorage = tokenStorage ?? TokenStorage() {
    _seedDefaultMockData();
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

  /// GET /api/customers/me/favorite-workers
  Future<List<FavoriteWorker>> getFavoriteWorkers() async {
    if (!isMock) {
      try {
        final uri = Uri.parse('$baseUrl/api/customers/me/favorite-workers');
        final request = await _httpClient.getUrl(uri).timeout(const Duration(milliseconds: 600));
        _setAuthHeader(request);

        final response = await request.close().timeout(const Duration(milliseconds: 600));
        if (response.statusCode == 200) {
          final body = await response.transform(utf8.decoder).join();
          final map = jsonDecode(body) as Map<String, dynamic>;
          final data = map['data'] as List<dynamic>? ?? [];
          return data
              .map((item) => FavoriteWorker.fromJson(item as Map<String, dynamic>))
              .toList();
        }
      } catch (_) {
        // Fallback
      }
    }

    // Sort by addedAt descending
    final sorted = List<FavoriteWorker>.from(_mockStore);
    sorted.sort((a, b) => (b.addedAt ?? DateTime(2000)).compareTo(a.addedAt ?? DateTime(2000)));
    return sorted;
  }

  /// PUT /api/customers/me/favorite-workers/{workerId} (Idempotent)
  Future<FavoriteWorker> addFavoriteWorker(int workerId) async {
    if (!isMock) {
      try {
        final uri = Uri.parse('$baseUrl/api/customers/me/favorite-workers/$workerId');
        final request = await _httpClient.putUrl(uri).timeout(const Duration(milliseconds: 600));
        _setAuthHeader(request);

        final response = await request.close().timeout(const Duration(milliseconds: 600));
        if (response.statusCode == 200) {
          final body = await response.transform(utf8.decoder).join();
          final map = jsonDecode(body) as Map<String, dynamic>;
          final data = map['data'] as Map<String, dynamic>;
          final worker = FavoriteWorker.fromJson(data);
          _upsertMockWorker(worker);
          return worker;
        }
      } catch (_) {
        // Fallback
      }
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
      try {
        final uri = Uri.parse('$baseUrl/api/customers/me/favorite-workers/$workerId');
        final request = await _httpClient.deleteUrl(uri).timeout(const Duration(milliseconds: 600));
        _setAuthHeader(request);

        final response = await request.close().timeout(const Duration(milliseconds: 600));
        if (response.statusCode != 200 && response.statusCode != 204) {
          final body = await response.transform(utf8.decoder).join();
          throw HttpException('Bỏ yêu thích thất bại: $body');
        }
      } catch (e) {
        if (e is HttpException) rethrow;
      }
    }

    _mockStore.removeWhere((w) => w.workerId == workerId);
  }

  void _upsertMockWorker(FavoriteWorker worker) {
    final idx = _mockStore.indexWhere((w) => w.workerId == worker.workerId);
    if (idx != -1) {
      _mockStore[idx] = worker;
    } else {
      _mockStore.insert(0, worker);
    }
  }

  void _setAuthHeader(HttpClientRequest request) {
    final token = _tokenStorage.accessToken;
    if (token != null && token.isNotEmpty) {
      request.headers.set('Authorization', 'Bearer $token');
    }
  }
}
