import React, { useEffect, useState } from 'react';
import { api } from '../api/client';
import { useAppStore } from '../store/appStore';
import { pollCommandResult } from '../utils/commandPoller';

interface Script {
  id: string;
  name: string;
  description: string;
  script_type: 'cmd' | 'powershell' | 'bash' | 'sh';
  content: string;
  created_by: string;
  created_at: string;
  updated_at: string;
}

interface ScriptExecution {
  id: string;
  script_id: string;
  device_id: string;
  command_id: string;
  status: string;
  exit_code: number | null;
  log_file: string;
  created_at: string;
}

export function ScriptManager() {
  const [scripts, setScripts] = useState<Script[]>([]);
  const [executions, setExecutions] = useState<ScriptExecution[]>([]);
  const [loading, setLoading] = useState(false);
  const [showUploadForm, setShowUploadForm] = useState(false);
  const [showExecuteForm, setShowExecuteForm] = useState(false);
  const [selectedScript, setSelectedScript] = useState<Script | null>(null);
  const [selectedExecution, setSelectedExecution] = useState<ScriptExecution | null>(null);
  const [showLog, setShowLog] = useState(false);
  const [logContent, setLogContent] = useState('');

  const [formData, setFormData] = useState({
    name: '',
    description: '',
    script_type: 'powershell' as 'cmd' | 'powershell' | 'bash' | 'sh',
    content: ''
  });

  const { selectedDeviceId, devices } = useAppStore();
  const selectedDevice = devices.find(d => d.id === selectedDeviceId);

  useEffect(() => {
    loadScripts();
  }, []);

  const loadScripts = async () => {
    try {
      setLoading(true);
      const data = await api.get<Script[]>('/api/scripts');
      setScripts(data);
    } catch (err) {
      console.error('Failed to load scripts:', err);
    } finally {
      setLoading(false);
    }
  };

  const loadExecutions = async (scriptId?: string) => {
    try {
      const url = scriptId ? `/api/scripts/${scriptId}/executions` : '/api/scripts/executions';
      const data = await api.get<ScriptExecution[]>(url);
      setExecutions(data);
    } catch (err) {
      console.error('Failed to load executions:', err);
    }
  };

  const handleUpload = async (e: React.FormEvent) => {
    e.preventDefault();
    try {
      setLoading(true);
      await api.post('/api/scripts', {
        name: formData.name,
        description: formData.description,
        script_type: formData.script_type,
        content: formData.content,
        created_by: localStorage.getItem('username') || 'admin'
      });

      setFormData({ name: '', description: '', script_type: 'powershell', content: '' });
      setShowUploadForm(false);
      await loadScripts();
    } catch (err) {
      alert('Failed to upload script: ' + (err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  const handleExecute = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!selectedScript || !selectedDevice) {
      alert('Please select a script and ensure a device is selected');
      return;
    }

    try {
      setLoading(true);
      const result = await api.sendCommand(selectedDevice.id, 'script', JSON.stringify({
        action: 'execute',
        script_id: selectedScript.id
      }));

      if (result.command_id) {
        // Poll for execution result
        try {
          await pollCommandResult({
            commandId: result.command_id,
            maxAttempts: 20,
            delayMs: 500
          });
          await loadExecutions(selectedScript.id);
          alert('Script executed successfully!');
        } catch (pollErr) {
          console.warn('Execution timed out or failed:', pollErr);
          alert('Script sent for execution. Check execution history for results.');
        }
      }
      setShowExecuteForm(false);
    } catch (err) {
      alert('Failed to execute script: ' + (err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  const handleViewLog = async (execution: ScriptExecution) => {
    try {
      const response = await fetch(`/api/scripts/${execution.script_id}/executions/${execution.id}/log`);
      const content = await response.text();
      setLogContent(content);
      setSelectedExecution(execution);
      setShowLog(true);
    } catch (err) {
      alert('Failed to load log: ' + (err as Error).message);
    }
  };

  const handleDeleteScript = async (scriptId: string) => {
    if (!confirm('Delete this script? This cannot be undone.')) return;

    try {
      setLoading(true);
      await api.delete(`/api/scripts/${scriptId}`);
      await loadScripts();
    } catch (err) {
      alert('Failed to delete script: ' + (err as Error).message);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-br from-slate-900 to-slate-800 p-8">
      <div className="max-w-7xl mx-auto">
        <div className="flex justify-between items-center mb-8">
          <h1 className="text-4xl font-bold text-white">Script Manager</h1>
          <button
            onClick={() => setShowUploadForm(true)}
            className="bg-blue-600 hover:bg-blue-700 text-white px-6 py-2 rounded-lg transition"
          >
            + Upload Script
          </button>
        </div>

        <div className="grid grid-cols-1 lg:grid-cols-3 gap-8">
          {/* Scripts List */}
          <div className="lg:col-span-2">
            <div className="bg-slate-800 rounded-lg shadow-lg p-6">
              <h2 className="text-2xl font-semibold text-white mb-4">Available Scripts</h2>
              
              {loading && scripts.length === 0 ? (
                <div className="text-center py-8">
                  <div className="inline-block animate-spin">⚙️</div>
                  <p className="text-gray-400 mt-2">Loading scripts...</p>
                </div>
              ) : scripts.length === 0 ? (
                <p className="text-gray-400 text-center py-8">No scripts yet. Upload one to get started!</p>
              ) : (
                <div className="space-y-4">
                  {scripts.map(script => (
                    <div
                      key={script.id}
                      className="bg-slate-700 rounded-lg p-4 hover:bg-slate-600 transition cursor-pointer"
                      onClick={() => setSelectedScript(script)}
                    >
                      <div className="flex justify-between items-start">
                        <div className="flex-1">
                          <h3 className="text-lg font-semibold text-white">{script.name}</h3>
                          <p className="text-gray-400 text-sm">{script.description}</p>
                          <div className="flex gap-4 mt-2 text-xs text-gray-500">
                            <span className={`px-2 py-1 rounded ${
                              script.script_type === 'powershell' ? 'bg-blue-900' :
                              script.script_type === 'bash' || script.script_type === 'sh' ? 'bg-green-900' :
                              'bg-gray-900'
                            } text-gray-200`}>
                              {script.script_type}
                            </span>
                            <span>By {script.created_by}</span>
                            <span>{new Date(script.created_at).toLocaleDateString()}</span>
                          </div>
                        </div>
                        <div className="flex gap-2">
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              setSelectedScript(script);
                              setShowExecuteForm(true);
                            }}
                            disabled={!selectedDevice}
                            className="bg-green-600 hover:bg-green-700 disabled:bg-gray-600 text-white px-3 py-1 rounded text-sm transition"
                          >
                            Execute
                          </button>
                          <button
                            onClick={(e) => {
                              e.stopPropagation();
                              handleDeleteScript(script.id);
                            }}
                            className="bg-red-600 hover:bg-red-700 text-white px-3 py-1 rounded text-sm transition"
                          >
                            Delete
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
                </div>
              )}
            </div>
          </div>

          {/* Details Panel */}
          <div className="bg-slate-800 rounded-lg shadow-lg p-6 h-fit">
            <h2 className="text-2xl font-semibold text-white mb-4">Details</h2>
            
            {selectedScript ? (
              <div className="space-y-4">
                <div>
                  <p className="text-gray-400 text-sm">Name</p>
                  <p className="text-white font-semibold">{selectedScript.name}</p>
                </div>
                <div>
                  <p className="text-gray-400 text-sm">Type</p>
                  <p className="text-white font-semibold uppercase">{selectedScript.script_type}</p>
                </div>
                <div>
                  <p className="text-gray-400 text-sm">Content Preview</p>
                  <pre className="bg-slate-900 text-gray-300 p-2 rounded text-xs overflow-auto max-h-32">
                    {selectedScript.content.substring(0, 200)}
                    {selectedScript.content.length > 200 ? '...' : ''}
                  </pre>
                </div>
                <button
                  onClick={() => {
                    setSelectedScript(selectedScript);
                    loadExecutions(selectedScript.id);
                  }}
                  className="w-full bg-purple-600 hover:bg-purple-700 text-white py-2 rounded transition"
                >
                  View Executions
                </button>
              </div>
            ) : (
              <p className="text-gray-400 text-center py-8">Select a script to view details</p>
            )}
          </div>
        </div>

        {/* Execution History */}
        {selectedScript && (
          <div className="mt-8 bg-slate-800 rounded-lg shadow-lg p-6">
            <h2 className="text-2xl font-semibold text-white mb-4">Execution History: {selectedScript.name}</h2>
            
            {executions.length === 0 ? (
              <p className="text-gray-400 text-center py-4">No executions yet</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-sm">
                  <thead>
                    <tr className="border-b border-slate-700">
                      <th className="text-left text-gray-400 py-2 px-4">Device ID</th>
                      <th className="text-left text-gray-400 py-2 px-4">Status</th>
                      <th className="text-left text-gray-400 py-2 px-4">Exit Code</th>
                      <th className="text-left text-gray-400 py-2 px-4">Date</th>
                      <th className="text-left text-gray-400 py-2 px-4">Log</th>
                    </tr>
                  </thead>
                  <tbody>
                    {executions.map(exec => (
                      <tr key={exec.id} className="border-b border-slate-700 hover:bg-slate-700">
                        <td className="text-white py-2 px-4 font-mono text-xs">{exec.device_id.substring(0, 8)}</td>
                        <td className="text-white py-2 px-4">
                          <span className={`px-2 py-1 rounded text-xs ${
                            exec.status === 'completed' ? 'bg-green-900 text-green-200' :
                            exec.status === 'running' ? 'bg-blue-900 text-blue-200' :
                            'bg-red-900 text-red-200'
                          }`}>
                            {exec.status}
                          </span>
                        </td>
                        <td className="text-white py-2 px-4">
                          {exec.exit_code !== null ? exec.exit_code : '-'}
                        </td>
                        <td className="text-gray-400 py-2 px-4 text-xs">
                          {new Date(exec.created_at).toLocaleString()}
                        </td>
                        <td className="text-white py-2 px-4">
                          {exec.log_file && (
                            <button
                              onClick={() => handleViewLog(exec)}
                              className="text-blue-400 hover:text-blue-300 underline"
                            >
                              View
                            </button>
                          )}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </div>
        )}

        {/* Upload Form Modal */}
        {showUploadForm && (
          <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50">
            <div className="bg-slate-800 rounded-lg max-w-2xl w-full p-6">
              <h2 className="text-2xl font-semibold text-white mb-4">Upload New Script</h2>
              <form onSubmit={handleUpload} className="space-y-4">
                <div>
                  <label className="block text-gray-400 text-sm mb-1">Script Name</label>
                  <input
                    type="text"
                    value={formData.name}
                    onChange={(e) => setFormData({ ...formData, name: e.target.value })}
                    className="w-full bg-slate-700 border border-slate-600 text-white px-3 py-2 rounded"
                    placeholder="e.g., System Cleanup"
                    required
                  />
                </div>
                <div>
                  <label className="block text-gray-400 text-sm mb-1">Description</label>
                  <input
                    type="text"
                    value={formData.description}
                    onChange={(e) => setFormData({ ...formData, description: e.target.value })}
                    className="w-full bg-slate-700 border border-slate-600 text-white px-3 py-2 rounded"
                    placeholder="What does this script do?"
                  />
                </div>
                <div>
                  <label className="block text-gray-400 text-sm mb-1">Script Type</label>
                  <select
                    value={formData.script_type}
                    onChange={(e) => setFormData({ ...formData, script_type: e.target.value as any })}
                    className="w-full bg-slate-700 border border-slate-600 text-white px-3 py-2 rounded"
                  >
                    <option value="powershell">PowerShell</option>
                    <option value="cmd">CMD</option>
                    <option value="bash">Bash</option>
                    <option value="sh">Shell</option>
                  </select>
                </div>
                <div>
                  <label className="block text-gray-400 text-sm mb-1">Script Content</label>
                  <textarea
                    value={formData.content}
                    onChange={(e) => setFormData({ ...formData, content: e.target.value })}
                    className="w-full bg-slate-700 border border-slate-600 text-white px-3 py-2 rounded font-mono text-sm h-48"
                    placeholder="Paste your script here..."
                    required
                  />
                </div>
                <div className="flex gap-4 justify-end">
                  <button
                    type="button"
                    onClick={() => setShowUploadForm(false)}
                    className="bg-gray-600 hover:bg-gray-700 text-white px-4 py-2 rounded transition"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={loading}
                    className="bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 text-white px-4 py-2 rounded transition"
                  >
                    {loading ? 'Uploading...' : 'Upload'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}

        {/* Execute Form Modal */}
        {showExecuteForm && selectedScript && (
          <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50">
            <div className="bg-slate-800 rounded-lg max-w-md w-full p-6">
              <h2 className="text-2xl font-semibold text-white mb-4">Execute Script</h2>
              <form onSubmit={handleExecute} className="space-y-4">
                <div>
                  <p className="text-gray-400 text-sm mb-2">Script</p>
                  <p className="text-white font-semibold">{selectedScript.name}</p>
                </div>
                <div>
                  <p className="text-gray-400 text-sm mb-2">Device</p>
                  <p className="text-white font-semibold">{selectedDevice?.id}</p>
                </div>
                <div className="bg-yellow-900 border border-yellow-700 rounded p-3 mb-4">
                  <p className="text-yellow-200 text-sm">
                    ⚠️ This will execute the script on the selected device. Make sure you understand what it does.
                  </p>
                </div>
                <div className="flex gap-4 justify-end">
                  <button
                    type="button"
                    onClick={() => setShowExecuteForm(false)}
                    className="bg-gray-600 hover:bg-gray-700 text-white px-4 py-2 rounded transition"
                  >
                    Cancel
                  </button>
                  <button
                    type="submit"
                    disabled={loading}
                    className="bg-green-600 hover:bg-green-700 disabled:bg-gray-600 text-white px-4 py-2 rounded transition"
                  >
                    {loading ? 'Executing...' : 'Execute'}
                  </button>
                </div>
              </form>
            </div>
          </div>
        )}

        {/* Log Viewer Modal */}
        {showLog && selectedExecution && (
          <div className="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center p-4 z-50">
            <div className="bg-slate-800 rounded-lg max-w-2xl w-full max-h-96 flex flex-col p-6">
              <h2 className="text-2xl font-semibold text-white mb-4">Execution Log</h2>
              <pre className="flex-1 bg-slate-900 text-gray-300 p-4 rounded text-sm overflow-auto">
                {logContent}
              </pre>
              <div className="mt-4 flex justify-end">
                <button
                  onClick={() => setShowLog(false)}
                  className="bg-gray-600 hover:bg-gray-700 text-white px-4 py-2 rounded transition"
                >
                  Close
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
