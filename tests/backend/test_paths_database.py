import json
import sqlite3
from pathlib import Path
import pytest
from app.core.paths import RuntimePaths
from app.core.database_migrations import apply_migrations


def test_relative_paths_local_override_and_environment(tmp_path, monkeypatch):
    (tmp_path / 'configs').mkdir()
    (tmp_path / 'configs/paths.example.json').write_text(json.dumps({'DatabasePath':'one.db'}))
    (tmp_path / 'configs/paths.local.json').write_text(json.dumps({'DatabasePath':'two.db'}))
    configuration = RuntimePaths(tmp_path)
    monkeypatch.chdir(tmp_path.parent)
    assert configuration.resolve('DatabasePath') == tmp_path / 'two.db'
    monkeypatch.setenv('BANNERGUARD_DATABASE_PATH', 'three.db')
    assert configuration.resolve('DatabasePath') == tmp_path / 'three.db'


@pytest.mark.integration
def test_schema_normalizes_legacy_verdict_and_preserves_rows(tmp_path):
    with sqlite3.connect(tmp_path / 'test.db') as connection:
        apply_migrations(connection)
        connection.execute("INSERT INTO human_reviews(alert_id,human_verdict,human_status_code,reviewed_at) VALUES('one','불법','01','2026-11-17')")
        connection.commit()
        apply_migrations(connection)
        assert connection.execute('SELECT human_verdict FROM human_reviews').fetchone()[0] == 'ILLEGAL'
        assert connection.execute('SELECT COUNT(*) FROM human_reviews').fetchone()[0] == 1
        assert connection.execute('PRAGMA integrity_check').fetchone()[0] == 'ok'
