package com.example.smarthydro.ui.theme

import android.app.Activity
import android.app.UiModeManager
import android.content.Context
import android.os.Build
import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.SideEffect
import androidx.compose.ui.graphics.toArgb
import androidx.compose.ui.platform.LocalView
import androidx.compose.runtime.CompositionLocalProvider
import androidx.compose.runtime.compositionLocalOf
import androidx.core.view.WindowCompat

// Light Color Scheme, rebuilt on each use so it follows the high contrast setting
private fun lightScheme() = lightColorScheme(
    primary = ForestGreen,
    onPrimary = SurfaceWhite,
    primaryContainer = MintBannerBg,
    onPrimaryContainer = ForestGreen,
    secondary = WaterBlue,
    onSecondary = SurfaceWhite,
    background = BackgroundLight,
    onBackground = TextPrimary,
    surface = SurfaceWhite,
    onSurface = TextPrimary,
    surfaceVariant = BackgroundLight,
    onSurfaceVariant = TextSecondary,
    outline = CardBorder
)

// Dark Color Scheme, rebuilt on each use so it follows the high contrast setting
private fun darkScheme() = darkColorScheme(
    primary = AccentGreenDark,
    onPrimary = SurfaceWhite,
    primaryContainer = MintBannerBgDark,
    onPrimaryContainer = InkGreenDark,
    secondary = WaterBlue,
    onSecondary = SurfaceWhite,
    background = BackgroundDark,
    onBackground = TextPrimaryDark,
    surface = SurfaceDark,
    onSurface = TextPrimaryDark,
    surfaceVariant = SurfaceDarkVariant,
    onSurfaceVariant = TextSecondaryDark,
    outline = CardBorderDark
)

// Whether the device is set to a high contrast level, which is only reported on Android 14 and newer
fun systemPrefersHighContrast(context: Context): Boolean {
    if (Build.VERSION.SDK_INT < Build.VERSION_CODES.UPSIDE_DOWN_CAKE) return false
    val uiModeManager = context.getSystemService(Context.UI_MODE_SERVICE) as? UiModeManager
    return (uiModeManager?.contrast ?: 0f) >= 0.5f
}

val LocalThemeIsDark = compositionLocalOf { false }
val LocalThemeToggle = compositionLocalOf { {} }
val LocalNavigateToSettings = compositionLocalOf<(() -> Unit)?> { null }

@Composable
fun SmartHydroTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    onToggleDarkTheme: () -> Unit = {},
    onNavigateToSettings: (() -> Unit)? = null,
    content: @Composable () -> Unit
) {
    val colorScheme = if (darkTheme) darkScheme() else lightScheme()

    // Sync Android Status Bar with Primary / TopBar Theme
    val view = LocalView.current
    if (!view.isInEditMode) {
        SideEffect {
            val window = (view.context as Activity).window
            window.statusBarColor = (if (darkTheme) HeaderDarkBg else ForestGreen).toArgb()
            WindowCompat.getInsetsController(window, view).isAppearanceLightStatusBars = false
        }
    }

    CompositionLocalProvider(
        LocalThemeIsDark provides darkTheme,
        LocalThemeToggle provides onToggleDarkTheme,
        LocalNavigateToSettings provides onNavigateToSettings
    ) {
        MaterialTheme(
            colorScheme = colorScheme,
            typography = Typography,
            content = content
        )
    }
}

// Button color helpers providing optimal contrast for both Light and Dark modes
@Composable
fun smartHydroButtonColors(
    containerColor: androidx.compose.ui.graphics.Color? = null,
    contentColor: androidx.compose.ui.graphics.Color? = null,
    isDark: Boolean = LocalThemeIsDark.current
) = androidx.compose.material3.ButtonDefaults.buttonColors(
    containerColor = containerColor ?: if (isDark) PrimaryButtonBgDark else ForestGreen,
    contentColor = contentColor ?: if (isDark) PrimaryButtonTextDark else androidx.compose.ui.graphics.Color.White,
    disabledContainerColor = (containerColor ?: if (isDark) PrimaryButtonBgDark else ForestGreen).copy(alpha = if (isDark) 0.35f else 0.4f),
    disabledContentColor = androidx.compose.ui.graphics.Color.White.copy(alpha = 0.7f)
)

@Composable
fun smartHydroOutlinedButtonColors(
    contentColor: androidx.compose.ui.graphics.Color? = null,
    isDark: Boolean = LocalThemeIsDark.current
) = androidx.compose.material3.ButtonDefaults.outlinedButtonColors(
    contentColor = contentColor ?: if (isDark) OutlinedButtonTextDark else ForestGreenDark
)
