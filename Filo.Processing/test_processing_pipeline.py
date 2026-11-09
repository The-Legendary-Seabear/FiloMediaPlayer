
import argparse
import tempfile
import subprocess
from pathlib import Path

from api_client import FiloApiClient
from downloader import download_media
from converter import convert_media


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("job_id", type=int)
    parser.add_argument("target_format")
    parser.add_argument("--codec", default=None)
    args = parser.parse_args()

    api = FiloApiClient()

    try:
        source = api.get_job_source(args.job_id)

        with tempfile.TemporaryDirectory() as temp_dir:
            work_dir = Path(temp_dir)

            source_ext = (
                Path(source["originalFileName"]).suffix or ".bin"
            )

            input_path = work_dir / f"input{source_ext}"

            output_path = (
                work_dir / f"converted.{args.target_format}"
            )

            print("Downloading original media...")

            download_media(
                source["downloadUrl"],
                input_path,
                source["fileSizeBytes"] or None,
            )

            print(f"Downloaded: {input_path.stat().st_size:,} bytes")

            print("Converting media...")

            converted = convert_media(
                str(input_path),
                str(output_path),
                args.target_format,
                args.codec,
            )

            print(f"Converted: {converted.stat().st_size:,} bytes")

            print("Inspecting output...")

            subprocess.run(
                [
                    "ffprobe",
                    "-v", "error",
                    "-show_entries",
                    "stream=codec_name,codec_type",
                    "-of", "default=noprint_wrappers=1",
                    str(converted),
                ],
                check=True,
            )

            print("Download and conversion successful!")

    finally:
        api.close()


if __name__ == "__main__":
    main()
