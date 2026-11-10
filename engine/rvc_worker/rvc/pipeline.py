import os
import sys
import time
import math
import logging
from typing import Optional, Tuple, Dict, Any

import numpy as np
import torch
import torch.nn.functional as F
import soundfile as sf
import librosa

try:
    import faiss
except ImportError:
    faiss = None

from rvc.lib.algorithm.synthesizers import Synthesizer
from rvc.lib.predictors.RMVPE import RMVPE0Predictor
from rvc.lib.utils import load_embedding, load_audio


class RVCInferencePipeline:
    """
    Headless RVC (Retrieval-based Voice Conversion) v1/v2 Inference Pipeline.
    Supports FP16/FP32 inference, CUDA/CPU execution, RMVPE pitch tracking,
    ContentVec feature extraction, and FAISS index retrieval.
    """

    def __init__(
        self,
        model_path: str,
        index_path: Optional[str] = None,
        embedder_dir: str = "models/base/contentvec",
        rmvpe_path: str = "models/base/predictors/rmvpe.pt",
        device: Optional[str] = None,
        is_half: bool = True,
    ):
        # 1. Device selection & fallback
        if device is None:
            self.device = torch.device("cuda:0" if torch.cuda.is_available() else "cpu")
        else:
            if "cuda" in device and not torch.cuda.is_available():
                print("[WARNING] CUDA requested but not available. Falling back to CPU.")
                self.device = torch.device("cpu")
            else:
                self.device = torch.device(device)

        # FP16 is only supported on CUDA devices
        self.is_half = is_half and self.device.type == "cuda"
        self.dtype = torch.float16 if self.is_half else torch.float32

        # 2. Load RVC voice model checkpoint
        if not os.path.isfile(model_path):
            raise FileNotFoundError(f"Model checkpoint not found: {model_path}")

        print(f"[RVC] Loading voice checkpoint: {model_path} (Device: {self.device}, FP16: {self.is_half})")
        t0 = time.perf_counter()
        cpt = torch.load(model_path, map_location="cpu", weights_only=False)

        self.version = cpt.get("version", "v1")
        self.use_f0 = cpt.get("f0", 1)
        raw_sr = cpt["config"][-1] if "config" in cpt and len(cpt["config"]) > 0 else cpt.get("sr", 40000)
        if isinstance(raw_sr, int):
            self.tgt_sr = raw_sr
        else:
            sr_str = str(raw_sr).lower().replace("k", "000")
            self.tgt_sr = int(sr_str)
        self.vocoder = cpt.get("vocoder", "HiFi-GAN")
        self.text_enc_hidden_dim = 768 if self.version == "v2" else 256

        # Multi-speaker setup
        cpt["config"][-3] = cpt["weight"]["emb_g.weight"].shape[0]

        self.net_g = Synthesizer(
            *cpt["config"],
            use_f0=self.use_f0,
            text_enc_hidden_dim=self.text_enc_hidden_dim,
            vocoder=self.vocoder,
        )
        if hasattr(self.net_g, "enc_q"):
            del self.net_g.enc_q

        self.net_g.load_state_dict(cpt["weight"], strict=False)
        self.net_g.to(self.device)
        if self.is_half:
            self.net_g = self.net_g.half()
        else:
            self.net_g = self.net_g.float()
        self.net_g.eval()
        t_model = (time.perf_counter() - t0) * 1000

        # 3. Load FAISS index if provided
        self.index = None
        self.big_npy = None
        if index_path and os.path.isfile(index_path) and faiss is not None:
            try:
                print(f"[RVC] Loading FAISS index: {index_path}")
                self.index = faiss.read_index(index_path)
                self.big_npy = self.index.reconstruct_n(0, self.index.ntotal)
                print(f"[RVC] Index loaded: {self.index.ntotal} speaker vectors (dim {self.big_npy.shape[1]})")
            except Exception as e:
                print(f"[WARNING] Failed to load index {index_path}: {e}")
                self.index = None
                self.big_npy = None

        # 4. Load ContentVec feature extractor
        print(f"[RVC] Loading ContentVec embedder from: {embedder_dir}")
        t0 = time.perf_counter()
        self.hubert = load_embedding(embedder_dir)
        self.hubert.to(self.device)
        if self.is_half:
            self.hubert = self.hubert.half()
        else:
            self.hubert = self.hubert.float()
        self.hubert.eval()
        t_hubert = (time.perf_counter() - t0) * 1000

        # 5. Load RMVPE pitch extractor
        print(f"[RVC] Loading RMVPE predictor from: {rmvpe_path}")
        t0 = time.perf_counter()
        self.rmvpe = RMVPE0Predictor(rmvpe_path, device=self.device)
        self.rmvpe.model.eval()
        t_rmvpe = (time.perf_counter() - t0) * 1000

        # Pitch quantization constants (MIDI 1 to 255)
        self.f0_min = 50.0
        self.f0_max = 1100.0
        self.f0_mel_min = 1127.0 * np.log(1.0 + self.f0_min / 700.0)
        self.f0_mel_max = 1127.0 * np.log(1.0 + self.f0_max / 700.0)

        print(f"[RVC] Pipeline ready! (Version: {self.version}, SampleRate: {self.tgt_sr}Hz, LoadTimes: Model={t_model:.1f}ms, HuBERT={t_hubert:.1f}ms, RMVPE={t_rmvpe:.1f}ms)")

    def extract_f0(self, audio_16k: np.ndarray, pitch_shift: int = 0) -> Tuple[np.ndarray, np.ndarray]:
        """
        Extract fundamental frequency using RMVPE and quantize to coarse MIDI bins.
        Returns: (f0_coarse, f0_fine)
        """
        # RMVPE inference (16kHz mono audio -> F0 contour at 100 Hz / 10ms frame stride)
        f0 = self.rmvpe.infer_from_audio(audio_16k, thred=0.03)

        # Apply pitch shift in semitones
        if pitch_shift != 0:
            f0 = f0 * (2.0 ** (pitch_shift / 12.0))

        f0_fine = f0.copy()

        # Quantize to 1..255 coarse pitch bins (mel scale)
        f0_mel = 1127.0 * np.log(1.0 + f0 / 700.0)
        f0_mel[f0_mel > 0] = (f0_mel[f0_mel > 0] - self.f0_mel_min) * 254.0 / (
            self.f0_mel_max - self.f0_mel_min
        ) + 1.0
        f0_mel = np.clip(f0_mel, 1.0, 255.0)
        f0_coarse = np.rint(f0_mel).astype(np.int64)

        return f0_coarse, f0_fine

    def extract_features(
        self,
        audio_16k: np.ndarray,
        index_rate: float = 0.75,
    ) -> torch.Tensor:
        """
        Extract speech representation features using ContentVec and apply optional FAISS index retrieval.
        Returns upsampled features tensor [1, T, hidden_dim] aligned at 100 Hz.
        """
        feats = torch.from_numpy(audio_16k).view(1, -1).to(self.device)
        feats = feats.half() if self.is_half else feats.float()

        # Forward pass through HuBERT / ContentVec Transformer encoder
        with torch.no_grad():
            outputs = self.hubert(feats)
            feats = outputs["last_hidden_state"]

            # RVC v1 models require final projection; v2 models use 768-dim last hidden state directly
            if self.version == "v1" and hasattr(self.hubert, "final_proj"):
                feats = self.hubert.final_proj(feats[0]).unsqueeze(0)

            # FAISS index retrieval blending
            if self.index is not None and self.big_npy is not None and index_rate > 0:
                npy = feats[0].detach().cpu().float().numpy()
                score, ix = self.index.search(npy, k=8)
                weight = np.square(1.0 / (score + 1e-6))
                weight /= weight.sum(axis=1, keepdims=True)
                retrieved = np.sum(self.big_npy[ix] * np.expand_dims(weight, axis=2), axis=1)
                retrieved_t = torch.from_numpy(retrieved).unsqueeze(0).to(self.device).type_as(feats)
                feats = index_rate * retrieved_t + (1.0 - index_rate) * feats

            # 2x upsampling (ContentVec produces ~50Hz frames; interpolate to ~100Hz to match F0)
            feats = F.interpolate(feats.permute(0, 2, 1), scale_factor=2.0, mode="nearest").permute(0, 2, 1)

        return feats

    def generate(
        self,
        feats: torch.Tensor,
        f0_coarse: np.ndarray,
        f0_fine: np.ndarray,
        sid: int = 0,
        protect: float = 0.5,
    ) -> np.ndarray:
        """
        Synthesize target voice waveform from features and pitch guidance using the NSF generator.
        """
        with torch.no_grad():
            # Align feature length with pitch frames
            p_len = min(feats.shape[1], len(f0_coarse))
            feats = feats[:, :p_len, :]

            pitch = torch.from_numpy(f0_coarse[:p_len]).unsqueeze(0).long().to(self.device)
            pitchf = torch.from_numpy(f0_fine[:p_len]).unsqueeze(0).to(self.device).type_as(feats)
            p_len_t = torch.tensor([p_len], device=self.device).long()
            sid_t = torch.tensor([sid], device=self.device).long()

            # Run VITS generator
            audio_out = self.net_g.infer(feats, p_len_t, pitch, pitchf, sid_t)[0][0, 0]
            audio_np = audio_out.detach().cpu().float().numpy()

        return audio_np

    @staticmethod
    def match_rms(source_16k: np.ndarray, target_audio: np.ndarray, target_sr: int, rate: float = 1.0, device: torch.device = torch.device("cpu")) -> np.ndarray:
        """
        Fast GPU/PyTorch RMS envelope matching.
        """
        if rate <= 0:
            return target_audio
        with torch.no_grad():
            src_t = torch.from_numpy(source_16k).float().to(device)
            tgt_t = torch.from_numpy(target_audio).float().to(device)

            src_win = 16000 // 4
            tgt_win = target_sr // 4

            src_p = F.avg_pool1d(src_t.abs().pow(2).view(1, 1, -1), kernel_size=src_win, stride=src_win // 2, padding=src_win // 2).clamp(min=1e-8)
            tgt_p = F.avg_pool1d(tgt_t.abs().pow(2).view(1, 1, -1), kernel_size=tgt_win, stride=tgt_win // 2, padding=tgt_win // 2).clamp(min=1e-8)

            src_rms = src_p.sqrt()
            tgt_rms = tgt_p.sqrt()

            src_rms_interp = F.interpolate(src_rms, size=tgt_t.shape[0], mode="linear", align_corners=False).view(-1)
            tgt_rms_interp = F.interpolate(tgt_rms, size=tgt_t.shape[0], mode="linear", align_corners=False).view(-1)

            gain = torch.pow(src_rms_interp, 1.0 - rate) * torch.pow(tgt_rms_interp, rate - 1.0)
            adjusted = tgt_t * gain

            max_val = torch.max(torch.abs(adjusted)).item()
            if max_val > 0.99:
                adjusted = (adjusted / max_val) * 0.99
            return adjusted.cpu().numpy()

    def convert_audio(
        self,
        audio_16k: np.ndarray,
        pitch_shift: int = 0,
        index_rate: float = 0.75,
        sid: int = 0,
        volume_match: bool = True,
    ) -> Tuple[np.ndarray, Dict[str, float]]:
        """
        Full end-to-end voice conversion with precise stage timings and VRAM measurement.
        """
        metrics = {}
        if self.device.type == "cuda":
            torch.cuda.reset_peak_memory_stats(self.device)
            torch.cuda.synchronize()

        t_total_start = time.perf_counter()

        # Step 1: Pitch extraction
        t0 = time.perf_counter()
        f0_coarse, f0_fine = self.extract_f0(audio_16k, pitch_shift=pitch_shift)
        if self.device.type == "cuda":
            torch.cuda.synchronize()
        metrics["f0_ms"] = (time.perf_counter() - t0) * 1000

        # Step 2: Feature extraction
        t0 = time.perf_counter()
        feats = self.extract_features(audio_16k, index_rate=index_rate)
        if self.device.type == "cuda":
            torch.cuda.synchronize()
        metrics["feature_ms"] = (time.perf_counter() - t0) * 1000

        # Step 3: Generator synthesis
        t0 = time.perf_counter()
        audio_out = self.generate(feats, f0_coarse, f0_fine, sid=sid)
        if self.device.type == "cuda":
            torch.cuda.synchronize()
        metrics["generator_ms"] = (time.perf_counter() - t0) * 1000

        # Step 4: RMS matching
        if volume_match:
            audio_out = self.match_rms(audio_16k, audio_out, self.tgt_sr, device=self.device)

        metrics["total_ms"] = (time.perf_counter() - t_total_start) * 1000

        audio_duration_s = len(audio_16k) / 16000.0
        metrics["duration_s"] = audio_duration_s
        metrics["rtf"] = (metrics["total_ms"] / 1000.0) / max(audio_duration_s, 1e-5)

        if self.device.type == "cuda":
            metrics["peak_vram_mb"] = torch.cuda.max_memory_allocated(self.device) / (1024 * 1024)
        else:
            metrics["peak_vram_mb"] = 0.0

        return audio_out, metrics
