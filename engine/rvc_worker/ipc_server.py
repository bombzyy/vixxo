import os
import sys

if hasattr(sys.stdout, "reconfigure"):
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
if hasattr(sys.stderr, "reconfigure"):
    sys.stderr.reconfigure(encoding="utf-8", errors="replace")

import time
import json
import struct
import zlib
import mmap
import ctypes
import threading
import argparse
from typing import Optional

import numpy as np
import torch

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from rvc.pipeline import RVCInferencePipeline
from rvc.stream_buffer import StreamBufferProcessor

# Windows API constants and functions for Event handling
kernel32 = ctypes.windll.kernel32
SYNCHRONIZE = 0x00100000
EVENT_MODIFY_STATE = 0x0002
WAIT_OBJECT_0 = 0
WAIT_TIMEOUT = 258

SHM_MAGIC = 0x56495858  # 'VIXX'
SHM_VERSION = 1
SHM_SIZE = 262144  # 256 KB
HEADER_SIZE = 128
IN_BUFFER_OFFSET = 128
OUT_BUFFER_OFFSET = 65664


class WindowsNamedPipeServer:
    """Lightweight Windows Named Pipe Server using ctypes."""

    PIPE_ACCESS_DUPLEX = 0x00000003
    PIPE_TYPE_BYTE = 0x00000000
    PIPE_READMODE_BYTE = 0x00000000
    PIPE_WAIT = 0x00000000
    PIPE_UNLIMITED_INSTANCES = 255
    BUFFER_SIZE = 65536

    def __init__(self, pipe_name: str):
        self.pipe_name = pipe_name
        self.handle = None
        self._read_buffer = ""

    def create(self):
        self.handle = kernel32.CreateNamedPipeW(
            self.pipe_name,
            self.PIPE_ACCESS_DUPLEX,
            self.PIPE_TYPE_BYTE | self.PIPE_READMODE_BYTE | self.PIPE_WAIT,
            self.PIPE_UNLIMITED_INSTANCES,
            self.BUFFER_SIZE,
            self.BUFFER_SIZE,
            0,
            None,
        )
        if self.handle == -1 or self.handle == 0:
            raise RuntimeError(f"Failed to create named pipe {self.pipe_name}, error: {kernel32.GetLastError()}")

    def wait_for_client(self):
        connected = kernel32.ConnectNamedPipe(self.handle, None)
        if not connected:
            err = kernel32.GetLastError()
            ERROR_PIPE_CONNECTED = 535
            if err != ERROR_PIPE_CONNECTED:
                raise RuntimeError(f"ConnectNamedPipe failed: {err}")
        self._read_buffer = ""
        return True

    def read_message(self) -> Optional[str]:
        # Read lines delimited by \n
        while "\n" not in self._read_buffer:
            buf = ctypes.create_string_buffer(self.BUFFER_SIZE)
            bytes_read = ctypes.c_ulong(0)
            res = kernel32.ReadFile(self.handle, buf, self.BUFFER_SIZE, ctypes.byref(bytes_read), None)
            if not res or bytes_read.value == 0:
                if self._read_buffer.strip():
                    msg = self._read_buffer.strip()
                    self._read_buffer = ""
                    return msg
                return None
            self._read_buffer += buf.raw[:bytes_read.value].decode("utf-8", errors="replace")

        line, self._read_buffer = self._read_buffer.split("\n", 1)
        return line.strip()

    def write_message(self, text: str) -> bool:
        if not text.endswith("\n"):
            text += "\n"
        data = text.encode("utf-8")
        bytes_written = ctypes.c_ulong(0)
        res = kernel32.WriteFile(self.handle, data, len(data), ctypes.byref(bytes_written), None)
        return bool(res)

    def close(self):
        if self.handle and self.handle != -1:
            kernel32.DisconnectNamedPipe(self.handle)
            kernel32.CloseHandle(self.handle)
            self.handle = None


