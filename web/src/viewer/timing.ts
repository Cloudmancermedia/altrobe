// Clip length for an M2 timeline. Some global sequences are 0 ms long; a 0 s three.js clip makes the
// action's time NaN (it divides by the duration), which poisons every bone the clip drives.
export const clipSeconds = (durationMs: number) => Math.max(durationMs, 1) / 1000
