import { useEffect, useState, useRef, useCallback } from 'react';
import { api } from '../api/client';
import { useAppStore } from '../store/appStore';
import { Send, Play, X, AlertCircle } from 'lucide-react';

interface Message {
  id: string;
  source: 'device' | 'dashboard';
  content: string;
  timestamp: Date;
}

export function OverlayPanel() {
  const selectedDeviceId = useAppStore((s) => s.selectedDeviceId);
  const [isRunning, setIsRunning] = useState(false);
  const [messages, setMessages] = useState<Message[]>([]);
  const [messageInput, setMessageInput] = useState('');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const messagesEndRef = useRef<HTMLDivElement>(null);

  const scrollToBottom = () => {
    messagesEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  };

  useEffect(() => {
    scrollToBottom();
  }, [messages]);

  const spawnOverlay = useCallback(async () => {
    if (!selectedDeviceId) {
      setError('Please select a device first');
      return;
    }

    setLoading(true);
    setError(null);

    try {
      await api.spawnOverlay(selectedDeviceId);
      setIsRunning(true);
      setMessages([]);
      setError(null);
      
      const welcomeMsg: Message = {
        id: 'welcome-' + Date.now(),
        source: 'device',
        content: 'Overlay spawned successfully. Waiting for connection...',
        timestamp: new Date()
      };
      setMessages([welcomeMsg]);
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Failed to spawn overlay';
      setError(errorMsg);
      console.error('Spawn overlay error:', err);
    } finally {
      setLoading(false);
    }
  }, [selectedDeviceId]);

  const closeOverlay = useCallback(async () => {
    if (!selectedDeviceId) return;

    setLoading(true);
    setError(null);

    try {
      await api.closeOverlay(selectedDeviceId);
      setIsRunning(false);
      
      const closeMsg: Message = {
        id: 'close-' + Date.now(),
        source: 'device',
        content: 'Overlay closed.',
        timestamp: new Date()
      };
      setMessages(prev => [...prev, closeMsg]);
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Failed to close overlay';
      setError(errorMsg);
      console.error('Close overlay error:', err);
    } finally {
      setLoading(false);
    }
  }, [selectedDeviceId]);

  const sendMessage = useCallback(async () => {
    if (!selectedDeviceId || !messageInput.trim() || !isRunning) return;

    const content = messageInput.trim();
    setMessageInput('');
    setLoading(true);

    try {
      // Add user message to local state immediately
      const userMsg: Message = {
        id: 'user-' + Date.now(),
        source: 'dashboard',
        content,
        timestamp: new Date()
      };
      setMessages(prev => [...prev, userMsg]);

      // Send to backend
      await api.sendOverlayMessage(selectedDeviceId, content);
    } catch (err) {
      const errorMsg = err instanceof Error ? err.message : 'Failed to send message';
      setError(errorMsg);
      console.error('Send message error:', err);
      
      // Remove the failed message
      setMessages(prev => prev.slice(0, -1));
      setMessageInput(content); // Restore input
    } finally {
      setLoading(false);
    }
  }, [selectedDeviceId, messageInput, isRunning]);

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      sendMessage();
    }
  };

  return (
    <div className="flex flex-col h-full bg-gray-900 rounded-lg overflow-hidden">
      {/* Header */}
      <div className="p-4 bg-gray-800 border-b border-gray-700 flex justify-between items-center">
        <div className="flex items-center gap-2">
          <div className={`w-3 h-3 rounded-full ${isRunning ? 'bg-green-500' : 'bg-gray-500'}`} />
          <h2 className="text-lg font-semibold text-white">
            Overlay Control
          </h2>
        </div>
        <div className="text-sm text-gray-400">
          {selectedDeviceId ? `Device: ${selectedDeviceId}` : 'No device selected'}
        </div>
      </div>

      {/* Error Message */}
      {error && (
        <div className="p-3 bg-red-900 bg-opacity-50 border-b border-red-700 flex items-center gap-2 text-red-200">
          <AlertCircle className="h-4 w-4" />
          <span>{error}</span>
        </div>
      )}

      {/* Messages Area */}
      <div className="flex-1 overflow-y-auto p-4 space-y-3 bg-gray-900">
        {messages.length === 0 && !isRunning ? (
          <div className="flex items-center justify-center h-full text-gray-500">
            <div className="text-center">
              <p className="mb-2">No overlay running</p>
              <p className="text-sm">Click "Spawn Overlay" to start</p>
            </div>
          </div>
        ) : (
          <>
            {messages.map((msg) => (
              <div key={msg.id} className="flex gap-3">
                <div className="flex-shrink-0 w-8 h-8 rounded-full bg-gradient-to-br from-blue-500 to-blue-600 flex items-center justify-center">
                  <span className="text-xs font-bold text-white">
                    {msg.source === 'device' ? 'D' : 'Y'}
                  </span>
                </div>
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-1">
                    <span className="text-sm font-medium text-gray-300">
                      {msg.source === 'device' ? 'Device' : 'You'}
                    </span>
                    <span className="text-xs text-gray-500">
                      {msg.timestamp.toLocaleTimeString()}
                    </span>
                  </div>
                  <div className="bg-gray-800 rounded px-3 py-2 text-gray-200 text-sm break-words">
                    {msg.content}
                  </div>
                </div>
              </div>
            ))}
            <div ref={messagesEndRef} />
          </>
        )}
      </div>

      {/* Controls */}
      <div className="p-3 bg-gray-800 border-t border-gray-700 space-y-3">
        {/* Spawn/Close Buttons */}
        <div className="flex gap-2">
          <button
            onClick={spawnOverlay}
            disabled={!selectedDeviceId || isRunning || loading}
            className="flex-1 px-4 py-2 bg-green-600 hover:bg-green-700 disabled:bg-gray-600 text-white rounded flex items-center justify-center gap-2 transition-colors font-medium"
          >
            <Play className="h-4 w-4" />
            Spawn Overlay
          </button>
          <button
            onClick={closeOverlay}
            disabled={!selectedDeviceId || !isRunning || loading}
            className="flex-1 px-4 py-2 bg-red-600 hover:bg-red-700 disabled:bg-gray-600 text-white rounded flex items-center justify-center gap-2 transition-colors font-medium"
          >
            <X className="h-4 w-4" />
            Close Overlay
          </button>
        </div>

        {/* Message Input */}
        {isRunning && (
          <div className="flex gap-2">
            <input
              type="text"
              value={messageInput}
              onChange={(e) => setMessageInput(e.target.value)}
              onKeyDown={handleKeyDown}
              placeholder="Type a message..."
              disabled={!selectedDeviceId || !isRunning || loading}
              className="flex-1 bg-gray-700 border border-gray-600 rounded px-3 py-2 text-white placeholder-gray-400 focus:outline-none focus:border-blue-500 disabled:opacity-50"
            />
            <button
              onClick={sendMessage}
              disabled={!selectedDeviceId || !messageInput.trim() || loading || !isRunning}
              className="px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 text-white rounded flex items-center gap-2 transition-colors"
            >
              <Send className="h-4 w-4" />
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
