from fastapi import APIRouter, HTTPException, status
from models.schemas import TokenRequest, TokenResponse
from auth import create_access_token, verify_password
from config import get_settings

router = APIRouter(prefix="/api/auth", tags=["auth"])
settings = get_settings()


@router.post("/token", response_model=TokenResponse)
async def login(request: TokenRequest):
    """Issue JWT for dashboard users"""
    if request.username != settings.admin_username or request.password != settings.admin_password:
        raise HTTPException(
            status_code=status.HTTP_401_UNAUTHORIZED,
            detail="Invalid credentials"
        )
    
    token = create_access_token({"sub": request.username, "type": "user"})
    return TokenResponse(access_token=token)


@router.post("/refresh", response_model=TokenResponse)
async def refresh_token(current_user: dict = None):
    """Refresh an existing JWT"""
    # In a real app, you'd validate the refresh token
    token = create_access_token({"sub": current_user.get("sub", "admin"), "type": "user"})
    return TokenResponse(access_token=token)
