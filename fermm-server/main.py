from contextlib import asynccontextmanager
from fastapi import FastAPI
from fastapi.middleware.cors import CORSMiddleware
from fastapi.staticfiles import StaticFiles
from slowapi import Limiter
from slowapi.util import get_remote_address
from slowapi.middleware import SlowAPIMiddleware
import os

from config import get_settings
from database import init_db
from routers import auth, devices, commands, files, ws, keylogs, tasks, scripts, chunks, overlay

settings = get_settings()
limiter = Limiter(key_func=get_remote_address)


@asynccontextmanager
async def lifespan(app: FastAPI):
    # Startup
    await init_db()
    yield
    # Shutdown


app = FastAPI(
    title="FERMM Server",
    description="Fast Execution Remote Management Module - Broker Server",
    version="1.0.0",
    lifespan=lifespan
)

# Rate limiting
app.state.limiter = limiter
app.add_middleware(SlowAPIMiddleware)

# CORS
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # Configure in production
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Include routers
app.include_router(auth.router)
app.include_router(devices.router)
app.include_router(commands.router)
app.include_router(files.router)
app.include_router(scripts.router)
app.include_router(chunks.router)
app.include_router(ws.router)
app.include_router(keylogs.router)
app.include_router(tasks.router)
app.include_router(overlay.router)


# Health check
@app.get("/health")
async def health():
    return {"status": "ok", "service": "fermm-server"}


# Serve screenshots directory
screenshots_path = os.path.join(os.path.dirname(__file__), "static")
os.makedirs(screenshots_path, exist_ok=True)
app.mount("/static", StaticFiles(directory=screenshots_path), name="static")

# Serve dashboard static files (if exists)
dashboard_path = os.path.join(os.path.dirname(__file__), "..", "fermm-dashboard", "dist")
if os.path.exists(dashboard_path):
    app.mount("/", StaticFiles(directory=dashboard_path, html=True), name="dashboard")


if __name__ == "__main__":
    import uvicorn
    uvicorn.run(
        "main:app",
        host=settings.host,
        port=settings.port,
        reload=True
    )
