from fastapi import APIRouter, Depends, HTTPException, status, WebSocket
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select
import json
import uuid

from database import get_db
from models.db import Device
from auth import get_current_user
from ws_manager import manager

router = APIRouter(prefix="/api/devices", tags=["overlay"])


@router.post("/{device_id}/overlay/spawn")
async def spawn_overlay(
    device_id: str,
    config: dict = None,
    current_user = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Spawn overlay on a remote device
    
    Sends a command to the connected agent to spawn the overlay window.
    The overlay will use Named Pipes for IPC communication with the agent.
    """
    # Verify device exists and belongs to user
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    
    if not device:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Device not found")
    
    # Check if device is connected via WebSocket
    if not manager.is_connected(device_id):
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Device not connected"
        )
    
    # Send spawn command to device via WebSocket
    command_id = str(uuid.uuid4())
    command = {
        "command_id": command_id,
        "type": "overlay",
        "payload": json.dumps({
            "action": "spawn",
            "config": config or {}
        }),
        "timeout_seconds": 30
    }
    
    await manager.send_command(device_id, command)
    
    return {
        "status": "success",
        "message": "Overlay spawn command sent to device",
        "device_id": device_id,
        "command_id": command_id
    }


@router.post("/{device_id}/overlay/close")
async def close_overlay(
    device_id: str,
    current_user = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Close overlay on a remote device"""
    # Verify device exists and belongs to user
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    
    if not device:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Device not found")
    
    if not manager.is_connected(device_id):
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Device not connected"
        )
    
    # Send close command
    command_id = str(uuid.uuid4())
    command = {
        "command_id": command_id,
        "type": "overlay",
        "payload": json.dumps({
            "action": "close"
        }),
        "timeout_seconds": 30
    }
    
    await manager.send_command(device_id, command)
    
    return {
        "status": "success",
        "message": "Overlay close command sent to device",
        "device_id": device_id,
        "command_id": command_id
    }


@router.post("/{device_id}/overlay/message")
async def send_overlay_message(
    device_id: str,
    message: dict,
    current_user = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Send a message to the overlay chatboard on a remote device"""
    # Verify device exists
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    
    if not device:
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Device not found")
    
    if not manager.is_connected(device_id):
        raise HTTPException(
            status_code=status.HTTP_503_SERVICE_UNAVAILABLE,
            detail="Device not connected"
        )
    
    # Extract message content
    content = message.get("content", "")
    if not content:
        raise HTTPException(status_code=status.HTTP_400_BAD_REQUEST, detail="Message content required")
    
    # Send message command
    command_id = str(uuid.uuid4())
    command = {
        "command_id": command_id,
        "type": "overlay",
        "payload": json.dumps({
            "action": "send_message",
            "message": content
        }),
        "timeout_seconds": 30
    }
    
    await manager.send_command(device_id, command)
    
    return {
        "status": "success",
        "message": "Message sent to overlay",
        "device_id": device_id,
        "command_id": command_id
    }
