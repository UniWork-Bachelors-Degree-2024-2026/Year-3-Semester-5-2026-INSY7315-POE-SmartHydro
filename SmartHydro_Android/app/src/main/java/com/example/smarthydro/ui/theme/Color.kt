package com.example.smarthydro.ui.theme

import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.setValue
import androidx.compose.ui.graphics.Color

// High contrast switch read by the tokens below, so turning it on recolours every screen that uses them
object HighContrast {
    var enabled by mutableStateOf(false)
}

// Returns the high contrast colour when that mode is on, otherwise the normal one
private fun pick(normal: Color, high: Color): Color = if (HighContrast.enabled) high else normal

// 1. BRAND GREENS
val ForestGreen = Color(0xFF204C2C)
val ForestGreenDark = Color(0xFF14331D)
val ForestGreenLight = Color(0xFF4BA866)
val HeaderSubtitleGreen = Color(0xFFC2DFC7)
val NavSelectedPill = Color(0xFFC6E3CA)
val NavSelectedPillDark = Color(0xFF17332F)
val MintBannerBg = Color(0xFFE9F5EB)
val MintBannerBgDark = Color(0x243AA88A)

// Dark Mode Green Accents
val AccentGreenDark = Color(0xFF3AA88A)
val InkGreenDark = Color(0xFF7FBFA6)
val InkStrongDark = Color(0xFFA3D4C1)
val HeaderDarkBg = Color(0xFF102B26)
val TopBarDarkBg = Color(0xFF0D1A21)

// Buttons & Actions (Dark Mode Contrast)
val PrimaryButtonBgDark = Color(0xFF2E8B68) // Bright, crisp green with high contrast against SurfaceDark (0xFF15242E)
val PrimaryButtonTextDark = Color(0xFFFFFFFF)
val PrimaryButtonDisabledBgDark = Color(0xFF1B4136)
val PrimaryButtonDisabledTextDark = Color(0x99FFFFFF)
val OutlinedButtonBorderDark = Color(0xFF3AA88A)
val OutlinedButtonTextDark = Color(0xFFE5E9F0)

// 2. SENSOR & METRIC ACCENT COLORS
val WaterBlue = Color(0xFF3E92B0)
val CleanWaterBlue = Color(0xFF0813EA)
val MoistureBlue = Color(0xFF1E1AEE)
val TempTerracotta = Color(0xFFC8583F)
val HumidityOrange = Color(0xFFE77A2C)
val NutrientsGreen = Color(0xFF204C2C)
val SunlightAmber = Color(0xFFD89A1F)

// Structure label colours: the Add Structure swatches and the structure card border/label
val LabelGreen = Color(0xFF387544)
val LabelRed = Color(0xFFC24445)
val LabelAmber = Color(0xFFD89538)
val LabelBlue = Color(0xFF64A1BD)
val LabelBrown = Color(0xFF93775A)

// 3. STATUS BADGE COLORS

// Light Mode Badges
val StatusGoodBg = Color(0xFFDEF2E0)
val StatusGoodText: Color get() = pick(Color(0xFF1D5A32), Color(0xFF0F3D20))
val StatusCautionBg = Color(0xFFFCECE8)
val StatusCautionText: Color get() = pick(Color(0xFFB0442F), Color(0xFF8F2A17))
val StatusNeutralBg = Color(0xFFEDF4EE)
val StatusNeutralText: Color get() = pick(Color(0xFF3B5641), Color(0xFF1F3326))
val StatusWarnBg = Color(0xFFFFF3D9)
val StatusWarnText: Color get() = pick(Color(0xFF8A5A00), Color(0xFF5E3C00))

// Dark Mode Badges
val StatusGoodBgDark = Color(0x293AA88A)
val StatusGoodTextDark: Color get() = pick(Color(0xFF7FBFA6), Color(0xFFB7E8D4))
val StatusCautionBgDark = Color(0x26FB7185)
val StatusCautionTextDark: Color get() = pick(Color(0xFFFDA4AF), Color(0xFFFFD1D6))
val StatusNeutralBgDark = Color(0xFF1D303B)
val StatusNeutralTextDark: Color get() = pick(Color(0xFF94A8B5), Color(0xFFE2E8F0))
val StatusWarnBgDark = Color(0x29F59E0B)
val StatusWarnTextDark: Color get() = pick(Color(0xFFFBBF24), Color(0xFFFFE08A))

// 4. NEUTRAL UI TOKENS (LIGHT THEME)
val SurfaceWhite = Color(0xFFFFFFFF)
val BackgroundLight = Color(0xFFF4F6F5)
val CardBorder: Color get() = pick(Color(0xFFE6E9E8), Color(0xFF4B5563))
val CardDivider: Color get() = pick(Color(0xFFEFF3F0), Color(0xFF9CA3AF))
val TextPrimary: Color get() = pick(Color(0xFF1F2937), Color(0xFF000000))
val TextSecondary: Color get() = pick(Color(0xFF646B78), Color(0xFF1F2937))
val TextMuted: Color get() = pick(Color(0xFF666B72), Color(0xFF374151))

// 5. NEUTRAL UI TOKENS (DARK THEME)
val BackgroundDark = Color(0xFF0C1F1E)
val BackgroundDarkEnd = Color(0xFF0B131E)
val SurfaceDark = Color(0xFF15242E)
val SurfaceDarkVariant = Color(0xFF1B2D38)
val CardBorderDark: Color get() = pick(Color(0xFF24394A), Color(0xFF94A8B5))
val CardBorderStrongDark: Color get() = pick(Color(0xFF30495A), Color(0xFFCBD5E1))
val CardDividerDark = Color(0xFF1D303B)
val TextPrimaryDark: Color get() = pick(Color(0xFFE5E9F0), Color(0xFFFFFFFF))
val TextSoftDark: Color get() = pick(Color(0xFFCBD5E1), Color(0xFFFFFFFF))
val TextSecondaryDark: Color get() = pick(Color(0xFF94A8B5), Color(0xFFE5E9F0))
val TextMutedDark: Color get() = pick(Color(0xFF8A9FAC), Color(0xFFCBD5E1))

// Switches & Toggles
val SwitchThumb = Color(0xFFFFFFFF)
val SwitchTrackActive = Color(0xFFC0DFC4)
val SwitchTrackInactive: Color get() = pick(Color(0xFF8A8F8B), Color(0xFF5B6560))
val SwitchTrackActiveDark = Color(0xFF2F8F78)
val SwitchTrackInactiveDark: Color get() = pick(Color(0xFF677781), Color(0xFF7C93A4))
val OnlineToggleBg = Color(0xFFE7ECE8)
val OnlineToggleActive = Color(0xFF204C2C)
