from __future__ import annotations
import re
from typing import List, Tuple, Dict

class TextAnalysisMixin:
    def levenshtein_similarity(self, s1: str, s2: str) -> float:
        """두 문자열 간의 Levenshtein 거리를 기반으로 유사도를 계산합니다."""
        s1 = s1.strip().replace(" ", "")
        s2 = s2.strip().replace(" ", "")
        if not s1 and not s2:
            return 1.0
        if not s1 or not s2:
            return 0.0

        m, n = len(s1), len(s2)
        dp = [[0] * (n + 1) for _ in range(m + 1)]
        for i in range(m + 1):
            dp[i][0] = i
        for j in range(n + 1):
            dp[0][j] = j

        for i in range(1, m + 1):
            for j in range(1, n + 1):
                if s1[i-1] == s2[j-1]:
                    dp[i][j] = dp[i-1][j-1]
                else:
                    dp[i][j] = min(
                        dp[i-1][j] + 1,    # 삭제
                        dp[i][j-1] + 1,    # 삽입
                        dp[i-1][j-1] + 1   # 대체
                    )

        distance = dp[m][n]
        max_len = max(m, n)
        return 1.0 - (distance / max_len)

    def _match_euphemism_regex(self, text: str) -> Tuple[str | None, List[str]]:
        """정규식 기반 은유 패턴 매칭. (불법유형, 탐지신호목록)"""
        signals: List[str] = []
        inferred: str | None = None
        for pattern, cat, label in self.EUPHEMISM_REGEX:
            if re.search(pattern, text):
                signals.append(label)
                if cat and not inferred:
                    inferred = cat
        return inferred, signals

    def detect_euphemism_signals(self, text: str) -> List[str]:
        """은유·은어 재검토가 필요한 신호 수집."""
        if not text:
            return []
        signals: List[str] = []
        checks = [
            (self.KW_EUPHEMISM_DRUG, "마약 은어"),
            (self.KW_EUPHEMISM_TRAFFICKING, "인신매매·성착취 미끼"),
            (self.KW_EUPHEMISM_CRIME, "범죄·사채 은어"),
            (self.KW_EUPHEMISM_OBSCENE, "성매매·유흥 은어"),
            (self.KW_EUPHEMISM_DEFAMATION, "스캔들·폭로 은유"),
            (self.KW_EUPHEMISM_INSULT, "모욕·초성 은어"),
            (self.KW_EUPHEMISM_HATE, "혐오 은유"),
        ]
        for kws, label in checks:
            if any(k in text for k in kws):
                signals.append(label)
        if any(t in text for t in self.EUPHEMISM_TRIGGER_WORDS):
            signals.append("폭로·비밀 암시")
        _, regex_signals = self._match_euphemism_regex(text)
        signals.extend(regex_signals)
        if re.search(r"은유\s*[:：]\s*있음", text):
            signals.append("VLM 은유탐지")
        return signals

    def has_euphemism_indicators(self, text: str) -> bool:
        """약한 신호 포함 광범위 탐지 — 2차 트리거에는 should_trigger_euphemism_review 사용."""
        return len(self.detect_euphemism_signals(text)) > 0

    def _extract_vlm_conclusion_section(self, raw: str) -> str:
        """VLM 출력 결론 블록만 추출 (분석 본문·프롬프트 잔여 오탐 방지)."""
        for pattern in (
            r"===\s*결론\s*===([\s\S]*)",
            r"##\s*결론\s*([\s\S]*)",
            r"【\s*결론\s*】([\s\S]*)",
        ):
            m = re.search(pattern, raw)
            if m:
                return m.group(1).strip()
        m2 = re.search(r"법적판정\s*[:：]", raw)
        if m2:
            return raw[m2.start():].strip()
        m3 = re.search(r"텍스트\s*[:：]\s*.+\n\s*실질의미\s*[:：]", raw, re.DOTALL)
        if m3:
            return raw[m3.start():].strip()
        return ""

    def _extract_reading_section(self, raw: str) -> str:
        """VLM '글자·내용 독해' 블록 추출."""
        m = re.search(
            r"===\s*글자[^=\n]*독해\s*===([\s\S]*?)(?====|\Z)",
            raw,
        )
        return m.group(1).strip() if m else ""

    def _parse_text_field(self, scope: str) -> str:
        """구조화 필드 '텍스트:' 한 줄·여러 줄 파싱."""
        if not scope:
            return ""
        m = re.search(
            r"텍스트\s*[:：]\s*(.+?)(?=\n\s*(?:실질의미|법적판정|추천분류|불법세부|신뢰도|판단이유|요약\s*1|요약1)\s*[:：]|\Z)",
            scope,
            re.DOTALL | re.MULTILINE,
        )
        if m:
            if cleaned := self._sanitize_banner_text(m.group(1)):
                return cleaned
        m_line = re.search(r"텍스트\s*[:：]\s*([^\n]+)", scope)
        if m_line:
            if cleaned := self._sanitize_banner_text(m_line.group(1)):
                return cleaned
        return ""

    def _fallback_banner_text_scan(self, raw: str) -> str:
        """결론 필드 실패 시 백업 — 인용부호·독해 본문·첫 유효 줄."""
        for pattern in (
            r"적힌\s*(?:모든\s*)?내용\s*[:：]\s*([^\n]+)",
            r"내용\s*[:：]\s*([^\n]+)",
            r"문구\s*[:：]\s*([^\n]+)",
        ):
            m = re.search(pattern, raw, re.MULTILINE)
            if m:
                found = m.group(1).strip().strip("\"'()")
                if found and not self.is_placeholder(found):
                    return found[:300]
        for pattern in (r'[\'"\u201c\u2018](.*?)[\'"\u201d\u2019]', r"\*\*(.*?)\*\*"):
            m = re.search(pattern, raw)
            if m:
                found = m.group(1).strip()
                if found and not self.is_placeholder(found):
                    return found[:300]
        skip = (
            "설치", "판정", "분류", "신뢰도", "이미지", "현수막", "분석", "결론",
            "독해", "관찰", "의미", "법적", "추천", "불법세부", "실질", "해당없음",
        )
        reading = self._extract_reading_section(raw)
        for block in (reading, raw):
            if not block:
                continue
            for line in block.split("\n"):
                line = line.strip().strip("()[]{} ")
                if len(line) < 2 or line.startswith("==="):
                    continue
                if any(k in line for k in skip):
                    continue
                if not self.is_placeholder(line):
                    return line[:300]
        return ""

    def should_trigger_euphemism_review(self, banner_text: str, vlm_raw_clean: str) -> bool:
        """은유·은어 2차 심층검토 트리거.

        호출 측에서 1차 판정이 합법(LEGAL)인 경우에만 검사할 것.

        켜지는 경우 (강한 신호 A~E 중 하나):
          A. VLM 결론에 '은유: 있음' / '은유탐지: 있음'
          B. 실질의미가 현수막 표면 텍스트와 실질적으로 다름 (4자 이상)
          C. 전화번호·당일대출·범죄은어 등 infer_category_from_euphemisms() 매칭
          D. 범죄은어 / 성매매 복합어 / 모욕·초성 / 혐오 은어 키워드
          E. 스캔들 은유 — 폭로·스캔들류 + 인명·직함, 또는 폭로·스캔들·밀회·불륜설·바람피

        켜지지 않는 경우:
          - '비밀'·'진실'·'충격' 등 TRIGGER_WORDS 단독
          - '출장'·'안마'·'스웨디시' 등 단일 상업어
          - 초성 정규식 단독 (다른 신호 없음)
          - VLM 분석 본문(결론 블록 밖)에만 등장하는 단어
        """
        banner_text = (banner_text or "").strip()
        conclusion = self._extract_vlm_conclusion_section(vlm_raw_clean)
        check_text = f"{banner_text} {conclusion}".strip()
        if not check_text:
            return False

        if conclusion and re.search(r"은유\s*[:：]\s*있음|은유탐지\s*[:：]\s*있음", conclusion):
            return True

        literal = self.extract_literal_meaning(conclusion or vlm_raw_clean)
        if literal and banner_text:
            norm_lit = re.sub(r"\s+", "", literal)
            norm_banner = re.sub(r"\s+", "", banner_text)
            if (
                len(norm_lit) >= 4
                and norm_lit not in ("해당없음", "없음", "N/A")
                and norm_lit not in norm_banner
                and norm_banner not in norm_lit
            ):
                return True

        if self.infer_category_from_euphemisms(check_text):
            return True

        if any(k in check_text for k in self.KW_EUPHEMISM_DRUG):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_TRAFFICKING):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_CRIME):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_OBSCENE_STRICT):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_INSULT):
            return True
        if any(k in check_text for k in self.KW_EUPHEMISM_HATE):
            return True

        if any(k in check_text for k in self.KW_EUPHEMISM_DEFAMATION):
            if re.search(r"[가-힣]{2,4}\s*(시장|구청장|의원|대표|교수|목사|장로|씨)", check_text):
                return True
            if any(t in check_text for t in ("폭로", "스캔들", "밀회", "불륜설", "바람피")):
                return True

        return False

    def infer_category_from_euphemisms(self, text: str) -> str | None:
        """은어·은유 패턴으로 6대 불법 유형 추정 (직설 키워드 없을 때)."""
        if not text or self._commercial_shield(text):
            return None
        regex_cat, _ = self._match_euphemism_regex(text)
        if regex_cat:
            return regex_cat
        if any(k in text for k in self.KW_EUPHEMISM_DRUG):
            return "범죄 조장·불법행위 광고"
        if any(k in text for k in self.KW_EUPHEMISM_TRAFFICKING):
            return "범죄 조장·불법행위 광고"
        if any(k in text for k in self.KW_EUPHEMISM_CRIME):
            return "범죄 조장·불법행위 광고"
        if any(k in text for k in self.KW_EUPHEMISM_OBSCENE_STRICT):
            return "음란·외설 표현"
        if any(k in text for k in self.KW_EUPHEMISM_OBSCENE):
            return "음란·외설 표현"
        if any(k in text for k in self.KW_EUPHEMISM_INSULT):
            return "모욕·인격침해"
        if any(k in text for k in self.KW_EUPHEMISM_HATE):
            return "혐오·차별·비하 표현"
        if any(k in text for k in self.KW_EUPHEMISM_DEFAMATION):
            if re.search(r"[가-힣]{2,4}\s*(시장|구청장|의원|대표|교수|목사|장로|씨)", text):
                return "명예훼손"
            if any(t in text for t in ("폭로한다", "폭로합니다", "스캔들", "밀회", "불륜설", "바람피")):
                return "명예훼손"
        return None

    def extract_literal_meaning(self, res: str) -> str:
        """VLM 출력에서 실질의미/해독 필드 추출."""
        for key in ("실질의미", "해독근거", "해독결과"):
            m = re.search(rf"{key}\s*[:：]\s*([^\n]+)", res)
            if m:
                val = m.group(1).strip()
                if val and val not in ("해당없음", "없음", "N/A"):
                    return val
        return ""

    def needs_euphemism_decode(self, category: str, parsed_text: str, raw_clean: str, ai_verdict: str) -> bool:
        """3차 은유 해독 패스 — 2차와 동일한 강한 신호 기준 (합법 잔류 시만)."""
        if ai_verdict != "LEGAL":
            return False
        return self.should_trigger_euphemism_review(parsed_text, raw_clean)

    def _normalize_category(self, category: str) -> str:
        """VLM 출력 → 4대 분류(상업·공익성·정당·불법)로 정규화."""
        c = category.strip()
        if not c:
            return ""
        if "불법" in c or any(k in c for k in ("혐오", "음란", "사행", "명예훼손", "모욕", "범죄", "도박", "마약")):
            return self.CATEGORY_ILLEGAL
        if "상업" in c or ("광고" in c and "불법" not in c):
            return self.CATEGORY_COMMERCIAL
        if any(k in c for k in ("부동산", "분양", "임대", "매매", "오피스텔", "아파트", "전세", "월세")):
            return self.CATEGORY_COMMERCIAL
        if any(k in c for k in ("정당", "선거", "후보", "탄핵", "국회", "정치")):
            return self.CATEGORY_PARTY
        if any(k in c for k in ("공익", "캠페인", "안내", "주민", "이웃", "환경", "안전", "종교", "사회")):
            return self.CATEGORY_PUBLIC
        if any(k in c for k in ("정부", "비판", "운동")):
            return self.CATEGORY_PUBLIC
        return c

    def _is_illegal_category(self, category: str) -> bool:
        norm = self._normalize_category(category)
        return norm == self.CATEGORY_ILLEGAL or any(
            ic in norm for ic in (
                "명예훼손", "허위사실", "모욕", "혐오", "음란", "범죄", "사행",
            )
        )

    def _is_party_text(self, text: str) -> bool:
        return any(k in text for k in self.KW_PARTY)

    def _is_commercial_text(self, text: str) -> bool:
        return any(k in text for k in self.KW_COMMERCIAL)

    def _is_explicit_illegal_commercial(self, text: str) -> bool:
        """사채·불법대출·일수 등 진짜 불법 상업만."""
        if not text:
            return False
        if any(k in text for k in self.KW_EXPLICIT_ILLEGAL_LOAN):
            return True
        if re.search(r"당일.*\d+\s*만", text) or re.search(r"\d+\s*만\s*원.*당일", text):
            return True
        return False

    def _commercial_shield(self, text: str) -> bool:
        """부동산·일반 상업 광고 — 불법대출 신호 없으면 불법 키워드/연락처 오탐 차단."""
        return self._is_commercial_text(text) and not self._is_explicit_illegal_commercial(text)

    def _is_public_text(self, text: str) -> bool:
        return any(k in text for k in self.KW_PUBLIC_INTEREST)

    def extract_illegal_subtype(self, res: str) -> str:
        conclusion = self._extract_vlm_conclusion_section(res) or res
        m = re.search(r"불법\s*세부\s*[:：]\s*([^\n]+)", conclusion)
        if m:
            return m.group(1).strip()[:20]
        if any(k in conclusion for k in ("혐오", "차별", "비하")):
            return "혐오"
        if any(k in conclusion for k in ("음란", "외설", "성매매")):
            return "음란"
        if any(k in conclusion for k in ("도박", "사채", "마약", "사행")):
            return "사행성"
        return "내용위반"

    def _infer_four_category(self, text: str) -> str:
        """키워드 보조 — 기본값 공익성 ('이웃을 생각해주세요' 등)."""
        if not text:
            return self.CATEGORY_PUBLIC
        if self._is_commercial_text(text):
            return self.CATEGORY_COMMERCIAL
        if self._is_party_text(text):
            return self.CATEGORY_PARTY
        return self.CATEGORY_PUBLIC

    def infer_category_from_keywords(self, text: str) -> str | None:
        """직설 키워드 + 은유 패턴으로 6대 불법 유형 추정."""
        if not text:
            return None
        if self._commercial_shield(text):
            return None
        if any(k in text for k in self.KW_CRIME_AD):
            return "범죄 조장·불법행위 광고"
        if any(k in text for k in self.KW_OBSCENE):
            return "음란·외설 표현"
        if any(k in text for k in self.KW_INSULT):
            return "모욕·인격침해"
        if any(k in text for k in self.KW_FALSE_FACT) and any(
            k in text for k in self.KW_DEFAMATION + ["범죄", "횡령", "사기"]
        ):
            return "허위사실 유포"
        if any(k in text for k in self.KW_DEFAMATION):
            return "명예훼손"
        if any(k in text for k in self.KW_HATE):
            return "혐오·차별·비하 표현"
        return self.infer_category_from_euphemisms(text)

    def _extract_markdown_field(self, raw: str, label: str) -> str:
        """`- **라벨**: 값` 마크다운 필드 추출."""
        if not raw:
            return ""
        pattern = rf"(?:^|\n)\s*[-*]\s*\*{{0,2}}{label}\*{{0,2}}\s*[:：]\s*([^\n]+)"
        m = re.search(pattern, raw, re.IGNORECASE)
        return m.group(1).strip() if m else ""

    def _parse_legal_verdict_value(self, val: str) -> str | None:
        if not val:
            return None
        if re.search(r"불법\s*가능|불법\s*의심", val):
            return "ILLEGAL"
        if "불법" in val and "합법" not in val:
            return "ILLEGAL"
        if "회색" in val:
            return "GRAY"
        if "합법" in val:
            return "LEGAL"
        return None

    def extract_vlm_legal_verdict(self, res: str) -> str | None:
        """VLM 법적판정 — 마크다운 우선, 결론 블록 폴백."""
        for key in ("법적판정", "재검토결과", "해독결과"):
            if val := self._extract_markdown_field(res, key):
                if verdict := self._parse_legal_verdict_value(val):
                    return verdict
        conclusion = self._extract_vlm_conclusion_section(res) or res
        for key in ("법적판정", "재검토결과", "해독결과"):
            m = re.search(rf"{key}\s*[:：]\s*([^\n]+)", conclusion)
            if m and (verdict := self._parse_legal_verdict_value(m.group(1).strip())):
                return verdict
        return None

    def extract_confidence(self, res: str) -> str:
        if val := self._extract_markdown_field(res, r"신뢰도"):
            return val
        conclusion = self._extract_vlm_conclusion_section(res) or res
        m = re.search(r"신뢰도\s*[:：]\s*([^\n]+)", conclusion)
        return m.group(1).strip() if m else "보통"

    def _vlm_confidence_to_ocr(self, vlm_conf: str, has_text: bool) -> float:
        """VLM 신뢰도 → OCR 신뢰도 (0~1). 임의 난수 사용 안 함."""
        if not has_text:
            return 0.0
        conf = (vlm_conf or "").strip()
        if "높음" in conf:
            return 0.92
        if "낮음" in conf:
            return 0.72
        return 0.85

    def is_low_confidence(self, res: str) -> bool:
        return "낮음" in self.extract_confidence(res)

    def needs_deep_review(
        self, category: str, parsed_text: str, raw_clean: str,
        mapped_verdict: str, vlm_legal_verdict: str | None
    ) -> bool:
        """AI 신뢰도 낮음·판정 불일치·회색지대일 때만 2차 심층 재검토."""
        if self.is_low_confidence(raw_clean):
            return True
        if vlm_legal_verdict == "ILLEGAL" and mapped_verdict == "LEGAL":
            return True
        if vlm_legal_verdict == "GRAY" and mapped_verdict == "LEGAL":
            return True
        return False

    def _map_legal_category(self, category: str, parsed_text: str) -> Dict[str, str]:
        """합법 → 4대 분류 중 상업·공익성·정당."""
        cat = self._normalize_category(category)
        text = parsed_text or ""
        if cat not in self.LEGAL_FOUR_CATEGORIES or cat == "":
            cat = self._infer_four_category(text)

        if cat == self.CATEGORY_COMMERCIAL:
            return {
                "ai_verdict": "LEGAL",
                "status_code": "LEGAL_COMMERCIAL",
                "category": self.CATEGORY_COMMERCIAL,
                "judgment_title": f"✅ {self.CATEGORY_COMMERCIAL} (영리 광고)",
                "violation": "없음 (내용 기준 합법)",
            }
        if cat == self.CATEGORY_PARTY:
            return {
                "ai_verdict": "LEGAL",
                "status_code": "LEGAL_POLITICS",
                "category": self.CATEGORY_PARTY,
                "judgment_title": f"✅ {self.CATEGORY_PARTY} (정치·선거)",
                "violation": "없음 (내용 기준 합법)",
            }
        return {
            "ai_verdict": "LEGAL",
            "status_code": "LEGAL_PUBLIC",
            "category": self.CATEGORY_PUBLIC,
            "judgment_title": f"✅ {self.CATEGORY_PUBLIC} (주민·캠페인·안내)",
            "violation": "없음 (내용 기준 합법)",
        }

    def map_category_to_verdict(
        self, category: str, parsed_text: str, raw_clean: str,
        vlm_legal_verdict: str | None = None,
    ) -> Dict[str, str]:
        """VLM 법적판정 최우선. 합법 판정은 키워드로 불법 전환 금지."""
        category = self._normalize_category(category)
        literal = self.extract_literal_meaning(raw_clean)
        combined = f"{parsed_text} {literal}"
        unknown = category in ("", "알 수 없음 (수동검토)") or "알 수 없음" in category

        # 부동산·상업 광고는 사채/불법대출 없으면 무조건 상업(합법)
        if self._commercial_shield(combined):
            return self._map_legal_category(self.CATEGORY_COMMERCIAL, parsed_text)

        if vlm_legal_verdict == "GRAY":
            vlm_legal_verdict = "LEGAL"

        if vlm_legal_verdict == "LEGAL":
            if self._normalize_category(category) == self.CATEGORY_ILLEGAL:
                category = self._infer_four_category(parsed_text)
            return self._map_legal_category(category, parsed_text)

        if vlm_legal_verdict == "ILLEGAL" or self._normalize_category(category) == self.CATEGORY_ILLEGAL:
            subtype = self.extract_illegal_subtype(raw_clean)
            if infer := self.infer_category_from_keywords(combined):
                if "혐오" in infer:
                    subtype = "혐오"
                elif "음란" in infer:
                    subtype = "음란"
                elif "범죄" in infer or "사채" in combined:
                    subtype = "사행성"
            return {
                "ai_verdict": "ILLEGAL",
                "status_code": "ILLEGAL_CONTENT",
                "category": f"{self.CATEGORY_ILLEGAL}({subtype})",
                "judgment_title": f"🚨 {self.CATEGORY_ILLEGAL} · {subtype}",
                "violation": "내용 자체가 불법 가능성 매우 높음",
            }

        # VLM 법적판정 미확정 — 불법 키워드만 보조, 나머지는 4대 분류 추론
        if self._is_illegal_category(category):
            subtype = self.extract_illegal_subtype(raw_clean)
            return {
                "ai_verdict": "ILLEGAL",
                "status_code": "ILLEGAL_CONTENT",
                "category": f"{self.CATEGORY_ILLEGAL}({subtype})",
                "judgment_title": f"🚨 {self.CATEGORY_ILLEGAL} · {subtype}",
                "violation": "내용 자체가 불법 가능성 매우 높음",
            }
        if unknown and (kw := self.infer_category_from_keywords(combined)):
            subtype = self.extract_illegal_subtype(raw_clean)
            if "혐오" in kw:
                subtype = "혐오"
            elif "음란" in kw:
                subtype = "음란"
            elif "범죄" in kw:
                subtype = "사행성"
            return {
                "ai_verdict": "ILLEGAL",
                "status_code": "ILLEGAL_CONTENT",
                "category": f"{self.CATEGORY_ILLEGAL}({subtype})",
                "judgment_title": f"🚨 {self.CATEGORY_ILLEGAL} · {subtype}",
                "violation": "내용 자체가 불법 가능성 매우 높음",
            }
        return self._map_legal_category(
            category if not unknown else self._infer_four_category(parsed_text),
            parsed_text,
        )

    _SECTION_HEADERS = {
        "글자·내용 독해", "이미지 관찰", "의미 분석", "결론",
        "현수막 원문 전체를 한 줄로", "설치 위치·배경만 간략히",
        "의도 한 줄", "1차에서 읽은 원문 유지 또는 수정본",
    }

    def _is_section_header(self, val: str) -> bool:
        t = val.strip().strip("*").strip()
        if t in self._SECTION_HEADERS:
            return True
        return bool(re.fullmatch(r"글자[^:：\n]{0,8}독해", t))

    def _clean_inline_field_dump(self, text: str) -> str:
        """VLM이 한 줄에 텍스트+실질의미+법적판정을 이어 붙인 경우 앞부분만."""
        if not text:
            return ""
        cleaned = text.strip().strip("\"'")
        for sep in (
            r"\s*-\s*실질의미\s*[:：]",
            r"\s*-\s*법적판정\s*[:：]",
            r"\s*-\s*추천분류\s*[:：]",
            r"\s*-\s*불법세부\s*[:：]",
            r"\s*-\s*신뢰도\s*[:：]",
            r"\s*-\s*판단이유\s*[:：]",
            r"\s*###\s*",
        ):
            cleaned = re.split(sep, cleaned, maxsplit=1)[0]
        return cleaned.strip().strip("\"'")

    def _sanitize_banner_text(self, text: str) -> str:
        text = self._clean_inline_field_dump(text)
        text = re.sub(r"\s+", " ", text).strip()
        if not text or self.is_placeholder(text) or self._is_section_header(text):
            return ""
        return text[:500]

    def is_placeholder(self, val: str) -> bool:
        val_clean = val.strip().strip("()[]{} \"'*")
        if len(val_clean) < 2:
            return True
        if self._is_section_header(val_clean):
            return True
        exact = {
            "(원문)", "원문)", "원문", "N/A", "해당없음", "없음",
            "간략히", "한 줄", "요약 금지", "없음 -",
        }
        if val_clean in exact:
            return True
        placeholders = [
            "읽어낸", "핵심 글자", "묶였는지", "철제 틀", "명확히 묘사",
            "사고 흐름", "논리적 근거", "해당하는 항목", "간단히 기술",
            "확인 필요", "플레이스홀더", "분석 불가능",
        ]
        return any(p == val_clean or val_clean.startswith(p + " ") for p in placeholders)

    def _extract_markdown_vlm_text(self, raw: str) -> str:
        """VLM 마크다운 bullet 출력 — **글자·내용 독해**: 원문 우선."""
        if not raw:
            return ""
        # 1) 글자·내용 독해 (최우선 — 프로그램 표시 텍스트의 기준)
        for label in (
            r"글자[^*\n:：]{0,12}독해",
            r"텍스트",
            r"결론",
        ):
            pattern = (
                rf"(?:^|\n)\s*[-*]\s*\*{{0,2}}{label}\*{{0,2}}\s*[:：]\s*"
                rf"(.+?)(?=\n\s*[-*]\s*\*{{0,2}}|\Z)"
            )
            m = re.search(pattern, raw, re.DOTALL | re.IGNORECASE)
            if m and (cleaned := self._sanitize_banner_text(m.group(1))):
                return cleaned
        # 2) bullet 없이 **글자·내용 독해**: 형태
        for label in (r"글자[^*\n:：]{0,12}독해", r"텍스트"):
            pattern = rf"\*{{1,2}}{label}\*{{1,2}}\s*[:：]\s*(.+?)(?=\n\s*[-*]|\n\s*\*{{1,2}}|\Z)"
            m = re.search(pattern, raw, re.DOTALL | re.IGNORECASE)
            if m and (cleaned := self._sanitize_banner_text(m.group(1))):
                return cleaned
        return ""

    def _extract_text_from_reading_section(self, raw: str) -> str:
        """VLM 독해 블록 — 글자 원문이 여기 적히는 경우가 많음."""
        reading = self._extract_reading_section(raw)
        if not reading:
            return ""
        skip = (
            "설치", "판정", "분류", "신뢰도", "이미지", "현수막", "분석", "결론",
            "독해", "관찰", "의미", "법적", "추천", "불법세부", "해당없음", "요약",
        )
        lines: List[str] = []
        for line in reading.split("\n"):
            line = line.strip().strip("()[]{} ")
            if len(line) < 2 or line.startswith("==="):
                continue
            m = re.match(r"^(?:텍스트|내용|문구)\s*[:：]\s*(.+)", line)
            if m:
                found = m.group(1).strip()
                if found and not self.is_placeholder(found):
                    lines.append(found)
                continue
            if any(k in line for k in skip):
                continue
            if self.is_placeholder(line):
                continue
            lines.append(line)
        if not lines:
            return ""
        return re.sub(r"\s+", " ", " ".join(lines)).strip()[:500]

    def _extract_text_label_legacy(self, res: str) -> str:
        """이전 동작 복원 — 전체 VLM 출력에서 텍스트: 한 줄 스캔."""
        for pattern in (
            r"텍스트\s*[:：]\s*([^\n]+)",
            r"적힌\s*(?:모든\s*)?내용\s*[:：]\s*([^\n]+)",
            r"내용\s*[:：]\s*([^\n]+)",
            r"문구\s*[:：]\s*([^\n]+)",
        ):
            m = re.search(pattern, res, re.MULTILINE)
            if m:
                found = m.group(1).strip().strip("\"'()")
                if cleaned := self._sanitize_banner_text(found):
                    return cleaned
        return ""

    def extract_category(self, res: str) -> str:
        """추천분류 — 마크다운 우선, 결론 블록·키워드 폴백."""
        for label in (r"추천\s*분류", r"내용\s*분류", r"최종\s*분류"):
            if found := self._extract_markdown_field(res, label):
                found = found.strip().strip("()[]{} ")
                if not self.is_placeholder(found):
                    return self._normalize_category(found[:40])
        conclusion = self._extract_vlm_conclusion_section(res) or ""
        scopes = [conclusion, res] if conclusion else [res]
        for scope in scopes:
            for pattern in (
                r"추천\s*분류\s*[:：]\s*([^\n]+)",
                r"내용\s*분류\s*[:：]\s*([^\n]+)",
                r"최종\s*분류\s*[:：]\s*([^\n]+)",
            ):
                m = re.search(pattern, scope, re.MULTILINE)
                if m:
                    found = m.group(1).strip().strip("()[]{} ")
                    if not self.is_placeholder(found):
                        return self._normalize_category(found[:40])
            for cat in [self.CATEGORY_ILLEGAL, self.CATEGORY_PARTY, self.CATEGORY_COMMERCIAL, self.CATEGORY_PUBLIC]:
                if cat in scope:
                    return cat
        return "알 수 없음 (수동검토)"

    def extract_reason(self, res: str) -> str:
        if found := self._extract_markdown_field(res, r"판단\s*이유"):
            if not self.is_placeholder(found):
                return found[:200]
        for pattern in (
            r"요약\s*3\s*[:：]\s*([^\n]+)",
            r"판단\s*이유\s*[:：]\s*([^\n]+)",
            r"(?:분류\s*근거|재검토\s*근거|근거)\s*[:：]\s*([^\n]+)",
        ):
            m = re.search(pattern, res, re.MULTILINE)
            if m:
                found = m.group(1).strip().strip("()[]{} ")
                if not self.is_placeholder(found):
                    return found[:200]
        return "분류 근거 식별 불가"

    def extract_banner_text(self, res: str) -> str:
        """VLM이 읽은 현수막 원문 — 마크다운 → 결론 → 독해 → 레거시 순."""
        if not res or "[VLM 추론 예외]" in res:
            return ""
        if md := self._extract_markdown_vlm_text(res):
            return md
        conclusion = self._extract_vlm_conclusion_section(res)
        for scope in (conclusion, res):
            if parsed := self._parse_text_field(scope):
                return parsed
        if reading := self._extract_text_from_reading_section(res):
            return reading
        if legacy := self._extract_text_label_legacy(res):
            if cleaned := self._sanitize_banner_text(legacy):
                return cleaned
        if fallback := self._fallback_banner_text_scan(res):
            if cleaned := self._sanitize_banner_text(fallback):
                return cleaned
        return ""

    def _extract_summary_line(self, res: str, num: int) -> str:
        for pattern in (
            rf"요약\s*{num}\s*[:：]\s*([^\n]+)",
            rf"요약{num}\s*[:：]\s*([^\n]+)",
        ):
            m = re.search(pattern, res)
            if m:
                found = m.group(1).strip()
                if found and not self.is_placeholder(found):
                    return found[:120]
        return ""

    def format_recognized_text_lines(self, banner_text: str) -> str:
        """인식 텍스트를 결과에 그대로 나열."""
        if not banner_text or banner_text in ("텍스트 추출 불가", "텍스트 미추출 (식별 불가)"):
            return "· (인식 없음)"
        parts = re.split(r"[|\n／/]+", banner_text)
        lines = [p.strip() for p in parts if p.strip()]
        if not lines:
            return f"· {banner_text}"
        return "\n".join(f"· {line}" for line in lines)

    def build_vlm_internal_record(self, raw: str, review_raw: str = "", review_label: str = "") -> str:
        """DB·감사용 전체 VLM 원문 (UI 미노출)."""
        parts = ["[VLM 1차 분석]\n" + raw.strip()]
        if review_raw:
            label = review_label or "2차 재검토"
            parts.append(f"\n[VLM {label}]\n" + review_raw.strip())
        return "\n".join(parts)

    def build_vlm_user_summary(
        self,
        banner_text: str,
        category: str,
        judgment_title: str,
        vlm_conf: str,
        final_source: str,
        ai_verdict: str | None = None,
    ) -> str:
        """WPF 표시용 — 분석 결과만 짧게."""
        conclusion = self._extract_vlm_conclusion_section(final_source) or final_source
        legal = ai_verdict or self.extract_vlm_legal_verdict(conclusion) or "미확정"
        legal_kr = {"ILLEGAL": "불법", "LEGAL": "합법", "GRAY": "회색지대"}.get(legal, legal)

        reason = self.extract_reason(final_source)
        literal = self.extract_literal_meaning(final_source)
        text_short = banner_text if banner_text and banner_text != "텍스트 추출 불가" else "인식 불명"
        if len(text_short) > 60:
            text_short = text_short[:60] + "…"

        lines = [f"{judgment_title}", f"판정: {legal_kr} | 분류: {category} | 신뢰도: {vlm_conf}"]
        if literal and literal not in ("해당없음", "없음"):
            lines.append(f"실질의미: {literal[:80]}")
        lines.append(f"판단: {reason}")
        lines.append(f"텍스트: {text_short}")
        return "\n".join(lines)
