import os
import sys
import requests
from tqdm import tqdm

MODELS = [
    {
        "name": "ContentVec Config",
        "url": "https://huggingface.co/IAHispano/Applio/resolve/main/Resources/embedders/contentvec/config.json",
        "dest": "models/base/contentvec/config.json",
    },
    {
        "name": "ContentVec Weights (pytorch_model.bin)",
        "url": "https://huggingface.co/IAHispano/Applio/resolve/main/Resources/embedders/contentvec/pytorch_model.bin",
        "dest": "models/base/contentvec/pytorch_model.bin",
    },
    {
        "name": "RMVPE Pitch Extractor (rmvpe.pt)",
        "url": "https://huggingface.co/IAHispano/Applio/resolve/main/Resources/predictors/rmvpe.pt",
        "dest": "models/base/predictors/rmvpe.pt",
    },
    {
        "name": "RVC v2 Default Character (default.pth)",
        "url": "https://huggingface.co/PhoenixStormJr/RVC-V2-default-voice/resolve/main/default.pth",
        "dest": "models/characters/default/default.pth",
    },
    {
        "name": "RVC v2 Default Index (added_...index)",
        "url": "https://huggingface.co/PhoenixStormJr/RVC-V2-default-voice/resolve/main/added_IVF511_Flat_nprobe_1_default_v2.index",
        "dest": "models/characters/default/default.index",
    },
    {
        "name": "RVC v2 Miku Female Character (MikuAI_e210_s6300.pth)",
        "url": "https://huggingface.co/HidekoHaruna/RVC_V2_Hatsune_Miku/resolve/main/weights/MikuAI_e210_s6300.pth",
        "dest": "models/characters/miku/miku.pth",
    },
]

def download_file(url: str, dest_path: str, desc: str):
    os.makedirs(os.path.dirname(dest_path), exist_ok=True)
    if os.path.exists(dest_path) and os.path.getsize(dest_path) > 1000:
        print(f"[SKIP] {desc} already exists at {dest_path} ({os.path.getsize(dest_path)} bytes)")
        return

    print(f"[DOWNLOADING] {desc} from {url} -> {dest_path}")
    headers = {"User-Agent": "Mozilla/5.0 (Windows NT 10.0; Win64; x64)"}
    with requests.get(url, stream=True, headers=headers, timeout=60) as resp:
        resp.raise_for_status()
        total_size = int(resp.headers.get("content-length", 0))
        with open(dest_path, "wb") as f, tqdm(
            desc=desc,
            total=total_size,
            unit="B",
            unit_scale=True,
            unit_divisor=1024,
        ) as bar:
            for chunk in resp.iter_content(chunk_size=1024 * 1024):
                if chunk:
                    f.write(chunk)
                    bar.update(len(chunk))
    print(f"[OK] Downloaded {desc} successfully ({os.path.getsize(dest_path)} bytes)")

if __name__ == "__main__":
    base_dir = os.path.dirname(os.path.abspath(__file__))
    os.chdir(base_dir)
    for m in MODELS:
        dest = os.path.join(base_dir, m["dest"])
        download_file(m["url"], dest, m["name"])
    print("\nAll prerequisite and test models downloaded successfully!")
