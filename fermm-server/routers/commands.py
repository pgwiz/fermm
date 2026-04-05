from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select, delete
from datetime import datetime
import uuid
import base64
import os
from PIL import Image
import io

from database import get_db
from models.db import Device, CommandQueue, CommandResult, Screenshot
from models.schemas import CommandRequest, CommandResponse, CommandResultSchema, PendingCommands
from auth import get_current_user, verify_device_token
from ws_manager import manager

router = APIRouter(prefix="/api", tags=["commands"])


@router.post("/devices/{device_id}/command", response_model=CommandResponse)
async def dispatch_command(
    device_id: str,
    command: CommandRequest,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Dispatch command to device - via WebSocket if online, otherwise queue"""
    # Verify device exists
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    command_id = str(uuid.uuid4())
    payload = {
        "command_id": command_id,
        "type": command.type,
        "payload": command.payload,
        "timeout_seconds": command.timeout_seconds
    }
    
    # Try WebSocket first
    sent = await manager.send_to_agent(device_id, payload)
    
    if sent:
        return CommandResponse(command_id=command_id, status="sent")
    
    # Queue for polling
    queue_entry = CommandQueue(
        id=str(uuid.uuid4()),
        device_id=device_id,
        payload=payload
    )
    db.add(queue_entry)
    await db.commit()
    
    return CommandResponse(command_id=command_id, status="queued")


@router.get("/devices/{device_id}/pending")
async def get_pending_commands(
    device_id: str,
    token: str = Depends(verify_device_token),
    db: AsyncSession = Depends(get_db)
):
    """Agent polls for queued commands"""
    result = await db.execute(
        select(CommandQueue)
        .where(CommandQueue.device_id == device_id)
        .order_by(CommandQueue.created_at)
    )
    entries = result.scalars().all()
    
    if not entries:
        return []
    
    # Extract payloads and delete from queue
    commands = [entry.payload for entry in entries]
    
    for entry in entries:
        await db.delete(entry)
    await db.commit()
    
    # Update device last_seen
    await db.execute(
        select(Device).where(Device.id == device_id)
    )
    device = (await db.execute(select(Device).where(Device.id == device_id))).scalar_one_or_none()
    if device:
        device.last_seen = datetime.utcnow()
        await db.commit()
    
    return commands


@router.post("/devices/{device_id}/results")
async def post_result(
    device_id: str,
    result_data: CommandResultSchema,
    token: str = Depends(verify_device_token),
    db: AsyncSession = Depends(get_db)
):
    """Agent posts command result"""
    # Store result
    cmd_result = CommandResult(
        id=str(uuid.uuid4()),
        command_id=result_data.command_id,
        device_id=device_id,
        result=result_data.model_dump()
    )
    db.add(cmd_result)
    await db.commit()
    
    # Handle screenshot results specially - save the image file
    if result_data.type == "screenshot" and result_data.output and len(result_data.output) > 0:
        import logging
        logger = logging.getLogger(__name__)
        
        try:
            logger.info(f"Processing screenshot for device {device_id}")
            logger.info(f"Output length: {len(result_data.output)}")
            logger.info(f"First output element type: {type(result_data.output[0])}")
            logger.info(f"First output element length: {len(str(result_data.output[0]))}")
            
            base64_data = result_data.output[0]  # First output line is the base64 image
            
            # Decode base64 image
            image_bytes = base64.b64decode(base64_data)
            logger.info(f"Decoded {len(image_bytes)} bytes of image data")
            
            # Get image dimensions
            with Image.open(io.BytesIO(image_bytes)) as img:
                width, height = img.size
            logger.info(f"Image dimensions: {width}x{height}")
            
            # Generate filename
            timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
            filename = f"{device_id}_{timestamp}.png"
            
            # Ensure screenshots directory exists
            screenshots_dir = "static/screenshots"
            os.makedirs(screenshots_dir, exist_ok=True)
            logger.info(f"Screenshots directory ready: {screenshots_dir}")
            
            # Save image file
            filepath = os.path.join(screenshots_dir, filename)
            with open(filepath, 'wb') as f:
                f.write(image_bytes)
            logger.info(f"Screenshot saved to {filepath}")
            
            # Create screenshot record in database
            screenshot = Screenshot(
                id=str(uuid.uuid4()),
                device_id=device_id,
                filename=filename,
                filepath=f"/static/screenshots/{filename}",
                file_size=len(image_bytes),
                width=width,
                height=height,
                capture_method="automatic",
                captured_by="agent",
                tags=None,
                notes=None,
                extra_data=None
            )
            
            db.add(screenshot)
            await db.commit()
            logger.info(f"Screenshot record created in database")
        except Exception as e:
            # Log error but don't fail the result posting
            import logging
            logger = logging.getLogger(__name__)
            logger.error(f"Failed to save screenshot: {str(e)}", exc_info=True)
            await db.rollback()
    
    # Broadcast to subscribed dashboards
    await manager.broadcast_to_dashboards(device_id, {
        "type": "result",
        "data": result_data.model_dump()
    })
    
    return {"status": "ok"}


@router.get("/commands/{command_id}/result")
async def get_command_result(
    command_id: str,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Dashboard polls for async result"""
    result = await db.execute(
        select(CommandResult)
        .where(CommandResult.command_id == command_id)
        .order_by(CommandResult.created_at.desc())
        .limit(1)
    )
    cmd_result = result.scalar_one_or_none()
    
    if not cmd_result:
        raise HTTPException(status_code=404, detail="Result not found")
    
    return cmd_result.result


@router.get("/devices/{device_id}/history")
async def get_command_history(
    device_id: str,
    limit: int = 50,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Get command history for a device"""
    result = await db.execute(
        select(CommandResult)
        .where(CommandResult.device_id == device_id)
        .order_by(CommandResult.created_at.desc())
        .limit(limit)
    )
    results = result.scalars().all()
    
    return [r.result for r in results]
