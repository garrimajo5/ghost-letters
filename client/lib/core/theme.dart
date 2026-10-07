import 'package:flutter/material.dart';

/// Палитра из прототипа: ночной синий, янтарный акцент, ледяной голубой, красный для Убийцы.
class AppColors {
  static const bg = Color(0xFF0D1724);
  static const night = Color(0xFF0A111B);
  static const surface = Color(0xFF152335);
  static const surface2 = Color(0xFF1C2E44);
  static const panel = Color(0xFF111D2C);
  static const border = Color(0xFF2B4260);

  static const text = Color(0xFFE8EEF4);
  static const muted = Color(0xFF9FB2C6);
  static const dim = Color(0xFF6F849A);

  static const amber = Color(0xFFE3A94B);
  static const amberLight = Color(0xFFF2C579);
  static const onAmber = Color(0xFF1A1206);
  static const ice = Color(0xFFA9D4EA);

  static const red = Color(0xFFB03E33);
  static const redBright = Color(0xFFD2584A);
  static const redSoft = Color(0xFFE08A7E);
  static const green = Color(0xFF2C8A57);
  static const believed = Color(0xFF3FBF7F);
  static const greenSoft = Color(0xFF8FD9B0);
}

/// Шрифты дизайна: Oswald — заголовки и кнопки (капсом, с разрядкой), Golos Text — текст.
class AppFonts {
  static const heading = 'Oswald';
  static const body = 'GolosText';
}

/// Заголовок в стиле прототипа: Oswald с разрядкой.
TextStyle heading(double size, {Color color = AppColors.text, double? spacing, FontWeight weight = FontWeight.w500}) =>
    TextStyle(fontFamily: AppFonts.heading, fontSize: size, color: color, letterSpacing: spacing ?? size * 0.08, fontWeight: weight, height: 1.15);

/// Мелкая подпись секции: «ВАША РУКА», «ПОДСКАЗКИ ПО РАУНДАМ».
TextStyle sectionLabel({Color color = AppColors.ice, double size = 11}) => heading(size, color: color, spacing: 1.5);

class AppTheme {
  // Старые имена — для совместимости с экранами.
  static const background = AppColors.bg;
  static const surface = AppColors.surface;
  static const accent = AppColors.amber;
  static const paper = Color(0xFFF3E9D2);
  static const danger = AppColors.redBright;
  static const ok = AppColors.green;
  static const believed = AppColors.believed;

