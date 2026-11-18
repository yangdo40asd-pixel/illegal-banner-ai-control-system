"""Compatibility import; persistence implementation lives in repositories."""
from app.repositories.banner_repository import DatabaseService

__all__ = ['DatabaseService']
