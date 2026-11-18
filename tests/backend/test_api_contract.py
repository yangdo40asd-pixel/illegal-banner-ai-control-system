import pytest
from fastapi.testclient import TestClient
from app.schemas.detection_schema import ChannelDetectionBatch, BannerDetectionEvent


def test_empty_batch_preserves_pascal_case_contract():
    payload = ChannelDetectionBatch(CctvId='CCTV_01', Detections=[]).model_dump()
    assert payload['MessageType'] == 'channel_batch'
    assert payload['Detections'] == []
    assert 'FrameWidth' in payload


@pytest.mark.integration
def test_health_and_websocket_ping_without_model_loading(monkeypatch):
    monkeypatch.setenv('BANNERGUARD_DISABLE_AI', '1')
    from app.main import app
    async def wait_only(websocket):
        import asyncio
        await asyncio.Event().wait()
    from app.api import websocket_endpoints
    monkeypatch.setattr(websocket_endpoints, 'run_real_ai_simulator', wait_only)
    with TestClient(app) as client:
        response = client.get('/health')
        assert response.status_code == 200
        assert response.json()['ai_ready'] is False
        with client.websocket_connect('/ws/alerts') as connection:
            connection.send_text('ping')
            assert connection.receive_text() == 'pong'
