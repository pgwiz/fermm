from fastapi import WebSocket
from typing import Dict, Set
import asyncio
import json


class ConnectionManager:
    def __init__(self):
        # device_id -> WebSocket
        self.agent_connections: Dict[str, WebSocket] = {}
        # session_id -> WebSocket
        self.dashboard_connections: Dict[str, WebSocket] = {}
        # device_id -> set of session_ids subscribed to it
        self.subscriptions: Dict[str, Set[str]] = {}
        self._lock = asyncio.Lock()

    async def connect_agent(self, device_id: str, websocket: WebSocket):
        await websocket.accept()
        async with self._lock:
            # Close existing connection if any
            if device_id in self.agent_connections:
                try:
                    await self.agent_connections[device_id].close()
                except:
                    pass
            self.agent_connections[device_id] = websocket

    async def disconnect_agent(self, device_id: str):
        async with self._lock:
            self.agent_connections.pop(device_id, None)

    async def connect_dashboard(self, session_id: str, websocket: WebSocket):
        await websocket.accept()
        async with self._lock:
            self.dashboard_connections[session_id] = websocket

    async def disconnect_dashboard(self, session_id: str):
        async with self._lock:
            self.dashboard_connections.pop(session_id, None)
            # Remove subscriptions
            for device_id in list(self.subscriptions.keys()):
                self.subscriptions[device_id].discard(session_id)
                if not self.subscriptions[device_id]:
                    del self.subscriptions[device_id]

    async def subscribe_to_device(self, session_id: str, device_id: str):
        async with self._lock:
            if device_id not in self.subscriptions:
                self.subscriptions[device_id] = set()
            self.subscriptions[device_id].add(session_id)

    async def send_to_agent(self, device_id: str, message: dict) -> bool:
        """Send command to agent. Returns True if sent, False if agent not connected."""
        ws = self.agent_connections.get(device_id)
        if ws:
            try:
                await ws.send_json(message)
                return True
            except:
                await self.disconnect_agent(device_id)
        return False

    async def broadcast_to_dashboards(self, device_id: str, message: dict):
        """Send agent output to all dashboards subscribed to this device."""
        subscribers = self.subscriptions.get(device_id, set())
        for session_id in list(subscribers):
            ws = self.dashboard_connections.get(session_id)
            if ws:
                try:
                    await ws.send_json(message)
                except:
                    await self.disconnect_dashboard(session_id)

    def is_agent_online(self, device_id: str) -> bool:
        return device_id in self.agent_connections

    def is_connected(self, device_id: str) -> bool:
        """Alias for is_agent_online for clarity"""
        return device_id in self.agent_connections

    async def send_command(self, device_id: str, command: dict) -> bool:
        """Send a command to agent. Returns True if sent, False if agent not connected."""
        return await self.send_to_agent(device_id, command)

    def get_online_devices(self) -> list:
        return list(self.agent_connections.keys())


# Global connection manager
manager = ConnectionManager()
