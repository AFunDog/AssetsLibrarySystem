from pydantic import BaseModel


class Settings(BaseModel):
    app_name: str = "Assets Library System API"
    app_version: str = "0.1.0"
    api_prefix: str = "/api/v1"


settings = Settings()
