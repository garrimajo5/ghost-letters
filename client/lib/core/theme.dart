import 'package:flutter/material.dart';

/// Тёмная «ночная» тема: глубокий синий фон, фиолетовый акцент, тёплая бумага карт.
class AppTheme {
  static const background = Color(0xFF14121F);
  static const surface = Color(0xFF1F1C2E);
  static const accent = Color(0xFF8B7CF6);
  static const paper = Color(0xFFF3E9D2);
  static const danger = Color(0xFFE5647A);
  static const ok = Color(0xFF3FB68B);
  static const believed = Color(0xFF3FB68B);

  static ThemeData build() {
    final scheme = ColorScheme.fromSeed(seedColor: accent, brightness: Brightness.dark, surface: surface);
    return ThemeData(
      useMaterial3: true,
      colorScheme: scheme,
      scaffoldBackgroundColor: background,
      appBarTheme: const AppBarTheme(backgroundColor: background, centerTitle: false),
      cardTheme: const CardThemeData(color: surface),
      inputDecorationTheme: const InputDecorationTheme(border: OutlineInputBorder()),
    );
  }
}
