package com.vixxo.mic.ui

import androidx.compose.foundation.isSystemInDarkTheme
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color

val VixxoBackground = Color(0xFF0E1116)
val VixxoSurface = Color(0xFF161B22)
val VixxoAccent = Color(0xFF4F8CFF)
val VixxoTextPrimary = Color(0xFFE6EDF3)
val VixxoTextSecondary = Color(0xFF8B98A8)
val VixxoSuccess = Color(0xFF22C55E)
val VixxoWarning = Color(0xFFF59E0B)
val VixxoError = Color(0xFFEF4444)

private val VixxoColorScheme = darkColorScheme(
    primary = VixxoAccent,
    background = VixxoBackground,
    surface = VixxoSurface,
    onPrimary = Color.White,
    onBackground = VixxoTextPrimary,
    onSurface = VixxoTextPrimary,
    error = VixxoError
)

@Composable
fun VixxoTheme(
    darkTheme: Boolean = isSystemInDarkTheme(),
    content: @Composable () -> Unit
) {
    MaterialTheme(
        colorScheme = VixxoColorScheme,
        content = content
    )
}
