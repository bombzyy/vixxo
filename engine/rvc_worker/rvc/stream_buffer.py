import numpy as np
import torch
import torch.nn.functional as F
from typing import Optional, Tuple, Dict, Any
from rvc.pipeline import RVCInferencePipeline


class StreamBufferProcessor:
    """
    Real-time streaming chunk processor with sliding context window and overlap-add crossfade.
    Prevents boundary clicks and phase discontinuities between consecutive audio chunks.
    """

    def __init__(
        self,
        pipeline: RVCInferencePipeline,
        chunk_ms: int = 80,
        context_ms: int = 400,
        crossfade_ms: int = 10,
        pitch_shift: int = 0,
        index_rate: float = 0.5,
        sid: int = 0,
    ):
        self.pipeline = pipeline
        self.chunk_ms = chunk_ms
        self.context_ms = context_ms
        self.crossfade_ms = crossfade_ms
        self.pitch_shift = pitch_shift
        self.index_rate = index_rate
        self.sid = sid

        self.input_sr = 16000
        self.tgt_sr = pipeline.tgt_sr

        # Chunk and context sizes in samples at 16kHz
        self.chunk_samples = int(self.input_sr * (chunk_ms / 1000.0))
        self.context_samples = int(self.input_sr * (context_ms / 1000.0))
        self.crossfade_in_samples = int(self.input_sr * (crossfade_ms / 1000.0))

        # Output chunk and crossfade sizes at tgt_sr
        self.tgt_ratio = self.tgt_sr / self.input_sr
        self.tgt_chunk_samples = int(self.chunk_samples * self.tgt_ratio)
        self.tgt_crossfade_samples = int(self.crossfade_ms / 1000.0 * self.tgt_sr)

        # Precompute cosine crossfade windows
        if self.tgt_crossfade_samples > 0:
            t = np.linspace(0, np.pi / 2, self.tgt_crossfade_samples, endpoint=False)
            self.fade_out = np.cos(t) ** 2
            self.fade_in = np.sin(t) ** 2
        else:
            self.fade_out = np.zeros(0)
            self.fade_in = np.zeros(0)

        # Ring/history buffer at 16kHz
        self.buffer_len = self.context_samples + self.chunk_samples
        self.input_buffer = np.zeros(self.buffer_len, dtype=np.float32)

        # Previous chunk's tail for crossfading
        self.prev_tail = np.zeros(self.tgt_crossfade_samples, dtype=np.float32)
        self.is_first_chunk = True

    def reset(self):
        """Reset internal buffers on stream start/stop."""
        self.input_buffer.fill(0)
        self.prev_tail.fill(0)
        self.is_first_chunk = True

    def process_chunk(self, chunk_16k: np.ndarray) -> Tuple[np.ndarray, Dict[str, float]]:
        """
        Process a single real-time audio chunk (e.g. 40ms, 60ms, 80ms, 120ms).
        Returns: (output_chunk_at_tgt_sr, metrics_dict)
        """
        n_in = len(chunk_16k)
        # Shift history left and append new chunk
        self.input_buffer[:-n_in] = self.input_buffer[n_in:]
        self.input_buffer[-n_in:] = chunk_16k

        # Run conversion on the full context window to maintain acoustic continuity
        converted_audio, metrics = self.pipeline.convert_audio(
            self.input_buffer,
            pitch_shift=self.pitch_shift,
            index_rate=self.index_rate,
            sid=self.sid,
            volume_match=True,
        )

        # The new chunk corresponds to the tail of the generated audio
        expected_out_len = int(n_in * self.tgt_ratio)
        out_tail = converted_audio[-expected_out_len:].copy()

        # Overlap-add crossfade with previous chunk to eliminate clicks
        if not self.is_first_chunk and self.tgt_crossfade_samples > 0 and len(out_tail) >= self.tgt_crossfade_samples:
            xf_len = min(self.tgt_crossfade_samples, len(out_tail))
            out_tail[:xf_len] = (
                self.prev_tail[:xf_len] * self.fade_out[:xf_len]
                + out_tail[:xf_len] * self.fade_in[:xf_len]
            )

        # Store tail for the next chunk's crossfade
        if self.tgt_crossfade_samples > 0 and len(out_tail) >= self.tgt_crossfade_samples:
            self.prev_tail = out_tail[-self.tgt_crossfade_samples:].copy()

        self.is_first_chunk = False

        # Update metrics to reflect real-time chunk metrics
        metrics["chunk_ms"] = (n_in / self.input_sr) * 1000.0
        metrics["rtf_chunk"] = (metrics["total_ms"] / 1000.0) / (n_in / self.input_sr)

        return out_tail, metrics
