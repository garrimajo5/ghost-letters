import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../core/theme.dart';

/// Размер готовой аватарки (квадрат, PNG).
const avatarOutputSize = 512;

/// Обрезка фото под аватарку: экран с кругом; в тестах подменяется.
final avatarCropperProvider = Provider<Future<Uint8List?> Function(BuildContext context, Uint8List bytes)>(
  (ref) => (context, bytes) => Navigator.of(context).push<Uint8List>(
        MaterialPageRoute(fullscreenDialog: true, builder: (_) => AvatarCropScreen(bytes: bytes)),
      ),
);

/// Какая часть исходной картинки (в её пикселях) видна в квадрате обрезки.
/// [transform] переводит координаты картинки в координаты экрана, [square] — квадрат обрезки на экране.
Rect cropSource(Matrix4 transform, Rect square) {
  final inverse = Matrix4.inverted(transform);
  final topLeft = MatrixUtils.transformPoint(inverse, square.topLeft);
  final bottomRight = MatrixUtils.transformPoint(inverse, square.bottomRight);
  return Rect.fromPoints(topLeft, bottomRight);
}

/// Масштаб и сдвиг, при которых картинка закрывает квадрат целиком: масштаб не меньше «закрыть квадрат»,
/// края картинки не заходят внутрь квадрата.
Matrix4 clampToSquare(Matrix4 transform, Size image, Rect square) {
  final cover = math.max(square.width / image.width, square.height / image.height);
  var scale = transform.storage[0];
  var dx = transform.getTranslation().x;
  var dy = transform.getTranslation().y;
  if (scale < cover) {
    // Увеличиваем вокруг центра квадрата.
    final k = cover / scale;
    dx = square.center.dx - (square.center.dx - dx) * k;
    dy = square.center.dy - (square.center.dy - dy) * k;
    scale = cover;
  }

  final w = image.width * scale;
  final h = image.height * scale;
  dx = dx.clamp(square.right - w, square.left).toDouble();
  dy = dy.clamp(square.bottom - h, square.top).toDouble();
  return _place(dx, dy, scale);
}

/// Сдвиг и равномерный масштаб (по всем осям — так считает и InteractiveViewer).
Matrix4 _place(double dx, double dy, double scale) =>
    Matrix4.translationValues(dx, dy, 0)..multiply(Matrix4.diagonal3Values(scale, scale, scale));

/// Вырезать квадрат [source] из картинки и уменьшить до [size]×[size] PNG.
Future<Uint8List> renderCrop(ui.Image image, Rect source, {int size = avatarOutputSize}) async {
  final recorder = ui.PictureRecorder();
  final canvas = Canvas(recorder);
  canvas.drawImageRect(
    image,
    source,
    Rect.fromLTWH(0, 0, size.toDouble(), size.toDouble()),
    Paint()..filterQuality = FilterQuality.high,
  );
  final picture = recorder.endRecording();
  final result = await picture.toImage(size, size);
  final data = await result.toByteData(format: ui.ImageByteFormat.png);
  picture.dispose();
  result.dispose();
  return data!.buffer.asUint8List();
}

/// Экран обрезки: фото под круглой рамкой — двигаете и приближаете пальцами (или кнопками), «Готово» — сохранить.
class AvatarCropScreen extends StatefulWidget {
  const AvatarCropScreen({super.key, required this.bytes});

  final Uint8List bytes;

  @override
  State<AvatarCropScreen> createState() => _AvatarCropScreenState();
}

