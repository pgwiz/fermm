import { api } from '../api/client';
import type { CommandResult } from '../api/client';

interface PollCommandOptions {
  commandId: string;
  maxAttempts?: number;
  delayMs?: number;
  onProgress?: (attempt: number, maxAttempts: number) => void;
}

/**
 * Polls for a command result with retry logic
 * @param options Polling configuration
 * @returns Promise that resolves with the command result
 */
export async function pollCommandResult({
  commandId,
  maxAttempts = 30,
  delayMs = 500,
  onProgress
}: PollCommandOptions): Promise<CommandResult> {
  let attempts = 0;
  
  while (attempts < maxAttempts) {
    await new Promise(resolve => setTimeout(resolve, delayMs));
    
    try {
      const result = await api.getCommandResult(commandId);
      return result; // Success - return the result
    } catch (err) {
      attempts++;
      
      if (onProgress) {
        onProgress(attempts, maxAttempts);
      }
      
      // If this was the last attempt, re-throw the error
      if (attempts >= maxAttempts) {
        throw new Error(`Command timed out after ${maxAttempts * delayMs / 1000} seconds`);
      }
    }
  }
  
  // This should never be reached, but TypeScript needs it
  throw new Error('Polling loop exited unexpectedly');
}

/**
 * Convenience function for common polling with status updates
 * @param commandId Command ID to poll
 * @param onStatusUpdate Callback for status updates
 * @returns Promise that resolves with the command result
 */
export async function pollCommandWithStatus(
  commandId: string,
  onStatusUpdate: (status: string) => void
): Promise<CommandResult> {
  return pollCommandResult({
    commandId,
    maxAttempts: 30,
    delayMs: 500,
    onProgress: (attempt, _maxAttempts) => {
      if (attempt % 5 === 0) { // Update every 2.5 seconds
        const seconds = Math.round(attempt * 0.5);
        onStatusUpdate(`Waiting for result... (${seconds}s)`);
      }
    }
  });
}