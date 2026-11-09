
import argparse
import logging

from worker import ProcessingWorker


def main():
    parser = argparse.ArgumentParser(
        description="Filo background media processing worker"
    )

    parser.add_argument(
        "--once",
        action="store_true",
        help="Claim and process at most one queued job."
    )

    parser.add_argument(
        "--run",
        action="store_true",
        help="Continuously poll for processing jobs."
    )

    args = parser.parse_args()

    logging.basicConfig(
        level=logging.INFO,
        format="%(asctime)s [%(levelname)s] %(message)s"
    )

    if not args.once and not args.run:
        parser.print_help()
        return

    worker = ProcessingWorker()

    try:
        if args.once:
            worker.run_once()
        else:
            worker.run_forever()

    finally:
        if args.once:
            worker.close()


if __name__ == "__main__":
    main()
