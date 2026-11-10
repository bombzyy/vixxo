"""Minimal configuration stub for headless RVC worker."""

def load_config():
    return {
        "rmvpe_high_register": {
            "enabled": False,
            "mode": "true_pitch",
            "f0_ceil": 1250.0,
        }
    }
