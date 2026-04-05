import { useState, useEffect } from 'react';
import { useAppStore } from '../store/appStore';
import { api } from '../api/client';
import { pollCommandResult } from '../utils/commandPoller';
import { AlertTriangle, Shield, Settings, Terminal, HardDrive, Activity, FileText, Server, CheckCircle, XCircle } from 'lucide-react';

interface GodModeState {
    isEnabled: boolean;
    isLoading: boolean;
}

const GodModeManager = () => {
    const { selectedDeviceId, devices } = useAppStore();
    const selectedDevice = devices.find(d => d.id === selectedDeviceId);
    const [godModeState, setGodModeState] = useState<GodModeState>({ isEnabled: false, isLoading: false });
    const [lastOutput, setLastOutput] = useState<string[]>([]);
    const [isExecuting, setIsExecuting] = useState(false);

    useEffect(() => {
        if (selectedDevice) {
            checkGodModeStatus();
        }
    }, [selectedDevice]);

    const checkGodModeStatus = async () => {
        if (!selectedDevice) return;
        
        setGodModeState(prev => ({ ...prev, isLoading: true }));
        
        try {
            const payload = JSON.stringify({ action: 'status' });
            const result = await api.sendCommand(selectedDevice.id, 'godmode', payload);
            const response = await pollCommandResult({ commandId: result.command_id, maxAttempts: 20, delayMs: 500 });
            
            if (response.exit_code === 0 && response.output) {
                const isEnabled = response.output.some((line: string) => 
                    line.includes('ENABLED')
                );
                setGodModeState({ isEnabled, isLoading: false });
            } else {
                setGodModeState({ isEnabled: false, isLoading: false });
            }
        } catch (error) {
            console.error('Failed to check GOD Mode status:', error);
            setGodModeState({ isEnabled: false, isLoading: false });
        }
    };

    const executeGodModeCommand = async (action: string, command?: string) => {
        if (!selectedDevice) throw new Error('No device selected');
        
        const payload = JSON.stringify({ action, ...(command && { command }) });
        const result = await api.sendCommand(selectedDevice.id, 'godmode', payload);
        return await pollCommandResult({ commandId: result.command_id, maxAttempts: 20, delayMs: 500 });
    };

    const handleGodModeToggle = async () => {
        if (!selectedDevice) return;
        
        setIsExecuting(true);
        try {
            const action = godModeState.isEnabled ? 'disable' : 'enable';
            const result = await executeGodModeCommand(action);
            
            if (result.exit_code === 0) {
                setGodModeState(prev => ({ ...prev, isEnabled: !prev.isEnabled }));
                setLastOutput(result.output || []);
            } else {
                console.error('GOD Mode toggle failed:', result.error);
                setLastOutput([`Error: ${result.error || 'Unknown error'}`]);
            }
        } catch (error) {
            console.error('Failed to toggle GOD Mode:', error);
            setLastOutput([`Error: ${error instanceof Error ? error.message : 'Unknown error'}`]);
        } finally {
            setIsExecuting(false);
        }
    };

    const executeAdminTool = async (action: string, toolName: string, command?: string) => {
        if (!selectedDevice) return;
        
        setIsExecuting(true);
        try {
            const result = await executeGodModeCommand(action, command);
            
            if (result.exit_code === 0) {
                setLastOutput(result.output || [`${toolName} executed successfully`]);
            } else {
                console.error(`${toolName} failed:`, result.error);
                setLastOutput([`Error: ${result.error || 'Unknown error'}`]);
            }
        } catch (error) {
            console.error(`Failed to execute ${toolName}:`, error);
            setLastOutput([`Error: ${error instanceof Error ? error.message : 'Unknown error'}`]);
        } finally {
            setIsExecuting(false);
        }
    };

    const adminTools = [
        { 
            id: 'godmode', 
            name: 'GOD Mode Folder', 
            description: 'Access all Windows settings from one place',
            icon: <Shield className="h-5 w-5" />,
            action: 'godmode'
        },
        { 
            id: 'admintools', 
            name: 'Administrative Tools', 
            description: 'Windows administrative utilities',
            icon: <Settings className="h-5 w-5" />,
            action: 'admintools'
        },
        { 
            id: 'taskmanager', 
            name: 'Task Manager', 
            description: 'View and manage running processes',
            icon: <Activity className="h-5 w-5" />,
            action: 'taskmanager'
        },
        { 
            id: 'services', 
            name: 'Services Console', 
            description: 'Manage Windows services',
            icon: <Server className="h-5 w-5" />,
            action: 'services'
        },
        { 
            id: 'registry', 
            name: 'Registry Editor', 
            description: 'Edit Windows registry',
            icon: <FileText className="h-5 w-5" />,
            action: 'registry',
            warning: true
        },
        { 
            id: 'eventviewer', 
            name: 'Event Viewer', 
            description: 'View system logs and events',
            icon: <FileText className="h-5 w-5" />,
            action: 'eventviewer'
        },
        { 
            id: 'devicemanager', 
            name: 'Device Manager', 
            description: 'Manage hardware devices',
            icon: <HardDrive className="h-5 w-5" />,
            action: 'devicemanager'
        },
        { 
            id: 'diskmanagement', 
            name: 'Disk Management', 
            description: 'Manage disk partitions',
            icon: <HardDrive className="h-5 w-5" />,
            action: 'diskmanagement'
        },
        { 
            id: 'systeminfo', 
            name: 'System Information', 
            description: 'View detailed system info',
            icon: <Terminal className="h-5 w-5" />,
            action: 'systeminfo'
        }
    ];

    const runDialogCommands = [
        { name: 'Control Panel', command: 'control' },
        { name: 'System Configuration', command: 'msconfig' },
        { name: 'Group Policy Editor', command: 'gpedit.msc' },
        { name: 'Local Security Policy', command: 'secpol.msc' },
        { name: 'Computer Management', command: 'compmgmt.msc' },
        { name: 'Performance Monitor', command: 'perfmon' },
        { name: 'Resource Monitor', command: 'resmon' },
        { name: 'Windows Features', command: 'optionalfeatures' },
        { name: 'User Accounts', command: 'netplwiz' },
        { name: 'System Properties', command: 'sysdm.cpl' }
    ];

    if (!selectedDevice) {
        return (
            <div className="flex items-center justify-center h-full text-gray-500">
                Select a device to manage GOD Mode features
            </div>
        );
    }

    return (
        <div className="space-y-6">
            {/* Header */}
            <div className="flex items-center justify-between">
                <div>
                    <h1 className="text-2xl font-bold">GOD Mode Manager</h1>
                    <p className="text-gray-600">Advanced administrative tools for {selectedDevice.hostname}</p>
                </div>
                <div className="flex items-center space-x-2">
                    <span className={`inline-flex items-center px-2.5 py-0.5 rounded-full text-xs font-medium ${
                        godModeState.isEnabled 
                            ? 'bg-green-100 text-green-800' 
                            : 'bg-gray-100 text-gray-800'
                    }`}>
                        {godModeState.isLoading ? (
                            "Checking..."
                        ) : (
                            <>
                                {godModeState.isEnabled ? (
                                    <CheckCircle className="h-3 w-3 mr-1" />
                                ) : (
                                    <XCircle className="h-3 w-3 mr-1" />
                                )}
                                {godModeState.isEnabled ? "Enabled" : "Disabled"}
                            </>
                        )}
                    </span>
                    <button
                        onClick={handleGodModeToggle}
                        disabled={isExecuting || godModeState.isLoading}
                        className={`px-4 py-2 rounded-md text-sm font-medium ${
                            godModeState.isEnabled 
                                ? 'bg-red-600 hover:bg-red-700 text-white' 
                                : 'bg-blue-600 hover:bg-blue-700 text-white'
                        } disabled:opacity-50`}
                    >
                        {isExecuting ? "..." : godModeState.isEnabled ? "Disable GOD Mode" : "Enable GOD Mode"}
                    </button>
                </div>
            </div>

            {/* Warning Banner */}
            <div className="border border-yellow-200 bg-yellow-50 rounded-lg p-4">
                <div className="flex items-center">
                    <AlertTriangle className="h-5 w-5 text-yellow-600 mr-2" />
                    <div>
                        <h3 className="text-sm font-medium text-yellow-800">Administrative Access Warning</h3>
                        <p className="text-sm text-yellow-700 mt-1">
                            These tools provide elevated system access. Use with caution and ensure you have proper authorization.
                        </p>
                    </div>
                </div>
            </div>

            {/* Admin Tools Grid */}
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-4">
                {adminTools.map((tool) => (
                    <div key={tool.id} className={`border rounded-lg p-4 cursor-pointer hover:shadow-md transition-shadow ${
                        tool.warning ? 'border-red-200 bg-red-50' : 'border-gray-200 bg-white'
                    }`}>
                        <div className="flex items-center mb-2">
                            {tool.icon}
                            <h3 className="ml-2 text-sm font-medium">{tool.name}</h3>
                            {tool.warning && <AlertTriangle className="h-4 w-4 ml-auto text-red-500" />}
                        </div>
                        <p className="text-xs text-gray-600 mb-3">{tool.description}</p>
                        <button
                            onClick={() => executeAdminTool(tool.action, tool.name)}
                            disabled={isExecuting}
                            className={`w-full px-3 py-1.5 rounded text-sm font-medium ${
                                tool.warning 
                                    ? 'bg-red-600 hover:bg-red-700 text-white' 
                                    : 'bg-gray-600 hover:bg-gray-700 text-white'
                            } disabled:opacity-50`}
                        >
                            {isExecuting ? "..." : "Open"}
                        </button>
                    </div>
                ))}
            </div>

            {/* Run Dialog Commands */}
            <div className="border border-gray-200 bg-white rounded-lg p-4">
                <div className="flex items-center mb-3">
                    <Terminal className="h-5 w-5 mr-2 text-gray-700" />
                    <h3 className="text-lg font-medium text-gray-900">Windows Run Commands</h3>
                </div>
                <p className="text-sm text-gray-600 mb-4">Execute common Windows run dialog commands</p>
                <div className="grid grid-cols-2 md:grid-cols-3 lg:grid-cols-5 gap-2">
                    {runDialogCommands.map((cmd) => (
                        <button
                            key={cmd.command}
                            onClick={() => executeAdminTool('winr', `Run: ${cmd.name}`, cmd.command)}
                            disabled={isExecuting}
                            className="px-2 py-1 text-xs text-gray-900 border border-gray-300 rounded hover:bg-gray-50 disabled:opacity-50"
                        >
                            {cmd.name}
                        </button>
                    ))}
                </div>
            </div>

            {/* Output Display */}
            {lastOutput.length > 0 && (
                <div className="border border-gray-200 bg-white rounded-lg p-4">
                    <h3 className="text-lg font-medium mb-3">Command Output</h3>
                    <div className="bg-black text-green-400 p-3 rounded font-mono text-sm max-h-40 overflow-y-auto">
                        {lastOutput.map((line, index) => (
                            <div key={index}>{line}</div>
                        ))}
                    </div>
                </div>
            )}
        </div>
    );
};

export default GodModeManager;