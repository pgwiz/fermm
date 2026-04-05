import { BrowserRouter, Routes, Route, Navigate, Outlet, NavLink } from 'react-router-dom';
import { useAppStore } from './store/appStore';
import { LoginPage } from './pages/LoginPage';
import { DeviceGrid } from './pages/DeviceGrid';
import { Terminal } from './pages/Terminal';
import { ProcessManager } from './pages/ProcessManager';
import { FileBrowser } from './pages/FileBrowser';
import { ScreenshotExplorer } from './pages/ScreenshotExplorer';
import { KeyloggerPage } from './pages/KeyloggerPage';
import GodModeManager from './pages/GodModeManager';
import { ScriptManager } from './pages/ScriptManager';
import { OverlayPanel } from './pages/OverlayPanel';
import {
  Monitor,
  TerminalSquare,
  Cpu,
  FolderOpen,
  Camera,
  Keyboard,
  Shield,
  Code,
  LogOut,
  Menu,
  Layers,
} from 'lucide-react';
import { useState } from 'react';

function ProtectedRoute() {
  const isAuthenticated = useAppStore((s) => s.isAuthenticated);
  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />;
}

function Layout() {
  const logout = useAppStore((s) => s.logout);
  const selectedDeviceId = useAppStore((s) => s.selectedDeviceId);
  const devices = useAppStore((s) => s.devices);
  const [sidebarOpen, setSidebarOpen] = useState(true);

  const selectedDevice = devices.find((d) => d.id === selectedDeviceId);

  const navItems = [
    { to: '/', icon: Monitor, label: 'Devices' },
    { to: '/terminal', icon: TerminalSquare, label: 'Terminal' },
    { to: '/processes', icon: Cpu, label: 'Processes' },
    { to: '/files', icon: FolderOpen, label: 'Files' },
    { to: '/screenshot', icon: Camera, label: 'Screenshot' },
    { to: '/keylogger', icon: Keyboard, label: 'Keylogger' },
    { to: '/scripts', icon: Code, label: 'Scripts' },
    { to: '/overlay', icon: Layers, label: 'Overlay' },
    { to: '/godmode', icon: Shield, label: 'GOD Mode' },
  ];

  return (
    <div className="flex h-screen bg-gray-900 text-white">
      {/* Sidebar */}
      <aside className={`${sidebarOpen ? 'w-64' : 'w-16'} bg-gray-800 border-r border-gray-700 flex flex-col transition-all duration-300`}>
        {/* Header */}
        <div className="p-4 border-b border-gray-700 flex items-center justify-between">
          {sidebarOpen && <h1 className="text-xl font-bold text-blue-400">FERMM</h1>}
          <button
            onClick={() => setSidebarOpen(!sidebarOpen)}
            className="p-2 hover:bg-gray-700 rounded"
          >
            <Menu className="h-5 w-5" />
          </button>
        </div>

        {/* Selected device indicator */}
        {selectedDevice && sidebarOpen && (
          <div className="p-3 mx-3 mt-3 bg-blue-600/20 border border-blue-500/30 rounded-lg">
            <div className="text-xs text-blue-400 uppercase font-semibold">Active Device</div>
            <div className="text-sm text-white truncate">{selectedDevice.hostname}</div>
          </div>
        )}

        {/* Navigation */}
        <nav className="flex-1 p-3 space-y-1">
          {navItems.map(({ to, icon: Icon, label }) => (
            <NavLink
              key={to}
              to={to}
              end={to === '/'}
              className={({ isActive }) =>
                `flex items-center gap-3 px-3 py-2 rounded-lg transition-colors ${
                  isActive
                    ? 'bg-blue-600 text-white'
                    : 'text-gray-400 hover:bg-gray-700 hover:text-white'
                }`
              }
            >
              <Icon className="h-5 w-5 flex-shrink-0" />
              {sidebarOpen && <span>{label}</span>}
            </NavLink>
          ))}
        </nav>

        {/* Logout */}
        <div className="p-3 border-t border-gray-700">
          <button
            onClick={logout}
            className="flex items-center gap-3 px-3 py-2 w-full text-gray-400 hover:text-white hover:bg-gray-700 rounded-lg transition-colors"
          >
            <LogOut className="h-5 w-5" />
            {sidebarOpen && <span>Logout</span>}
          </button>
        </div>
      </aside>

      {/* Main content */}
      <main className="flex-1 overflow-auto p-6">
        <Outlet />
      </main>
    </div>
  );
}

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/login" element={<LoginPage />} />
        <Route element={<ProtectedRoute />}>
          <Route element={<Layout />}>
            <Route path="/" element={<DeviceGrid />} />
            <Route path="/terminal" element={<Terminal />} />
            <Route path="/processes" element={<ProcessManager />} />
            <Route path="/files" element={<FileBrowser />} />
            <Route path="/screenshot" element={<ScreenshotExplorer />} />
            <Route path="/keylogger" element={<KeyloggerPage />} />
            <Route path="/scripts" element={<ScriptManager />} />
            <Route path="/overlay" element={<OverlayPanel />} />
            <Route path="/godmode" element={<GodModeManager />} />
          </Route>
        </Route>
      </Routes>
    </BrowserRouter>
  );
}

export default App;
