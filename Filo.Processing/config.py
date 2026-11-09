import os
from pathlib import Path

API_BASE_URL = os.getenv(
    "FILO_API_URL",
    "https://localhost:7038"
).rstrip("/")

WORKER_ID = os.getenv(
    "FILO_WORKER_ID",
    "python-worker-1"
)

WORKER_API_KEY = os.getenv(
    "FILO_WORKER_KEY",
    ""
)

DEV_CERT_PATH = os.getenv(
    "FILO_DEV_CERT_PATH",
    str(Path.home() / ".aspnet" / "filo-certs" / "localhost.pem")
)

REQUEST_TIMEOUT = 15