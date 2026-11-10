import os
import sys
import logging
import numpy as np
import torch
import torch.nn as nn
import soundfile as sf
import librosa

_HubertModelWithFinalProj = None

def get_hubert_model_class():
    global _HubertModelWithFinalProj
    if _HubertModelWithFinalProj is None:
        from transformers import HubertModel

        class _RealHubertModelWithFinalProj(HubertModel):
            def __init__(self, config):
                super().__init__(config)
                self.final_proj = nn.Linear(
                    config.hidden_size, config.classifier_proj_size
                )

        _HubertModelWithFinalProj = _RealHubertModelWithFinalProj
    return _HubertModelWithFinalProj


class HubertModelWithFinalProj:
    def __new__(cls, *args, **kwargs):
        real_cls = get_hubert_model_class()
        return real_cls(*args, **kwargs)

    @classmethod
    def from_pretrained(cls, *args, **kwargs):
        real_cls = get_hubert_model_class()
        return real_cls.from_pretrained(*args, **kwargs)


def load_embedding(embedder_path: str):
    """
    Loads HuBERT / ContentVec feature extractor from local path.
    """
    if not os.path.exists(embedder_path):
        raise FileNotFoundError(f"ContentVec model not found at {embedder_path}")
    return HubertModelWithFinalProj.from_pretrained(embedder_path)


def load_audio(file_path: str, sample_rate: int = 16000) -> np.ndarray:
    """
    Load any audio file, convert to mono float32, and resample to target sample rate.
    """
    file_path = file_path.strip().strip('"')
    if not os.path.isfile(file_path):
        raise FileNotFoundError(f"Audio file not found: {file_path}")

    audio, sr = sf.read(file_path)
    if audio.ndim > 1:
        audio = librosa.to_mono(audio.T)
    audio = np.asarray(audio, dtype=np.float32)

    if sr != sample_rate:
        audio = librosa.resample(audio, orig_sr=sr, target_sr=sample_rate, res_type="soxr_vhq")

    return audio.flatten()
