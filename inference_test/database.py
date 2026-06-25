import os
import sqlite3
from datetime import datetime

DB_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "banner_tracking.db")

CCTV_LOCATION_MAP = {
    "CCTV_01": {"gu": "연수구", "dong": "송도동", "detail": "인천광역시 연수구 송도동 12"},
    "CCTV_02": {"gu": "남동구", "dong": "구월동", "detail": "인천광역시 남동구 구월동 45"},
    "CCTV_03": {"gu": "부평구", "dong": "부평동", "detail": "인천광역시 부평구 부평동 78"},
    "CCTV_04": {"gu": "미추홀구", "dong": "주안동", "detail": "인천광역시 미추홀구 주안동 10"},
    "CCTV_05": {"gu": "서구", "dong": "청라동", "detail": "인천광역시 서구 청라동 55"}
}

def get_location_for_cctv(cctv_id: str) -> dict:
    """CCTV ID를 기반으로 구/동/상세 주소를 반환합니다."""
    return CCTV_LOCATION_MAP.get(cctv_id, {"gu": "미추홀구", "dong": "용현동", "detail": "인천광역시 미추홀구 용현동"})


def db_init():
    """SQLite 데이터베이스 및 테이블을 초기화합니다."""
    conn = sqlite3.connect(DB_PATH)
    cursor = conn.cursor()
    cursor.execute("""
        CREATE TABLE IF NOT EXISTS tracked_banners (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            cctv_id TEXT NOT NULL,
            x1 INTEGER,
            y1 INTEGER,
            x2 INTEGER,
            y2 INTEGER,
            first_seen TIMESTAMP NOT NULL,
            last_seen TIMESTAMP NOT NULL,
            banner_type TEXT,
            text TEXT,
            environment TEXT,
            violation TEXT,
            verdict TEXT,
            reason TEXT,
            status TEXT DEFAULT '감시중'
        )
    """)
    conn.commit()
    conn.close()
    print(f"[DB] 데이터베이스 초기화 완료: {DB_PATH}")


def calculate_iou(boxA, boxB):
    """두 바운딩 박스 간의 IoU(Intersection over Union)를 계산합니다."""
    xA = max(boxA[0], boxB[0])
    yA = max(boxA[1], boxB[1])
    xB = min(boxA[2], boxB[2])
    yB = min(boxA[3], boxB[3])

    interArea = max(0, xB - xA) * max(0, yB - yA)
    boxAArea = (boxA[2] - boxA[0]) * (boxA[3] - boxA[1])
    boxBArea = (boxB[2] - boxB[0]) * (boxB[3] - boxB[1])

    if float(boxAArea + boxBArea - interArea) == 0:
        return 0.0

    iou = interArea / float(boxAArea + boxBArea - interArea)
    return iou


def find_matching_banner(cctv_id: str, bbox: list):
    """
    현재 감시 중인 현수막 중 새로운 bbox와 IoU 0.4 이상 겹치는 기존 현수막을 찾습니다.
    발견하면 해당 현수막의 전체 정보를 딕셔너리로 반환하고, 없으면 None을 반환합니다.
    """
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cursor = conn.cursor()

    cursor.execute(
        "SELECT * FROM tracked_banners WHERE cctv_id = ? AND status = '감시중'",
        (cctv_id,)
    )
    rows = cursor.fetchall()
    conn.close()

    for row in rows:
        db_box = [row['x1'], row['y1'], row['x2'], row['y2']]
        if calculate_iou(bbox, db_box) > 0.4:
            return dict(row)
    return None


