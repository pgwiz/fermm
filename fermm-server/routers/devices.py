from fastapi import APIRouter, Depends, HTTPException, status, Request
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select, update, desc
from datetime import datetime
import uuid
import os
import glob
import json
import base64
from PIL import Image
import io

from database import get_db
from models.db import Device, Screenshot
from models.schemas import DeviceRegister, DeviceResponse, DeviceList
from auth import verify_device_token, hash_token, get_current_user
from ws_manager import manager

router = APIRouter(prefix="/api/devices", tags=["devices"])


@router.get("/discover", tags=["discovery"])
async def discover_server():
    """Public endpoint for agents to discover server and auto-register
    
    Agents curl this endpoint to get server configuration and receive
    a registration token. No authentication required.
    """
    import secrets
    
    # Generate a one-time registration token (valid for 5 minutes)
    # In production, this would be stored in Redis with TTL
    token = secrets.token_urlsafe(32)
    
    return {
        "server_url": "http://localhost",  # In production, use environment variable
        "registration_token": token,
        "poll_interval_seconds": 15,
        "description": "Auto-discovery endpoint for agents"
    }


@router.post("/register", response_model=DeviceResponse)
async def register_device(
    data: DeviceRegister,
    request: Request,
    token: str = Depends(verify_device_token),
    db: AsyncSession = Depends(get_db)
):
    """Agent self-registration - upserts device record"""
    client_ip = request.client.host if request.client else None
    
    # Check if device exists
    result = await db.execute(select(Device).where(Device.id == data.device_id))
    device = result.scalar_one_or_none()
    
    if device:
        # Update existing device
        device.hostname = data.hostname
        device.os = data.os
        device.arch = data.arch
        device.ip = client_ip
        device.last_seen = datetime.utcnow()
        device.online = True
    else:
        # Create new device
        device = Device(
            id=data.device_id,
            hostname=data.hostname,
            os=data.os,
            arch=data.arch,
            ip=client_ip,
            token_hash=hash_token(token),
            online=True,
            last_seen=datetime.utcnow()
        )
        db.add(device)
    
    await db.commit()
    await db.refresh(device)
    
    return device


