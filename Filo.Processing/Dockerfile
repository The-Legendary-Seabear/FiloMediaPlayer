# Filo media-processing worker (conversion, grafting, loudness analysis).
# It talks only to the Filo API and to S3 through presigned URLs, so it
# needs no AWS credentials.
#   docker build -t filo-worker Filo.Processing

FROM python:3.12-slim

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY requirements.txt ./
RUN pip install --no-cache-dir -r requirements.txt

COPY *.py ./

RUN useradd --create-home filo
USER filo

ENV PYTHONUNBUFFERED=1

CMD ["python", "Filo.Processing.py", "--run"]
