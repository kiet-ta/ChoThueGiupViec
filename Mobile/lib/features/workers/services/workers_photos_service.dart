import '../../../core/network/api_client.dart';
import '../../../core/network/api_response.dart';
import '../models/job_photo_models.dart';

/// Service for uploading and managing Before/After job photos with VoL verification.
class WorkersPhotosService {
  final ApiClient _apiClient;

  WorkersPhotosService({ApiClient? apiClient})
      : _apiClient = apiClient ?? ApiClient();

  /// Uploads and verifies a job photo (contract workers.md §2.4.1).
  Future<ApiResponse<JobPhotoDto>> uploadPhoto({
    required int assignmentId,
    required UploadJobPhotoRequest request,
  }) {
    return _apiClient.post<JobPhotoDto>(
      '/api/workers/assignments/$assignmentId/photos',
      body: request.toJson(),
      fromJson: (json) => JobPhotoDto.fromJson(json as Map<String, dynamic>),
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
