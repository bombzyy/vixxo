import os
import sys
import time
import json
from typing import Dict, List
import numpy as np
import soundfile as sf
import torch

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from rvc.pipeline import RVCInferencePipeline
from rvc.stream_buffer import StreamBufferProcessor
from rvc.lib.utils import load_audio


def measure_discontinuities(audio: np.ndarray, chunk_samples: int, crossfade_samples: int) -> Dict[str, float]:
    """
    Measure sample-to-sample discontinuities at chunk boundaries.
    """
    if len(audio) < chunk_samples * 2:
        return {"max_boundary_jump": 0.0, "mean_boundary_jump": 0.0, "interior_mean_jump": 0.0}

    boundary_jumps = []
    # Inspect boundaries between consecutive chunks
    for i in range(1, len(audio) // chunk_samples):
        b_idx = i * chunk_samples
        jump = abs(audio[b_idx] - audio[b_idx - 1])
        boundary_jumps.append(jump)

    interior_jumps = np.abs(np.diff(audio))

    return {
        "max_boundary_jump": float(np.max(boundary_jumps)) if boundary_jumps else 0.0,
        "mean_boundary_jump": float(np.mean(boundary_jumps)) if boundary_jumps else 0.0,
        "interior_mean_jump": float(np.mean(interior_jumps)) if len(interior_jumps) > 0 else 0.0,
    }


def run_benchmarks():
    base_dir = os.path.dirname(os.path.abspath(__file__))
    input_wav = os.path.join(base_dir, "test_audio", "input_speech.wav")
    model_path = os.path.join(base_dir, "models", "characters", "default", "default.pth")
    index_path = os.path.join(base_dir, "models", "characters", "default", "default.index")
    embedder_dir = os.path.join(base_dir, "models", "base", "contentvec")
    rmvpe_path = os.path.join(base_dir, "models", "base", "predictors", "rmvpe.pt")

    print("=" * 70)
    print("  VIXXO AI VOICE ENGINE 2.0 - HARDWARE BENCHMARK SUITE")
    print("=" * 70)

    # 1. System & GPU Information
    gpu_name = torch.cuda.get_device_name(0) if torch.cuda.is_available() else "None (CPU)"
    vram_total_mb = (
        torch.cuda.get_device_properties(0).total_memory / (1024 * 1024)
        if torch.cuda.is_available()
        else 0
    )
    print(f"Device:           {gpu_name}")
    print(f"Total VRAM:       {vram_total_mb:.0f} MB")
    print(f"PyTorch Version:  {torch.__version__}")
    print(f"CUDA Available:   {torch.cuda.is_available()}")

    # 2. Measure Model Loading Latency
    print("\n--- 1. Model Loading Latency Test ---")
    t0 = time.perf_counter()
    pipeline = RVCInferencePipeline(
        model_path=model_path,
        index_path=index_path,
        embedder_dir=embedder_dir,
        rmvpe_path=rmvpe_path,
        device="cuda" if torch.cuda.is_available() else "cpu",
        is_half=True,
    )
    total_load_ms = (time.perf_counter() - t0) * 1000.0

    load_metrics = {
        "total_load_ms": total_load_ms,
        "sample_rate": pipeline.tgt_sr,
        "version": pipeline.version,
    }
    print(f"Model Pipeline Loaded in: {total_load_ms:.2f} ms")

    # 3. Audio Chunk Benchmarks
    chunk_configs = [40, 60, 80, 120]  # in milliseconds
    audio_16k = load_audio(input_wav, sample_rate=16000)
    audio_duration_s = len(audio_16k) / 16000.0

    results = []

    print(f"\n--- 2. Streaming Chunk Latency Benchmarks ({audio_duration_s:.2f}s test speech) ---")
    print(f"{'Chunk':>8} | {'Context':>8} | {'F0 (ms)':>8} | {'Feat (ms)':>9} | {'Gen (ms)':>8} | {'Total':>8} | {'RTF':>7} | {'Headroom':>9} | {'VRAM':>7} | {'Verdict'}")
    print("-" * 96)

    for chunk_ms in chunk_configs:
        context_ms = 350
        crossfade_ms = 10

        processor = StreamBufferProcessor(
            pipeline=pipeline,
            chunk_ms=chunk_ms,
            context_ms=context_ms,
            crossfade_ms=crossfade_ms,
            pitch_shift=0,
            index_rate=0.75,
            sid=0,
        )

        chunk_samples = int(16000 * (chunk_ms / 1000.0))
        num_chunks = int(np.ceil(len(audio_16k) / chunk_samples))
        output_chunks = []
        metrics_list = []

        for i in range(num_chunks):
            start_idx = i * chunk_samples
            end_idx = min(start_idx + chunk_samples, len(audio_16k))
            chunk = audio_16k[start_idx:end_idx]
            if len(chunk) < chunk_samples:
                chunk = np.pad(chunk, (0, chunk_samples - len(chunk)))

            out_chunk, metrics = processor.process_chunk(chunk)
            output_chunks.append(out_chunk)
            metrics_list.append(metrics)

        # Discard the warmup chunk (first chunk incurs kernel initialization)
        steady_metrics = metrics_list[1:] if len(metrics_list) > 1 else metrics_list
        avg_f0 = float(np.mean([m["f0_ms"] for m in steady_metrics]))
        avg_feat = float(np.mean([m["feature_ms"] for m in steady_metrics]))
        avg_gen = float(np.mean([m["generator_ms"] for m in steady_metrics]))
        avg_total = float(np.mean([m["total_ms"] for m in steady_metrics]))
        p95_total = float(np.percentile([m["total_ms"] for m in steady_metrics], 95))
        rtf = avg_total / chunk_ms
        headroom = chunk_ms - avg_total
        peak_vram = float(max([m.get("peak_vram_mb", 0.0) for m in metrics_list]))

        full_audio = np.concatenate(output_chunks)
        out_file = os.path.join(base_dir, "test_audio", f"benchmark_{chunk_ms}ms.wav")
        sf.write(out_file, full_audio, pipeline.tgt_sr)

        # Discontinuity analysis
        tgt_chunk_samples = int(chunk_samples * (pipeline.tgt_sr / 16000))
        tgt_xf_samples = int(crossfade_ms / 1000.0 * pipeline.tgt_sr)
        disc = measure_discontinuities(full_audio, tgt_chunk_samples, tgt_xf_samples)

        verdict = "REALTIME" if rtf <= 1.0 else "UNDERRUN"

        print(f"{chunk_ms:>6}ms | {context_ms:>6}ms | {avg_f0:>8.2f} | {avg_feat:>9.2f} | {avg_gen:>8.2f} | {avg_total:>8.2f} | {rtf:>6.2f}x | {headroom:>+7.2f}ms | {peak_vram:>5.0f}MB | {verdict}")

        results.append({
            "chunk_ms": chunk_ms,
            "context_ms": context_ms,
            "crossfade_ms": crossfade_ms,
            "avg_f0_ms": avg_f0,
            "avg_feature_ms": avg_feat,
            "avg_generator_ms": avg_gen,
            "avg_total_ms": avg_total,
            "p95_total_ms": p95_total,
            "real_time_factor": rtf,
            "headroom_ms": headroom,
            "peak_vram_mb": peak_vram,
            "realtime_capable": bool(rtf <= 1.0),
            "discontinuity": disc,
            "output_wav": out_file,
        })

    # 4. Female Voice (Miku) Full-File Benchmark
    print("\n--- 3. Female Voice Conversion Benchmark (Miku +12 Semitones) ---")
    miku_model_path = os.path.join(base_dir, "models", "characters", "miku", "miku.pth")
    pipeline_miku = RVCInferencePipeline(
        model_path=miku_model_path,
        index_path=None,
        embedder_dir=embedder_dir,
        rmvpe_path=rmvpe_path,
        device="cuda" if torch.cuda.is_available() else "cpu",
        is_half=True,
    )
    converted_miku, miku_metrics = pipeline_miku.convert_audio(
        audio_16k,
        pitch_shift=12,
        index_rate=0.0,
        sid=0,
        volume_match=True,
    )
    miku_out_file = os.path.join(base_dir, "test_audio", "output_miku_full.wav")
    sf.write(miku_out_file, converted_miku, pipeline_miku.tgt_sr)
    print(f"Miku Output: {miku_out_file} (SR: {pipeline_miku.tgt_sr}Hz, Duration: {len(converted_miku)/pipeline_miku.tgt_sr:.2f}s)")
    print(f"  F0 Extraction:     {miku_metrics['f0_ms']:.2f} ms")
    print(f"  Feature Extract:   {miku_metrics['feature_ms']:.2f} ms")
    print(f"  Generator Synthes: {miku_metrics['generator_ms']:.2f} ms")
    print(f"  Total Time:        {miku_metrics['total_ms']:.2f} ms")
    print(f"  Real-Time Factor:  {miku_metrics['rtf']:.4f}x")
    print(f"  Peak VRAM:         {miku_metrics['peak_vram_mb']:.1f} MB")

    # 5. Export JSON Report
    report = {
        "hardware": {
            "device": gpu_name,
            "vram_total_mb": vram_total_mb,
            "cuda_available": torch.cuda.is_available(),
            "pytorch_version": torch.__version__,
        },
        "model_loading": load_metrics,
        "streaming_benchmarks": results,
        "female_voice_test": {
            "model": "MikuAI_e210_s6300 (RVC v2)",
            "pitch_shift": +12,
            "metrics": miku_metrics,
            "output_wav": miku_out_file,
        },
    }

    report_path = os.path.join(base_dir, "benchmark_report.json")
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, indent=2)

    print(f"\n[OK] Benchmark Report written to: {report_path}")
    print("=" * 70)


if __name__ == "__main__":
    run_benchmarks()
