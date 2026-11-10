# Vixxo AI Voice Engine 2.0 - Standalone RVC Worker (Phase 1)

Headless Retrieval-based Voice Conversion (RVC) v1/v2 inference engine optimized for NVIDIA GeForce RTX GPUs with pure PyTorch, RMVPE pitch tracking, and ContentVec feature extraction.

---

## 1. Architecture Overview

```
                      +---------------------------------------+
                      |       Input Audio (WAV / Stream)      |
                      +---------------------------------------+
                                          |
                                          v Resample to 16,000 Hz Mono
                      +---------------------------------------+
                      |         Sliding Buffer & Window       |
                      +---------------------------------------+
                                    |           |
            +-----------------------+           +-----------------------+
            |                                                           |
            v                                                           v
+-------------------------------+                       +-------------------------------+
|      RMVPE Predictor          |                       |     ContentVec Embedder       |
| (ResNet + BiGRU E2E Pitch Net)|                       |  (12-layer Transformer Enc)   |
|         [100 Hz F0]           |                       |     [50 Hz Representations]   |
+-------------------------------+                       +-------------------------------+
            |                                                           |
            v Pitch Shift & MIDI Bins                                   v 2x Interpolation
    [f0_coarse, f0_fine]                                        +-------------------------------+
            |                                                   |     FAISS Index Search        |
            |                                                   |    (Cosine Similarity Mix)    |
            |                                                   +-------------------------------+
            |                                                                   |
            +---------------------------+---------------------------------------+
                                        |
                                        v
                    +---------------------------------------+
                    |    VITS Synthesizer (HiFi-GAN NSF)    |
                    | (Neural Source Filter Harmonic Gen)   |
                    +---------------------------------------+
                                        |
                                        v Target SR (40kHz / 48kHz)
                    +---------------------------------------+
                    |       GPU RMS Envelope Matching       |
                    +---------------------------------------+
                                        |
                                        v Overlap-Add Crossfade (10ms)
                    +---------------------------------------+
                    |    Clean Converted Waveform Output    |
                    +---------------------------------------+
```

---

## 2. Directory Structure

```
engine/rvc_worker/
├── .venv/                      # Python 3.11 virtual environment with PyTorch cu121
├── models/
│   ├── base/
│   │   ├── contentvec/         # ContentVec Transformer (config.json + pytorch_model.bin)
│   │   └── predictors/         # RMVPE pitch extractor (rmvpe.pt)
│   └── characters/
│       ├── default/            # Default RVC v2 voice model (default.pth + default.index)
│       └── miku/               # Miku female anime voice model (miku.pth)
├── rvc/
│   ├── lib/
│   │   ├── algorithm/          # VITS synthesizers and HiFi-GAN NSF generators
│   │   ├── predictors/         # RMVPE pitch extractor & chunking
│   │   ├── utils.py            # Audio loading & HuBERT model definition
│   │   └── user_config.py      # Minimal headless configuration stub
│   ├── pipeline.py             # Core RVCInferencePipeline (PyTorch GPU/CPU)
│   └── stream_buffer.py        # Real-time sliding buffer & overlap-add crossfader
├── test_audio/                 # Input speech and converted benchmark WAV files
├── worker.py                   # Standalone CLI and stream simulation worker
├── benchmark.py                # Automated hardware benchmarking suite
├── download_models.py          # Automated model downloader
├── requirements.txt            # Pinned Python package dependencies
└── benchmark_report.json       # Measured RTX 4050 benchmark metrics
```

---

## 3. Hardware Benchmark Results (NVIDIA GeForce RTX 4050 6GB Laptop GPU)

### Streaming Chunk Performance (Audio Context: 350ms, Crossfade: 10ms)

| Chunk Size | Context | F0 (RMVPE) | Features (HuBERT) | Generator (HiFi-GAN) | Total Inference | Real-Time Factor | Headroom | Peak VRAM | Verdict |
|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **40 ms** | 350 ms | 22.38 ms | 9.38 ms | 40.97 ms | **73.49 ms** | 1.84x | -33.49 ms | 626 MB | **UNDERRUN** |
| **60 ms** | 350 ms | 21.90 ms | 9.53 ms | 42.03 ms | **74.19 ms** | 1.24x | -14.19 ms | 626 MB | **UNDERRUN** |
| **80 ms** | 350 ms | 22.27 ms | 9.86 ms | 41.59 ms | **74.46 ms** | **0.93x** | **+5.54 ms** | 626 MB | **REALTIME** |
| **120 ms** | 350 ms | 22.27 ms | 9.59 ms | 40.81 ms | **73.37 ms** | **0.61x** | **+46.63 ms** | 626 MB | **REALTIME** |

### Batch Mode Performance (5.0s Speech Input)

- **Default Voice Model (Male / Neutral, Pitch 0)**:
  - Total Inference Time: **825.70 ms** for 5.0s audio
  - Real-Time Factor (RTF): **0.1651x** (6x faster than real-time)
  - Peak VRAM: **722.5 MB**
- **Miku Female Voice Model (Pitch +12 semitones / 1 octave up)**:
  - Total Inference Time: **196.77 ms** for 5.0s audio
  - Real-Time Factor (RTF): **0.0394x** (25x faster than real-time)
  - Peak VRAM: **1,347.5 MB**

### CPU Fallback Execution (1.0s Speech Input)
- Device: `cpu` (FP32)
- Total Inference Time: **763.99 ms**
- RTF: **0.7640x**
- Peak VRAM: **0.0 MB**

---

## 4. Usage Instructions

### Run File Conversion
```bash
python worker.py --mode file --model models/characters/default/default.pth --index models/characters/default/default.index --input test_audio/input_speech.wav --output test_audio/output.wav --pitch 0 --index-rate 0.75
```

### Run Female Voice (+12 semitone shift)
```bash
python worker.py --mode file --model models/characters/miku/miku.pth --input test_audio/input_speech.wav --output test_audio/output_female.wav --pitch 12 --index-rate 0.0
```

### Run Streaming Simulation (e.g. 80ms or 120ms chunk)
```bash
python worker.py --mode stream --chunk-ms 80 --context-ms 350 --input test_audio/input_speech.wav --output test_audio/output_stream.wav
```

### Run Automated Benchmark Suite
```bash
python benchmark.py
```
