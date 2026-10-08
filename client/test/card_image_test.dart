import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/widgets/common.dart';

/// Карты декодируются под размер на экране, а не в исходные 512×512.
void main() {
  test('размер декодирования: по экрану, шагами по 64 px, не больше исходника', () {
    expect(CardImage.decodeSize(56, 2.625), 192); // 147 px → 192
    expect(CardImage.decodeSize(56, 3), 192); // 168 px → 192
    expect(CardImage.decodeSize(20, 1), 64); // минимум
    expect(CardImage.decodeSize(160, 3), 512); // 480 → 512
    expect(CardImage.decodeSize(400, 3), 512); // больше исходника не нужно
  });

  testWidgets('CardImage просит у движка уменьшенную картинку', (tester) async {
    tester.view.devicePixelRatio = 2;
    addTearDown(tester.view.reset);
    await tester.pumpWidget(const MaterialApp(home: Center(child: CardImage(cardId: 'orig_0022', size: 60))));

    final image = tester.widget<Image>(find.byType(Image));
    expect(image.image, isA<ResizeImage>());
    final resize = image.image as ResizeImage;
    expect(resize.width, 128);
    expect(resize.height, 128);
  });
}
