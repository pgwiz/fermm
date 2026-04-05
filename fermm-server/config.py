from pydantic_settings import BaseSettings
from functools import lru_cache


class Settings(BaseSettings):
    # Database - read from environment variables directly
    database_url: str = None
    postgres_user: str = "fermm"
    postgres_password: str = "fermm"
    postgres_host: str = "fermm-postgres"
    postgres_port: int = 5432
    postgres_db: str = "fermm"
    
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
        # Read from environment variables directly
        case_sensitive = False
    
    def __init__(self, **data):
        super().__init__(**data)
        # If DATABASE_URL not provided, build it from components
        if not self.database_url:
            self.database_url = (
                f"postgresql+asyncpg://{self.postgres_user}:{self.postgres_password}"
                f"@{self.postgres_host}:{self.postgres_port}/{self.postgres_db}"
            )


@lru_cache
def get_settings() -> Settings:
    return Settings()
