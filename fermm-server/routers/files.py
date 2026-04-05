from fastapi import APIRouter, Depends, HTTPException, UploadFile, File
from fastapi.responses import Response
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select
import base64

from database import get_db
from models.db import Device
from auth import get_current_user
from ws_manager import manager

router = APIRouter(prefix="/api/devices", tags=["files"])


@router.post("/{device_id}/upload")
async def upload_file(
    device_id: str,
    path: str,
    file: UploadFile = File(...),
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Push file from dashboard to agent"""
    # Verify device exists
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    if not manager.is_agent_online(device_id):
        raise HTTPException(status_code=503, detail="Device is offline")
    
    # Read file and encode
    content = await file.read()
    b64_content = base64.b64encode(content).decode()
    
    # Send upload command to agent
    import uuid
    command_id = str(uuid.uuid4())
    
    payload = f"{path}|{b64_content}"
    sent = await manager.send_to_agent(device_id, {
        "command_id": command_id,
        "type": "upload",
        "payload": payload,
        "timeout_seconds": 300
    })
    
    if not sent:
        raise HTTPException(status_code=503, detail="Failed to send to device")
    
    return {"status": "uploading", "command_id": command_id, "size": len(content)}


@router.get("/{device_id}/download")
async def download_file(
    device_id: str,
    path: str,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Request file download from agent"""
    # Verify device exists
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    if not manager.is_agent_online(device_id):
        raise HTTPException(status_code=503, detail="Device is offline")
    
    import uuid
    command_id = str(uuid.uuid4())
    
    sent = await manager.send_to_agent(device_id, {
        "command_id": command_id,
        "type": "download",
        "payload": path,
        "timeout_seconds": 300
    })
    
    if not sent:
        raise HTTPException(status_code=503, detail="Failed to send to device")
    
    return {"status": "downloading", "command_id": command_id}
