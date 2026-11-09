
import logging
import threading
import time

import requests

import json
import subprocess
import tempfile
from pathlib import Path

from downloader import download_media
from converter import convert_media
from uploader import upload_media


from api_client import FiloApiClient
from config import WORKER_ID


POLL_INTERVAL = 5
HEARTBEAT_INTERVAL = 10

logger = logging.getLogger(__name__)


class ProcessingWorker:
    def __init__(self):
        self.api = FiloApiClient()
        self.running = False

    def send_heartbeats(self, job_id, stop_event):
        """
        Sends heartbeats independently while a job is running.
        Uses a separate API client for this thread.
        """
        client = None

        try:
            client = FiloApiClient()

            while not stop_event.wait(HEARTBEAT_INTERVAL):
                try:
                    client.heartbeat(job_id)
                    logger.info(
                        "Heartbeat sent for job %s", job_id
                    )

                except requests.RequestException as error:
                    logger.warning(
                        "Heartbeat failed for job %s: %s",
                        job_id,
                        error
                    )

        except Exception:
            logger.exception(
                "Heartbeat thread stopped unexpectedly."
            )

        finally:
            if client is not None:
                client.close()

    
    def process_job(self, job):
        job_id = job["processingJobId"]
        job_type = job["jobType"]

        if job_type != "Conversion":
            raise ValueError(
                f"Unsupported processing job type: {job_type}"
            )

        parameters = job["parametersJson"]

        if isinstance(parameters, str):
            parameters = json.loads(parameters)

        target_format = parameters["TargetFormat"].lower()
        video_codec = parameters.get("VideoCodec")

        logger.info(
            "Processing job %s: %s (%s)",
            job_id,
            target_format,
            video_codec or "default"
        )

        source = self.api.get_job_source(job_id)
        upload = self.api.prepare_output_upload(job_id)

        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)

            source_extension = (
                Path(source["originalFileName"]).suffix or ".bin"
            )

            input_path = folder / f"input{source_extension}"
            output_path = folder / f"output.{target_format}"

            logger.info("Downloading source for job %s", job_id)

            download_media(
                source["downloadUrl"],
                input_path,
                source["fileSizeBytes"] or None
            )

            logger.info("Converting job %s", job_id)

            convert_media(
                str(input_path),
                str(output_path),
                target_format,
                video_codec
            )

            logger.info("Validating output for job %s", job_id)

            probe = subprocess.run(
                [
                    "ffprobe",
                    "-v", "error",
                    "-show_entries",
                    "stream=codec_name,codec_type",
                    "-of", "json",
                    str(output_path)
                ],
                capture_output=True,
                text=True,
                check=True
            )

            streams = json.loads(probe.stdout).get("streams", [])

            if not streams:
                raise RuntimeError(
                    "Converted file contains no media streams."
                )

            if target_format in {"mp4", "webm", "mkv"}:
                videos = [
                    stream for stream in streams
                    if stream.get("codec_type") == "video"
                ]

                if not videos:
                    raise RuntimeError(
                        "Video conversion produced no video stream."
                    )

                expected_codec = {
                    "h264": "h264",
                    "h265": "hevc",
                    "vp8": "vp8",
                    "vp9": "vp9",
                    "av1": "av1"
                }.get(video_codec or (
                    "vp9" if target_format == "webm" else "h264"
                ))

                if expected_codec and not any(
                    stream.get("codec_name") == expected_codec
                    for stream in videos
                ):
                    raise RuntimeError(
                        f"Expected video codec {expected_codec} "
                        "was not found in the output."
                    )

            else:
                if not any(
                    stream.get("codec_type") == "audio"
                    for stream in streams
                ):
                    raise RuntimeError(
                        "Audio conversion produced no audio stream."
                    )

            logger.info("Uploading output for job %s", job_id)

            upload_media(
                upload["uploadUrl"],
                output_path,
                upload["contentType"]
            )

            logger.info("Finalizing job %s", job_id)

            result = self.api.finalize_output(job_id)

            logger.info(
                "Job %s completed. Output MediaFileId: %s",
                job_id,
                result["outputMediaFileId"]
            )

        return True


    
    def execute_job(self, job):
        job_id = job["processingJobId"]

        stop_event = threading.Event()

        heartbeat_thread = threading.Thread(
            target=self.send_heartbeats,
            args=(job_id, stop_event),
            daemon=True
        )

        heartbeat_thread.start()

        try:
            finalized = self.process_job(job)

            if not finalized:
                self.api.complete_job(job_id)

            logger.info("Job %s finished successfully", job_id)

        except Exception as error:
            logger.exception("Processing failed for job %s", job_id)

            try:
                self.api.fail_job(job_id, str(error))
                logger.warning("Job %s marked Failed", job_id)
            except requests.RequestException:
                logger.exception(
                    "Unable to report failure for job %s",
                    job_id
                )

        finally:
            stop_event.set()
            heartbeat_thread.join()


    def run_once(self):
        """
        Claims and handles one queued job.
        Returns False when the queue is empty.
        """
        job = self.api.claim_job()

        if job is None:
            logger.debug("No queued jobs available.")
            return False

        self.execute_job(job)
        return True

    def run_forever(self):
        """
        Continuously polls the API until stopped.
        """
        self.running = True

        logger.info(
            "Filo processing worker started: %s",
            WORKER_ID
        )

        try:
            while self.running:
                try:
                    processed = self.run_once()

                    if not processed:
                        time.sleep(POLL_INTERVAL)

                except requests.RequestException as error:
                    logger.error(
                        "API communication error: %s",
                        error
                    )

                    time.sleep(POLL_INTERVAL)

                except Exception:
                    logger.exception(
                        "Unexpected worker error."
                    )

                    time.sleep(POLL_INTERVAL)

        except KeyboardInterrupt:
            logger.info("Worker shutdown requested.")

        finally:
            self.running = False
            self.api.close()

            logger.info("Worker stopped.")

    def close(self):
        self.running = False
        self.api.close()