def update_existing_banner(banner_id: int, bbox: list) -> dict:
    """
    이미 탐지된 기존 현수막의 좌표와 관측 시각(last_seen)을 갱신합니다.
    이때 정당현수막의 15일 초과 여부를 대조해 자동 불법 처리를 수행합니다.
    """
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cursor = conn.cursor()

    # 1. 기존 데이터 조회
    cursor.execute("SELECT * FROM tracked_banners WHERE id = ?", (banner_id,))
    row = cursor.fetchone()
    if not row:
        conn.close()
        return {}

    banner = dict(row)
    now = datetime.now()
    first_seen_time = datetime.fromisoformat(banner['first_seen'])
    elapsed_days = (now - first_seen_time).days

    verdict = banner['verdict']
    violation = banner['violation']
    reason = banner['reason']

    # 2. 15일 만료 기한 체크 (정당현수막 전용)
    if banner['banner_type'] == "정당현수막" and elapsed_days > 15:
        verdict = "불법"
        violation = "기간초과"
        reason = f"정당 현수막 허가 기간(15일)을 초과하였습니다. (현재 {elapsed_days}일째 장기 게시중)"

    # 3. DB 업데이트
    cursor.execute("""
        UPDATE tracked_banners 
        SET last_seen = ?, x1 = ?, y1 = ?, x2 = ?, y2 = ?, verdict = ?, violation = ?, reason = ?
        WHERE id = ?
    """, (now.isoformat(), bbox[0], bbox[1], bbox[2], bbox[3], verdict, violation, reason, banner_id))

    conn.commit()

    # 업데이트된 최신 정보 다시 조회하여 반환
    cursor.execute("SELECT * FROM tracked_banners WHERE id = ?", (banner_id,))
    updated_row = dict(cursor.fetchone())
    conn.close()

    # 구/동 매핑 정보 추가
    loc = get_location_for_cctv(updated_row['cctv_id'])
    updated_row['gu'] = loc['gu']
    updated_row['dong'] = loc['dong']
    updated_row['location_detail'] = loc['detail']

    updated_row['is_new'] = False
    updated_row['elapsed_days'] = elapsed_days
    return updated_row


def insert_new_banner(cctv_id: str, bbox: list, detect_info: dict) -> dict:
    """
    VLM을 거쳐 새로 분류된 현수막을 DB에 최초 등록합니다.
    """
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cursor = conn.cursor()

    now = datetime.now()
    cursor.execute("""
        INSERT INTO tracked_banners 
        (cctv_id, x1, y1, x2, y2, first_seen, last_seen, banner_type, text, environment, violation, verdict, reason, status)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, '감시중')
    """, (
        cctv_id, bbox[0], bbox[1], bbox[2], bbox[3], 
        now.isoformat(), now.isoformat(), 
        detect_info.get("banner_type", "일반현수막"),
        detect_info.get("text", ""),
        detect_info.get("environment", ""),
        detect_info.get("violation", "없음"),
        detect_info.get("verdict", "합법"),
        detect_info.get("reason", "")
    ))
    new_id = cursor.lastrowid
    conn.commit()

    cursor.execute("SELECT * FROM tracked_banners WHERE id = ?", (new_id,))
    new_row = dict(cursor.fetchone())
    conn.close()

    # 구/동 매핑 정보 추가
    loc = get_location_for_cctv(cctv_id)
    new_row['gu'] = loc['gu']
    new_row['dong'] = loc['dong']
    new_row['location_detail'] = loc['detail']

    new_row['is_new'] = True
    new_row['elapsed_days'] = 0
    return new_row


def get_all_tracked_banners():
    """대시보드 조회를 위한 전체 내역을 반환합니다."""
    conn = sqlite3.connect(DB_PATH)
    conn.row_factory = sqlite3.Row
    cursor = conn.cursor()
    cursor.execute("SELECT * FROM tracked_banners ORDER BY last_seen DESC")
    rows = []
    for r in cursor.fetchall():
        d = dict(r)
        loc = get_location_for_cctv(d['cctv_id'])
        d['gu'] = loc['gu']
        d['dong'] = loc['dong']
        d['location_detail'] = loc['detail']
        rows.append(d)
    conn.close()
    return rows

