
import json
import subprocess
from pathlib import Path


def probe_streams(path):
    result = subprocess.run(
        [
            "ffprobe",
            "-v", "error",
            "-show_entries", "stream=codec_type",
            "-of", "json",
            str(path)
        ],
        capture_output=True,
        text=True,
        check=True,
        timeout=60
    )

    return json.loads(result.stdout).get("streams", [])


def graft_media(input_paths, output_path, media_type):
    if len(input_paths) < 2:
        raise ValueError("At least two inputs are required.")

    if media_type not in {"audio", "video"}:
        raise ValueError("Unsupported grafting media type.")

    for path in input_paths:
        if not Path(path).is_file():
            raise FileNotFoundError(path)

        streams = probe_streams(path)

        has_audio = any(
            stream.get("codec_type") == "audio"
            for stream in streams
        )

        has_video = any(
            stream.get("codec_type") == "video"
            for stream in streams
        )

        if not has_audio:
            raise ValueError(
                f"Source has no audio stream: {path}"
            )

        if media_type == "video" and not has_video:
            raise ValueError(
                f"Source has no video stream: {path}"
            )

    command = ["ffmpeg", "-y", "-hide_banner", "-nostdin"]

    for path in input_paths:
        command += ["-i", str(path)]

    filters = []
    concat_inputs = ""

    for index in range(len(input_paths)):
        audio_filter = (
            f"[{index}:a:0]"
            "aresample=48000,"
            "aformat=channel_layouts=stereo,"
            "asetpts=PTS-STARTPTS"
            f"[a{index}]"
        )

        if media_type == "audio":
            filters.append(audio_filter)
            concat_inputs += f"[a{index}]"

        else:
            video_filter = (
                f"[{index}:v:0]"
                "scale=1280:720:force_original_aspect_ratio=decrease,"
                "pad=1280:720:(ow-iw)/2:(oh-ih)/2,"
                "setsar=1,"
                "fps=30,"
                "setpts=PTS-STARTPTS,"
                "format=yuv420p"
                f"[v{index}]"
            )

            filters.append(video_filter)
            filters.append(audio_filter)

            concat_inputs += f"[v{index}][a{index}]"

    if media_type == "audio":
        filters.append(
            f"{concat_inputs}"
            f"concat=n={len(input_paths)}:v=0:a=1[aout]"
        )

        command += [
            "-filter_complex", ";".join(filters),
            "-map", "[aout]",
            "-c:a", "libmp3lame",
            "-b:a", "192k"
        ]

    else:
        filters.append(
            f"{concat_inputs}"
            f"concat=n={len(input_paths)}:v=1:a=1[vout][aout]"
        )

        command += [
            "-filter_complex", ";".join(filters),
            "-map", "[vout]",
            "-map", "[aout]",
            "-c:v", "libx264",
            "-preset", "medium",
            "-crf", "23",
            "-pix_fmt", "yuv420p",
            "-c:a", "aac",
            "-b:a", "192k",
            "-movflags", "+faststart"
        ]

    command += [str(output_path)]

    result = subprocess.run(
        command,
        capture_output=True,
        text=True,
        timeout=3600
    )

    if result.returncode != 0:
        raise RuntimeError(
            "FFmpeg grafting failed:\n"
            + result.stderr[-4000:]
        )

    output = Path(output_path)

    if not output.is_file() or output.stat().st_size == 0:
        raise RuntimeError("Grafted output is missing or empty.")

    output_streams = probe_streams(output)

    expected_types = (
        {"audio"} if media_type == "audio"
        else {"video", "audio"}
    )

    actual_types = {
        stream.get("codec_type")
        for stream in output_streams
    }

    if not expected_types.issubset(actual_types):
        raise RuntimeError(
            "Grafted output is missing expected streams."
        )

    return output

