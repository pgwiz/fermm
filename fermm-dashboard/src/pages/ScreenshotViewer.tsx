import { useState, useCallback, useEffect } from 'react';
import { api } from '../api/client';
import { useAppStore } from '../store/appStore';
import { Camera, RefreshCw, Download, Maximize2, AlertCircle, LoaderCircle } from 'lucide-react';

export function ScreenshotViewer() {
  const [screenshot, setScreenshot] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [loadingStatus, setLoadingStatus] = useState('');
  const [fullscreen, setFullscreen] = useState(false);
  const [existingScreenshots, setExistingScreenshots] = useState<any[]>([]);
  const selectedDeviceId = useAppStore((s) => s.selectedDeviceId);

  // Load existing screenshots on mount and device change
  useEffect(() => {
    if (selectedDeviceId) {
      loadExistingScreenshots();
    }
  }, [selectedDeviceId]);

  const loadExistingScreenshots = async () => {
    if (!selectedDeviceId) return;
    
    try {
      const response = await fetch(`http://localhost/api/devices/${selectedDeviceId}/screenshots`, {
        headers: {
          'Authorization': `Bearer ${localStorage.getItem('fermm_token') || ''}`
        }
      });
      
      if (response.ok) {
        const data = await response.json();
        setExistingScreenshots(data.screenshots || []);
        
        // Show the most recent screenshot if available and no current screenshot
        if (data.screenshots && data.screenshots.length > 0 && !screenshot) {
          setScreenshot(`${window.location.origin}${data.screenshots[0].filepath}`);
        }
      }
    } catch (error) {
      console.log('Could not load existing screenshots:', error);
      // Don't show error to user since this is a fallback feature
    }
  };

  const captureScreenshot = useCallback(async () => {
    if (!selectedDeviceId) return;
    setLoading(true);
    setError('');
    setLoadingStatus('Capturing screenshot...');

    try {
      await api.sendCommand(selectedDeviceId, 'screenshot', '');
      
      // Poll for results directly
      const maxAttempts = 20;
      let delay = 1000;
      const commandStartTime = Date.now();
      
      for (let attempt = 1; attempt <= maxAttempts; attempt++) {
        setLoadingStatus(`Attempt ${attempt}/${maxAttempts}: Processing screenshot...`);
        
        try {
          // Get command history for this device
          const results = await api.getCommandHistory(selectedDeviceId, 10);
          
          // Find the most recent screenshot result
          const screenshotResult = results.find((r: any) => 
            r.type === 'screenshot' && 
            r.exit_code === 0 &&
            r.output &&
            r.output.length > 0 &&
            new Date(r.timestamp) > new Date(commandStartTime - 5000)
          );
          
          if (screenshotResult && screenshotResult.output && screenshotResult.output[0]) {
            try {
              const outputData: any = screenshotResult.output[0];
              
              // Check if it's the new file-based format
              if (typeof outputData === 'object' && outputData !== null && outputData.type === 'screenshot_file') {
                // New file-based format - construct URL to static file
                const imageUrl = `${window.location.origin}${outputData.filepath}`;
                setScreenshot(imageUrl);
                setLoadingStatus(`✓ Screenshot captured (${outputData.size} bytes)`);
                // Refresh the existing screenshots list
                loadExistingScreenshots();
                return;
              } else if (typeof outputData === 'string' && outputData.startsWith('data:')) {
                // Old base64 format (fallback)
                setScreenshot(outputData);
                setLoadingStatus('✓ Screenshot captured (base64)');
                return;
              } else {
                console.log('Unexpected screenshot format:', outputData);
                setLoadingStatus(`Attempt ${attempt}: Unexpected data format`);
              }
            } catch (parseError) {
              console.error('Failed to process screenshot data:', parseError);
              setLoadingStatus(`Attempt ${attempt}: Data processing failed`);
            }
          } else {
            const screenshotResults = results.filter((r: any) => r.type === 'screenshot');
            setLoadingStatus(`Attempt ${attempt}: No valid data (${screenshotResults.length} screenshot results)`);
          }
          
        } catch (fetchError: any) {
          if (fetchError?.message?.includes('404')) {
            setLoadingStatus(`Attempt ${attempt}: Results not ready (404)`);
          } else {
            console.error('Fetch error:', fetchError);
            setLoadingStatus(`Attempt ${attempt}: ${fetchError?.message || 'Unknown error'}`);
          }
        }
        
        // Wait before next attempt
        if (attempt < maxAttempts) {
          await new Promise(resolve => setTimeout(resolve, delay));
          delay = Math.min(delay * 1.3, 3000);
        }
      }
      
      setError('Screenshot capture timed out. Check browser console for details.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to capture screenshot');
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

  const downloadScreenshot = () => {
    if (!screenshot) return;
    
    const link = document.createElement('a');
    link.href = screenshot;
    link.download = `screenshot-${selectedDeviceId}-${Date.now()}.png`;
    link.click();
  };

  if (!selectedDeviceId) {
    return (
      <div className="flex items-center justify-center h-64 text-gray-400">
        <p>Select a device to capture screenshots</p>
      </div>
    );
  }

  return (
    <div className="bg-gray-800 rounded-lg overflow-hidden">
      {/* Toolbar */}
      <div className="p-4 border-b border-gray-700 flex items-center justify-between">
        <h2 className="text-lg font-semibold text-white flex items-center gap-2">
          <Camera className="h-5 w-5" />
          Screenshot
        </h2>
        <div className="flex gap-2">
          <button
            onClick={captureScreenshot}
            disabled={loading}
            className="flex items-center gap-2 px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 rounded text-white transition-colors"
          >
            <RefreshCw className={`h-4 w-4 ${loading ? 'animate-spin' : ''}`} />
            {loading ? 'Capturing...' : 'Capture'}
          </button>
          {screenshot && (
            <>
              <button
                onClick={downloadScreenshot}
                className="flex items-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 rounded text-white transition-colors"
              >
                <Download className="h-4 w-4" />
                Download
              </button>
              <button
                onClick={() => setFullscreen(true)}
                className="flex items-center gap-2 px-4 py-2 bg-gray-700 hover:bg-gray-600 rounded text-white transition-colors"
              >
                <Maximize2 className="h-4 w-4" />
              </button>
            </>
          )}
        </div>
      </div>

      {/* Status Display */}
      {(loading || loadingStatus) && !error && (
        <div className="px-4 py-2 border-b border-gray-700 bg-gray-750">
          {loading && (
            <div className="flex items-center gap-2 text-blue-400">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              <span className="text-sm">{loadingStatus || 'Capturing screenshot...'}</span>
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

      {/* Screenshot display */}
      <div className="p-4">
        {screenshot ? (
          <div className="relative">
            <img
              src={screenshot}
              alt="Screenshot"
              className="w-full rounded-lg shadow-lg cursor-pointer"
              onClick={() => setFullscreen(true)}
            />
          </div>
        ) : (
          <div className="flex flex-col items-center justify-center h-64 text-gray-400 border-2 border-dashed border-gray-600 rounded-lg">
            <Camera className="h-16 w-16 mb-4 opacity-50" />
            <p>Click "Capture" to take a screenshot</p>
          </div>
        )}
      </div>

      {/* Previous Screenshots */}
      {existingScreenshots.length > 0 && (
        <div className="border-t border-gray-700 p-4">
          <h3 className="text-sm font-medium text-gray-300 mb-3 flex items-center gap-2">
            <Camera className="h-4 w-4" />
            Previous Screenshots ({existingScreenshots.length})
          </h3>
          <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 gap-3">
            {existingScreenshots.map((screenshot, index) => (
              <div 
                key={screenshot.filename} 
                className="relative group cursor-pointer"
                onClick={() => setScreenshot(`${window.location.origin}${screenshot.filepath}`)}
              >
                <img
                  src={`${window.location.origin}${screenshot.filepath}`}
                  alt={`Screenshot ${index + 1}`}
                  className="w-full aspect-video object-cover rounded-lg border border-gray-600 hover:border-blue-500 transition-colors"
                />
                <div className="absolute inset-0 bg-black/0 hover:bg-black/20 rounded-lg transition-colors" />
                <div className="absolute bottom-1 left-1 bg-black/70 text-white text-xs px-1 py-0.5 rounded">
                  {new Date(screenshot.created).toLocaleDateString()}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Fullscreen modal */}
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
            ✕
          </button>
        </div>
      )}
    </div>
  );
}
