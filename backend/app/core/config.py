import os
import sys

class Settings:
    PROJECT_NAME: str = "BannerGuardSystem Backend"
    API_VERSION: str = "v1"
    
    # Port / Host Configurations
    HOST: str = "0.0.0.0"
    PORT: int = 8000
    
    # Paths setup
    if getattr(sys, 'frozen', False):
        BASE_DIR: str = os.path.dirname(sys.executable)
    else:
        BASE_DIR: str = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
        
    ASSETS_DIR: str = os.path.join(BASE_DIR, "assets")

settings = Settings()

# Ensure directories exist
os.makedirs(settings.ASSETS_DIR, exist_ok=True)
