-- Schema snapshot; no user data.
CREATE TABLE citizen_reports (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    report_no TEXT NOT NULL UNIQUE,
    reported_at TEXT NOT NULL,
    location TEXT NOT NULL,
    reporter_name TEXT NOT NULL,
    report_type TEXT NOT NULL,
    attachment_count INTEGER NOT NULL DEFAULT 0,
    attachment_path TEXT,
    ai_confidence REAL NOT NULL DEFAULT 0,
    vlm_summary TEXT,
    status TEXT NOT NULL DEFAULT 'Pending',
    assignee_name TEXT,
    reject_reason TEXT,
    processed_at TEXT
);

CREATE TABLE enforcement_records (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    enforcement_no TEXT NOT NULL UNIQUE,
                    alert_id TEXT NOT NULL UNIQUE,
                    cctv_id TEXT,
                    source_image TEXT,
                    detected_at TIMESTAMP NOT NULL,
                    location TEXT,
                    ocr_text TEXT,
                    ai_status_code TEXT,
                    violation_type TEXT DEFAULT 'IllegalBanner',
                    violator_name TEXT DEFAULT '미확인',
                    business_name TEXT DEFAULT '',
                    fine_manwon INTEGER DEFAULT 300,
                    processing_status TEXT NOT NULL DEFAULT 'Urgent',
                    is_urgent INTEGER NOT NULL DEFAULT 1,
                    is_repeat INTEGER DEFAULT 0,
                    repeat_count INTEGER DEFAULT 0,
                    assignee TEXT DEFAULT '배정대기',
                    human_verdict TEXT,
                    human_status_code TEXT,
                    reviewed_at TIMESTAMP,
                    created_at TIMESTAMP NOT NULL,
                    updated_at TIMESTAMP NOT NULL
                , crop_image_b64 TEXT);

CREATE TABLE human_reviews (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                alert_id TEXT NOT NULL,
                cctv_id TEXT,
                source_image TEXT,
                ai_status_code TEXT,
                human_verdict TEXT NOT NULL,
                human_status_code TEXT NOT NULL,
                reviewer TEXT,
                note TEXT,
                ocr_text TEXT,
                reviewed_at TIMESTAMP NOT NULL
            );

CREATE TABLE similar_enforcement_reviews (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    source_enforcement_no TEXT NOT NULL,
                    matched_enforcement_no TEXT NOT NULL,
                    matched_location TEXT,
                    matched_ocr_summary TEXT,
                    matched_detected_at TIMESTAMP,
                    image_similarity_pct INTEGER NOT NULL DEFAULT 0,
                    text_similarity_pct INTEGER NOT NULL DEFAULT 0,
                    created_at TIMESTAMP NOT NULL,
                    UNIQUE(source_enforcement_no, matched_enforcement_no)
                );

CREATE TABLE tracked_banners (
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
                );

CREATE UNIQUE INDEX idx_enforcement_source_image ON enforcement_records(source_image) WHERE source_image IS NOT NULL AND source_image != '';
