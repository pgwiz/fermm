from fastapi import APIRouter, WebSocket, WebSocketDisconnect, Depends, Query
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select, update
from datetime import datetime
import json
import base64
import os
import uuid

from database import get_db, async_session
from models.db import Device, CommandResult, Screenshot
from ws_manager import manager
from auth import decode_token

router = APIRouter(tags=["websocket"])


async def process_result_message(message, device_id):
    """Process command result messages, saving large data as files"""
    if message.get("type") == "screenshot" and message.get("exit_code") == 0:
        return await process_screenshot_result(message, device_id)
    return message


async def process_screenshot_result(message, device_id):
    """Save screenshot base64 as file and return metadata"""
    try:
        if not message.get("output") or not message["output"]:
            return message
            
        base64_data = message["output"][0]
        if not base64_data:
            return message
            
        # Create screenshots directory
        screenshots_dir = "static/screenshots"
        os.makedirs(screenshots_dir, exist_ok=True)
        
        # Generate filename
        timestamp = datetime.utcnow().strftime("%Y%m%d_%H%M%S")
        filename = f"{device_id}_{message['command_id']}_{timestamp}.png"
        filepath = os.path.join(screenshots_dir, filename)
        
        # Decode and save base64 as PNG file
        image_data = base64.b64decode(base64_data)
        with open(filepath, "wb") as f:
            f.write(image_data)
        
        # Get image dimensions
        width = height = None
        try:
            from PIL import Image
            import io
            with Image.open(io.BytesIO(image_data)) as img:
                width, height = img.size
        except Exception:
            pass
        
        # Save to database
        screenshot_id = str(uuid.uuid4())
        async with async_session() as db:
            screenshot = Screenshot(
                id=screenshot_id,
                device_id=device_id,
                filename=filename,
                filepath=f"/static/screenshots/{filename}",
                file_size=len(image_data),
                width=width,
                height=height,
                capture_method="command",
                captured_by="agent"
            )
            db.add(screenshot)
            await db.commit()
            
        # Return metadata instead of base64
        processed_message = message.copy()
        processed_message["output"] = [{
            "type": "screenshot_file",
            "filename": filename,
            "filepath": f"/static/screenshots/{filename}",
            "size": len(image_data),
            "timestamp": timestamp,
            "screenshot_id": screenshot_id
        }]
        
        return processed_message
        
    except Exception as e:
        print(f"Error processing screenshot: {e}")
        return message


@router.websocket("/ws/agent/{device_id}")
async def agent_websocket(websocket: WebSocket, device_id: str, token: str = Query(...)):
    """Agent persistent control channel"""
    # Validate token (simple validation - in production, verify against device token)
    try:
        # For agents, we just verify the token is present
        # In production, you'd verify against the stored token_hash
        if not token:
            await websocket.close(code=4001)
            return
    except Exception:
        await websocket.close(code=4001)
        return
    
    await manager.connect_agent(device_id, websocket)
    
    # Update device status in DB
    async with async_session() as db:
        result = await db.execute(select(Device).where(Device.id == device_id))
        device = result.scalar_one_or_none()
        if device:
            device.online = True
            device.last_seen = datetime.utcnow()
            await db.commit()
    
    try:
        while True:
            data = await websocket.receive_text()
            message = json.loads(data)
            
            # Handle different message types from agent
            if message.get("type") == "stream":
                # Real-time output streaming
                await manager.broadcast_to_dashboards(device_id, message)
            
            elif "command_id" in message and "exit_code" in message:
                # Final command result (not streaming messages)
                processed_message = await process_result_message(message, device_id)
                
                async with async_session() as db:
                    import uuid
                    cmd_result = CommandResult(
                        id=str(uuid.uuid4()),
                        command_id=message["command_id"],
                        device_id=device_id,
                        result=processed_message
                    )
                    db.add(cmd_result)
                    await db.commit()
                
                await manager.broadcast_to_dashboards(device_id, {
                    "type": "result",
                    "data": processed_message
                })
            
            # Update last_seen
            async with async_session() as db:
                result = await db.execute(select(Device).where(Device.id == device_id))
                device = result.scalar_one_or_none()
                if device:
                    device.last_seen = datetime.utcnow()
                    await db.commit()
    
    except WebSocketDisconnect as e:
        print(f"[WS] Agent {device_id} disconnected: {e}")
    except Exception as e:
        print(f"[WS] Error in agent websocket {device_id}: {type(e).__name__}: {e}")
    finally:
        await manager.disconnect_agent(device_id)
        
        # Update device status
        async with async_session() as db:
            result = await db.execute(select(Device).where(Device.id == device_id))
            device = result.scalar_one_or_none()
            if device:
                device.online = False
                await db.commit()


@router.websocket("/ws/dashboard/{session_id}")
async def dashboard_websocket(websocket: WebSocket, session_id: str, token: str = Query(...)):
    """Dashboard receives live agent output streams"""
    # Validate JWT
    try:
        payload = decode_token(token)
        if payload.get("type") != "user":
            await websocket.close(code=4001)
            return
    except Exception:
        await websocket.close(code=4001)
        return
    
    await manager.connect_dashboard(session_id, websocket)
    
    try:
        while True:
            data = await websocket.receive_text()
            message = json.loads(data)
            
            # Handle subscription requests
            if message.get("action") == "subscribe":
                device_id = message.get("device_id")
                if device_id:
                    await manager.subscribe_to_device(session_id, device_id)
                    await websocket.send_json({"type": "subscribed", "device_id": device_id})
            
            elif message.get("action") == "ping":
                await websocket.send_json({"type": "pong"})
    
    except WebSocketDisconnect:
        pass
    finally:
        await manager.disconnect_dashboard(session_id)
