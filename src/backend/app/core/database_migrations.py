"""Both runtimes apply the same SQL baseline under a SQLite write lock."""
from pathlib import Path
import sqlite3
from app.core.paths import paths


def apply_migrations(connection: sqlite3.Connection) -> None:
    migration = paths.root / 'database' / 'migrations' / '001-initial.sql'
    if not migration.is_file():
        raise FileNotFoundError(migration)
    connection.execute('BEGIN IMMEDIATE')
    try:
        if connection.execute('PRAGMA user_version').fetchone()[0] > 1:
            raise RuntimeError('Database is newer than this application')
        statement = ''
        for line in migration.read_text(encoding='utf-8-sig').splitlines(keepends=True):
            statement += line
            if sqlite3.complete_statement(statement):
                connection.execute(statement)
                statement = ''
        columns = {row[1] for row in connection.execute('PRAGMA table_info(enforcement_records)')}
        if 'crop_image_b64' not in columns:
            connection.execute('ALTER TABLE enforcement_records ADD COLUMN crop_image_b64 TEXT')
        for old, new in [('합법', 'LEGAL'), ('불법', 'ILLEGAL'), ('재검토', 'REVIEW')]:
            for table in ('human_reviews', 'enforcement_records'):
                connection.execute(f'UPDATE {table} SET human_verdict=? WHERE human_verdict=?', (new, old))
        connection.execute('PRAGMA user_version=1')
        connection.commit()
    except BaseException:
        connection.rollback()
        raise
