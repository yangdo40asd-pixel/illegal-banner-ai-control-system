"""Shared configuration compatible with desktop RuntimePaths; no AI imports."""
from __future__ import annotations

import json
import os
import re
from pathlib import Path


def find_root() -> Path:
    if configured := os.getenv("BANNERGUARD_ROOT"):
        return Path(configured).resolve()
    for directory in Path(__file__).resolve().parents:
        if (directory / '.bannerguard-root').is_file():
            return directory
    for directory in Path(__file__).resolve().parents:
        if (directory / "configs" / "paths.example.json").is_file():
            return directory
    raise FileNotFoundError("configs/paths.example.json not found; set BANNERGUARD_ROOT for deployment")


class RuntimePaths:
    def __init__(self, root: Path | None = None):
        self.root = Path(root).resolve() if root is not None else find_root()
        self.values: dict[str, str] = {}
        example = self.root / "configs" / "paths.example.json"
        local = Path(os.getenv("BANNERGUARD_CONFIG", str(self.root / "configs" / "paths.local.json")))
        self.configuration_file = local if local.is_file() else example
        for file in dict.fromkeys((example, self.configuration_file)):
            if file.is_file():
                self.values.update(json.loads(file.read_text(encoding="utf-8-sig")))

    def get(self, key: str, fallback: str = "") -> str:
        variable = "BANNERGUARD_" + re.sub(r"([a-z0-9])([A-Z])", r"\1_\2", key).upper()
        return os.getenv(variable, self.values.get(key, fallback))

    def resolve(self, key: str, fallback: str = "") -> Path:
        path = Path(os.path.expandvars(self.get(key, fallback)))
        return (path if path.is_absolute() else self.root / path).resolve()

    def runtime_directory(self, category: str) -> Path:
        directory = self.resolve("RuntimeDirectory", "runtime") / category
        directory.mkdir(parents=True, exist_ok=True)
        return directory


paths = RuntimePaths()
