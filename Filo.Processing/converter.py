
import subprocess
import queue
import threading
import time
import tempfile
from pathlib import Path


AUDIO_FORMATS = {"mp3", "flac", "wav"}
VIDEO_FORMATS = {"mp4", "webm", "mkv"}

SUPPORTED_FORMATS = AUDIO_FORMATS | VIDEO_FORMATS


def get_media_duration(input_path: Path) -> float | None:
    try:
        result = subprocess.run(
            [
                "ffprobe",
                "-v", "error",
                "-show_entries", "format=duration",
                "-of", "default=noprint_wrappers=1:nokey=1",
                str(input_path),
            ],
            capture_output=True,
            text=True,
            timeout=30,
            check=True,
        )

        duration = float(result.stdout.strip())

        if not 0 < duration < float("inf"):
            return None

        return duration

    except (
        FileNotFoundError,
        subprocess.CalledProcessError,
        subprocess.TimeoutExpired,
        ValueError,
    ):
        return None



def convert_media(
    input_path: str,
    output_path: str,
    target_format: str,
    video_codec: str | None = None,
    timeout_seconds: int = 3600,
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

    if timeout_seconds <= 0:
        raise ValueError("Conversion timeout must be positive.")

    destination.parent.mkdir(parents=True, exist_ok=True)
    duration_seconds = get_media_duration(source)

    command = [
        "ffmpeg",
        "-hide_banner",
        "-nostdin",
        "-y",
        "-progress", "pipe:1",
        "-stats_period", "1",
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
            "-movflags", "+faststart",
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
            raise ValueError("MKV currently supports H.264 only.")

        command += [
            "-c:v", "libx264",
            "-c:a", "aac",
        ]

    command.append(str(destination))

    progress_lines = queue.Queue()
    progress_data = {}
    process = None

    with tempfile.TemporaryFile(mode="w+t") as error_log:
        try:
            process = subprocess.Popen(
                command,
                stdout=subprocess.PIPE,
                stderr=error_log,
                text=True,
            )

            started_at = time.monotonic()

            def read_progress():
                try:
                    for line in process.stdout:
                        progress_lines.put(line)
                finally:
                    progress_lines.put(None)

            reader = threading.Thread(
                target=read_progress,
                daemon=True,
            )
            reader.start()

            reader_finished = False

            while not reader_finished:
                if time.monotonic() - started_at > timeout_seconds:
                    raise subprocess.TimeoutExpired(
                        command,
                        timeout_seconds,
                    )

                try:
                    line = progress_lines.get(timeout=0.25)

                except queue.Empty:
                    continue

                if line is None:
                    reader_finished = True
                    continue

                line = line.strip()

                if "=" not in line:
                    continue

                key, value = line.split("=", 1)
                progress_data[key] = value

                if key == "progress":
                    if value in ("continue", "end"):
                        raw_time = progress_data.get("out_time_us")

                        if raw_time is not None:
                            try:
                                processed_seconds = int(raw_time) / 1_000_000

                               
                                if processed_seconds >= 0:
                                    if duration_seconds is not None:
                                        percentage = min(
                                            100.0,
                                            max(
                                                0.0,
                                                (processed_seconds / duration_seconds) * 100,
                                            ),
                                        )

                                        print(
                                            f"Processed: {processed_seconds:.1f} / "
                                            f"{duration_seconds:.1f} seconds "
                                            f"({percentage:.1f}%)"
                                        )
                                    else:
                                        print(
                                            f"Processed: {processed_seconds:.1f} seconds "
                                            "(duration unknown)"
                                        )


                            except ValueError:
                                pass

                    progress_data.clear()

            remaining_time = max(
                0.001,
                timeout_seconds - (time.monotonic() - started_at),
            )

            process.wait(timeout=remaining_time)

            if process.returncode != 0:
                error_log.seek(0)
                error_message = error_log.read()[-4000:]

                raise RuntimeError(
                    f"FFmpeg conversion failed:\n{error_message}"
                )

        except FileNotFoundError as error:
            raise RuntimeError(
                "FFmpeg executable was not found. Check your PATH."
            ) from error

        except subprocess.TimeoutExpired as error:
            raise RuntimeError(
                f"FFmpeg conversion exceeded the "
                f"{timeout_seconds}-second time limit."
            ) from error

        finally:
            if process is not None:
                if process.poll() is None:
                    process.kill()

                process.wait()

                if process.stdout is not None:
                    process.stdout.close()

    if not destination.is_file() or destination.stat().st_size == 0:
        raise RuntimeError("FFmpeg did not produce a valid output file.")

    return destination
