# Weapon Detection AI Service

This is a FastAPI-based AI service for real-time weapon detection using YOLOv10.

## Setup

1. Install dependencies:
```bash
pip install -r requirements.txt
```

2. Ensure the YOLOv10 model is at `../Yolov10/model.pt`

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

The service sends detection results to the backend at `http://192.168.100.35:5000/api/detections/receive`

Make sure the backend is running and accessible.