import subprocess
import sys
import os
from pathlib import Path
import pytest
from app.inference.parsers import TextAnalysisMixin


@pytest.mark.parametrize('source,expected', [
    ('- **글자·내용 독해**: 공원 내 모든 구역은 금연입니다.\n- **결론**: 금연 안내', '금연'),
    ('텍스트: 인천교육 바꿔 교육감 이대형" - 실질의미: 인천교육감', '이대형'),
    ('- **텍스트**: 봄맞이 행사 안내', '봄맞이'),
])
def test_extracts_literal_banner_text(source, expected):
    assert expected in TextAnalysisMixin().extract_banner_text(source)


@pytest.mark.parametrize('verdict,expected', [('합법','LEGAL'), ('불법','ILLEGAL'), ('회색지대','GRAY')])
def test_legal_verdict_codes(verdict, expected):
    assert TextAnalysisMixin().extract_vlm_legal_verdict(f'- **법적판정**: {verdict}') == expected


def test_parser_import_does_not_load_models():
    environment = dict(os.environ, PYTHONPATH=str(Path(__file__).resolve().parents[2] / 'src/backend'))
    result = subprocess.run([sys.executable, '-c', 'from app.inference.parsers import TextAnalysisMixin; import sys; assert "torch" not in sys.modules; assert "ultralytics" not in sys.modules'], env=environment, capture_output=True, text=True)
    assert result.returncode == 0, result.stderr
