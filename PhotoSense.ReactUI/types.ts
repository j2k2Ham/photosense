// Shared DTO-type definitions aligned with backend Domain / Application contracts.
export interface DuplicateGroupDto {
  key: string;
  photos: PhotoDto[];
  perceptual?: boolean;
  distance?: number; // for near duplicates
}

export interface PhotoDto {
  id: string;
  fileName: string;
  sourcePath: string;
  fileSizeBytes: number;
  contentHash?: string;
  perceptualHash?: string;
  takenOn?: string;
  cameraModel?: string;
  latitude?: number;
  longitude?: number;
  set: 'Primary' | 'Secondary';
  categories?: string[];
}

export interface ScanProgressSnapshotDto {
  instanceId: string;
  startedUtc: string;
  completedUtc?: string;
  primaryTotal: number;
  primaryProcessed: number;
  secondaryTotal: number;
  secondaryProcessed: number;
  primaryPercent: number;
  secondaryPercent: number;
  overallPercent: number;
}

export interface StartScanRequest {
  primaryLocation: string;
  secondaryLocation?: string;
  recursive: boolean;
  hammingThreshold: number;
}