  static ThemeData build() {
    const scheme = ColorScheme.dark(
      primary: AppColors.amber,
      onPrimary: AppColors.onAmber,
      secondary: AppColors.ice,
      onSecondary: AppColors.bg,
      tertiary: AppColors.believed,
      surface: AppColors.surface,
      onSurface: AppColors.text,
      onSurfaceVariant: AppColors.muted,
      surfaceContainerLowest: AppColors.night,
      surfaceContainerLow: AppColors.panel,
      surfaceContainer: AppColors.surface,
      surfaceContainerHigh: AppColors.surface2,
      surfaceContainerHighest: AppColors.surface2,
      secondaryContainer: AppColors.surface2,
      onSecondaryContainer: AppColors.text,
      primaryContainer: Color(0xFF3A2B12),
      onPrimaryContainer: AppColors.amberLight,
      error: AppColors.redBright,
      onError: Colors.white,
      outline: AppColors.border,
      outlineVariant: AppColors.surface2,
    );

    final base = ThemeData(useMaterial3: true, brightness: Brightness.dark, colorScheme: scheme, fontFamily: AppFonts.body);
    final text = base.textTheme.apply(bodyColor: AppColors.text, displayColor: AppColors.text);
    final buttonText = heading(16, spacing: 1.5);
    const shape14 = RoundedRectangleBorder(borderRadius: BorderRadius.all(Radius.circular(14)));

    return base.copyWith(
      scaffoldBackgroundColor: AppColors.bg,
      canvasColor: AppColors.bg,
      textTheme: text.copyWith(
        headlineLarge: heading(32),
        headlineMedium: heading(26),
        headlineSmall: heading(22),
        titleLarge: heading(20),
        titleMedium: text.titleMedium?.copyWith(fontWeight: FontWeight.w600),
        titleSmall: sectionLabel(size: 12),
        labelMedium: text.labelMedium?.copyWith(color: AppColors.muted),
        labelSmall: text.labelSmall?.copyWith(color: AppColors.muted),
        bodySmall: text.bodySmall?.copyWith(color: AppColors.muted),
      ),
      appBarTheme: AppBarTheme(
        backgroundColor: AppColors.bg,
        surfaceTintColor: Colors.transparent,
        centerTitle: false,
        elevation: 0,
        titleTextStyle: heading(20),
        iconTheme: const IconThemeData(color: AppColors.muted),
        actionsIconTheme: const IconThemeData(color: AppColors.muted),
      ),
      cardTheme: const CardThemeData(
        color: AppColors.surface,
        surfaceTintColor: Colors.transparent,
        elevation: 0,
        margin: EdgeInsets.zero,
        shape: shape14,
      ),
      filledButtonTheme: FilledButtonThemeData(
        style: FilledButton.styleFrom(
          backgroundColor: AppColors.amber,
          foregroundColor: AppColors.onAmber,
          disabledBackgroundColor: AppColors.surface2,
          disabledForegroundColor: AppColors.dim,
          minimumSize: const Size(64, 52),
          shape: shape14,
          textStyle: buttonText,
        ),
      ),
      outlinedButtonTheme: OutlinedButtonThemeData(
        style: OutlinedButton.styleFrom(
          foregroundColor: AppColors.text,
          side: const BorderSide(color: AppColors.border),
          minimumSize: const Size(64, 52),
          shape: shape14,
          textStyle: buttonText.copyWith(fontSize: 15),
        ),
      ),
      textButtonTheme: TextButtonThemeData(
        style: TextButton.styleFrom(foregroundColor: AppColors.amber, textStyle: const TextStyle(fontWeight: FontWeight.w600)),
      ),
      chipTheme: base.chipTheme.copyWith(
        backgroundColor: AppColors.surface2,
        selectedColor: const Color(0xFF3A2B12),
        side: const BorderSide(color: AppColors.border),
        shape: const StadiumBorder(side: BorderSide(color: AppColors.border)),
        labelStyle: const TextStyle(color: AppColors.text, fontFamily: AppFonts.body),
        checkmarkColor: AppColors.amber,
      ),
      inputDecorationTheme: InputDecorationTheme(
        filled: true,
        fillColor: AppColors.surface,
        hintStyle: const TextStyle(color: AppColors.dim),
        labelStyle: const TextStyle(color: AppColors.muted),
        border: OutlineInputBorder(borderRadius: BorderRadius.circular(14), borderSide: const BorderSide(color: AppColors.border)),
        enabledBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(14), borderSide: const BorderSide(color: AppColors.border)),
        focusedBorder: OutlineInputBorder(borderRadius: BorderRadius.circular(14), borderSide: const BorderSide(color: AppColors.amber, width: 1.5)),
      ),
      bottomSheetTheme: const BottomSheetThemeData(
        backgroundColor: AppColors.panel,
        surfaceTintColor: Colors.transparent,
        showDragHandle: true,
        dragHandleColor: AppColors.border,
        shape: RoundedRectangleBorder(borderRadius: BorderRadius.vertical(top: Radius.circular(22))),
      ),
      dialogTheme: const DialogThemeData(backgroundColor: AppColors.panel, surfaceTintColor: Colors.transparent),
      snackBarTheme: const SnackBarThemeData(
        backgroundColor: AppColors.surface2,
        contentTextStyle: TextStyle(color: AppColors.text, fontFamily: AppFonts.body),
        behavior: SnackBarBehavior.floating,
      ),
      dividerTheme: const DividerThemeData(color: AppColors.surface2, space: 24),
      listTileTheme: const ListTileThemeData(iconColor: AppColors.muted),
      switchTheme: SwitchThemeData(
        thumbColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? AppColors.onAmber : AppColors.muted),
        trackColor: WidgetStateProperty.resolveWith((s) => s.contains(WidgetState.selected) ? AppColors.amber : AppColors.surface2),
      ),
      segmentedButtonTheme: SegmentedButtonThemeData(
        style: SegmentedButton.styleFrom(
          selectedBackgroundColor: AppColors.amber,
          selectedForegroundColor: AppColors.onAmber,
          foregroundColor: AppColors.muted,
          side: const BorderSide(color: AppColors.border),
        ),
      ),
      popupMenuTheme: const PopupMenuThemeData(color: AppColors.surface2, surfaceTintColor: Colors.transparent),
      badgeTheme: const BadgeThemeData(backgroundColor: AppColors.amber, textColor: AppColors.onAmber),
      progressIndicatorTheme: const ProgressIndicatorThemeData(color: AppColors.amber),
    );
  }
}
