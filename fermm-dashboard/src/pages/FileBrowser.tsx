import { useState, useEffect, useCallback } from 'react';
import { api } from '../api/client';
import { useAppStore } from '../store/appStore';
import { pollForFileList } from '../utils/smartPoller';
import {
  Folder,
  File,
  ChevronRight,
  Home,
  RefreshCw,
  ArrowUp,
  AlertCircle,
  LoaderCircle,
} from 'lucide-react';

interface FileEntry {
  name: string;
  is_dir: boolean;
  size: number;
  modified: string;
}

export function FileBrowser() {
  const [currentPath, setCurrentPath] = useState('C:\\');
  const [entries, setEntries] = useState<FileEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [loadingStatus, setLoadingStatus] = useState('');
  const selectedDeviceId = useAppStore((s) => s.selectedDeviceId);

  const fetchDirectory = useCallback(async (path: string) => {
    if (!selectedDeviceId) return;
    setLoading(true);
    setError('');
    setLoadingStatus('Listing directory...');

    try {
      const response = await api.sendCommand(selectedDeviceId, 'file', JSON.stringify({
        action: 'ls',
        path: path,
      }));

      const result = await pollForFileList(response.command_id, (attempt, error) => {
        if (error?.includes('404')) {
          setLoadingStatus(`Attempt ${attempt}: Waiting for data...`);
        } else if (error) {
          setLoadingStatus(`Attempt ${attempt}: ${error}`);
        } else {
          setLoadingStatus(`Attempt ${attempt}: Reading directory...`);
        }
      });

      if (result.success) {
        setEntries(Array.isArray(result.data) ? result.data : []);
        setCurrentPath(path);
        setLoadingStatus(`✓ Found ${Array.isArray(result.data) ? result.data.length : 0} items`);
      } else {
        setError(result.error || 'Failed to list directory');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to list directory');
    } finally {
      setLoading(false);
      // Clear status after delay
      setTimeout(() => {
        if (loadingStatus.startsWith('✓')) {
          setLoadingStatus('');
        }
      }, 3000);
    }
  }, [selectedDeviceId]);

  useEffect(() => {
    if (selectedDeviceId) {
      fetchDirectory(currentPath);
    }
  }, [selectedDeviceId]);

  const navigateTo = (path: string) => {
    fetchDirectory(path);
  };

  const goUp = () => {
    const parts = currentPath.split(/[/\\]/).filter(Boolean);
    if (parts.length > 1) {
      parts.pop();
      const newPath = parts.join('\\') + '\\';
      fetchDirectory(newPath);
    } else if (parts.length === 1) {
      fetchDirectory(parts[0] + '\\');
    }
  };

  const handleEntryClick = (entry: FileEntry) => {
    if (entry.is_dir) {
      const newPath = currentPath.endsWith('\\') || currentPath.endsWith('/')
        ? currentPath + entry.name
        : currentPath + '\\' + entry.name;
      navigateTo(newPath);
    }
  };

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return bytes + ' B';
    if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + ' KB';
    if (bytes < 1024 * 1024 * 1024) return (bytes / 1024 / 1024).toFixed(1) + ' MB';
    return (bytes / 1024 / 1024 / 1024).toFixed(1) + ' GB';
  };

  const formatDate = (dateStr: string) => {
    return new Date(dateStr).toLocaleString();
  };

  const pathParts = currentPath.split(/[/\\]/).filter(Boolean);

  if (!selectedDeviceId) {
    return (
      <div className="flex items-center justify-center h-64 text-gray-400">
        <p>Select a device to browse files</p>
      </div>
    );
  }

  return (
    <div className="bg-gray-800 rounded-lg overflow-hidden">
      {/* Toolbar */}
      <div className="p-4 border-b border-gray-700">
        <div className="flex items-center gap-2 mb-4">
          <button
            onClick={goUp}
            className="p-2 bg-gray-700 hover:bg-gray-600 rounded text-white transition-colors"
            title="Go up"
          >
            <ArrowUp className="h-4 w-4" />
          </button>
          <button
            onClick={() => fetchDirectory(currentPath)}
            disabled={loading}
            className="p-2 bg-gray-700 hover:bg-gray-600 rounded text-white transition-colors"
            title="Refresh"
          >
            <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
          </button>
          <button
            onClick={() => navigateTo('C:\\')}
            className="p-2 bg-gray-700 hover:bg-gray-600 rounded text-white transition-colors"
            title="Home"
          >
            <Home className="h-4 w-4" />
          </button>
        </div>

        {/* Breadcrumb */}
        <div className="flex items-center gap-1 text-sm overflow-x-auto pb-2">
          {pathParts.map((part, index) => (
            <div key={index} className="flex items-center">
              {index > 0 && <ChevronRight className="h-4 w-4 text-gray-500" />}
              <button
                onClick={() => navigateTo(pathParts.slice(0, index + 1).join('\\') + '\\')}
                className="px-2 py-1 hover:bg-gray-700 rounded text-blue-400 hover:text-blue-300"
              >
                {part}
              </button>
            </div>
          ))}
        </div>
      </div>

      {/* Status Display */}
      {(loading || loadingStatus) && !error && (
        <div className="px-4 py-2 border-b border-gray-700 bg-gray-750">
          {loading && (
            <div className="flex items-center gap-2 text-blue-400">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              <span className="text-sm">{loadingStatus || 'Loading directory...'}</span>
            </div>
          )}
          {!loading && loadingStatus && (
            <div className="text-sm text-green-400">
              {loadingStatus}
            </div>
          )}
        </div>
      )}

      {/* Error */}
      {error && (
        <div className="px-4 py-3 border-b border-gray-700 bg-red-500/10">
          <div className="flex items-center gap-2 text-red-400">
            <AlertCircle className="h-4 w-4" />
            <span className="text-sm">{error}</span>
          </div>
        </div>
      )}

      {/* Old Error (to replace) */}
      {false && error && (
        <div className="mx-4 mt-4 p-3 bg-red-500/20 border border-red-500 rounded text-red-300">
          {error}
        </div>
      )}

      {/* File list */}
      <div className="max-h-[500px] overflow-auto">
        <table className="w-full">
          <thead className="bg-gray-700 sticky top-0">
            <tr>
              <th className="text-left px-4 py-3 text-gray-300 font-medium">Name</th>
              <th className="text-right px-4 py-3 text-gray-300 font-medium">Size</th>
              <th className="text-right px-4 py-3 text-gray-300 font-medium">Modified</th>
            </tr>
          </thead>
          <tbody>
            {entries
              .sort((a, b) => {
                // Directories first
                if (a.is_dir && !b.is_dir) return -1;
                if (!a.is_dir && b.is_dir) return 1;
                return a.name.localeCompare(b.name);
              })
              .map((entry) => (
                <tr
                  key={entry.name}
                  onClick={() => handleEntryClick(entry)}
                  className={`border-t border-gray-700 hover:bg-gray-700/50 ${
                    entry.is_dir ? 'cursor-pointer' : ''
                  }`}
                >
                  <td className="px-4 py-3 flex items-center gap-2">
                    {entry.is_dir ? (
                      <Folder className="h-5 w-5 text-yellow-400" />
                    ) : (
                      <File className="h-5 w-5 text-gray-400" />
                    )}
                    <span className={entry.is_dir ? 'text-blue-400' : 'text-white'}>
                      {entry.name}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right text-gray-400">
                    {entry.is_dir ? '-' : formatSize(entry.size)}
                  </td>
                  <td className="px-4 py-3 text-right text-gray-400">
                    {formatDate(entry.modified)}
                  </td>
                </tr>
              ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
