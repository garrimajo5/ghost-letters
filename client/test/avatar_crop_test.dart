import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/features/profile/avatar_crop.dart';

/// Обрезка аватарки: что попадает в круг и как фото не «уезжает» из него.
void main() {
  const square = Rect.fromLTWH(20, 100, 300, 300);

  Matrix4 place(double dx, double dy, double s) => Matrix4.translationValues(dx, dy, 0)..multiply(Matrix4.diagonal3Values(s, s, s));

  test('в квадрат попадает нужный кусок фото', () {
    // Фото 1200×600 уменьшено вдвое и сдвинуто так, что его точка (400, 100) — в левом верхнем углу квадрата.
    final source = cropSource(place(20 - 200, 100 - 50, 0.5), square);
    expect(source.left, closeTo(400, 0.001));
    expect(source.top, closeTo(100, 0.001));
    expect(source.width, closeTo(600, 0.001));
    expect(source.height, closeTo(600, 0.001));
  });

  test('слишком мелкое фото увеличивается, чтобы закрыть круг', () {
    const image = Size(1200, 600);
    final m = clampToSquare(place(20, 100, 0.1), image, square);
    expect(m.storage[0], closeTo(0.5, 0.0001), reason: '300 / 600 — высота фото равна стороне квадрата');
    final source = cropSource(m, square);
    expect(source.top, greaterThanOrEqualTo(-0.001));
    expect(source.bottom, lessThanOrEqualTo(600.001));
  });

  test('фото нельзя утащить так, чтобы в круге появилась пустота', () {
    const image = Size(1200, 600);
    final m = clampToSquare(place(5000, -3000, 1), image, square);
    final source = cropSource(m, square);
    expect(source.left, greaterThanOrEqualTo(-0.001));
    expect(source.top, greaterThanOrEqualTo(-0.001));
    expect(source.right, lessThanOrEqualTo(1200.001));
    expect(source.bottom, lessThanOrEqualTo(600.001));
  });

  testWidgets('экран обрезки: не картинка — понятная ошибка', (tester) async {
    await tester.pumpWidget(MaterialApp(home: AvatarCropScreen(bytes: Uint8List.fromList(const [1, 2, 3]))));
    await tester.runAsync(() => Future<void>.delayed(const Duration(milliseconds: 200)));
    await tester.pump();
    expect(find.textContaining('Не получилось открыть картинку'), findsOneWidget);
  });
}
