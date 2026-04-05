from fastapi import APIRouter, Depends, HTTPException
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select, desc
from database import get_db
from models.db import Script, ScriptExecution, Device
from auth import get_current_user
import uuid
from datetime import datetime
from typing import List, Optional
from pydantic import BaseModel

router = APIRouter(prefix="/api/scripts", tags=["scripts"])


class ScriptCreate(BaseModel):
    name: str
    description: Optional[str] = None
    script_type: str  # cmd, powershell, sh, bash
    content: str


class ScriptUpdate(BaseModel):
    name: Optional[str] = None
    description: Optional[str] = None
    content: Optional[str] = None


class ScriptExecuteRequest(BaseModel):
    device_id: str


@router.post("")
async def create_script(
    script: ScriptCreate,
    db: AsyncSession = Depends(get_db),
    current_user: dict = Depends(get_current_user)
):
    """Create a new script"""
    script_id = str(uuid.uuid4())
    
    db_script = Script(
        id=script_id,
        name=script.name,
        description=script.description,
        script_type=script.script_type,
        content=script.content,
        created_by=current_user.get("username", "unknown")
    )
    
    db.add(db_script)
    await db.commit()
    await db.refresh(db_script)
    
    return {
        "id": db_script.id,
        "name": db_script.name,
        "description": db_script.description,
        "script_type": db_script.script_type,
        "created_by": db_script.created_by,
        "created_at": db_script.created_at.isoformat()
    }


@router.get("")
async def list_scripts(
    db: AsyncSession = Depends(get_db),
    current_user: dict = Depends(get_current_user)
):
    """List all scripts"""
    result = await db.execute(select(Script).order_by(desc(Script.created_at)))
    scripts = result.scalars().all()
    
    return [{
        "id": s.id,
        "name": s.name,
        "description": s.description,
        "script_type": s.script_type,
        "created_by": s.created_by,
        "created_at": s.created_at.isoformat()
    } for s in scripts]


@router.get("/{script_id}")
async def get_script(
    script_id: str,
    db: AsyncSession = Depends(get_db),
    current_user: dict = Depends(get_current_user)
):
    """Get a specific script"""
    result = await db.execute(select(Script).where(Script.id == script_id))
    script = result.scalar_one_or_none()
    if not script:
        raise HTTPException(status_code=404, detail="Script not found")
    
    return {
        "id": script.id,
        "name": script.name,
        "description": script.description,
        "script_type": script.script_type,
        "content": script.content,
        "created_by": script.created_by,
        "created_at": script.created_at.isoformat(),
        "updated_at": script.updated_at.isoformat() if script.updated_at else None
    }


@router.put("/{script_id}")
async def update_script(
    script_id: str,
    script_update: ScriptUpdate,
    db: AsyncSession = Depends(get_db),
    current_user: dict = Depends(get_current_user)
):
    """Update a script"""
    result = await db.execute(select(Script).where(Script.id == script_id))
    script = result.scalar_one_or_none()
    if not script:
        raise HTTPException(status_code=404, detail="Script not found")
    
    if script_update.name:
        script.name = script_update.name
    if script_update.description is not None:
        script.description = script_update.description
    if script_update.content:
        script.content = script_update.content
    
    script.updated_at = datetime.utcnow()
    await db.commit()
    await db.refresh(script)
    
    return {"message": "Script updated", "id": script.id}


@router.delete("/{script_id}")
async def delete_script(
    script_id: str,
    db: AsyncSession = Depends(get_db),
    current_user: dict = Depends(get_current_user)
):
    """Delete a script"""
    result = await db.execute(select(Script).where(Script.id == script_id))
    script = result.scalar_one_or_none()
    if not script:
        raise HTTPException(status_code=404, detail="Script not found")
    
    await db.delete(script)
    await db.commit()
    
    return {"message": "Script deleted"}


@router.post("/{script_id}/execute")
async def execute_script(
    script_id: str,
    request: ScriptExecuteRequest,
    db: AsyncSession = Depends(get_db),
    current_user: dict = Depends(get_current_user)
):
    """Execute a script on a device"""
    from routers.devices import send_command
    
    # Get the script
    result = await db.execute(select(Script).where(Script.id == script_id))
    script = result.scalar_one_or_none()
    if not script:
        raise HTTPException(status_code=404, detail="Script not found")
    
    # Verify device exists
    dev_result = await db.execute(select(Device).where(Device.id == request.device_id))
    device = dev_result.scalar_one_or_none()
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    # Create execution record
    execution_id = str(uuid.uuid4())
    
    # Send command to device
    import json
    payload = json.dumps({
        "script_id": script_id,
        "script_type": script.script_type,
        "content": script.content
    })
    
    result = await send_command(request.device_id, "script", payload, db, current_user)
    
    # Store execution record
    execution = ScriptExecution(
        id=execution_id,
        script_id=script_id,
        device_id=request.device_id,
        command_id=result["command_id"],
        status="pending",
        executed_by=current_user.get("username", "unknown")
    )
    
    db.add(execution)
    await db.commit()
    
    return {
        "execution_id": execution_id,
        "command_id": result["command_id"],
        "script_name": script.name,
        "device_id": request.device_id
    }


@router.get("/{script_id}/executions")
async def get_script_executions(
    script_id: str,
    db: AsyncSession = Depends(get_db),
    current_user: dict = Depends(get_current_user)
):
    """Get execution history for a script"""
    result = await db.execute(
        select(ScriptExecution)
        .where(ScriptExecution.script_id == script_id)
        .order_by(desc(ScriptExecution.started_at))
        .limit(50)
    )
    executions = result.scalars().all()
    
    return [{
        "id": e.id,
        "device_id": e.device_id,
        "command_id": e.command_id,
        "status": e.status,
        "exit_code": e.exit_code,
        "log_file": e.log_file,
        "executed_by": e.executed_by,
        "started_at": e.started_at.isoformat(),
        "completed_at": e.completed_at.isoformat() if e.completed_at else None
    } for e in executions]
