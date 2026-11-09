
from pathlib import Path
import requests


def download_media(download_url, destination, expected_size=None):
    destination = Path(destination)
    destination.parent.mkdir(parents=True, exist_ok=True)

    try:
        with requests.get(
            download_url,
            stream=True,
            timeout=(15, 120),
            allow_redirects=False,
        ) as response:
            response.raise_for_status()

            if response.status_code != 200:
                raise RuntimeError(
                    f"Unexpected S3 response: {response.status_code}"
                )

            with destination.open("wb") as file:
                for chunk in response.iter_content(
                    chunk_size=1024 * 1024
                ):
                    if chunk:
                        file.write(chunk)

        actual_size = destination.stat().st_size

        if actual_size == 0:
            raise RuntimeError("Downloaded file is empty.")

        if expected_size is not None:
            if actual_size != expected_size:
                raise RuntimeError(
                    f"Download size mismatch: expected "
                    f"{expected_size}, got {actual_size}"
                )

        return destination

    except Exception:
        destination.unlink(missing_ok=True)
        raise
