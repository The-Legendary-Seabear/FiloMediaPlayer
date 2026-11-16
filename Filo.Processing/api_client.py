
import requests

from config import (
    API_BASE_URL,
    WORKER_API_KEY,
    WORKER_ID,
    REQUEST_TIMEOUT,
    DEV_CERT_PATH
)


class FiloApiClient:
    def __init__(self):
        if not WORKER_API_KEY:
            raise ValueError(
                "FILO_WORKER_KEY is not configured."
            )

        self.session = requests.Session()

        if API_BASE_URL.startswith("https://localhost:"):
            self.session.verify = DEV_CERT_PATH

        self.session.headers.update({
            "X-Worker-Key": WORKER_API_KEY
        })

    def _post(self, endpoint, data):
        url = f"{API_BASE_URL}/{endpoint.lstrip('/')}"

        response = self.session.post(
            url,
            json=data,
            timeout=REQUEST_TIMEOUT
        )

        if not response.ok:
            raise requests.HTTPError(
                f"API request failed ({response.status_code}): "
                f"{response.text}",
                response=response
            )

        return response


    def claim_job(self):
        response = self._post(
            "api/processing-jobs/claim",
            {"workerId": WORKER_ID}
        )

        if response.status_code == 204:
            return None

        return response.json()

    def heartbeat(self, job_id):
        self._post(
            f"api/processing-jobs/{job_id}/heartbeat",
            {"workerId": WORKER_ID}
        )

    def complete_job(self, job_id):
        self._post(
            f"api/processing-jobs/{job_id}/complete",
            {"workerId": WORKER_ID}
        )

    def fail_job(self, job_id, error_message):
        self._post(
            f"api/processing-jobs/{job_id}/fail",
            {
                "workerId": WORKER_ID,
                "errorMessage": str(error_message)[:2000]
            }
        )

    def close(self):
        self.session.close()

    
    def get_job_source(self, job_id):
        return self._post(
            f"api/processing-jobs/{job_id}/source",
            {"workerId": WORKER_ID}
        ).json()

    
    def prepare_output_upload(self, job_id):
        return self._post(
            f"api/processing-jobs/{job_id}/output-upload",
            {"workerId": WORKER_ID}
        ).json()

    
    def finalize_output(self, job_id):
        return self._post(
            f"api/processing-jobs/{job_id}/finalize-output",
            {"workerId": WORKER_ID}
        ).json()

    
    def save_loudness_result(self, job_id, measurements, gain_db):
        return self._post(
            f"api/processing-jobs/{job_id}/loudness-result",
            {
                "workerId": WORKER_ID,
                "integratedLufs": measurements["integrated_lufs"],
                "truePeakDbfs": measurements["true_peak_dbfs"],
                "loudnessRangeLu": measurements["loudness_range_lu"],
                "loudnessThresholdLufs": measurements["threshold_lufs"],
                "normalizationGainDb": gain_db
            }
        ).json()

    
    def get_grafting_sources(self, job_id):
        return self._post(
            f"api/processing-jobs/{job_id}/grafting-sources",
            {"workerId": WORKER_ID}
        ).json()


    def prepare_grafting_upload(self, job_id):
        return self._post(
            f"api/processing-jobs/{job_id}/grafting-output-upload",
            {"workerId": WORKER_ID}
        ).json()


    def finalize_grafting(self, job_id):
        return self._post(
            f"api/processing-jobs/{job_id}/grafting-finalize",
            {"workerId": WORKER_ID}
        ).json()
