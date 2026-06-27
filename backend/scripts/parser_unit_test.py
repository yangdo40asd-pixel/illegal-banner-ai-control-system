import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from app.services.ai_inference_service import AIInferenceService

s = AIInferenceService.__new__(AIInferenceService)
raw = """- **글자·내용 독해**: 공원 내 모든 구역은 금연입니다. 흡연 시 5만원의 과태료가 부과됩니다.
- **의도 한 줄**: 공원 내 금연
- **결론**: 공원 내 모든 구역은 금연입니다."""
print("md:", s.extract_banner_text(raw))
raw2 = '텍스트: 인천교육 바꿔 교육감 이대형" - 실질의미: 인천교육감'
print("inline:", s.extract_banner_text(raw2))
assert "금연" in s.extract_banner_text(raw)
assert "이대형" in s.extract_banner_text(raw2)
print("OK")