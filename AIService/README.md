# Weapon Detection AI Service

This FastAPI service performs real-time weapon, suspicious-activity, and face detection.

Weapon alerts use layered confirmation to reduce false positives:

- All weapon candidates must have at least 60% custom-model confidence.
- Knife candidates require at least 70% confidence and overlapping confirmation from the COCO-pretrained YOLO11n model.
- Knife and pistol candidates must remain spatially consistent across three sampled frames before an alert is sent.

## Setup

1. Install dependencies:
```bash
pip install -r requirements.txt
```

2. Ensure the custom weapon model is at `../Yolov10/model.pt`. The service also uses `yolo11n.pt` as an independent knife verifier.

## Running the Service

```bash
python main.py
```

The service will run on `http://localhost:8000`

## API Endpoints

### POST /start-detection
Start weapon detection on an RTSP stream.

Request body:
```json
{
  "rtsp_url": "rtsp://example.com/stream",
  "location": "Main Entrance",
  "user_id": "user123"
}
```

### POST /stop-detection
Stop the current detection process.

### GET /health
Health check endpoint.

## Integration

The service sends detection results to the configured ASP.NET backend. Set `BACKEND_URL` when the backend is not available at `http://localhost:5217`.

Make sure the backend is running and accessible.