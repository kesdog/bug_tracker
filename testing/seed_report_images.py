import base64
import json
import sqlite3
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DB_PATH = ROOT / "bug_tracker.db"
IMAGES_DIR = ROOT / "testing" / "Test_Images"


def guess_content_type(path: Path) -> str:
    suffix = path.suffix.lower()
    if suffix == ".png":
        return "image/png"
    if suffix in {".jpg", ".jpeg"}:
        return "image/jpeg"
    if suffix == ".webp":
        return "image/webp"
    if suffix == ".gif":
        return "image/gif"
    raise ValueError(f"Unsupported image extension: {path.name}")


def to_data_url(path: Path) -> dict:
    content_type = guess_content_type(path)
    raw = path.read_bytes()
    encoded = base64.b64encode(raw).decode("ascii")
    return {
        "name": path.name,
        "contentType": content_type,
        "dataUrl": f"data:{content_type};base64,{encoded}",
    }


def build_supported_images() -> list[Path]:
    supported_suffixes = {".png", ".jpg", ".jpeg", ".webp", ".gif"}
    return sorted(path for path in IMAGES_DIR.iterdir() if path.is_file() and path.suffix.lower() in supported_suffixes)


def main() -> None:
    image_paths = build_supported_images()
    if not image_paths:
        raise RuntimeError(f"No supported images found in {IMAGES_DIR}")

    encoded_images = [to_data_url(path) for path in image_paths]

    with sqlite3.connect(DB_PATH) as connection:
        tickets = connection.execute(
            "SELECT id, reporter_user_id FROM bug_tickets ORDER BY created_at"
        ).fetchall()

        for index, (ticket_id, reporter_user_id) in enumerate(tickets):
            base_image = encoded_images[index % len(encoded_images)]
            payload = [base_image]

            if reporter_user_id == "usr_admin_001":
                second_image = encoded_images[(index + 1) % len(encoded_images)]
                third_image = encoded_images[(index + 2) % len(encoded_images)]
                payload = [base_image, second_image, third_image]

            connection.execute(
                "UPDATE bug_tickets SET report_images_json = ? WHERE id = ?",
                (json.dumps(payload), ticket_id),
            )

        connection.commit()


if __name__ == "__main__":
    main()
