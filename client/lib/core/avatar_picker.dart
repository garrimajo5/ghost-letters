import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

/// Выбранная картинка: байты и имя файла.
typedef PickedImage = ({Uint8List bytes, String name});

/// Выбор фото из галереи (до 1600 px — дальше его обрезают под круг; в тестах подменяется).
final avatarPickerProvider = Provider<Future<PickedImage?> Function()>((ref) => () async {
      final file = await ImagePicker().pickImage(source: ImageSource.gallery, maxWidth: 1600, maxHeight: 1600, imageQuality: 90);
      if (file == null) return null;
      return (bytes: await file.readAsBytes(), name: file.name);
    });
