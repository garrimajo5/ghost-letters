import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/widgets/common.dart';

void main() {
  testWidgets('фото заполняет круг, обводка равномерна и не перекрыта квадратом', (tester) async {
    final bytes = await tester.runAsync(() async {
      final recorder = ui.PictureRecorder();
      Canvas(recorder).drawRect(const Rect.fromLTWH(0, 0, 100, 100), Paint()..color = Colors.red);
      final picture = recorder.endRecording();
      final image = await picture.toImage(100, 100);
      final png = await image.toByteData(format: ui.ImageByteFormat.png);
      image.dispose();
      picture.dispose();
      return png!.buffer.asUint8List();
    });
    final key = GlobalKey();
    await tester.pumpWidget(MaterialApp(
      home: Center(
        child: RepaintBoundary(
          key: key,
          child: Avatar(nickname: 'Бот', color: '#0000FF', photoBytes: bytes, size: 72, ring: Colors.green),
        ),
      ),
    ));
    await tester.runAsync(() => precacheImage(MemoryImage(bytes!), tester.element(find.byType(Avatar))));
    await tester.pump();
    final boundary = key.currentContext!.findRenderObject()! as RenderRepaintBoundary;
    final pixels = await tester.runAsync(() async {
      final image = await boundary.toImage(pixelRatio: 4);
      final rgba = await image.toByteData(format: ui.ImageByteFormat.rawRgba);
      image.dispose();
      return rgba!;
    });
    int pixel(int x, int y) {
      final offset = ((y * 4) * 288 + x * 4) * 4;
      return Color.fromARGB(pixels!.getUint8(offset + 3), pixels.getUint8(offset), pixels.getUint8(offset + 1), pixels.getUint8(offset + 2)).toARGB32();
    }

    expect(pixel(36, 36), Colors.red.toARGB32());
    expect(pixel(4, 36), Colors.red.toARGB32(), reason: 'нет цветной полосы слева');
    expect(pixel(68, 36), Colors.red.toARGB32(), reason: 'нет цветной полосы справа');
    expect(pixel(1, 36), Colors.green.toARGB32());
    expect(pixel(36, 1), Colors.green.toARGB32());
    expect(pixel(12, 12), Colors.green.toARGB32(), reason: 'картинка не перекрывает диагональ обводки');
    expect(pixel(60, 12), Colors.green.toARGB32());
    expect(pixel(0, 0) >> 24, 0, reason: 'углы обрезаны по кругу');
  });
}
