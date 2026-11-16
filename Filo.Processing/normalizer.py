
import json
import re
import subprocess
import math
from pathlib import Path


def analyze_loudness(input_path: str) -> dict:
    source = Path(input_path)

    if not source.is_file():
        raise FileNotFoundError(
            f"Source file not found: {source}"
        )

    command = [
        "ffmpeg",
        "-hide_banner",
        "-nostdin",
        "-i", str(source),
        "-vn",
        "-af", "loudnorm=I=-16:TP=-1.5:LRA=11:print_format=json",
        "-f", "null",
        "-",
    ]

    result = subprocess.run(
        command,
        capture_output=True,
        text=True,
        timeout=3600,
    )

    if result.returncode != 0:
        raise RuntimeError(
            f"Loudness analysis failed:\n{result.stderr[-4000:]}"
        )

    match = re.search(
        r'\{\s*"input_i".*?\}',
        result.stderr,
        re.DOTALL,
    )

    if match is None:
        raise RuntimeError(
            "FFmpeg did not return loudness measurements."
        )

    measurements = json.loads(match.group())

    return {
        "integrated_lufs": float(measurements["input_i"]),
        "true_peak_dbfs": float(measurements["input_tp"]),
        "loudness_range_lu": float(measurements["input_lra"]),
        "threshold_lufs": float(measurements["input_thresh"]),
    }




def calculate_normalization_gain(
    measurements: dict,
    target_lufs: float = -16.0,
    max_true_peak_dbfs: float = -1.5,
) -> float:
    integrated_lufs = measurements["integrated_lufs"]
    true_peak_dbfs = measurements["true_peak_dbfs"]

    if not math.isfinite(integrated_lufs):
        raise ValueError("Invalid integrated loudness measurement.")

    if not math.isfinite(true_peak_dbfs):
        raise ValueError("Invalid true peak measurement.")

    desired_gain = target_lufs - integrated_lufs
    maximum_safe_gain = max_true_peak_dbfs - true_peak_dbfs

    return min(desired_gain, maximum_safe_gain)

