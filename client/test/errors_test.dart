import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ghost_letters/core/api.dart';

import 'support/fakes.dart';
import 'support/fixtures.dart';

/// Ошибки — понятным языком, без технических подробностей, и с возможностью повторить.
void main() {
  DioException dio(DioExceptionType type, {int? status}) {
    final options = RequestOptions(path: '/x');
    return DioException(
      requestOptions: options,
      type: type,
      response: status == null ? null : Response<Object?>(requestOptions: options, statusCode: status),
    );
  }

  test('нет сети и долгий ответ — разные тексты', () {
    expect(ApiError.from(dio(DioExceptionType.connectionError)).message, ApiError.offlineMessage);
    expect(ApiError.from(dio(DioExceptionType.connectionTimeout)).message, ApiError.slowMessage);
  });

  test('502–504 — сервер обновляется', () {
    final e = ApiError.from(dio(DioExceptionType.badResponse, status: 502));
    expect(e.code, 'UNAVAILABLE');
    expect(e.message, ApiError.restartingMessage);
    expect(ApiError.from(dio(DioExceptionType.badResponse, status: 500)).message, contains('500'));
  });

  test('внутренняя ошибка не показывает технический текст', () {
    final e = ApiError.from(StateError('Bad state: null check operator'));
    expect(e.message, ApiError.unknownMessage);
    expect(e.message, isNot(contains('null')));
  });

  testWidgets('главная: ошибка списка партий и «Повторить»', (tester) async {
    tester.view.physicalSize = const Size(1080, 2400);
    tester.view.devicePixelRatio = 2.625;
    addTearDown(tester.view.reset);
    final app = await TestApp.create(user: watson);
    addTearDown(app.container.dispose);
    app.api.failWith = const ApiError('OFFLINE', ApiError.offlineMessage);
    await tester.pumpWidget(app.widget);
    await tester.pumpAndSettle();

    expect(find.text(ApiError.offlineMessage), findsOneWidget);

    app.api.failWith = null;
    await tester.tap(find.byKey(const Key('retry')));
    await tester.pumpAndSettle();

    expect(find.text(ApiError.offlineMessage), findsNothing);
    expect(find.text('Пока нет идущих партий'), findsOneWidget);
  });
}
