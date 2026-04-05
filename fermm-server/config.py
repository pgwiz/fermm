from pydantic_settings import BaseSettings
from pydantic import Field, AliasChoices
from functools import lru_cache
from typing import Optional


class Settings(BaseSettings):
    # Database - read from environment variables directly
    database_url: Optional[str] = Field(
        default=None,
        validation_alias=AliasChoices("DATABASE_URL", "FERMM_DATABASE_URL"),
    )
    postgres_user: str = "fermm"
    postgres_password: str = "fermm"
    postgres_host: str = "fermm-postgres"
    postgres_port: int = 5432
    postgres_db: str = "fermm"
    
    # JWT
    jwt_secret: str = Field(
        default="change-me-in-production",
        validation_alias=AliasChoices("JWT_SECRET", "FERMM_JWT_SECRET"),
    )
    jwt_algorithm: str = "HS256"
    jwt_expire_minutes: int = 60
    
    # Admin
    admin_username: str = Field(
        default="admin",
        validation_alias=AliasChoices("ADMIN_USERNAME", "FERMM_ADMIN_USERNAME"),
    )
    admin_password: str = Field(
        default="admin",
        validation_alias=AliasChoices("ADMIN_PASSWORD", "FERMM_ADMIN_PASSWORD"),
    )
    
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
