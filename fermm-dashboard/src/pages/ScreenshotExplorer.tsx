import { useState, useCallback, useEffect } from 'react';
import { api } from '../api/client';
import { useAppStore } from '../store/appStore';
import { pollCommandWithStatus } from '../utils/commandPoller';
import { 
  Camera, RefreshCw, Download, Maximize2, AlertCircle, LoaderCircle, 
  Search, Filter, Tag, Monitor, User, X, Edit3,
  Save, Grid, List, Trash2, Settings, Clock
} from 'lucide-react';

interface ScreenshotMetadata {
  id: string;
  filename: string;
  filepath: string;
  file_size: number;
  width: number;
  height: number;
  active_window_title?: string;
  active_process_name?: string;
  active_process_id?: number;
  capture_method: string;
  captured_by?: string;
  tags: string[];
  notes?: string;
  captured_at: string;
  metadata: Record<string, any>;
}

export function ScreenshotExplorer() {
  const [screenshot, setScreenshot] = useState<string | null>(null);
  const [selectedScreenshot, setSelectedScreenshot] = useState<ScreenshotMetadata | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [loadingStatus, setLoadingStatus] = useState('');
  const [fullscreen, setFullscreen] = useState(false);
  const [screenshots, setScreenshots] = useState<ScreenshotMetadata[]>([]);
  const [viewMode, setViewMode] = useState<'grid' | 'list'>('grid');
  
  // Search and filter states
  const [searchTerm, setSearchTerm] = useState('');
  const [filterTags, setFilterTags] = useState('');
  const [filterUser, setFilterUser] = useState('');
  const [showFilters, setShowFilters] = useState(false);
  
  // Mini explorer states
  const [showMiniExplorer, setShowMiniExplorer] = useState(false);
  const [editingScreenshot, setEditingScreenshot] = useState<ScreenshotMetadata | null>(null);
  const [newTags, setNewTags] = useState('');
  const [newNotes, setNewNotes] = useState('');
  
  const selectedDeviceId = useAppStore((s) => s.selectedDeviceId);

  // Load screenshots on mount and device change
  useEffect(() => {
    if (selectedDeviceId) {
      loadScreenshots();
    }
  }, [selectedDeviceId, searchTerm, filterTags, filterUser]);

  const loadScreenshots = async () => {
    if (!selectedDeviceId) return;
    
    try {
      const params = new URLSearchParams();
      params.append('limit', '50');
      if (searchTerm) params.append('search', searchTerm);
      if (filterTags) params.append('tags', filterTags);
      if (filterUser) params.append('captured_by', filterUser);
      
      // Use the API client to ensure proper authentication
      const data = await api.get<{ screenshots: ScreenshotMetadata[] }>(`/api/devices/${selectedDeviceId}/screenshots?${params}`);
      setScreenshots(data.screenshots || []);
      
      // Show the most recent screenshot if no current screenshot
      if (data.screenshots && data.screenshots.length > 0 && !screenshot) {
        setScreenshot(`${window.location.origin}${data.screenshots[0].filepath}`);
        setSelectedScreenshot(data.screenshots[0]);
      }
    } catch (error) {
      console.log('Could not load screenshots:', error);
      // Don't show error to user since this is a fallback feature
    }
  };

  const captureScreenshot = useCallback(async () => {
    if (!selectedDeviceId) return;
    setLoading(true);
    setError('');
    setLoadingStatus('Capturing screenshot...');

    try {
      const response = await api.sendCommand(selectedDeviceId, 'screenshot');
      
      // Poll for the result with retry logic
      setLoadingStatus('Waiting for screenshot...');
      const result = await pollCommandWithStatus(response.command_id, setLoadingStatus);
      
      if (result.exit_code === 0 && result.output && result.output.length > 0) {
        const outputData = result.output[0] as any;
        
        // Check if the output is already processed (file metadata) or raw base64
        if (typeof outputData === 'object' && outputData.type === 'screenshot_file') {
          // Screenshot was already saved by server via WebSocket
          setLoadingStatus('Screenshot saved successfully');
          setScreenshot(outputData.filepath);  // Use server filepath
          
          // Reload screenshots list
          await loadScreenshots();
          
          setTimeout(() => setLoadingStatus(''), 3000);
        } else {
          // Raw base64 data - need to save it
          const base64Data = outputData as string;
          const metadata = (result as any).metadata || {};
          
          setLoadingStatus('Saving screenshot...');
          
          // Save screenshot with metadata to server
          const saveData = {
            image_data: base64Data,
            metadata: metadata,
            capture_method: 'manual'
          };
          
          const saveResponse = await api.post<{
            status: string;
            screenshot_id: string;
            filename: string;
            filepath: string;
          }>(`/api/devices/${selectedDeviceId}/screenshots`, saveData);
          
          if (saveResponse.status === 'saved') {
            setLoadingStatus('Screenshot saved successfully');
            setScreenshot(`data:image/png;base64,${base64Data}`);
            
            // Reload screenshots list
            await loadScreenshots();
            
            setTimeout(() => setLoadingStatus(''), 3000);
          } else {
            throw new Error('Failed to save screenshot metadata');
          }
        }
      } else {
        throw new Error(result.error || 'Screenshot capture failed');
      }
    } catch (err: any) {
      console.error('Screenshot capture failed:', err);
      setError(err.message || 'Failed to capture screenshot');
    } finally {
      setLoading(false);
    }
  }, [selectedDeviceId]);

  const downloadScreenshot = useCallback(() => {
    if (!screenshot) return;
    
    const link = document.createElement('a');
    link.href = screenshot;
    link.download = `screenshot-${new Date().toISOString()}.png`;
    link.click();
  }, [screenshot]);

  const refreshScreenshots = useCallback(async () => {
    await loadScreenshots();
  }, [selectedDeviceId]);

  const selectScreenshot = (screenshotData: ScreenshotMetadata) => {
    setScreenshot(`${window.location.origin}${screenshotData.filepath}`);
    setSelectedScreenshot(screenshotData);
  };

  const openEditDialog = (screenshotData: ScreenshotMetadata) => {
    setEditingScreenshot(screenshotData);
    setNewTags(screenshotData.tags.join(', '));
    setNewNotes(screenshotData.notes || '');
  };

  const saveScreenshotMetadata = async () => {
    if (!editingScreenshot || !selectedDeviceId) return;
    
    try {
      const updateData = {
        tags: newTags.split(',').map(t => t.trim()).filter(t => t),
        notes: newNotes
      };
      
      await api.put(`/api/devices/${selectedDeviceId}/screenshots/${editingScreenshot.id}`, updateData);
      setEditingScreenshot(null);
      await loadScreenshots();
    } catch (error) {
      console.error('Failed to update screenshot metadata:', error);
    }
  };

  const deleteScreenshot = async (screenshotData: ScreenshotMetadata) => {
    if (!selectedDeviceId || !confirm('Are you sure you want to delete this screenshot?')) return;
    
    try {
      await api.delete(`/api/devices/${selectedDeviceId}/screenshots/${screenshotData.id}`);
      
      // If this was the selected screenshot, clear it
      if (selectedScreenshot?.id === screenshotData.id) {
        setScreenshot(null);
        setSelectedScreenshot(null);
      }
      await loadScreenshots();
    } catch (error) {
      console.error('Failed to delete screenshot:', error);
    }
  };

  const formatFileSize = (bytes: number | undefined): string => {
    if (!bytes || bytes === 0) return '0 B';
    const k = 1024;
    const sizes = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
  };

  const formatDate = (dateStr: string): string => {
    return new Date(dateStr).toLocaleString();
  };

  // Get unique users for filter suggestions
  const allUsers = Array.from(new Set(screenshots.map(s => s.captured_by).filter(Boolean)));

  return (
    <div className="bg-gray-800 rounded-lg shadow-lg overflow-hidden">
      {/* Header */}
      <div className="px-4 py-3 border-b border-gray-700 bg-gray-750">
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-3">
            <h2 className="text-lg font-semibold text-white flex items-center gap-2">
              <Camera className="h-5 w-5 text-blue-400" />
              Screenshot Explorer
            </h2>
            <span className="px-2 py-1 bg-blue-500/20 text-blue-300 rounded text-sm">
              {screenshots.length} screenshots
            </span>
          </div>
          
          <div className="flex items-center gap-2">
            {/* View mode toggle */}
            <div className="flex border border-gray-600 rounded overflow-hidden">
              <button
                onClick={() => setViewMode('grid')}
                className={`p-2 ${viewMode === 'grid' ? 'bg-blue-500 text-white' : 'bg-gray-700 text-gray-300 hover:bg-gray-600'}`}
              >
                <Grid className="h-4 w-4" />
              </button>
              <button
                onClick={() => setViewMode('list')}
                className={`p-2 ${viewMode === 'list' ? 'bg-blue-500 text-white' : 'bg-gray-700 text-gray-300 hover:bg-gray-600'}`}
              >
                <List className="h-4 w-4" />
              </button>
            </div>
            
            <button
              onClick={() => setShowFilters(!showFilters)}
              className={`p-2 rounded ${showFilters ? 'bg-blue-500 text-white' : 'bg-gray-700 text-gray-300 hover:bg-gray-600'}`}
            >
              <Filter className="h-4 w-4" />
            </button>
            
            <button
              onClick={() => setShowMiniExplorer(!showMiniExplorer)}
              className={`p-2 rounded ${showMiniExplorer ? 'bg-blue-500 text-white' : 'bg-gray-700 text-gray-300 hover:bg-gray-600'}`}
            >
              <Settings className="h-4 w-4" />
            </button>
            
            <button
              onClick={refreshScreenshots}
              className="p-2 bg-gray-700 text-gray-300 rounded hover:bg-gray-600"
              disabled={loading}
            >
              <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
            </button>
            
            <button
              onClick={captureScreenshot}
              disabled={loading || !selectedDeviceId}
              className="px-3 py-2 bg-blue-500 text-white rounded hover:bg-blue-600 disabled:opacity-50 disabled:cursor-not-allowed flex items-center gap-2"
            >
              {loading ? (
                <LoaderCircle className="h-4 w-4 animate-spin" />
              ) : (
                <Camera className="h-4 w-4" />
              )}
              Capture
            </button>
          </div>
        </div>
        
        {/* Search and Filter Bar */}
        {showFilters && (
          <div className="mt-3 grid grid-cols-1 md:grid-cols-3 gap-3">
            <div className="relative">
              <Search className="h-4 w-4 absolute left-3 top-3 text-gray-400" />
              <input
                type="text"
                placeholder="Search window titles, processes, notes..."
                value={searchTerm}
                onChange={(e) => setSearchTerm(e.target.value)}
                className="w-full pl-9 pr-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400"
              />
            </div>
            
            <div className="relative">
              <Tag className="h-4 w-4 absolute left-3 top-3 text-gray-400" />
              <input
                type="text"
                placeholder="Filter by tags (comma separated)"
                value={filterTags}
                onChange={(e) => setFilterTags(e.target.value)}
                className="w-full pl-9 pr-3 py-2 bg-gray-700 border border-gray-600 rounded text-white placeholder-gray-400"
              />
            </div>
            
            <div className="relative">
              <User className="h-4 w-4 absolute left-3 top-3 text-gray-400" />
              <select
                value={filterUser}
                onChange={(e) => setFilterUser(e.target.value)}
                className="w-full pl-9 pr-3 py-2 bg-gray-700 border border-gray-600 rounded text-white"
              >
                <option value="">All users</option>
                {allUsers.map(user => (
                  <option key={user} value={user}>{user}</option>
                ))}
              </select>
            </div>
          </div>
        )}
      </div>

      {/* Loading Status */}
      {loading && (
        <div className="px-4 py-3 border-b border-gray-700 bg-blue-500/10">
          <div className="flex items-center gap-3">
            <LoaderCircle className="h-4 w-4 animate-spin text-blue-400" />
            <span className="text-sm text-blue-300">
              {loadingStatus || 'Taking screenshot...'}
            </span>
          </div>
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

      <div className="flex h-[600px]">
        {/* Main Screenshot Display */}
        <div className={`${showMiniExplorer ? 'w-2/3' : 'w-full'} p-4`}>
          {screenshot ? (
            <div className="relative h-full">
              <img
                src={screenshot}
                alt="Screenshot"
                className="w-full h-full object-contain rounded-lg shadow-lg cursor-pointer bg-gray-900"
                onClick={() => setFullscreen(true)}
              />
              
              {/* Screenshot Info Overlay */}
              {selectedScreenshot && (
                <div className="absolute top-4 left-4 bg-black/70 text-white p-3 rounded-lg max-w-sm">
                  <h3 className="font-medium text-sm mb-1">{selectedScreenshot.filename}</h3>
                  <div className="text-xs space-y-1 text-gray-300">
                    {selectedScreenshot.active_window_title && (
                      <div className="flex items-center gap-1">
                        <Monitor className="h-3 w-3" />
                        {selectedScreenshot.active_window_title}
                      </div>
                    )}
                    {selectedScreenshot.active_process_name && (
                      <div className="flex items-center gap-1">
                        <Settings className="h-3 w-3" />
                        {selectedScreenshot.active_process_name}
                      </div>
                    )}
                    <div className="flex items-center gap-1">
                      <Clock className="h-3 w-3" />
                      {formatDate(selectedScreenshot.captured_at)}
                    </div>
                    <div>
                      {selectedScreenshot.width}×{selectedScreenshot.height} • {formatFileSize(selectedScreenshot.file_size)}
                    </div>
                  </div>
                </div>
              )}
              
              {/* Action buttons */}
              <div className="absolute bottom-4 right-4 flex gap-2">
                <button
                  onClick={downloadScreenshot}
                  className="p-2 bg-black/70 text-white rounded hover:bg-black/80"
                >
                  <Download className="h-4 w-4" />
                </button>
                <button
                  onClick={() => setFullscreen(true)}
                  className="p-2 bg-black/70 text-white rounded hover:bg-black/80"
                >
                  <Maximize2 className="h-4 w-4" />
                </button>
              </div>
            </div>
          ) : (
            <div className="flex flex-col items-center justify-center h-full text-gray-400 border-2 border-dashed border-gray-600 rounded-lg">
              <Camera className="h-16 w-16 mb-4 opacity-50" />
              <p>Click "Capture" to take a screenshot</p>
            </div>
          )}
        </div>

        {/* Mini Explorer Panel */}
        {showMiniExplorer && (
          <div className="w-1/3 border-l border-gray-700 bg-gray-750">
            <div className="p-3 border-b border-gray-700">
              <h3 className="font-medium text-white">Screenshot Library</h3>
            </div>
            
            <div className="overflow-y-auto h-full p-3 space-y-2">
              {screenshots.map((screenshotData) => (
                <div
                  key={screenshotData.id}
                  className={`p-2 rounded border cursor-pointer transition-colors ${
                    selectedScreenshot?.id === screenshotData.id
                      ? 'border-blue-500 bg-blue-500/10'
                      : 'border-gray-600 hover:border-gray-500 bg-gray-800'
                  }`}
                  onClick={() => selectScreenshot(screenshotData)}
                >
                  <div className="flex items-center gap-2 mb-1">
                    <img
                      src={`${window.location.origin}${screenshotData.filepath}`}
                      alt="Thumbnail"
                      className="w-12 h-8 object-cover rounded"
                    />
                    <div className="flex-1 min-w-0">
                      <div className="text-xs font-medium text-white truncate">
                        {screenshotData.active_window_title || screenshotData.filename}
                      </div>
                      <div className="text-xs text-gray-400">
                        {formatDate(screenshotData.captured_at)}
                      </div>
                    </div>
                  </div>
                  
                  <div className="flex items-center justify-between">
                    <div className="text-xs text-gray-500">
                      {screenshotData.width}×{screenshotData.height}
                    </div>
                    <div className="flex gap-1">
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          openEditDialog(screenshotData);
                        }}
                        className="p-1 text-gray-400 hover:text-white"
                      >
                        <Edit3 className="h-3 w-3" />
                      </button>
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          deleteScreenshot(screenshotData);
                        }}
                        className="p-1 text-gray-400 hover:text-red-400"
                      >
                        <Trash2 className="h-3 w-3" />
                      </button>
                    </div>
                  </div>
                  
                  {screenshotData.tags && screenshotData.tags.length > 0 && (
                    <div className="flex flex-wrap gap-1 mt-1">
                      {screenshotData.tags.map((tag, idx) => (
                        <span
                          key={idx}
                          className="px-1 py-0.5 bg-blue-500/20 text-blue-300 rounded text-xs"
                        >
                          {tag}
                        </span>
                      ))}
                    </div>
                  )}
                </div>
              ))}
            </div>
          </div>
        )}
      </div>

      {/* Full Screenshots Grid/List (when mini explorer is closed) */}
      {!showMiniExplorer && screenshots.length > 0 && (
        <div className="border-t border-gray-700 p-4">
          <h3 className="text-sm font-medium text-gray-300 mb-3 flex items-center gap-2">
            <Camera className="h-4 w-4" />
            Screenshot Library ({screenshots.length})
          </h3>
          
          {viewMode === 'grid' ? (
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-6 gap-3">
              {screenshots.map((screenshotData) => (
                <div 
                  key={screenshotData.id} 
                  className="relative group cursor-pointer"
                  onClick={() => selectScreenshot(screenshotData)}
                >
                  <img
                    src={`${window.location.origin}${screenshotData.filepath}`}
                    alt="Screenshot thumbnail"
                    className="w-full aspect-video object-cover rounded-lg border border-gray-600 hover:border-blue-500 transition-colors"
                  />
                  <div className="absolute inset-0 bg-black/0 hover:bg-black/20 rounded-lg transition-colors" />
                  <div className="absolute bottom-1 left-1 bg-black/70 text-white text-xs px-1 py-0.5 rounded">
                    {new Date(screenshotData.captured_at).toLocaleDateString()}
                  </div>
                  
                  {/* Actions */}
                  <div className="absolute top-1 right-1 opacity-0 group-hover:opacity-100 transition-opacity">
                    <div className="flex gap-1">
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          openEditDialog(screenshotData);
                        }}
                        className="p-1 bg-black/70 text-white rounded hover:bg-black/80"
                      >
                        <Edit3 className="h-3 w-3" />
                      </button>
                      <button
                        onClick={(e) => {
                          e.stopPropagation();
                          deleteScreenshot(screenshotData);
                        }}
                        className="p-1 bg-black/70 text-white rounded hover:bg-red-600"
                      >
                        <Trash2 className="h-3 w-3" />
                      </button>
                    </div>
                  </div>
                  
                  {/* Tags */}
                  {screenshotData.tags && screenshotData.tags.length > 0 && (
                    <div className="absolute top-1 left-1">
                      <Tag className="h-3 w-3 text-blue-300" />
                    </div>
                  )}
                </div>
              ))}
            </div>
          ) : (
            <div className="space-y-2">
              {screenshots.map((screenshotData) => (
                <div
                  key={screenshotData.id}
                  className="flex items-center gap-3 p-3 bg-gray-750 rounded-lg border border-gray-600 hover:border-gray-500 cursor-pointer"
                  onClick={() => selectScreenshot(screenshotData)}
                >
                  <img
                    src={`${window.location.origin}${screenshotData.filepath}`}
                    alt="Screenshot thumbnail"
                    className="w-16 h-10 object-cover rounded"
                  />
                  
                  <div className="flex-1 min-w-0">
                    <h4 className="text-sm font-medium text-white truncate">
                      {screenshotData.active_window_title || screenshotData.filename}
                    </h4>
                    <div className="flex items-center gap-4 text-xs text-gray-400">
                      <span>{formatDate(screenshotData.captured_at)}</span>
                      <span>{screenshotData.width}×{screenshotData.height}</span>
                      <span>{formatFileSize(screenshotData.file_size)}</span>
                      {screenshotData.captured_by && <span>by {screenshotData.captured_by}</span>}
                    </div>
                    {screenshotData.active_process_name && (
                      <div className="text-xs text-gray-500">
                        Process: {screenshotData.active_process_name}
                      </div>
                    )}
                  </div>
                  
                  <div className="flex flex-col gap-1">
                    {screenshotData.tags && screenshotData.tags.length > 0 && (
                      <div className="flex flex-wrap gap-1">
                        {screenshotData.tags.map((tag, idx) => (
                          <span
                            key={idx}
                            className="px-1 py-0.5 bg-blue-500/20 text-blue-300 rounded text-xs"
                          >
                            {tag}
                          </span>
                        ))}
                      </div>
                    )}
                  </div>
                  
                  <div className="flex gap-1">
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        openEditDialog(screenshotData);
                      }}
                      className="p-2 text-gray-400 hover:text-white"
                    >
                      <Edit3 className="h-4 w-4" />
                    </button>
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        deleteScreenshot(screenshotData);
                      }}
                      className="p-2 text-gray-400 hover:text-red-400"
                    >
                      <Trash2 className="h-4 w-4" />
                    </button>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* Edit Screenshot Dialog */}
      {editingScreenshot && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-gray-800 rounded-lg p-6 w-full max-w-md">
            <div className="flex items-center justify-between mb-4">
              <h3 className="text-lg font-semibold text-white">Edit Screenshot</h3>
              <button
                onClick={() => setEditingScreenshot(null)}
                className="text-gray-400 hover:text-white"
              >
                <X className="h-5 w-5" />
              </button>
            </div>
            
            <div className="space-y-4">
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Tags (comma separated)
                </label>
                <input
                  type="text"
                  value={newTags}
                  onChange={(e) => setNewTags(e.target.value)}
                  placeholder="bug, ui, important"
                  className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white"
                />
              </div>
              
              <div>
                <label className="block text-sm font-medium text-gray-300 mb-2">
                  Notes
                </label>
                <textarea
                  value={newNotes}
                  onChange={(e) => setNewNotes(e.target.value)}
                  placeholder="Add notes about this screenshot..."
                  rows={3}
                  className="w-full px-3 py-2 bg-gray-700 border border-gray-600 rounded text-white"
                />
              </div>
            </div>
            
            <div className="flex gap-2 mt-6">
              <button
                onClick={saveScreenshotMetadata}
                className="flex-1 px-4 py-2 bg-blue-500 text-white rounded hover:bg-blue-600"
              >
                <Save className="h-4 w-4 inline mr-2" />
                Save
              </button>
              <button
                onClick={() => setEditingScreenshot(null)}
                className="px-4 py-2 bg-gray-600 text-white rounded hover:bg-gray-700"
              >
                Cancel
              </button>
            </div>
          </div>
        </div>
      )}

      {/* Fullscreen Modal */}
      {fullscreen && screenshot && (
        <div
          className="fixed inset-0 bg-black/90 z-50 flex items-center justify-center p-4"
          onClick={() => setFullscreen(false)}
        >
          <img
            src={screenshot}
            alt="Screenshot fullscreen"
            className="max-w-full max-h-full object-contain"
          />
          <button
            className="absolute top-4 right-4 text-white bg-gray-800 hover:bg-gray-700 rounded-full p-2"
            onClick={() => setFullscreen(false)}
          >
            <X className="h-5 w-5" />
          </button>
        </div>
      )}
    </div>
  );
}