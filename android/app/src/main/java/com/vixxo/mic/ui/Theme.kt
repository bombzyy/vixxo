package com.vixxo.mic.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

// =========================================================================
// VIXXO STUDIO DESIGN SYSTEM (UI/UX PRO MAX)
// Theme: True OLED Dark + Studio Audio Hardware
// =========================================================================

val VixxoBackground = Color(0xFF070A0F)      // Deep OLED Space Black
val VixxoSurface = Color(0xFF101622)         // Primary Surface Card
val VixxoSurfaceElevated = Color(0xFF162030) // Elevated Layer Card
val VixxoSurfaceSubtle = Color(0xFF0C111C)   // Inset / Input Background
val VixxoBorder = Color(0xFF1F2B3E)          // Subtle Card Border
val VixxoBorderLight = Color(0xFF2B3A52)     // Highlight Border

// Accents
val VixxoAccent = Color(0xFF00F2FE)          // Cyber Cyan Primary
val VixxoAccentHover = Color(0xFF38BDF8)     // Sky Blue Accent
val VixxoAccentSubtle = Color(0xFF0B243B)    // Cyan Glow Tint
val VixxoElectricBlue = Color(0xFF3B82F6)    // Secondary Electric Blue

// Status
val VixxoSuccess = Color(0xFF10B981)         // Studio Green (Streaming)
val VixxoWarning = Color(0xFFF59E0B)         // Studio Amber (Connecting/Buffer)
val VixxoError = Color(0xFFEF4444)           // Studio Red (Error/Mute)

// Typography
val VixxoTextPrimary = Color(0xFFF8FAFC)     // High-Contrast White
val VixxoTextSecondary = Color(0xFF94A3B8)   // Muted Slate
val VixxoTextMuted = Color(0xFF64748B)       // Subtle Dim Text

private val VixxoColorScheme = darkColorScheme(
    primary = VixxoAccent,
    secondary = VixxoAccentHover,
    tertiary = VixxoElectricBlue,
    background = VixxoBackground,
    surface = VixxoSurface,
    surfaceVariant = VixxoSurfaceElevated,
    onPrimary = Color(0xFF060D1A),
    onSecondary = Color(0xFF060D1A),
    onBackground = VixxoTextPrimary,
    onSurface = VixxoTextPrimary,
    error = VixxoError
)

@Composable
fun VixxoTheme(
    @Suppress("UNUSED_PARAMETER") darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = VixxoColorScheme,
        content = content
    )
}
