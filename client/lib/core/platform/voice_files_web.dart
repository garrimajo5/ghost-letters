import 'dart:js_interop';
import 'dart:typed_data';

import 'package:audioplayers/audioplayers.dart';
import 'package:dio/dio.dart';
import 'package:web/web.dart' as web;

/// В браузере путь не нужен: запись возвращается ссылкой blob:.
Future<String> recordingPath() async => '';

/// Байты записи берём по blob-ссылке, которую вернул браузер.
Future<MultipartFile> recordingUpload(String blobUrl, String mime) async {
  final r = await Dio().get<List<int>>(blobUrl, options: Options(responseType: ResponseType.bytes));
  final ext = mime.contains('webm') ? 'webm' : (mime.contains('ogg') ? 'ogg' : 'm4a');
  return MultipartFile.fromBytes(r.data!, filename: 'voice.$ext', contentType: DioMediaType.parse(mime));
}

final _cache = <String, String>{};

/// Голосовое скачивается с токеном (обычная ссылка без него не откроется) и играется по blob-ссылке.
Future<Source> playableVoice(String mediaId, Future<Uint8List> Function() download) async {
  final url = _cache[mediaId] ??= web.URL.createObjectURL(web.Blob(<JSAny>[(await download()).toJS].toJS));
  return UrlSource(url);
}
