
import argparse
import subprocess
import tempfile
from pathlib import Path

from api_client import FiloApiClient
from downloader import download_media


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("job_id", type=int)
    args = parser.parse_args()

    api = FiloApiClient()

    try:
        source = api.get_job_source(args.job_id)

        print("Source:", source["originalFileName"])
        print("Content type:", source["contentType"])

        with tempfile.TemporaryDirectory() as directory:
            extension = Path(
                source["originalFileName"]
            ).suffix or ".bin"

            output = Path(directory) / f"source{extension}"

            download_media(
                source["downloadUrl"],
                output,
                source["fileSizeBytes"] or None,
            )

            print("Download successful!")
            print("Downloaded bytes:", output.stat().st_size)

            subprocess.run([
                "ffprobe",
                "-v", "error",
                "-show_entries",
                "stream=codec_name,codec_type",
                "-of", "default=noprint_wrappers=1",
                str(output)
            ], check=True)

    finally:
        api.close()


if __name__ == "__main__":
    main()
