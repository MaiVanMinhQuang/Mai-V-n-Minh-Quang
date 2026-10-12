import 'dart:convert';

import 'package:http/http.dart' as http;

class ToeicApi {
  ToeicApi({String? baseUrl})
    : baseUrl =
          (baseUrl ??
                  const String.fromEnvironment(
                    'API_BASE_URL',
                    defaultValue: 'http://10.0.2.2:5055/api',
                  ))
              .replaceFirst(RegExp(r'/+$'), '');

  final String baseUrl;
  final http.Client _client = http.Client();

  Future<dynamic> get(String path) => _send('GET', path);

  Future<dynamic> post(String path, {String? attemptKey}) =>
      _send('POST', path, attemptKey: attemptKey);

  Future<dynamic> put(
    String path, {
    required Map<String, dynamic> body,
    String? attemptKey,
  }) => _send('PUT', path, body: body, attemptKey: attemptKey);

  Future<dynamic> _send(
    String method,
    String path, {
    Map<String, dynamic>? body,
    String? attemptKey,
  }) async {
    final headers = <String, String>{'Accept': 'application/json'};
    if (attemptKey != null && attemptKey.isNotEmpty) {
      headers['X-Attempt-Key'] = attemptKey;
    }
    if (body != null) headers['Content-Type'] = 'application/json';
    final response = await _client.send(
      http.Request(method, Uri.parse('$baseUrl$path'))
        ..headers.addAll(headers)
        ..body = body == null ? '' : jsonEncode(body),
    );
    final responseBody = await response.stream.bytesToString();
    dynamic payload;
    if (responseBody.isNotEmpty) {
      try {
        payload = jsonDecode(responseBody);
      } on FormatException {
        payload = responseBody;
      }
    }

    if (response.statusCode < 200 || response.statusCode >= 300) {
      Object? error;
      if (payload is Map<String, dynamic>) {
        error = payload['message'];
        final errorPayload = payload['error'];
        final resultPayload = payload['result'];
        if (error == null && errorPayload is Map<String, dynamic>) {
          error = errorPayload['message'];
        }
        if (error == null && resultPayload is Map<String, dynamic>) {
          error = resultPayload['message'];
        }
      }
      throw Exception(error ?? 'Lỗi máy chủ (${response.statusCode}).');
    }

    if (payload is Map<String, dynamic> && payload.containsKey('result')) {
      return payload['result'];
    }
    return payload;
  }

  void close() => _client.close();
}
