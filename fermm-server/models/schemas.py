from pydantic import BaseModel
from datetime import datetime
from typing import Optional, List, Any


# Auth
class TokenRequest(BaseModel):
    username: str
    password: str


class TokenResponse(BaseModel):
    access_token: str
    token_type: str = "bearer"


# Devices
class DeviceRegister(BaseModel):
    device_id: str
    hostname: str
    os: str
    arch: Optional[str] = None


class DeviceResponse(BaseModel):
    id: str
    hostname: str
    os: str
    arch: Optional[str]
    ip: Optional[str]
    online: bool
    last_seen: Optional[datetime]
    registered_at: datetime

    class Config:
        from_attributes = True


class DeviceList(BaseModel):
    devices: List[DeviceResponse]


# Commands
class CommandRequest(BaseModel):
    type: str
    payload: Optional[str] = None
    timeout_seconds: int = 30


class CommandResponse(BaseModel):
    command_id: str
    status: str = "queued"


class CommandResultSchema(BaseModel):
    command_id: str
    device_id: str
    type: str
    exit_code: int
    output: List[str]
    error: Optional[str]
    duration_ms: int
    timestamp: datetime


class PendingCommands(BaseModel):
    commands: List[dict]


# Tasks
class TaskCreate(BaseModel):
    type: str  # shell, upload, download, execute, screenshot
    payload: str


class TaskResponse(BaseModel):
    id: str
    device_id: str
    type: str
    payload: str
    status: str
    result: Optional[Any] = None
    error: Optional[str] = None
    created_at: datetime
    started_at: Optional[datetime] = None
    completed_at: Optional[datetime] = None
    retries: int
    max_retries: int

    class Config:
        from_attributes = True


class TaskListResponse(BaseModel):
    tasks: List[TaskResponse]


class TaskUpdate(BaseModel):
    status: str
    result: Optional[Any] = None
    error: Optional[str] = None


# WebSocket messages
class WsMessage(BaseModel):
    type: str
    data: Any
