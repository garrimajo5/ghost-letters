import 'dart:typed_data';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:image_picker/image_picker.dart';

/// Выбранная картинка: байты и имя файла.
typedef PickedImage = ({Uint8List bytes, String name});

/// Выбор фото из галереи, сразу уменьшенного до 512 px (в тестах подменяется).
final avatarPickerProvider = Provider<Future<PickedImage?> Function()>((ref) => () async {
      final file = await ImagePicker().pickImage(source: ImageSource.gallery, maxWidth: 512, maxHeight: 512, imageQuality: 85);
      if (file == null) return null;
      return (bytes: await file.readAsBytes(), name: file.name);
    });