class _AvatarCropScreenState extends State<AvatarCropScreen> {
  final _controller = TransformationController();
  ui.Image? _image;
  Rect _square = Rect.zero;
  bool _placed = false;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _decode();
  }

  Future<void> _decode() async {
    try {
      final codec = await ui.instantiateImageCodec(widget.bytes);
      final frame = await codec.getNextFrame();
      if (mounted) setState(() => _image = frame.image);
    } catch (_) {
      if (mounted) setState(() => _error = 'Не получилось открыть картинку. Выберите другую.');
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    _image?.dispose();
    super.dispose();
  }

  Size get _imageSize => Size(_image!.width.toDouble(), _image!.height.toDouble());

  /// Исходное положение: фото закрывает круг и стоит по центру.
  void _center() {
    final image = _imageSize;
    final scale = math.max(_square.width / image.width, _square.height / image.height);
    final dx = _square.center.dx - image.width * scale / 2;
    final dy = _square.center.dy - image.height * scale / 2;
    _controller.value = _place(dx, dy, scale);
  }

  void _zoom(double factor) {
    final m = _controller.value;
    final c = _square.center;
    final zoomed = Matrix4.translationValues(c.dx * (1 - factor), c.dy * (1 - factor), 0)
      ..multiply(Matrix4.diagonal3Values(factor, factor, factor))
      ..multiply(m);
    _controller.value = clampToSquare(zoomed, _imageSize, _square);
  }

  Future<void> _done() async {
    setState(() => _busy = true);
    final source = cropSource(_controller.value, _square);
    final png = await renderCrop(_image!, source);
    if (mounted) Navigator.of(context).pop(png);
  }

  @override
  Widget build(BuildContext context) {
    final image = _image;
    return Scaffold(
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        title: const Text('ФОТО ДЛЯ АВАТАРКИ'),
        actions: [
          TextButton(
            key: const Key('crop-done'),
            onPressed: image == null || _busy ? null : _done,
            child: const Text('Готово'),
          ),
        ],
      ),
      body: _error != null
          ? Center(child: Text(_error!, style: const TextStyle(color: AppColors.muted)))
          : image == null
              ? const Center(child: CircularProgressIndicator())
              : Column(children: [
                  Expanded(
                    child: LayoutBuilder(builder: (context, box) {
                      final side = math.max(120.0, math.min(box.maxWidth, box.maxHeight) - 48);
                      final square = Rect.fromCenter(center: box.biggest.center(Offset.zero), width: side, height: side);
                      if (square != _square) {
                        _square = square;
                        if (!_placed) {
                          _placed = true;
                          _center();
                        } else {
                          // Повернули экран: поправляем после кадра (во время сборки менять нельзя).
                          WidgetsBinding.instance.addPostFrameCallback((_) {
                            if (mounted) _controller.value = clampToSquare(_controller.value, _imageSize, _square);
                          });
                        }
                      }
                      return Stack(fit: StackFit.expand, children: [
                        InteractiveViewer(
                          key: const Key('crop-viewer'),
                          transformationController: _controller,
                          constrained: false,
                          boundaryMargin: const EdgeInsets.all(double.infinity),
                          minScale: 0.01,
                          maxScale: 20,
                          onInteractionEnd: (_) =>
                              _controller.value = clampToSquare(_controller.value, _imageSize, _square),
                          child: RawImage(image: image, width: image.width.toDouble(), height: image.height.toDouble()),
                        ),
                        IgnorePointer(child: CustomPaint(painter: _CircleMask(square))),
                      ]);
                    }),
                  ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 8, 16, 24),
                    child: Column(children: [
                      const Text('Двигайте фото пальцем, приближайте двумя пальцами — в круге то, что увидят игроки.',
                          textAlign: TextAlign.center, style: TextStyle(color: AppColors.muted, fontSize: 13)),
                      const SizedBox(height: 8),
                      Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                        IconButton(key: const Key('crop-zoom-out'), tooltip: 'Отдалить', onPressed: () => _zoom(1 / 1.25), icon: const Icon(Icons.zoom_out, color: Colors.white)),
                        TextButton.icon(
                          key: const Key('crop-center'),
                          onPressed: () => setState(_center),
                          icon: const Icon(Icons.center_focus_strong_outlined),
                          label: const Text('По центру'),
                        ),
                        IconButton(key: const Key('crop-zoom-in'), tooltip: 'Приблизить', onPressed: () => _zoom(1.25), icon: const Icon(Icons.zoom_in, color: Colors.white)),
                      ]),
                    ]),
                  ),
                ]),
    );
  }
}

/// Затемнение вокруг круга и его обводка.
class _CircleMask extends CustomPainter {
  const _CircleMask(this.square);

  final Rect square;

  @override
  void paint(Canvas canvas, Size size) {
    final outside = Path()
      ..fillType = PathFillType.evenOdd
      ..addRect(Offset.zero & size)
      ..addOval(square);
    canvas.drawPath(outside, Paint()..color = const Color(0xB3000000));
    canvas.drawOval(square, Paint()
      ..color = AppColors.amber
      ..style = PaintingStyle.stroke
      ..strokeWidth = 2);
  }

  @override
  bool shouldRepaint(_CircleMask old) => old.square != square;
}
