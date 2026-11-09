
from pathlib import Path
from converter import convert_media

# Change this to an existing video or audio file on your computer.
source = Path("conversion_tests/sample_video.mp4")

output_directory = Path("conversion_tests")
output_directory.mkdir(exist_ok=True)

tests = [
    ("mp3", None),
    ("flac", None),
    ("wav", None),
    ("mp4", "h264"),
    ("webm", "vp8"),
    ("webm", "vp9"),
    ("mkv", None),
]

for target_format, codec in tests:
    suffix = f"_{codec}" if codec else ""
    output = output_directory / f"test{suffix}.{target_format}"

    print(f"\nConverting to {target_format} ({codec or 'default'})...")

    try:
        result = convert_media(
            str(source),
            str(output),
            target_format,
            codec,
        )

        print(f"SUCCESS: {result}")
        print(f"Size: {result.stat().st_size:,} bytes")

    except Exception as error:
        print(f"FAILED: {error}")
