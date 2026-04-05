import { useEffect, useRef, useState, useCallback } from 'react';
import { Terminal as XTerm } from '@xterm/xterm';
import { FitAddon } from '@xterm/addon-fit';
import { api } from '../api/client';
import { useAppStore } from '../store/appStore';
import { Send, Trash2 } from 'lucide-react';
import '@xterm/xterm/css/xterm.css';

export function Terminal() {
  const terminalRef = useRef<HTMLDivElement>(null);
  const xtermRef = useRef<XTerm | null>(null);
  const fitAddonRef = useRef<FitAddon | null>(null);
  const [command, setCommand] = useState('');
  const [loading, setLoading] = useState(false);
  const selectedDeviceId = useAppStore((s) => s.selectedDeviceId);

  useEffect(() => {
    if (!terminalRef.current) return;

    const xterm = new XTerm({
      theme: {
        background: '#1a1b26',
        foreground: '#a9b1d6',
        cursor: '#c0caf5',
        cursorAccent: '#1a1b26',
        selectionBackground: '#33467c',
        black: '#32344a',
        red: '#f7768e',
        green: '#9ece6a',
        yellow: '#e0af68',
        blue: '#7aa2f7',
        magenta: '#ad8ee6',
        cyan: '#449dab',
        white: '#787c99',
        brightBlack: '#444b6a',
        brightRed: '#ff7a93',
        brightGreen: '#b9f27c',
        brightYellow: '#ff9e64',
        brightBlue: '#7da6ff',
        brightMagenta: '#bb9af7',
        brightCyan: '#0db9d7',
        brightWhite: '#acb0d0',
      },
      fontFamily: 'Consolas, Monaco, "Courier New", monospace',
      fontSize: 14,
      cursorBlink: true,
      convertEol: true,
    });

    const fitAddon = new FitAddon();
    xterm.loadAddon(fitAddon);
    xterm.open(terminalRef.current);
    fitAddon.fit();

    xtermRef.current = xterm;
    fitAddonRef.current = fitAddon;

    xterm.writeln('\x1b[1;34m=== FERMM Terminal ===\x1b[0m');
    xterm.writeln('Select a device and enter a command below.\n');

    const handleResize = () => fitAddon.fit();
    window.addEventListener('resize', handleResize);

    return () => {
      window.removeEventListener('resize', handleResize);
      xterm.dispose();
    };
  }, []);

  const executeCommand = useCallback(async () => {
    if (!selectedDeviceId || !command.trim() || !xtermRef.current) return;

    const xterm = xtermRef.current;
    setLoading(true);

    xterm.writeln(`\x1b[1;32m$ ${command}\x1b[0m`);

    try {
      const response = await api.sendCommand(selectedDeviceId, 'shell', command);
      
      // Poll for result
      let attempts = 0;
      while (attempts < 30) {
        await new Promise((r) => setTimeout(r, 500));
        try {
          const result = await api.getCommandResult(response.command_id);
          
          // Write output
          result.output.forEach((line) => {
            if (line.startsWith('[stderr]')) {
              xterm.writeln(`\x1b[31m${line}\x1b[0m`);
            } else {
              xterm.writeln(line);
            }
          });

          if (result.error) {
            xterm.writeln(`\x1b[31mError: ${result.error}\x1b[0m`);
          }

          xterm.writeln(`\x1b[90m[exit: ${result.exit_code}, ${result.duration_ms}ms]\x1b[0m\n`);
          break;
        } catch {
          attempts++;
        }
      }

      if (attempts >= 30) {
        xterm.writeln('\x1b[33mCommand timed out\x1b[0m\n');
      }
    } catch (err) {
      xterm.writeln(`\x1b[31mFailed: ${err instanceof Error ? err.message : 'Unknown error'}\x1b[0m\n`);
    } finally {
      setLoading(false);
      setCommand('');
    }
  }, [selectedDeviceId, command]);

  const clearTerminal = () => {
    xtermRef.current?.clear();
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      executeCommand();
    }
  };

  return (
    <div className="flex flex-col h-full bg-gray-900 rounded-lg overflow-hidden">
      <div className="flex-1 min-h-0">
        <div ref={terminalRef} className="h-full" />
      </div>
      
      <div className="p-3 bg-gray-800 border-t border-gray-700">
        <div className="flex gap-2">
          <input
            type="text"
            value={command}
            onChange={(e) => setCommand(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder={selectedDeviceId ? "Enter command..." : "Select a device first"}
            disabled={!selectedDeviceId || loading}
            className="flex-1 bg-gray-700 border border-gray-600 rounded px-3 py-2 text-white placeholder-gray-400 focus:outline-none focus:border-blue-500 disabled:opacity-50"
          />
          <button
            onClick={executeCommand}
            disabled={!selectedDeviceId || !command.trim() || loading}
            className="px-4 py-2 bg-blue-600 hover:bg-blue-700 disabled:bg-gray-600 text-white rounded flex items-center gap-2 transition-colors"
          >
            <Send className="h-4 w-4" />
          </button>
          <button
            onClick={clearTerminal}
            className="px-4 py-2 bg-gray-700 hover:bg-gray-600 text-white rounded flex items-center gap-2 transition-colors"
          >
            <Trash2 className="h-4 w-4" />
          </button>
        </div>
      </div>
    </div>
  );
}