class RvcIpcServer:
    def __init__(
        self,
        pipe_prefix: str = "vixxo_voice",
        default_model: str = "models/characters/default/default.pth",
        default_index: str = "models/characters/default/default.index",
        device: str = "cuda",
        is_half: bool = True,
        chunk_ms: int = 80,
        context_ms: int = 350,
    ):
        self.base_dir = os.path.dirname(os.path.abspath(__file__))
        self.pipe_prefix = pipe_prefix
        self.control_pipe_name = f"\\\\.\\pipe\\{pipe_prefix}_control"
        self.shm_name = f"Local\\{pipe_prefix}_shm"
        self.event_in_name = f"Local\\{pipe_prefix}_event_in"
        self.event_out_name = f"Local\\{pipe_prefix}_event_out"

        self.device = device
        self.is_half = is_half
        self.chunk_ms = chunk_ms
        self.context_ms = context_ms

        self.pipeline: Optional[RVCInferencePipeline] = None
        self.processor: Optional[StreamBufferProcessor] = None

        self.running = False
        self.current_pitch = 0
        self.current_index_rate = 0.75
        self.current_sid = 0

        # SHM and Events
        self.shm: Optional[mmap.mmap] = None
        self.h_event_in = None
        self.h_event_out = None

        # Telemetry
        self.last_f0_ms = 0.0
        self.last_feature_ms = 0.0
        self.last_generator_ms = 0.0
        self.last_total_ms = 0.0
        self.processed_chunks_count = 0

        # Load default model
        model_full = os.path.join(self.base_dir, default_model)
        index_full = os.path.join(self.base_dir, default_index)
        self.load_model(model_full, index_full)

    def load_model(self, model_path: str, index_path: Optional[str] = None):
        if not os.path.isabs(model_path):
            model_path = os.path.join(self.base_dir, model_path)
        if index_path and not os.path.isabs(index_path):
            index_path = os.path.join(self.base_dir, index_path)

        embedder_dir = os.path.join(self.base_dir, "models", "base", "contentvec")
        rmvpe_path = os.path.join(self.base_dir, "models", "base", "predictors", "rmvpe.pt")

        print(f"[IPC] Loading model: {model_path} (Index: {index_path})")
        t0 = time.perf_counter()
        self.pipeline = RVCInferencePipeline(
            model_path=model_path,
            index_path=index_path if index_path and os.path.exists(index_path) else None,
            embedder_dir=embedder_dir,
            rmvpe_path=rmvpe_path,
            device=self.device,
            is_half=self.is_half,
        )
        self.processor = StreamBufferProcessor(
            pipeline=self.pipeline,
            chunk_ms=self.chunk_ms,
            context_ms=self.context_ms,
            crossfade_ms=10,
            pitch_shift=self.current_pitch,
            index_rate=self.current_index_rate,
            sid=self.current_sid,
        )
        print(f"[IPC] Model loaded successfully in {(time.perf_counter()-t0)*1000:.1f}ms! (SR: {self.pipeline.tgt_sr}Hz)")

        # Warm up CUDA kernels, memory allocator, and cuDNN with dummy chunk
        try:
            warmup_samples = int(16000 * (self.chunk_ms / 1000.0))
            dummy_chunk = np.zeros(warmup_samples, dtype=np.float32)
            self.processor.process_chunk(dummy_chunk)
            self.processor.reset()
            print(f"[IPC] Inference pipeline warmed up on {self.device.upper()}.")
        except Exception as e:
            print(f"[IPC WARNING] Warmup inference error: {e}")

    def init_shared_memory(self):
        print(f"[IPC] Opening Shared Memory: {self.shm_name} ({SHM_SIZE} bytes)")
        self.shm = mmap.mmap(-1, SHM_SIZE, tagname=self.shm_name)

        # Initialize header
        self.shm.seek(0)
        header = struct.pack(
            "<IIIIIIIIqfIII",
            SHM_MAGIC,
            SHM_VERSION,
            0,  # in_seq
            0,  # in_samples
            16000,  # in_sr
            0,  # out_seq
            0,  # out_samples
            self.pipeline.tgt_sr if self.pipeline else 40000,
            0,  # timestamp_us
            0.0,  # processing_ms
            1,  # flags: 1 = active
            0,  # in_checksum
            0,  # out_checksum
        )
        self.shm.write(header)

        # Open/Create named synchronization events
        self.h_event_in = kernel32.CreateEventW(None, False, False, self.event_in_name)
        self.h_event_out = kernel32.CreateEventW(None, False, False, self.event_out_name)
        print(f"[IPC] Shared Memory & Synchronization Events Initialized")

    def audio_loop(self):
        """High-priority audio processing loop waiting on SHM input event."""
        print("[IPC] Audio inference worker loop started")
        last_in_seq = 0

        while self.running:
            # Wait for C# to signal input chunk (100ms timeout)
            res = kernel32.WaitForSingleObject(self.h_event_in, 100)
            if res != WAIT_OBJECT_0:
                continue
            if not self.running:
                break

            # Read header
            self.shm.seek(0)
            header_bytes = self.shm.read(56)
            (
                magic,
                ver,
                in_seq,
                in_samples,
                in_sr,
                out_seq,
                out_samples,
                out_sr,
                ts_us,
                proc_ms,
                flags,
                in_cksum,
                out_cksum,
            ) = struct.unpack("<IIIIIIIIqfIII", header_bytes)

            if magic != SHM_MAGIC or in_samples == 0:
                continue

            # Read audio data from SHM input buffer
            self.shm.seek(IN_BUFFER_OFFSET)
            raw_audio = self.shm.read(in_samples * 4)

            # Packet checksum validation
            calc_cksum = zlib.crc32(raw_audio) & 0xFFFFFFFF
            if in_cksum != 0 and calc_cksum != in_cksum:
                print(f"[IPC WARNING] Checksum mismatch! in={in_cksum}, calc={calc_cksum}. Skipping packet {in_seq}.")
                continue

            in_audio = np.frombuffer(raw_audio, dtype=np.float32)

            # Perform neural inference via StreamBufferProcessor
            t_start = time.perf_counter()
            out_audio, metrics = self.processor.process_chunk(in_audio)
            proc_time = (time.perf_counter() - t_start) * 1000.0

            self.last_f0_ms = metrics.get("f0_ms", 0.0)
            self.last_feature_ms = metrics.get("feature_ms", 0.0)
            self.last_generator_ms = metrics.get("generator_ms", 0.0)
            self.last_total_ms = proc_time
            self.processed_chunks_count += 1

            # Write converted audio to SHM output buffer
            out_raw = out_audio.astype(np.float32).tobytes()
            out_cksum = zlib.crc32(out_raw) & 0xFFFFFFFF

            self.shm.seek(OUT_BUFFER_OFFSET)
            self.shm.write(out_raw)

            # Update SHM output header fields
            self.shm.seek(20)  # offset of out_seq
            out_header = struct.pack(
                "<IIIqfIII",
                in_seq,  # Echo matching sequence number
                len(out_audio),  # out_samples
                self.pipeline.tgt_sr,  # out_sr
                ts_us,  # Echo original timestamp
                proc_time,  # processing_ms
                flags,  # flags
                in_cksum,
                out_cksum,
            )
            self.shm.write(out_header)

            # Signal C# that converted audio is ready
            kernel32.SetEvent(self.h_event_out)

        print("[IPC] Audio inference worker loop exited")

    def control_loop(self):
        """Asynchronous control channel over Windows Named Pipe."""
        print(f"[IPC] Control channel listening on {self.control_pipe_name}")
        pipe = WindowsNamedPipeServer(self.control_pipe_name)

        while self.running:
            try:
                pipe.create()
                print("[IPC] Control channel waiting for client connection...")
                pipe.wait_for_client()
                print("[IPC] C# Voice Client connected!")

                while self.running:
                    msg = pipe.read_message()
                    if msg is None:
                        print("[IPC] Client disconnected from control channel")
                        break

                    msg = msg.lstrip("\ufeff").strip()
                    if not msg:
                        continue

                    try:
                        req = json.loads(msg)
                    except json.JSONDecodeError:
                        pipe.write_message(json.dumps({"status": "error", "message": "Invalid JSON"}))
                        continue

                    cmd = req.get("cmd")
                    msg_id = req.get("id")

                    if cmd == "ping":
                        resp = {"status": "pong", "id": msg_id, "timestamp": time.time()}
                        pipe.write_message(json.dumps(resp))

                    elif cmd == "stats":
                        vram_mb = torch.cuda.max_memory_allocated(0) / (1024 * 1024) if torch.cuda.is_available() else 0.0
                        resp = {
                            "status": "ok",
                            "id": msg_id,
                            "f0_ms": self.last_f0_ms,
                            "feature_ms": self.last_feature_ms,
                            "generator_ms": self.last_generator_ms,
                            "total_ms": self.last_total_ms,
                            "chunks_processed": self.processed_chunks_count,
                            "peak_vram_mb": vram_mb,
                            "sample_rate": self.pipeline.tgt_sr if self.pipeline else 40000,
                            "device": str(self.pipeline.device) if self.pipeline else "unknown",
                        }
                        pipe.write_message(json.dumps(resp))

                    elif cmd == "set_params":
                        if "pitch" in req:
                            self.current_pitch = int(req["pitch"])
                            self.processor.pitch_shift = self.current_pitch
                        if "index_rate" in req:
                            self.current_index_rate = float(req["index_rate"])
                            self.processor.index_rate = self.current_index_rate
                        if "sid" in req:
                            self.current_sid = int(req["sid"])
                            self.processor.sid = self.current_sid
                        print(f"[IPC] Parameters updated: pitch={self.current_pitch}, index_rate={self.current_index_rate}")
                        pipe.write_message(json.dumps({"status": "ok", "id": msg_id}))

                    elif cmd == "load_model":
                        model_path = req.get("model_path")
                        index_path = req.get("index_path")
                        try:
                            self.load_model(model_path, index_path)
                            # Update SHM with new target sample rate
                            self.shm.seek(28)
                            self.shm.write(struct.pack("<I", self.pipeline.tgt_sr))
                            resp = {
                                "status": "ok",
                                "id": msg_id,
                                "sample_rate": self.pipeline.tgt_sr,
                                "version": self.pipeline.version,
                            }
                            pipe.write_message(json.dumps(resp))
                        except Exception as e:
                            print(f"[IPC ERROR] Failed to load model: {e}")
                            pipe.write_message(json.dumps({"status": "error", "id": msg_id, "message": str(e)}))

                    elif cmd == "reset_buffer":
                        if self.processor:
                            self.processor.reset()
                        pipe.write_message(json.dumps({"status": "ok", "id": msg_id}))

                    elif cmd == "shutdown":
                        print("[IPC] Received shutdown command")
                        pipe.write_message(json.dumps({"status": "bye", "id": msg_id}))
                        self.running = False
                        # Wake up audio thread
                        if self.h_event_in:
                            kernel32.SetEvent(self.h_event_in)
                        break

            except Exception as e:
                print(f"[IPC Exception] Control pipe error: {e}")
            finally:
                pipe.close()

    def start(self):
        self.running = True
        self.init_shared_memory()

        # Start audio worker in dedicated background thread
        audio_thread = threading.Thread(target=self.audio_loop, daemon=True, name="RvcAudioWorker")
        audio_thread.start()

        # Run control loop in main thread
        try:
            self.control_loop()
        finally:
            self.running = False
            if self.h_event_in:
                kernel32.SetEvent(self.h_event_in)
            audio_thread.join(timeout=1.0)
            self.cleanup()

    def cleanup(self):
        print("[IPC] Cleaning up handles...")
        if self.shm:
            try:
                self.shm.close()
            except Exception:
                pass
            self.shm = None

        if self.h_event_in:
            kernel32.CloseHandle(self.h_event_in)
            self.h_event_in = None
        if self.h_event_out:
            kernel32.CloseHandle(self.h_event_out)
            self.h_event_out = None
        print("[IPC] Cleaned up cleanly.")


def main():
    parser = argparse.ArgumentParser(description="Vixxo AI RVC IPC Worker Server")
    parser.add_argument("--pipe-prefix", type=str, default="vixxo_voice", help="Named Pipe and SHM prefix")
    parser.add_argument("--model", type=str, default="models/characters/default/default.pth", help="Default RVC model")
    parser.add_argument("--index", type=str, default="models/characters/default/default.index", help="Default FAISS index")
    parser.add_argument("--device", type=str, default="cuda", help="Inference device ('cuda' or 'cpu')")
    parser.add_argument("--chunk-ms", type=int, default=80, help="Audio chunk size in ms (80 or 120)")
    parser.add_argument("--context-ms", type=int, default=350, help="Context history in ms")
    args = parser.parse_args()

    server = RvcIpcServer(
        pipe_prefix=args.pipe_prefix,
        default_model=args.model,
        default_index=args.index,
        device=args.device,
        chunk_ms=args.chunk_ms,
        context_ms=args.context_ms,
    )
    server.start()


if __name__ == "__main__":
    main()
