import 'dart:io';
import 'dart:typed_data';

import 'package:audioplayers/audioplayers.dart';
import 'package:dio/dio.dart';
import 'package:path_provider/path_provider.dart';

/// Куда писать запись с микрофона.
Future<String> recordingPath() async {
  final dir = await getTemporaryDirectory();
  return '${dir.path}/voice-${DateTime.now().millisecondsSinceEpoch}.m4a';
}

/// Готовая запись для отправки на сервер.
Future<MultipartFile> recordingUpload(String path, String mime) =>
    MultipartFile.fromFile(path, filename: 'voice.m4a', contentType: DioMediaType.parse(mime));

final _cache = <String, String>{};

/// Источник для проигрывания: скачанное однажды голосовое лежит во временном файле.
Future<Source> playableVoice(String mediaId, Future<Uint8List> Function() download) async {
  var path = _cache[mediaId];
  if (path == null || !File(path).existsSync()) {
    final dir = await getTemporaryDirectory();
    path = '${dir.path}/media-$mediaId.m4a';
    await File(path).writeAsBytes(await download());
    _cache[mediaId] = path;
  }
  return DeviceFileSource(path);
}
