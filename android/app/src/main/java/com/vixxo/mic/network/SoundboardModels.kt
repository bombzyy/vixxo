package com.vixxo.mic.network

data class SoundboardClip(
    val id: String,
    val name: String,
    val category: String,
    val durationText: String,
    val shortcutKey: String = "",
    val isPlaying: Boolean = false
)

data class SoundboardSyncData(
    val isPlaying: Boolean,
    val currentPlayingClipName: String?,
    val clips: List<SoundboardClip>
)
