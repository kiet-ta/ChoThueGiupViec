/// Standard backend response envelope conforming to Backend/Application/Common/Models/ApiResponse.cs.
class ApiResponse<T> {
  final bool success;
  final String message;
  final T? data;

  const ApiResponse({
    required this.success,
    required this.message,
    this.data,
  });

  factory ApiResponse.fromJson(
    Map<String, dynamic> json, [
    T Function(dynamic dataJson)? fromJsonT,
  ]) {
    final success = json['success'] as bool? ?? false;
    final message = json['message'] as String? ?? '';
    final rawData = json['data'];

    T? data;
    if (rawData != null && fromJsonT != null) {
      data = fromJsonT(rawData);
    } else if (rawData != null && rawData is T) {
      data = rawData;
    }

    return ApiResponse<T>(
      success: success,
      message: message,
      data: data,
    );
  }

  Map<String, dynamic> toJson([Object? Function(T data)? toJsonT]) {
    return {
      'success': success,
      'message': message,
      if (data != null && toJsonT != null)
        'data': toJsonT(data as T)
      else if (data != null)
        'data': data
      else
        'data': null,
    };
  }
}
