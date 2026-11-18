import os
import sys
from urllib.parse import urlsplit
from app.core.paths import paths

class Settings:
    PROJECT_NAME: str = "BannerGuardSystem Backend"
    API_VERSION: str = "v1"
    
    # Port / Host Configurations
    HOST: str = os.getenv("BANNERGUARD_HOST", "127.0.0.1")
    PORT: int = urlsplit(paths.get("ApiBaseUrl", "http://localhost:8000")).port or 8000
    
    # Paths setup
    if getattr(sys, 'frozen', False):
        BASE_DIR: str = os.path.dirname(sys.executable)
    else:
        BASE_DIR: str = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        
    ASSETS_DIR: str = str(paths.runtime_directory("assets"))

settings = Settings()

# Ensure directories exist
os.makedirs(settings.ASSETS_DIR, exist_ok=True)
