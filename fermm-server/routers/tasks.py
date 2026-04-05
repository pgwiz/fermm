from fastapi import APIRouter, Depends, HTTPException, Query
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select, update
from datetime import datetime
import uuid

from database import get_db
from models.db import Device, Task
from models.schemas import TaskCreate, TaskResponse, TaskListResponse, TaskUpdate
from auth import get_current_user, verify_device_token
from ws_manager import manager

router = APIRouter(prefix="/api/devices", tags=["tasks"])


@router.post("/{device_id}/tasks", response_model=TaskResponse)
async def create_task(
    device_id: str,
    task_data: TaskCreate,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Queue task for device"""
    # Verify device exists
    result = await db.execute(select(Device).where(Device.id == device_id))
    if not result.scalar_one_or_none():
        raise HTTPException(status_code=404, detail="Device not found")
    
    task = Task(
        id=str(uuid.uuid4()),
        device_id=device_id,
        type=task_data.type,
        payload=task_data.payload,
        status="pending"
    )
    db.add(task)
    await db.commit()
    await db.refresh(task)
    
    return task


@router.get("/{device_id}/tasks", response_model=TaskListResponse)
async def list_tasks(
    device_id: str,
    status: str = Query(None),
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """List tasks for device"""
    query = select(Task).where(Task.device_id == device_id)
    
    if status:
        query = query.where(Task.status == status)
    
    result = await db.execute(query)
    tasks = result.scalars().all()
    
    return TaskListResponse(tasks=tasks)


@router.get("/{device_id}/tasks/{task_id}", response_model=TaskResponse)
async def get_task(
    device_id: str,
    task_id: str,
    user: dict = Depends(get_current_user),
    db: AsyncSession = Depends(get_db)
):
    """Get task details"""
    result = await db.execute(
        select(Task).where(Task.id == task_id).where(Task.device_id == device_id)
    )
    task = result.scalar_one_or_none()
    
    if not task:
        raise HTTPException(status_code=404, detail="Task not found")
    
    return task


@router.get("/{device_id}/pending-tasks")
async def get_pending_tasks(
    device_id: str,
    token: str = Depends(verify_device_token),
    db: AsyncSession = Depends(get_db)
):
    """Agent polls for pending tasks (used by agent)"""
    # Verify device token matches
    result = await db.execute(select(Device).where(Device.id == device_id))
    device = result.scalar_one_or_none()
    if not device:
        raise HTTPException(status_code=404, detail="Device not found")
    
    result = await db.execute(
        select(Task).where(Task.device_id == device_id).where(Task.status == "pending")
    )
    tasks = result.scalars().all()
    
    # Convert to JSON-serializable format
    return [{"id": t.id, "type": t.type, "payload": t.payload} for t in tasks]


@router.put("/{device_id}/tasks/{task_id}")
async def update_task_status(
    device_id: str,
    task_id: str,
    update_data: TaskUpdate,
    token: str = Depends(verify_device_token),
    db: AsyncSession = Depends(get_db)
):
    """Agent reports task completion/update"""
    # Verify device exists
    result = await db.execute(select(Device).where(Device.id == device_id))
    if not result.scalar_one_or_none():
        raise HTTPException(status_code=404, detail="Device not found")
    
    # Get task to verify it exists
    result = await db.execute(
        select(Task).where(Task.id == task_id).where(Task.device_id == device_id)
    )
    task = result.scalar_one_or_none()
    if not task:
        raise HTTPException(status_code=404, detail="Task not found")
    
    # Prepare update values
    update_values = {
        "status": update_data.status,
        "result": update_data.result,
        "error": update_data.error
    }
    
    # Set completion timestamp if task is completed
    if update_data.status == "completed":
        update_values["completed_at"] = datetime.utcnow()
    elif update_data.status == "running":
        update_values["started_at"] = datetime.utcnow()
    
    await db.execute(
        update(Task)
        .where(Task.id == task_id)
        .where(Task.device_id == device_id)
        .values(**update_values)
    )
    await db.commit()
    
    return {"status": "ok"}
