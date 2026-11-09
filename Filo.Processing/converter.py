
import subprocess
from pathlib import Path


AUDIO_FORMATS = {"mp3", "flac", "wav"}
VIDEO_FORMATS = {"mp4", "webm", "mkv"}

SUPPORTED_FORMATS = AUDIO_FORMATS | VIDEO_FORMATS


def convert_media(
    input_path: str,
    output_path: str,
    target_format: str,
    video_codec: str | None = None,
):
    source = Path(input_path)
    destination = Path(output_path)
    target_format = target_format.lower().lstrip(".")

    if not source.is_file():
        raise FileNotFoundError(f"Source file not found: {source}")

    if target_format not in SUPPORTED_FORMATS:
        raise ValueError(f"Unsupported output format: {target_format}")

    if destination.suffix.lower() != f".{target_format}":
        raise ValueError("Output filename extension must match target format.")

    destination.parent.mkdir(parents=True, exist_ok=True)

    command = [
        "ffmpeg",
        "-hide_banner",
        "-nostdin",
        "-y",
        "-i", str(source),
    ]

    if target_format in AUDIO_FORMATS:
        command += ["-vn"]

        if target_format == "mp3":
            command += ["-c:a", "libmp3lame", "-q:a", "2"]
        elif target_format == "flac":
            command += ["-c:a", "flac"]
        elif target_format == "wav":
            command += ["-c:a", "pcm_s16le"]

    elif target_format == "mp4":
        if video_codec not in (None, "h264", "libx264"):
            raise ValueError("MP4 currently supports H.264 only.")

        command += [
            "-c:v", "libx264",
            "-c:a", "aac",
            "-pix_fmt", "yuv420p",
        ]

    
    elif target_format == "webm":
        codec = (video_codec or "vp9").lower()

        if codec in ("vp8", "libvpx"):
            command += [
                "-c:v", "libvpx",
                "-b:v", "1M",
                "-c:a", "libopus",
            ]
        elif codec in ("vp9", "libvpx-vp9"):
            command += [
                "-c:v", "libvpx-vp9",
                "-b:v", "0",
                "-crf", "32",
                "-c:a", "libopus",
            ]
        else:
            raise ValueError(f"Unsupported WebM codec: {codec}")


    
    elif target_format == "mkv":
        if video_codec not in (None, "h264", "libx264"):
            raise ValueError(
                "MKV currently supports H.264 only."
            )

        command += [
            "-c:v", "libx264",
            "-c:a", "aac",
        ]


    command.append(str(destination))

    try:
        subprocess.run(
            command,
            check=True,
            capture_output=True,
            text=True,
        )
    except FileNotFoundError as error:
        raise RuntimeError(
            "FFmpeg executable was not found. Check your PATH."
        ) from error
    except subprocess.CalledProcessError as error:
        raise RuntimeError(
            f"FFmpeg conversion failed:\n{error.stderr[-4000:]}"
        ) from error

    if not destination.is_file() or destination.stat().st_size == 0:
        raise RuntimeError("FFmpeg did not produce a valid output file.")

    return destination
