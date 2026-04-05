import { api } from '../api/client';

export interface SmartPollerOptions {
  commandId: string;
  maxAttempts?: number;
  initialDelay?: number;
  backoffMultiplier?: number;
  maxDelay?: number;
  onProgress?: (attempt: number, error?: string) => void;
}

export interface PollerResult<T = any> {
  success: boolean;
  data?: T;
  error?: string;
  attempts: number;
}

/**
 * Smart poller that handles 404s and waits for actual data
 * Implements exponential backoff with jitter for better performance
 */
export async function pollForResult<T = any>(options: SmartPollerOptions): Promise<PollerResult<T>> {
  const {
    commandId,
    maxAttempts = 30,
    initialDelay = 500,
    backoffMultiplier = 1.2,
    maxDelay = 5000,
    onProgress
  } = options;

  let attempt = 0;
  let delay = initialDelay;

  while (attempt < maxAttempts) {
    attempt++;

    try {
      const result = await api.getCommandResult(commandId);
      
      // Check if we have meaningful data
      if (result && result.output && result.output.length > 0) {
        // For processes/files, check if output is not empty
        const firstOutput = result.output[0];
        
        if (typeof firstOutput === 'string' && firstOutput.trim()) {
          // Try to parse JSON if it's a string
          try {
            const parsedData = JSON.parse(firstOutput);
            return {
              success: true,
              data: parsedData,
              attempts: attempt
            };
          } catch {
            // Not JSON, return raw string
            return {
              success: true,
              data: firstOutput as T,
              attempts: attempt
            };
          }
        } else if (typeof firstOutput === 'object' && firstOutput !== null) {
          // Already parsed object (like screenshot metadata)
          return {
            success: true,
            data: firstOutput as T,
            attempts: attempt
          };
        }
      }

      // Check for error condition
      if (result.error) {
        return {
          success: false,
          error: result.error,
          attempts: attempt
        };
      }

      // If exit_code indicates failure but no specific error
      if (result.exit_code !== 0 && !result.output?.length) {
        return {
          success: false,
          error: `Command failed with exit code ${result.exit_code}`,
          attempts: attempt
        };
      }

      onProgress?.(attempt, 'Waiting for data...');

    } catch (error: any) {
      // Handle 404 or network errors gracefully
      const errorMessage = error?.message || 'Unknown error';
      
      if (errorMessage.includes('404') || errorMessage.includes('Not Found')) {
        onProgress?.(attempt, '404 - Result not ready yet');
      } else if (errorMessage.includes('Not authenticated')) {
        return {
          success: false,
          error: 'Authentication failed. Please log in again.',
          attempts: attempt
        };
      } else {
        onProgress?.(attempt, `Error: ${errorMessage}`);
      }
    }

    // Don't wait on the last attempt
    if (attempt < maxAttempts) {
      await new Promise(resolve => setTimeout(resolve, delay));
      
      // Exponential backoff with jitter
      delay = Math.min(delay * backoffMultiplier + Math.random() * 100, maxDelay);
    }
  }

  return {
    success: false,
    error: `Polling timeout after ${maxAttempts} attempts`,
    attempts: attempt
  };
}

/**
 * Convenience function for common command types
 */
export async function pollForProcesses(commandId: string, onProgress?: (attempt: number, error?: string) => void) {
  const result = await pollForResult({
    commandId,
    maxAttempts: 25,
    onProgress
  });

  if (result.success && result.data) {
    // Extract processes array from the data
    return {
      ...result,
      data: result.data.processes || result.data || []
    };
  }

  return result;
}

export async function pollForFileList(commandId: string, onProgress?: (attempt: number, error?: string) => void) {
  const result = await pollForResult({
    commandId,
    maxAttempts: 20,
    onProgress
  });

  if (result.success && result.data) {
    // Extract entries array from the data
    return {
      ...result,
      data: result.data.entries || result.data || []
    };
  }

  return result;
}

export async function pollForScreenshot(commandId: string, onProgress?: (attempt: number, error?: string) => void) {
  return pollForResult({
    commandId,
    maxAttempts: 40, // Screenshots take longer
    initialDelay: 1000,
    maxDelay: 3000,
    onProgress
  });
}