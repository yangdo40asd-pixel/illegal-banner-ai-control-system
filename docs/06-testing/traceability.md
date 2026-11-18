# 요구사항 추적

최종 검토: 2026-11-17.

| 요구사항 | 구현 | 검증 |
|---|---|---|
| FR-CCTV-01 | CctvMonitoringViewModel, media_service, yolo_detector | GPU YOLO·UI |
| FR-CCTV-02 | CctvChannel.ApplyDetectionBatch | EmptyBatchRemovesPreviousDetection |
| FR-VLM-01 | VlmMixin, InferencePipelineMixin, AlertService | GPU VLM·파서 시험 |
| FR-REVIEW-01 | DatabaseService.Reviews, db_service | 기존 6범주 회귀·DB migration |
| FR-ENF-01 | EnforcementStatusViewModel, DatabaseService.Enforcement | 회귀·UI 전환 |
| FR-REPORT-01 | PdfReportService, StatisticsExcelExportService | 기존 PDF 생성 회귀, Excel 생성 후 실제 셀 값 검증 |
| FR-STATUS-01 | SystemMonitorService, telemetry_service, /health | mock 상태 전이·실제 UI GPU 수치 |
| NFR-PATH-01 | RuntimePaths, core.paths, 공통 JSON | cwd 독립·우선순위·발행본 C#/Python 경로 일치·HTTP/WS |
| NFR-DATA-01 | 공유 SQL, SQLite 백업 | 5개 테이블 및 integrity_check |
| NFR-LIFECYCLE-01 | App Mutex, BackendManager | 기존 소유권 회귀 |
| NFR-REPRO-01 | manifest, lock, setup/verify/test scripts | 모델 해시·단계별 실행 기록 |
