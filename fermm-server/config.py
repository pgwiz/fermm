from pydantic_settings import BaseSettings
from functools import lru_cache


class Settings(BaseSettings):
    # Database
    database_url: str = "postgresql+asyncpg://fermm:fermm@localhost:5432/fermm"
    
    # JWT
    jwt_secret: str = "change-me-in-production"
    jwt_algorithm: str = "HS256"
    jwt_expire_minutes: int = 60
    
    # Admin
    admin_username: str = "admin"
    admin_password: str = "admin"
    
    # Server
    host: str = "0.0.0.0"
    port: int = 8000
    
    class Config:
        env_file = ".env"
        env_prefix = "FERMM_"


@lru_cache
def get_settings() -> Settings:
    return Settings()
