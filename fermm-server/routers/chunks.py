import os
import hashlib
import json
from fastapi import APIRouter, Depends, HTTPException, UploadFile, File
from sqlalchemy.ext.asyncio import AsyncSession
from sqlalchemy import select
from database import get_db
from auth import get_current_user
import uuid
from datetime import datetime, timedelta
from pathlib import Path

router = APIRouter(prefix="/api/files/chunks", tags=["chunks"])

# In-memory session storage (in production, use Redis)
UPLOAD_SESSIONS = {}
CHUNK_STORAGE_DIR = Path("./chunks")
CHUNK_STORAGE_DIR.mkdir(exist_ok=True)
MAX_CHUNK_SIZE = 256 * 1024  # 256KB


class UploadSession:
    def __init__(self, session_id: str, device_id: str, filepath: str, filename: str, total_chunks: int, file_size: int):
        self.session_id = session_id
        self.device_id = device_id
        self.filepath = filepath
        self.filename = filename
        self.total_chunks = total_chunks
        self.file_size = file_size
        self.chunks_received = set()
        self.created_at = datetime.utcnow()
        self.last_activity = datetime.utcnow()
        self.chunk_hashes = {}  # chunk_index -> hash for verification


def cleanup_old_sessions():
    """Remove sessions older than 24 hours"""
    now = datetime.utcnow()
    expired = [sid for sid, session in UPLOAD_SESSIONS.items() 
               if (now - session.created_at) > timedelta(hours=24)]
    for sid in expired:
        cleanup_session(sid)
        del UPLOAD_SESSIONS[sid]


def cleanup_session(session_id: str):
    """Delete all chunk files for a session"""
    session_dir = CHUNK_STORAGE_DIR / session_id
    if session_dir.exists():
        import shutil
        shutil.rmtree(session_dir)


@router.post("/start")
async def start_upload(
    data: dict,
    current_user: dict = Depends(get_current_user)
):
    """Initiate a chunked upload session
    
    Request body:
    {
        "device_id": "device-uuid",
        "filepath": "/path/to/file/on/device",
        "filename": "filename.ext",
        "file_size": 5242880
    }
    """
    cleanup_old_sessions()
    
    device_id = data.get("device_id")
    filepath = data.get("filepath")
    filename = data.get("filename")
    file_size = data.get("file_size", 0)
    
    if not all([device_id, filepath, filename, file_size > 0]):
        raise HTTPException(status_code=400, detail="Missing required fields")
    
    session_id = str(uuid.uuid4())
    total_chunks = (file_size + MAX_CHUNK_SIZE - 1) // MAX_CHUNK_SIZE
    
    session = UploadSession(
        session_id=session_id,
        device_id=device_id,
        filepath=filepath,
        filename=filename,
        total_chunks=total_chunks,
        file_size=file_size
    )
    
    UPLOAD_SESSIONS[session_id] = session
    
    # Create directory for this session's chunks
    (CHUNK_STORAGE_DIR / session_id).mkdir(exist_ok=True)
    
    return {
        "session_id": session_id,
        "total_chunks": total_chunks,
        "chunk_size": MAX_CHUNK_SIZE,
        "expires_at": (session.created_at + timedelta(hours=24)).isoformat()
    }


@router.post("/{session_id}/upload")
async def upload_chunk(
    session_id: str,
    chunk_index: int,
    chunk_hash: str,
    file: UploadFile = File(...),
    current_user: dict = Depends(get_current_user)
):
    """Upload a single chunk
    
    Query parameters:
    - chunk_index: 0-based index
    - chunk_hash: SHA256 hash of chunk for verification
    """
    if session_id not in UPLOAD_SESSIONS:
        raise HTTPException(status_code=404, detail="Session not found or expired")
    
    session = UPLOAD_SESSIONS[session_id]
    session.last_activity = datetime.utcnow()
    
    if chunk_index < 0 or chunk_index >= session.total_chunks:
        raise HTTPException(status_code=400, detail="Invalid chunk index")
    
    if chunk_index in session.chunks_received:
        return {
            "status": "duplicate",
            "message": f"Chunk {chunk_index} already received"
        }
    
    # Read and verify chunk
    chunk_data = await file.read()
    calculated_hash = hashlib.sha256(chunk_data).hexdigest()
    
    if calculated_hash != chunk_hash:
        raise HTTPException(status_code=400, detail="Chunk hash mismatch")
    
    # Save chunk
    chunk_path = CHUNK_STORAGE_DIR / session_id / f"chunk_{chunk_index:06d}"
    with open(chunk_path, 'wb') as f:
        f.write(chunk_data)
    
    session.chunks_received.add(chunk_index)
    session.chunk_hashes[chunk_index] = chunk_hash
    
    return {
        "status": "received",
        "chunk_index": chunk_index,
        "chunks_received": len(session.chunks_received),
        "total_chunks": session.total_chunks,
        "progress_percent": int((len(session.chunks_received) / session.total_chunks) * 100)
    }


