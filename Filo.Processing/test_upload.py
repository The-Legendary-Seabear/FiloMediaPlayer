
import argparse
import tempfile
from pathlib import Path

from api_client import FiloApiClient
from downloader import download_media
from converter import convert_media
from uploader import upload_media


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("job_id", type=int)
    parser.add_argument("--codec", default=None)
    args = parser.parse_args()

    api = FiloApiClient()

    try:
        source = api.get_job_source(args.job_id)
        upload = api.prepare_output_upload(args.job_id)

        target_format = Path(
            upload["outputFileName"]
        ).suffix.lstrip(".").lower()

        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)

            source_extension = (
                Path(source["originalFileName"]).suffix or ".bin"
            )

            input_path = folder / f"input{source_extension}"
            output_path = folder / f"output.{target_format}"

            print("Downloading source...")

            download_media(
                source["downloadUrl"],
                input_path,
                source["fileSizeBytes"] or None,
            )

            print("Converting to", target_format)

            convert_media(
                str(input_path),
                str(output_path),
                target_format,
                args.codec,
            )

            print("Uploading converted file...")

            uploaded_bytes = upload_media(
                upload["uploadUrl"],
                output_path,
                upload["contentType"],
            )

            print("S3 upload successful!")
            print("Uploaded bytes:", uploaded_bytes)

    finally:
        api.close()


if __name__ == "__main__":
    main()
