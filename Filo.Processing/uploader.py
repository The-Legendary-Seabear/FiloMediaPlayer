
from pathlib import Path
import requests


def upload_media(upload_url, file_path, content_type):
    file_path = Path(file_path)

    if not file_path.is_file():
        raise FileNotFoundError(file_path)

    file_size = file_path.stat().st_size

    if file_size == 0:
        raise ValueError("Cannot upload an empty media file.")

    with file_path.open("rb") as file:
        response = requests.put(
            upload_url,
            data=file,
            headers={"Content-Type": content_type},
            timeout=(15, 300),
            allow_redirects=False,
        )

    if response.status_code != 200:
        raise RuntimeError(
            f"S3 upload failed with HTTP {response.status_code}"
        )

    return file_size
