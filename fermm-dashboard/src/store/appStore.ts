import { create } from 'zustand';
import { persist } from 'zustand/middleware';
import { api, type Device } from '../api/client';

interface AppState {
  // Auth
  token: string | null;
  isAuthenticated: boolean;
  login: (username: string, password: string) => Promise<void>;
  logout: () => void;
  initializeAuth: () => void;

  // Devices
  devices: Device[];
  selectedDeviceId: string | null;
  selectedDevice: Device | null;
  fetchDevices: () => Promise<void>;
  selectDevice: (id: string | null) => void;

  // WebSocket
  wsConnected: boolean;
  setWsConnected: (connected: boolean) => void;
}

export const useAppStore = create<AppState>()(
  persist(
    (set, get) => ({
      // Auth
      token: null,
      isAuthenticated: false,

      login: async (username: string, password: string) => {
        const token = await api.login(username, password);
        api.setToken(token);
        set({ token, isAuthenticated: true });
      },

      logout: () => {
        api.setToken(null);
        set({ token: null, isAuthenticated: false, devices: [], selectedDeviceId: null });
      },

      initializeAuth: () => {
        const state = get();
        if (state.token) {
          api.setToken(state.token);
          set({ isAuthenticated: true });
        }
      },

      // Devices
      devices: [],
      selectedDeviceId: null,

      get selectedDevice() {
        const state = get();
        return state.devices.find(d => d.id === state.selectedDeviceId) || null;
      },

      fetchDevices: async () => {
        const devices = await api.getDevices();
        set({ devices });
      },

      selectDevice: (id: string | null) => {
        set({ selectedDeviceId: id });
      },

      // WebSocket
      wsConnected: false,
      setWsConnected: (connected: boolean) => set({ wsConnected: connected }),
    }),
    {
      name: 'fermm-storage',
      partialize: (state) => ({ token: state.token, isAuthenticated: state.isAuthenticated }),
      onRehydrateStorage: () => (state) => {
        // Initialize auth after store rehydration
        state?.initializeAuth();
      },
    }
  )
);
