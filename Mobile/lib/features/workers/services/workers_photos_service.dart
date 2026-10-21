import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/job_photo_models.dart';

/// Service for uploading and managing Before/After job photos with VoL verification.
class WorkersPhotosService {
  final ApiClient _apiClient;

  WorkersPhotosService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  /// Uploads the photo as a file and has the server measure its sharpness (contract workers.md §2.4.1).
  Future<ApiResponse<JobPhotoDto>> uploadPhoto({
    required int assignmentId,
    required UploadJobPhotoRequest request,
  }) {
    return _apiClient.postMultipart<JobPhotoDto>(
      '/api/workers/assignments/$assignmentId/photos',
      fields: {
        'photoPhase': request.photoPhase,
        'angleNo': '${request.angleNo}',
      },
      files: [
        ApiUploadFile(
          field: 'file',
          fileName: request.fileName,
          contentType: request.contentType,
          bytes: request.bytes,
        ),
      ],
      fromJson: (json) => JobPhotoDto.fromJson(json as Map<String, dynamic>),
    );
  }

  /// Where and how to load a stored photo (contract workers.md §2.4.3), or null when it is not one the API serves.
  ///
  /// The file endpoint is not anonymous, so the request carries the Bearer token. A `photoUrl` that is not an `/api/`
  /// address (a client URL kept from before the upload existed) is never given the token: it would go to another host.
  ({String url, Map<String, String> headers})? photoImage(String photoUrl) {
    if (!photoUrl.startsWith('/api/')) return null;
    return (
      url: _apiClient.resolveUrl(photoUrl),
      headers: _apiClient.authHeaders(),
    );
  }

  /// Hands the finished job to the customer (POST .../submit-completion -> AWAITING_ACCEPTANCE); needs 3 BEFORE + 3 AFTER photos.
  Future<ApiResponse<void>> submitCompletion(
    int assignmentId, {
    String? notes,
  }) {
    return _apiClient.post<void>(
      '/api/workers/assignments/$assignmentId/submit-completion',
      body: {'notes': notes},
    );
  }

  /// Returns all uploaded job photos for an assignment (contract workers.md §2.4.2).
  Future<ApiResponse<List<JobPhotoDto>>> getPhotos(int assignmentId) {
    return _apiClient.get<List<JobPhotoDto>>(
      '/api/workers/assignments/$assignmentId/photos',
      fromJson: (json) {
        if (json is List) {
          return json
              .map((e) => JobPhotoDto.fromJson(e as Map<String, dynamic>))
              .toList();
        }
        return <JobPhotoDto>[];
      },
    );
  }
}
