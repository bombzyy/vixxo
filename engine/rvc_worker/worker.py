import os
import sys
import argparse
import time
import json
import soundfile as sf
import numpy as np

# Add worker directory to sys.path
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from rvc.pipeline import RVCInferencePipeline
from rvc.stream_buffer import StreamBufferProcessor
from rvc.lib.utils import load_audio


def run_file_mode(pipeline: RVCInferencePipeline, args):
    print(f"\n[Worker] Running File Mode Conversion...")
    print(f"  Input: {args.input}")
    print(f"  Output: {args.output}")
    print(f"  Pitch Shift: {args.pitch} semitones")
    print(f"  Index Rate: {args.index_rate}")

    audio_16k = load_audio(args.input, sample_rate=16000)
    duration_s = len(audio_16k) / 16000.0
    print(f"  Loaded Input Duration: {duration_s:.2f}s ({len(audio_16k)} samples at 16kHz)")

    converted_audio, metrics = pipeline.convert_audio(
        audio_16k,
        pitch_shift=args.pitch,
        index_rate=args.index_rate,
        sid=args.sid,
        volume_match=True,
    )

    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    sf.write(args.output, converted_audio, pipeline.tgt_sr)
    file_size = os.path.getsize(args.output)
    print(f"[Worker] File conversion completed successfully!")
    print(f"  Saved to: {args.output} ({file_size:,} bytes, {pipeline.tgt_sr}Hz)")
    print(f"  Metrics:")
    print(f"    F0 Extraction:     {metrics['f0_ms']:.2f} ms")
    print(f"    Feature Extract:   {metrics['feature_ms']:.2f} ms")
    print(f"    Generator Synthes: {metrics['generator_ms']:.2f} ms")
    print(f"    Total Time:        {metrics['total_ms']:.2f} ms")
    print(f"    Real-Time Factor:  {metrics['rtf']:.4f}x ({'Faster than realtime' if metrics['rtf'] < 1.0 else 'Slower than realtime'})")
    print(f"    Peak VRAM:         {metrics['peak_vram_mb']:.1f} MB")

    return metrics


def run_stream_mode(pipeline: RVCInferencePipeline, args):
    print(f"\n[Worker] Running Streaming Simulation Mode...")
    print(f"  Chunk Size: {args.chunk_ms} ms")
    print(f"  Context History: {args.context_ms} ms")
    print(f"  Crossfade: {args.crossfade_ms} ms")
    print(f"  Input: {args.input}")
    print(f"  Output: {args.output}")

    audio_16k = load_audio(args.input, sample_rate=16000)
    chunk_samples = int(16000 * (args.chunk_ms / 1000.0))

    processor = StreamBufferProcessor(
        pipeline=pipeline,
        chunk_ms=args.chunk_ms,
        context_ms=args.context_ms,
        crossfade_ms=args.crossfade_ms,
        pitch_shift=args.pitch,
        index_rate=args.index_rate,
        sid=args.sid,
    )

    num_chunks = int(np.ceil(len(audio_16k) / chunk_samples))
    print(f"  Processing {len(audio_16k)/16000:.2f}s audio across {num_chunks} chunks ({args.chunk_ms}ms each)...")

    output_chunks = []
    chunk_timings = []

    # Warmup pass (first chunk incurs CUDA kernel launch overhead)
    for i in range(num_chunks):
        start_idx = i * chunk_samples
        end_idx = min(start_idx + chunk_samples, len(audio_16k))
        chunk = audio_16k[start_idx:end_idx]

        if len(chunk) < chunk_samples:
            chunk = np.pad(chunk, (0, chunk_samples - len(chunk)))

        out_chunk, metrics = processor.process_chunk(chunk)
        output_chunks.append(out_chunk)
        chunk_timings.append(metrics)

    # Discard warmup chunk for steady-state statistics
    steady_metrics = chunk_timings[1:] if len(chunk_timings) > 1 else chunk_timings
    avg_total = np.mean([m["total_ms"] for m in steady_metrics])
    p95_total = np.percentile([m["total_ms"] for m in steady_metrics], 95)
    avg_f0 = np.mean([m["f0_ms"] for m in steady_metrics])
    avg_feat = np.mean([m["feature_ms"] for m in steady_metrics])
    avg_gen = np.mean([m["generator_ms"] for m in steady_metrics])
    avg_rtf = avg_total / args.chunk_ms
    peak_vram = max([m.get("peak_vram_mb", 0.0) for m in chunk_timings])

    full_output = np.concatenate(output_chunks)
    os.makedirs(os.path.dirname(os.path.abspath(args.output)), exist_ok=True)
    sf.write(args.output, full_output, pipeline.tgt_sr)

    print(f"\n[Worker] Streaming simulation finished!")
    print(f"  Processed Chunks:    {num_chunks}")
    print(f"  Steady Avg Total:    {avg_total:.2f} ms per {args.chunk_ms}ms chunk")
    print(f"  Steady P95 Total:    {p95_total:.2f} ms")
    print(f"  Breakdown (Avg):")
    print(f"    - F0 Extraction:   {avg_f0:.2f} ms")
    print(f"    - Feature Extract: {avg_feat:.2f} ms")
    print(f"    - Generator Synt:  {avg_gen:.2f} ms")
    print(f"  Real-Time Margin:    {args.chunk_ms - avg_total:+.2f} ms headroom ({'REALTIME CAPABLE' if avg_total < args.chunk_ms else 'UNDERRUN / BUFFER STARVATION'})")
    print(f"  Chunk RTF:           {avg_rtf:.4f}x")
    print(f"  Peak VRAM:           {peak_vram:.1f} MB")
    print(f"  Saved Output:        {args.output} ({os.path.getsize(args.output):,} bytes)")

    summary = {
        "chunk_ms": args.chunk_ms,
        "context_ms": args.context_ms,
        "num_chunks": num_chunks,
        "avg_total_ms": float(avg_total),
        "p95_total_ms": float(p95_total),
        "avg_f0_ms": float(avg_f0),
        "avg_feature_ms": float(avg_feat),
        "avg_generator_ms": float(avg_gen),
        "avg_rtf": float(avg_rtf),
        "peak_vram_mb": float(peak_vram),
        "realtime_capable": bool(avg_total < args.chunk_ms),
    }
    return summary


