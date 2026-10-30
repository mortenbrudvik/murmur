import { useCallback, useRef } from "react";

const ignore = () => undefined;

/**
 * Runs async tasks one at a time, in the order they were queued. A task that fails rejects its own promise
 * and nothing else: the tasks queued behind it still run.
 *
 * Saves go through this so that each one is built from the document the previous one produced.
 */
export function useSerialQueue(): <T>(task: () => Promise<T>) => Promise<T> {
  const tail = useRef<Promise<unknown>>(Promise.resolve());

  return useCallback(<T>(task: () => Promise<T>): Promise<T> => {
    const run = tail.current.then(task);
    tail.current = run.then(ignore, ignore);
    return run;
  }, []);
}
