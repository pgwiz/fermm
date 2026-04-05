import { useEffect, useState, useCallback } from 'react';
import { api } from '../api/client';
import { useAppStore } from '../store/appStore';
import { 
  RefreshCw, 
  XCircle, 
  Search,
  HardDrive,
  Clock,
  AlertCircle,
  LoaderCircle
} from 'lucide-react';

interface Process {
  pid: number;
  name: string;
  memory_mb?: number;
  cpu_seconds?: number;
}

export function ProcessManager() {
  const [processes, setProcesses] = useState<Process[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [loadingStatus, setLoadingStatus] = useState('');
  const [search, setSearch] = useState('');
  const [sortBy, setSortBy] = useState<'name' | 'memory_mb' | 'pid'>('memory_mb');
  const [sortDesc, setSortDesc] = useState(true);
  const selectedDeviceId = useAppStore((s) => s.selectedDeviceId);

  const fetchProcesses = useCallback(async () => {
    if (!selectedDeviceId) return;
    
    setLoading(true);
    setError('');
    setLoadingStatus('Sending command...');

    try {
      // Send the processes command
      await api.sendCommand(selectedDeviceId, 'processes', '');
      setLoadingStatus('Waiting for process data...');
      
      // Poll for results directly
      const maxAttempts = 20;
      let delay = 1000; // Start with 1 second delay
      const commandStartTime = Date.now();
      
      for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        setLoadingStatus(`Attempt ${attempt}/${maxAttempts}: Checking for data...`);
        
        try {
          // Get command history for this device
          const results = await api.getCommandHistory(selectedDeviceId, 20);
          
          // Find the most recent processes result
          const processResult = results.find((r: any) => 
            r.type === 'processes' && 
            r.exit_code === 0 &&
            r.output &&
            r.output.length > 0 &&
            new Date(r.timestamp) > new Date(commandStartTime - 5000) // 5 seconds before command start
          );
          
          if (processResult && processResult.output && processResult.output[0]) {
            try {
              // Parse the JSON string in output[0]
              const processData = JSON.parse(processResult.output[0]);
              if (Array.isArray(processData) && processData.length > 0) {
                setProcesses(processData);
                setLoadingStatus(`✓ Loaded ${processData.length} processes (${processResult.command_id.slice(0, 8)})`);
                return; // Success!
              } else {
                setLoadingStatus(`Attempt ${attempt}: Empty process list received`);
              }
            } catch (parseError) {
              console.error('Failed to parse process data:', parseError);
              setLoadingStatus(`Attempt ${attempt}: Data found but couldn't parse`);
              console.log('Raw process data:', processResult.output[0]); // Debug log
            }
          } else {
            // Count results for debugging
            const allProcessResults = results.filter((r: any) => r.type === 'processes');
            const recentResults = results.filter((r: any) => 
              new Date(r.timestamp) > new Date(commandStartTime - 5000)
            );
            setLoadingStatus(`Attempt ${attempt}: No valid data (${results.length} total, ${allProcessResults.length} process, ${recentResults.length} recent)`);
          }
          
        } catch (fetchError: any) {
          if (fetchError?.message?.includes('404')) {
            setLoadingStatus(`Attempt ${attempt}: Results not ready (404)`);
          } else {
            console.error('Fetch error:', fetchError);
            setLoadingStatus(`Attempt ${attempt}: ${fetchError?.message || 'Unknown error'}`);
          }
        }
        
        // Wait before next attempt (with exponential backoff)
        if (attempt < maxAttempts) {
          await new Promise(resolve => setTimeout(resolve, delay));
          delay = Math.min(delay * 1.3, 3000); // Max 3 second delay, slower backoff
        }
      }
      
      // If we get here, all attempts failed
      setError('Timed out waiting for process data. Check browser console for details.');
      setLoadingStatus('');
      
    } catch (err) {
      console.error('Failed to fetch processes:', err);
      setError(err instanceof Error ? err.message : 'Failed to fetch processes');
      setLoadingStatus('');
    } finally {
      setLoading(false);
      // Clear success status after delay
      setTimeout(() => {
        if (loadingStatus.startsWith('✓')) {
          setLoadingStatus('');
        }
      }, 5000); // Show success longer
    }
  }, [selectedDeviceId]);

  useEffect(() => {
    fetchProcesses();
  }, [fetchProcesses]);

  const killProcess = async (pid: number) => {
    if (!selectedDeviceId) return;
    if (!confirm(`Kill process ${pid}?`)) return;

    try {
      // Detect OS from device info for the correct kill command
      const isWindows = true; // Default to Windows for now
      await api.sendCommand(selectedDeviceId, 'shell', 
        isWindows
          ? `taskkill /PID ${pid} /F` 
          : `kill -9 ${pid}`
      );
      // Refresh after kill
      setTimeout(fetchProcesses, 1000);
    } catch (err) {
      console.error('Failed to kill process:', err);
    }
  };

  const filteredProcesses = processes
    .filter((p) => p.name.toLowerCase().includes(search.toLowerCase()))
    .sort((a, b) => {
      const aVal = a[sortBy];
      const bVal = b[sortBy];
      if (typeof aVal === 'string' && typeof bVal === 'string') {
        return sortDesc ? bVal.localeCompare(aVal) : aVal.localeCompare(bVal);
      }
      // Handle undefined/null numeric values
      const aNum = (aVal as number) || 0;
      const bNum = (bVal as number) || 0;
      return sortDesc ? bNum - aNum : aNum - bNum;
    });

  const totalMemory = processes.reduce((sum, p) => sum + (p.memory_mb || 0), 0);

  if (!selectedDeviceId) {
    return (
      <div className="flex items-center justify-center h-64 text-gray-400">
        <p>Select a device to view processes</p>
      </div>
    );
  }

  return (
    <div className="bg-gray-800 rounded-lg overflow-hidden">
      <div className="p-4 border-b border-gray-700">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-4">
            <h2 className="text-lg font-semibold text-white">Processes</h2>
            <span className="text-sm text-gray-400">
              {processes.length} processes · {totalMemory.toFixed(0)} MB total
            </span>
          </div>
          <button
            onClick={fetchProcesses}
            disabled={loading}
            className="flex items-center gap-2 px-3 py-2 bg-gray-700 hover:bg-gray-600 rounded text-white transition-colors"
          >
            <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
            Refresh
          </button>
        </div>

        <div className="flex gap-4">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-2.5 h-5 w-5 text-gray-400" />
            <input
              type="text"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search processes..."
              className="w-full bg-gray-700 border border-gray-600 rounded pl-10 pr-4 py-2 text-white placeholder-gray-400 focus:outline-none focus:border-blue-500"
            />
          </div>
          <select
            value={sortBy}
            onChange={(e) => setSortBy(e.target.value as typeof sortBy)}
            className="bg-gray-700 border border-gray-600 rounded px-3 py-2 text-white focus:outline-none"
          >
            <option value="memory_mb">Memory</option>
            <option value="name">Name</option>
            <option value="pid">PID</option>
          </select>
          <button
            onClick={() => setSortDesc(!sortDesc)}
            className="px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white"
          >
            {sortDesc ? '↓' : '↑'}
          </button>
        </div>

        {/* Status Display */}
        {(loading || error || loadingStatus) && (
          <div className="px-4 py-2 border-b border-gray-700 bg-gray-750">
            {error && (
              <div className="flex items-center gap-2 text-red-400">
                <AlertCircle className="h-4 w-4" />
                <span className="text-sm">{error}</span>
              </div>
            )}
            {loading && !error && (
              <div className="flex items-center gap-2 text-blue-400">
                <LoaderCircle className="h-4 w-4 animate-spin" />
                <span className="text-sm">{loadingStatus || 'Loading processes...'}</span>
              </div>
            )}
            {!loading && !error && loadingStatus && (
              <div className="text-sm text-green-400">
                {loadingStatus}
              </div>
            )}
          </div>
        )}
      </div>

      <div className="max-h-[600px] overflow-auto">
        <table className="w-full">
          <thead className="bg-gray-700 sticky top-0">
            <tr>
              <th className="text-left px-4 py-3 text-gray-300 font-medium">PID</th>
              <th className="text-left px-4 py-3 text-gray-300 font-medium">Name</th>
              <th className="text-right px-4 py-3 text-gray-300 font-medium">Memory</th>
              <th className="text-right px-4 py-3 text-gray-300 font-medium">CPU</th>
              <th className="text-center px-4 py-3 text-gray-300 font-medium">Action</th>
            </tr>
          </thead>
          <tbody>
            {filteredProcesses.map((proc) => (
              <tr key={proc.pid} className="border-t border-gray-700 hover:bg-gray-700/50">
                <td className="px-4 py-3 text-gray-400 font-mono">{proc.pid}</td>
                <td className="px-4 py-3 text-white">{proc.name}</td>
                <td className="px-4 py-3 text-right text-gray-300">
                  <span className="flex items-center justify-end gap-1">
                    <HardDrive className="h-3 w-3 text-gray-500" />
                    {(proc.memory_mb || 0).toFixed(1)} MB
                  </span>
                </td>
                <td className="px-4 py-3 text-right text-gray-300">
                  <span className="flex items-center justify-end gap-1">
                    <Clock className="h-3 w-3 text-gray-500" />
                    {proc.cpu_seconds?.toFixed(1) || '-'}s
                  </span>
                </td>
                <td className="px-4 py-3 text-center">
                  <button
                    onClick={() => killProcess(proc.pid)}
                    className="p-1 text-red-400 hover:text-red-300 hover:bg-red-500/20 rounded transition-colors"
                    title="Kill process"
                  >
                    <XCircle className="h-5 w-5" />
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