@router.post("/{session_id}/complete")
async def complete_upload(
    session_id: str,
    data: dict,
    current_user: dict = Depends(get_current_user)
):
    """Finalize upload and reassemble file
    
    Request body:
    {
        "file_hash": "SHA256 of complete file"
    }
    """
    if session_id not in UPLOAD_SESSIONS:
        raise HTTPException(status_code=404, detail="Session not found or expired")
    
    session = UPLOAD_SESSIONS[session_id]
    session.last_activity = datetime.utcnow()
    
    # Verify all chunks received
    if len(session.chunks_received) != session.total_chunks:
        missing = [i for i in range(session.total_chunks) if i not in session.chunks_received]
        raise HTTPException(
            status_code=400, 
            detail=f"Missing chunks: {missing[:10]}"
        )
    
    # Reassemble file
    output_dir = CHUNK_STORAGE_DIR / "completed"
    output_dir.mkdir(exist_ok=True)
    output_path = output_dir / session_id / session.filename
    output_path.parent.mkdir(exist_ok=True)
    
    try:
        with open(output_path, 'wb') as outfile:
            for i in range(session.total_chunks):
                chunk_path = CHUNK_STORAGE_DIR / session_id / f"chunk_{i:06d}"
                with open(chunk_path, 'rb') as chunk_file:
                    outfile.write(chunk_file.read())
        
        # Verify complete file hash
        file_hash = data.get("file_hash")
        if file_hash:
            with open(output_path, 'rb') as f:
                calculated_hash = hashlib.sha256(f.read()).hexdigest()
                if calculated_hash != file_hash:
                    output_path.unlink()
                    raise HTTPException(status_code=400, detail="Complete file hash mismatch")
        
        # Cleanup chunks directory
        cleanup_session(session_id)
        del UPLOAD_SESSIONS[session_id]
        
        return {
            "status": "completed",
            "session_id": session_id,
            "file_path": str(output_path),
            "file_size": output_path.stat().st_size,
            "download_url": f"/api/files/chunks/completed/{session_id}/{session.filename}"
        }
        
    except Exception as e:
        # Clean up on error
        if output_path.exists():
            output_path.unlink()
        cleanup_session(session_id)
        del UPLOAD_SESSIONS[session_id]
        raise HTTPException(status_code=500, detail=f"Reassembly failed: {str(e)}")


@router.get("/{session_id}/status")
async def get_upload_status(
    session_id: str,
    current_user: dict = Depends(get_current_user)
):
    """Check upload progress"""
    if session_id not in UPLOAD_SESSIONS:
        raise HTTPException(status_code=404, detail="Session not found or expired")
    
    session = UPLOAD_SESSIONS[session_id]
    
    return {
        "session_id": session_id,
        "device_id": session.device_id,
        "filename": session.filename,
        "file_size": session.file_size,
        "total_chunks": session.total_chunks,
        "chunks_received": len(session.chunks_received),
        "progress_percent": int((len(session.chunks_received) / session.total_chunks) * 100),
        "missing_chunks": [i for i in range(session.total_chunks) if i not in session.chunks_received],
        "created_at": session.created_at.isoformat(),
        "last_activity": session.last_activity.isoformat()
    }


@router.get("/completed/{session_id}/{filename}")
async def download_completed_file(
    session_id: str,
    filename: str,
    current_user: dict = Depends(get_current_user)
):
    """Download completed file"""
    from fastapi.responses import FileResponse
    
    file_path = CHUNK_STORAGE_DIR / "completed" / session_id / filename
    
    if not file_path.exists():
        raise HTTPException(status_code=404, detail="File not found")
    
    return FileResponse(
        path=file_path,
        filename=filename,
        media_type="application/octet-stream"
    )