def main():
    base_dir = os.path.dirname(os.path.abspath(__file__))

    def resolve_default(rel_path):
        return os.path.join(base_dir, rel_path)

    parser = argparse.ArgumentParser(description="Vixxo Standalone RVC Voice Engine Worker")
    parser.add_argument("--model", type=str, default=resolve_default("models/characters/default/default.pth"), help="Path to RVC .pth checkpoint")
    parser.add_argument("--index", type=str, default=resolve_default("models/characters/default/default.index"), help="Path to FAISS .index file")
    parser.add_argument("--embedder", type=str, default=resolve_default("models/base/contentvec"), help="Path to ContentVec directory")
    parser.add_argument("--rmvpe", type=str, default=resolve_default("models/base/predictors/rmvpe.pt"), help="Path to rmvpe.pt")
    parser.add_argument("--input", type=str, default=resolve_default("test_audio/input_speech.wav"), help="Input WAV path")
    parser.add_argument("--output", type=str, default=resolve_default("test_audio/output_worker.wav"), help="Output WAV path")
    parser.add_argument("--pitch", type=int, default=0, help="Pitch shift in semitones (-24 to +24)")
    parser.add_argument("--index-rate", type=float, default=0.75, help="FAISS retrieval mix rate (0.0 to 1.0)")
    parser.add_argument("--sid", type=int, default=0, help="Speaker ID")
    parser.add_argument("--device", type=str, default="cuda", help="Inference device ('cuda' or 'cpu')")
    parser.add_argument("--fp16", action="store_true", default=True, help="Enable FP16 half precision")
    parser.add_argument("--mode", type=str, choices=["file", "stream"], default="file", help="Processing mode: 'file' or 'stream'")
    parser.add_argument("--chunk-ms", type=int, default=80, help="Audio chunk size in ms for streaming mode")
    parser.add_argument("--context-ms", type=int, default=350, help="Context history in ms for streaming mode")
    parser.add_argument("--crossfade-ms", type=int, default=10, help="Crossfade overlap in ms for streaming mode")

    args = parser.parse_args()

    pipeline = RVCInferencePipeline(
        model_path=args.model,
        index_path=args.index if args.index and os.path.exists(args.index) else None,
        embedder_dir=args.embedder,
        rmvpe_path=args.rmvpe,
        device=args.device,
        is_half=args.fp16,
    )

    if args.mode == "file":
        run_file_mode(pipeline, args)
    else:
        run_stream_mode(pipeline, args)


if __name__ == "__main__":
    main()
