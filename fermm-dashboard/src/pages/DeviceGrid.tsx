import { useEffect } from 'react';
import { useAppStore } from '../store/appStore';
import { 
  Monitor, 
  Wifi, 
  WifiOff, 
  Clock,
  Cpu,
  HardDrive
} from 'lucide-react';

interface DeviceCardProps {
  device: {
    id: string;
    hostname: string;
    os: string;
    arch: string | null;
    ip: string | null;
    online: boolean;
    last_seen: string | null;
  };
  selected: boolean;
  onClick: () => void;
}

function DeviceCard({ device, selected, onClick }: DeviceCardProps) {
  const lastSeen = device.last_seen 
    ? new Date(device.last_seen).toLocaleString() 
    : 'Never';

  const osIcon = device.os.toLowerCase().includes('windows') 
    ? '🪟' 
    : device.os.toLowerCase().includes('linux') 
    ? '🐧' 
    : '💻';

  return (
    <div
      onClick={onClick}
      className={`p-4 rounded-lg cursor-pointer transition-all ${
        selected 
          ? 'bg-blue-600 ring-2 ring-blue-400' 
          : 'bg-gray-800 hover:bg-gray-700'
      }`}
    >
      <div className="flex items-start justify-between mb-3">
        <div className="flex items-center gap-2">
          <span className="text-2xl">{osIcon}</span>
          <div>
            <h3 className="font-semibold text-white">{device.hostname}</h3>
            <p className="text-sm text-gray-400">{device.id.slice(0, 8)}...</p>
          </div>
        </div>
        <div className={`flex items-center gap-1 px-2 py-1 rounded-full text-xs ${
          device.online 
            ? 'bg-green-500/20 text-green-400' 
            : 'bg-red-500/20 text-red-400'
        }`}>
          {device.online ? <Wifi className="h-3 w-3" /> : <WifiOff className="h-3 w-3" />}
          {device.online ? 'Online' : 'Offline'}
        </div>
      </div>

      <div className="space-y-2 text-sm text-gray-300">
        <div className="flex items-center gap-2">
          <Monitor className="h-4 w-4 text-gray-500" />
          <span className="truncate">{device.os}</span>
        </div>
        {device.arch && (
          <div className="flex items-center gap-2">
            <Cpu className="h-4 w-4 text-gray-500" />
            <span>{device.arch}</span>
          </div>
        )}
        {device.ip && (
          <div className="flex items-center gap-2">
            <HardDrive className="h-4 w-4 text-gray-500" />
            <span>{device.ip}</span>
          </div>
        )}
        <div className="flex items-center gap-2">
          <Clock className="h-4 w-4 text-gray-500" />
          <span>Last seen: {lastSeen}</span>
        </div>
      </div>
    </div>
  );
}

export function DeviceGrid() {
  const { devices, selectedDeviceId, selectDevice, fetchDevices } = useAppStore();

  useEffect(() => {
    fetchDevices();
    const interval = setInterval(fetchDevices, 5000);
    return () => clearInterval(interval);
  }, [fetchDevices]);

  if (devices.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center h-64 text-gray-400">
        <Monitor className="h-16 w-16 mb-4 opacity-50" />
        <p className="text-lg">No devices registered</p>
        <p className="text-sm mt-2">Run the FERMM agent on a device to get started</p>
      </div>
    );
  }

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
      {devices.map((device) => (
        <DeviceCard
          key={device.id}
          device={device}
          selected={device.id === selectedDeviceId}
          onClick={() => selectDevice(device.id)}
        />
      ))}
    </div>
  );
}