@router.get("", response_model=DeviceList)
async def list_devices(
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """List all registered devices"""
    result = await db.execute(select(Device))
    devices = result.scalars().all()
    
    # Update online status based on WebSocket connections
    for device in devices:
        device.online = manager.is_agent_online(device.id)
    
    return DeviceList(devices=list(devices))


@router.get("/{device_id}", response_model=DeviceResponse)
async def get_device(
    device_id: str,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Get single device details"""
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    device.online = manager.is_agent_online(device_id)
    return device


@router.delete("/{device_id}")
async def delete_device(
    device_id: str,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Deregister device and revoke its token"""
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    await db.delete(device)
    await db.commit()
    
    return {"status": "deleted", "device_id": device_id}


@router.get("/{device_id}/screenshots")
async def list_device_screenshots(
    device_id: str,
    limit: int = 10,
    search: str = None,
    tags: str = None,
    captured_by: str = None,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """List screenshots for a device with enhanced filtering and metadata"""
    # Verify device exists
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    # Build query with filters
    query = select(Screenshot).where(Screenshot.device_id == device_id)
    
    if search:
        # Search in window title, process name, or notes
        search_filter = (
            (Screenshot.active_window_title.ilike(f"%{search}%")) |
            (Screenshot.active_process_name.ilike(f"%{search}%")) |
            (Screenshot.notes.ilike(f"%{search}%"))
        )
        query = query.where(search_filter)
    
    if captured_by:
        query = query.where(Screenshot.captured_by == captured_by)
    
    if tags:
        # Filter by tags (simplified - assumes tag is in JSON array)
        tag_list = [t.strip() for t in tags.split(",")]
        for tag in tag_list:
            query = query.where(Screenshot.tags.op("->>")(0).ilike(f"%{tag}%"))
    
    # Order by captured_at descending and limit
    query = query.order_by(desc(Screenshot.captured_at)).limit(limit)
    
    result = await db.execute(query)
    screenshots = result.scalars().all()
    
    # Convert to response format
    screenshot_list = []
    for screenshot in screenshots:
        screenshot_dict = {
            "id": screenshot.id,
            "filename": screenshot.filename,
            "filepath": screenshot.filepath,
            "file_size": screenshot.file_size,
            "width": screenshot.width,
            "height": screenshot.height,
            "active_window_title": screenshot.active_window_title,
            "active_process_name": screenshot.active_process_name,
            "active_process_id": screenshot.active_process_id,
            "capture_method": screenshot.capture_method,
            "captured_by": screenshot.captured_by,
            "tags": screenshot.tags or [],
            "notes": screenshot.notes,
            "captured_at": screenshot.captured_at.isoformat(),
            "metadata": screenshot.extra_data or {}
        }
        screenshot_list.append(screenshot_dict)
    
    return {"screenshots": screenshot_list}


@router.post("/{device_id}/screenshots")
async def save_screenshot_metadata(
    device_id: str,
    request: Request,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Save screenshot metadata to database when a screenshot is captured"""
    # Verify device exists
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    data = await request.json()
    
    # Extract base64 image data and metadata
    base64_data = data.get('image_data', '')
    metadata = data.get('metadata', {})
    capture_method = data.get('capture_method', 'manual')
    notes = data.get('notes', '')
    tags = data.get('tags', [])
    
    if not base64_data:
        raise HTTPException(status_code=400, detail="No image data provided")
    
    try:
        # Decode base64 image
        image_bytes = base64.b64decode(base64_data)
        
        # Get image dimensions
        with Image.open(io.BytesIO(image_bytes)) as img:
            width, height = img.size
        
        # Generate filename
        timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
        filename = f"{device_id}_{timestamp}.png"
        
        # Ensure screenshots directory exists
        screenshots_dir = "static/screenshots"
        os.makedirs(screenshots_dir, exist_ok=True)
        
        # Save image file
        filepath = os.path.join(screenshots_dir, filename)
        with open(filepath, 'wb') as f:
            f.write(image_bytes)
        
        # Create screenshot record
        screenshot = Screenshot(
            id=str(uuid.uuid4()),
            device_id=device_id,
            filename=filename,
            filepath=f"/static/screenshots/{filename}",
            file_size=len(image_bytes),
            width=width,
            height=height,
            active_window_title=metadata.get('active_window_title'),
            active_process_name=metadata.get('active_process_name'),
            active_process_id=metadata.get('active_process_id'),
            capture_method=capture_method,
            captured_by=user.get('username', 'unknown'),
            tags=tags if tags else None,
            notes=notes if notes else None,
            extra_data=metadata if metadata else None
        )
        
        db.add(screenshot)
        await db.commit()
        
        return {
            "status": "saved",
            "screenshot_id": screenshot.id,
            "filename": filename,
            "filepath": screenshot.filepath,
            "size": len(image_bytes),
            "width": width,
            "height": height
        }
        
    except Exception as e:
        await db.rollback()
        raise HTTPException(status_code=500, detail=f"Failed to save screenshot: {str(e)}")


@router.put("/{device_id}/screenshots/{screenshot_id}")
async def update_screenshot_metadata(
    device_id: str,
    screenshot_id: str,
    request: Request,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Update screenshot metadata (tags, notes, etc.)"""
    # Verify screenshot exists and belongs to device
    result = await db.execute(
        select(Screenshot).where(
            Screenshot.id == screenshot_id,
            Screenshot.device_id == device_id
        )
    )
    screenshot = result.scalar_one_or_none()
    
    if not screenshot:
        raise HTTPException(status_code=404, detail="Screenshot not found")
    
    data = await request.json()
    
    # Update editable fields
    if 'tags' in data:
        screenshot.tags = data['tags']
    if 'notes' in data:
        screenshot.notes = data['notes']
    if 'metadata' in data:
        # Merge new metadata with existing
        existing_metadata = screenshot.extra_data or {}
        existing_metadata.update(data['metadata'])
        screenshot.extra_data = existing_metadata
    
    await db.commit()
    
    return {
        "status": "updated",
        "screenshot_id": screenshot_id,
        "tags": screenshot.tags,
        "notes": screenshot.notes
    }


@router.delete("/{device_id}/screenshots/{screenshot_id}")
async def delete_screenshot(
    device_id: str,
    screenshot_id: str,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Delete a screenshot and its metadata"""
    # Verify screenshot exists and belongs to device
    result = await db.execute(
        select(Screenshot).where(
            Screenshot.id == screenshot_id,
            Screenshot.device_id == device_id
        )
    )
    screenshot = result.scalar_one_or_none()
    
    if not screenshot:
        raise HTTPException(status_code=404, detail="Screenshot not found")
    
    # Delete file if it exists
    if screenshot.filepath:
        file_path = screenshot.filepath.lstrip('/')  # Remove leading slash for local path
        if os.path.exists(file_path):
            try:
                os.remove(file_path)
            except OSError:
                pass  # File might already be deleted
    
    # Delete database record
    await db.delete(screenshot)
    await db.commit()
    
    return {"status": "deleted", "screenshot_id": screenshot_id}
    
    return {"screenshots": screenshots}
